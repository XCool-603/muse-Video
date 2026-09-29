using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShortDrama.Application.Adapters;
using ShortDrama.Domain.Entities;
using ShortDrama.Infrastructure.Adapters;
using ShortDrama.Infrastructure.Data;

namespace ShortDrama.Infrastructure.Services
{
    /// <summary>
    /// 采集源播种器：用短剧相关关键词走搜索接口批量入库。
    ///
    /// 为什么不用各源的「最新上架」接口入库？
    /// 实测多数苹果CMS 采集站对分类参数（t / type_id）支持不一致，甚至直接忽略，
    /// 返回的是全站最新（动漫、综艺居多）。用关键词搜索播种，入库内容才贴合短剧定位。
    ///
    /// 性能：抓详情走并发（采集站网络往返是瓶颈），落库走串行
    /// （EF Core 的 DbContext 不是线程安全的，并发写会抛异常）。
    /// </summary>
    public class SourceBootstrapper
    {
        private readonly IAdapterFactory _adapters;
        private readonly AppDbContext _db;
        private readonly AppleCmsOptions _options;
        private readonly ILogger<SourceBootstrapper> _logger;

        public SourceBootstrapper(
            IAdapterFactory adapters,
            AppDbContext db,
            AppleCmsOptions options,
            ILogger<SourceBootstrapper> logger)
        {
            _adapters = adapters;
            _db = db;
            _options = options;
            _logger = logger;
        }

        /// <summary>用关键词播种指定平台，返回入库短剧数</summary>
        public async Task<int> BootstrapAsync(string platformCode, CancellationToken ct = default)
        {
            var adapter = _adapters.Get(platformCode);
            if (adapter is null) return 0;

            var keywords = _options.BootstrapKeywords
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Distinct()
                .ToList();

            if (keywords.Count == 0) return 0;

            // ===== 1) 收集候选 =====
            // 优先走全量目录分页（适用于标题中英双语、关键词命中率低的站点）；
            // 否则按关键词搜索；再不行回退到榜单/分类页。
            var candidates = new ConcurrentDictionary<string, PlatformSearchItem>(StringComparer.Ordinal);

            if (_options.FullCatalogSync && adapter is IPagedCatalogAdapter paged)
            {
                await CollectFromCatalogAsync(adapter, paged, candidates);
            }
            else
            {
                await CollectFromKeywordsAsync(adapter, keywords, candidates);

                if (candidates.IsEmpty)
                {
                    _logger.LogInformation("平台 {Platform} 关键词搜索无结果，回退到榜单/分类页拉取目录",
                        adapter.PlatformName);
                    await FallbackToCatalogAsync(adapter, candidates);
                }
            }

            if (candidates.IsEmpty)
            {
                _logger.LogInformation("平台 {Platform} 播种无结果", adapter.PlatformName);
                return 0;
            }

            // 全量目录模式下不设 80 部的小上限，按目录实际规模入库
            var cap = _options.FullCatalogSync
                ? Math.Max(_options.BootstrapMaxPerSource, _options.MaxCatalogPages * 20)
                : _options.BootstrapMaxPerSource;

            var queue = candidates.Values.Take(Math.Max(1, cap)).ToList();

            // ===== 目录快速入库 =====
            // 全量目录模式下，先只把目录元数据落库（几百部只需几十个请求），
            // 分集留到用户真正打开详情页时再懒加载（见 DramaService.EnsureEpisodesAsync）。
            if (_options.CatalogOnlySync)
            {
                var catalogSaved = await PersistCatalogOnlyAsync(platformCode, queue, ct);

                var sourceRow = await _db.PlatformSources.FirstOrDefaultAsync(s => s.PlatformCode == platformCode, ct);
                if (sourceRow is not null) sourceRow.LastSyncAt = DateTime.UtcNow;

                await _db.SaveChangesAsync(ct);
                _logger.LogInformation("平台 {Platform} 目录入库完成：{Saved} 部（分集将在访问详情页时懒加载）",
                    adapter.PlatformName, catalogSaved);

                return catalogSaved;
            }

            // ===== 并发抓详情（纯网络，无数据库访问）=====
            var details = new ConcurrentBag<PlatformDramaDetail>();
            using var gate = new SemaphoreSlim(Math.Clamp(_options.BootstrapConcurrency, 1, 16));

            var detailTasks = queue.Select(async item =>
            {
                await gate.WaitAsync(ct);
                try
                {
                    var detail = await adapter.GetDramaDetailAsync(item.PlatformDramaId);
                    if (detail is not null && detail.Episodes.Count > 0)
                    {
                        details.Add(detail);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("抓取 {Platform}/{DramaId} 详情失败（{Reason}）",
                        platformCode, item.PlatformDramaId, ex.GetBaseException().Message);
                }
                finally
                {
                    gate.Release();
                }
            });

            await Task.WhenAll(detailTasks);

            // ===== 3) 串行落库 =====
            var saved = 0;
            foreach (var detail in details)
            {
                try
                {
                    await PersistAsync(platformCode, detail, ct);
                    saved++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("入库 {Platform}/{DramaId} 失败（{Reason}）",
                        platformCode, detail.PlatformDramaId, ex.GetBaseException().Message);
                }
            }

            var source = await _db.PlatformSources.FirstOrDefaultAsync(s => s.PlatformCode == platformCode, ct);
            if (source is not null) source.LastSyncAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("平台 {Platform} 播种完成：候选 {Candidates} 部，抓到详情 {Details} 部，入库 {Saved} 部",
                adapter.PlatformName, queue.Count, details.Count, saved);

            return saved;
        }

        /// <summary>按关键词搜索收集候选</summary>
        private async Task CollectFromKeywordsAsync(
            IPlatformAdapter adapter,
            List<string> keywords,
            ConcurrentDictionary<string, PlatformSearchItem> candidates)
        {
            var searchTasks = keywords.Select(async keyword =>
            {
                try
                {
                    var result = await adapter.SearchAsync(keyword, 1, _options.BootstrapLimitPerKeyword);
                    foreach (var item in result.Items)
                    {
                        if (!string.IsNullOrWhiteSpace(item.PlatformDramaId))
                        {
                            candidates.TryAdd(item.PlatformDramaId, item);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("播种关键词 \"{Keyword}\" 失败（{Platform}，{Reason}）",
                        keyword, adapter.PlatformName, ex.GetBaseException().Message);
                }
            });

            await Task.WhenAll(searchTasks);
        }

        /// <summary>
        /// 逐页遍历全量目录。
        /// 适用于标题中英双语、中文关键词命中率低的站点（如黄果短剧 513 部）。
        /// </summary>
        private async Task CollectFromCatalogAsync(
            IPlatformAdapter adapter,
            IPagedCatalogAdapter paged,
            ConcurrentDictionary<string, PlatformSearchItem> candidates)
        {
            var total = await paged.GetCatalogTotalAsync();
            var pageSize = 20;   // 该类接口的官方上限
            var maxPages = Math.Clamp(_options.MaxCatalogPages, 1, 200);
            var effectivePages = total > 0
                ? Math.Min(maxPages, (int)Math.Ceiling(total / (double)pageSize))
                : maxPages;

            _logger.LogInformation("平台 {Platform} 全量目录遍历开始：总数 {Total}，计划 {Pages} 页",
                adapter.PlatformName, total, effectivePages);

            for (var page = 1; page <= effectivePages; page++)
            {
                try
                {
                    var items = await paged.GetCatalogPageAsync(page, pageSize);
                    if (items.Count == 0) break;

                    foreach (var item in items)
                    {
                        if (string.IsNullOrWhiteSpace(item.PlatformDramaId)) continue;

                        candidates.TryAdd(item.PlatformDramaId, new PlatformSearchItem
                        {
                            PlatformDramaId = item.PlatformDramaId,
                            Title = item.Title,
                            CoverUrl = item.CoverUrl,
                            Category = item.Category,
                            Description = item.Description,
                            TotalEpisodes = item.TotalEpisodes,
                            Rating = item.Rating,
                            Status = item.Status
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("全量目录第 {Page} 页拉取失败（{Platform}，{Reason}）",
                        page, adapter.PlatformName, ex.GetBaseException().Message);
                }
            }

            _logger.LogInformation("平台 {Platform} 全量目录遍历完成，收集候选 {Count} 部",
                adapter.PlatformName, candidates.Count);
        }

        /// <summary>
        /// 搜索不可用时的兜底：从榜单与分类页拉取目录。
        /// 适用于只提供目录浏览、不提供搜索接口的站点（如红果官网）。
        /// </summary>
        private async Task FallbackToCatalogAsync(
            IPlatformAdapter adapter,
            ConcurrentDictionary<string, PlatformSearchItem> candidates)
        {
            var targets = new List<(string Kind, string Key)>
            {
                ("rank", "hot"),
                ("rank", "recommend"),
                ("latest", "全部")
            };

            foreach (var (kind, key) in targets)
            {
                try
                {
                    if (kind == "rank")
                    {
                        var rank = await adapter.GetRankAsync(key, _options.BootstrapMaxPerSource);
                        foreach (var item in rank)
                        {
                            if (!string.IsNullOrWhiteSpace(item.PlatformDramaId))
                            {
                                candidates.TryAdd(item.PlatformDramaId, new PlatformSearchItem
                                {
                                    PlatformDramaId = item.PlatformDramaId,
                                    Title = item.Title,
                                    CoverUrl = item.CoverUrl,
                                    Category = item.Category,
                                    Status = "ongoing"
                                });
                            }
                        }
                    }
                    else
                    {
                        var latest = await adapter.GetLatestAsync(key, _options.BootstrapMaxPerSource);
                        foreach (var item in latest)
                        {
                            if (!string.IsNullOrWhiteSpace(item.PlatformDramaId))
                            {
                                candidates.TryAdd(item.PlatformDramaId, new PlatformSearchItem
                                {
                                    PlatformDramaId = item.PlatformDramaId,
                                    Title = item.Title,
                                    CoverUrl = item.CoverUrl,
                                    Category = item.Category,
                                    Status = "ongoing"
                                });
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("回退拉取 {Kind}/{Key} 失败（{Platform}，{Reason}）",
                        kind, key, adapter.PlatformName, ex.GetBaseException().Message);
                }
            }
        }

        /// <summary>
        /// 只入库目录元数据（不抓分集）。用于全量目录快速同步。
        /// 分集由 DramaService 在用户打开详情页时懒加载补齐。
        /// </summary>
        private async Task<int> PersistCatalogOnlyAsync(
            string platformCode,
            List<PlatformSearchItem> items,
            CancellationToken ct)
        {
            var saved = 0;

            foreach (var item in items)
            {
                try
                {
                    var drama = await _db.Dramas
                        .FirstOrDefaultAsync(d => d.PlatformCode == platformCode && d.PlatformDramaId == item.PlatformDramaId, ct);

                    if (drama is null)
                    {
                        drama = new Drama
                        {
                            PlatformCode = platformCode,
                            PlatformDramaId = item.PlatformDramaId,
                            CreatedAt = DateTime.UtcNow
                        };
                        _db.Dramas.Add(drama);
                    }

                    // 已有分集的剧不要用目录里的粗粒度信息覆盖
                    if (string.IsNullOrWhiteSpace(drama.Title)) drama.Title = item.Title;
                    if (string.IsNullOrWhiteSpace(drama.Description)) drama.Description = item.Description;
                    if (string.IsNullOrWhiteSpace(drama.CoverUrl)) drama.CoverUrl = item.CoverUrl;
                    if (string.IsNullOrWhiteSpace(drama.Category)) drama.Category = item.Category;

                    if (drama.TotalEpisodes == 0 && item.TotalEpisodes > 0)
                    {
                        drama.TotalEpisodes = item.TotalEpisodes;
                    }

                    drama.Status = item.Status;
                    drama.UpdatedAt = DateTime.UtcNow;
                    saved++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("目录入库 {Platform}/{DramaId} 失败（{Reason}）",
                        platformCode, item.PlatformDramaId, ex.GetBaseException().Message);
                }
            }

            return saved;
        }

        /// <summary>把一部短剧及其剧集写入数据库（幂等 upsert）</summary>
        private async Task PersistAsync(string platformCode, PlatformDramaDetail detail, CancellationToken ct)
        {
            var drama = await _db.Dramas
                .Include(d => d.Episodes)
                .FirstOrDefaultAsync(d => d.PlatformCode == platformCode && d.PlatformDramaId == detail.PlatformDramaId, ct);

            if (drama is null)
            {
                drama = new Drama
                {
                    PlatformCode = platformCode,
                    PlatformDramaId = detail.PlatformDramaId,
                    CreatedAt = DateTime.UtcNow
                };
                _db.Dramas.Add(drama);
            }

            drama.Title = detail.Title;
            drama.Description = detail.Description;
            drama.CoverUrl = detail.CoverUrl;
            drama.Category = detail.Category;
            drama.TotalEpisodes = detail.Episodes.Count;
            drama.Status = detail.Status;
            drama.Rating = detail.Rating;
            drama.PlatformPlayUrl = detail.PlatformDramaId;
            drama.UpdatedAt = DateTime.UtcNow;

            // 按集号去重，防止源站重复集号触发 (DramaId, EpisodeNumber) 唯一约束
            var byNumber = drama.Episodes.ToDictionary(e => e.EpisodeNumber);

            foreach (var ep in detail.Episodes)
            {
                if (byNumber.TryGetValue(ep.EpisodeNumber, out var entity))
                {
                    entity.Title = ep.Title;
                    entity.CoverUrl = ep.CoverUrl;
                    entity.DurationSeconds = ep.DurationSeconds;
                    entity.VideoUrl = ep.VideoUrl;
                    entity.IsFree = ep.IsFree;
                    continue;
                }

                entity = new Episode
                {
                    EpisodeNumber = ep.EpisodeNumber,
                    Title = ep.Title,
                    CoverUrl = ep.CoverUrl,
                    DurationSeconds = ep.DurationSeconds,
                    VideoUrl = ep.VideoUrl,
                    IsFree = ep.IsFree,
                    IsLocked = false,
                    CreatedAt = DateTime.UtcNow
                };

                drama.Episodes.Add(entity);
                byNumber[ep.EpisodeNumber] = entity;
            }
        }
    }
}
