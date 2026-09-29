using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShortDrama.Application.Adapters;
using ShortDrama.Application.Common;
using ShortDrama.Application.DTOs;
using ShortDrama.Application.Services;
using ShortDrama.Domain.Entities;
using ShortDrama.Infrastructure.Data;

namespace ShortDrama.Infrastructure.Services
{
    /// <summary>
    /// 聚合服务：并行调度各平台适配器，做结果合并、去重、统一排序与分页。
    /// </summary>
    public class AggregationService : IAggregationService
    {
        private readonly IAdapterFactory _adapters;
        private readonly AppDbContext _db;
        private readonly SourceBootstrapper _bootstrapper;
        private readonly ILogger<AggregationService> _logger;

        private const double DedupThreshold = 0.85;

        public AggregationService(
            IAdapterFactory adapters,
            AppDbContext db,
            SourceBootstrapper bootstrapper,
            ILogger<AggregationService> logger)
        {
            _adapters = adapters;
            _db = db;
            _bootstrapper = bootstrapper;
            _logger = logger;
        }

        public async Task<PagedResult<DramaDto>> SearchAsync(string keyword, int page, int pageSize, string? platformCode = null, CancellationToken ct = default)
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 50);

            var sw = Stopwatch.StartNew();

            var adapters = _adapters.GetAll();
            if (!string.IsNullOrWhiteSpace(platformCode) && platformCode != "all")
            {
                adapters = adapters.Where(a => a.PlatformCode.Equals(platformCode, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            // 1) 并行请求各平台
            var tasks = adapters.Select(async adapter =>
            {
                try
                {
                    var result = await adapter.SearchAsync(keyword, 1, pageSize * 2);
                    return (adapter.PlatformCode, Items: result.Items);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "平台 {Platform} 搜索失败，已跳过", adapter.PlatformCode);
                    return (adapter.PlatformCode, Items: new List<PlatformSearchItem>());
                }
            }).ToList();

            var platformResults = await Task.WhenAll(tasks);

            // 2) 合并
            var merged = new List<DramaDto>();
            foreach (var (code, items) in platformResults)
            {
                merged.AddRange(items.Select(i => Mapper.FromPlatformItem(i, code)));
            }

            // 3) 本地库补充（已同步入库的内容）
            if (!string.IsNullOrWhiteSpace(keyword))
            {
                var local = await _db.Dramas.AsNoTracking()
                    .Where(d => EF.Functions.Like(d.Title, $"%{keyword}%") || EF.Functions.Like(d.Description, $"%{keyword}%"))
                    .Take(pageSize * 2)
                    .ToListAsync(ct);

                merged.AddRange(local.Select(d => Mapper.ToDto(d)));
            }

            // 4) 去重合并（同剧多源合并为一条，Sources 记录所有来源）
            var deduped = DeduplicateAndMerge(merged);

            // 5) 统一排序：相关度优先，其次热度/评分
            var ordered = string.IsNullOrWhiteSpace(keyword)
                ? deduped.OrderByDescending(d => d.Rating).ThenByDescending(d => d.TotalEpisodes).ToList()
                : deduped
                    .OrderByDescending(d => RelevanceScore(d, keyword))
                    .ThenByDescending(d => d.Rating)
                    .ToList();

            var total = ordered.Count;
            var pageItems = ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            sw.Stop();
            _logger.LogInformation("聚合搜索 \"{Keyword}\" 命中 {Total} 条（{Platforms} 个平台，去重后），耗时 {Elapsed}ms",
                keyword, total, adapters.Count, sw.ElapsedMilliseconds);

            return PagedResult<DramaDto>.Create(pageItems, total, page, pageSize);
        }

        public async Task<List<DramaDto>> GetRankAsync(string type, int limit = 20, CancellationToken ct = default)
        {
            var adapters = _adapters.GetAll();

            var tasks = adapters.Select(async adapter =>
            {
                try
                {
                    var items = await adapter.GetRankAsync(type, limit);
                    return items.Select(i => new DramaDto
                    {
                        Title = i.Title,
                        CoverUrl = i.CoverUrl,
                        Category = i.Category,
                        PlatformCode = adapter.PlatformCode,
                        PlatformName = adapter.PlatformName,
                        PlatformDramaId = i.PlatformDramaId,
                        PlayCount = i.PlayCount,
                        Sources = new List<string> { adapter.PlatformCode },
                        UpdatedAt = DateTime.UtcNow
                    }).ToList();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "平台 {Platform} 榜单获取失败", adapter.PlatformCode);
                    return new List<DramaDto>();
                }
            });

            var all = (await Task.WhenAll(tasks)).SelectMany(x => x).ToList();

            return DeduplicateAndMerge(all)
                .OrderByDescending(d => d.PlayCount)
                .Take(limit)
                .ToList();
        }

        public async Task<List<DramaDto>> GetLatestAsync(string category = "全部", int limit = 20, CancellationToken ct = default)
        {
            var adapters = _adapters.GetAll();

            var tasks = adapters.Select(async adapter =>
            {
                try
                {
                    var items = await adapter.GetLatestAsync(category, limit);
                    return items.Select(i => new DramaDto
                    {
                        Title = i.Title,
                        CoverUrl = i.CoverUrl,
                        Category = i.Category,
                        PlatformCode = adapter.PlatformCode,
                        PlatformName = adapter.PlatformName,
                        PlatformDramaId = i.PlatformDramaId,
                        Sources = new List<string> { adapter.PlatformCode },
                        UpdatedAt = DateTime.UtcNow
                    }).ToList();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "平台 {Platform} 上新获取失败", adapter.PlatformCode);
                    return new List<DramaDto>();
                }
            });

            var all = (await Task.WhenAll(tasks)).SelectMany(x => x).ToList();
            return DeduplicateAndMerge(all)
                .GroupBy(d => d.PlatformDramaId)
                .Select(g => g.First())
                .Take(limit)
                .ToList();
        }

        public Task<int> SyncPlatformAsync(string platformCode, CancellationToken ct = default)
        {
            // 用关键词搜索播种（而非各源的「最新上架」），原因见 SourceBootstrapper 注释
            return _bootstrapper.BootstrapAsync(platformCode, ct);
        }

        /// <summary>按标题相似度合并同剧多源，保留信息最完整的一条并汇总来源</summary>
        private static List<DramaDto> DeduplicateAndMerge(List<DramaDto> items)
        {
            var result = new List<DramaDto>();

            foreach (var item in items)
            {
                var match = result.FirstOrDefault(r => TextSimilarity.Similarity(r.Title, item.Title) >= DedupThreshold);

                if (match is null)
                {
                    result.Add(item);
                    continue;
                }

                // 合并来源
                foreach (var src in item.Sources)
                {
                    if (!match.Sources.Contains(src)) match.Sources.Add(src);
                }

                // 补全缺失字段
                if (string.IsNullOrWhiteSpace(match.CoverUrl)) match.CoverUrl = item.CoverUrl;
                if (string.IsNullOrWhiteSpace(match.Description)) match.Description = item.Description;
                if (match.TotalEpisodes == 0) match.TotalEpisodes = item.TotalEpisodes;
                if (match.PlayCount == 0) match.PlayCount = item.PlayCount;
                if (match.Rating == 0) match.Rating = item.Rating;
                if (string.IsNullOrWhiteSpace(match.Category)) match.Category = item.Category;
            }

            return result;
        }

        private static double RelevanceScore(DramaDto d, string keyword)
        {
            var norm = TextSimilarity.Normalize(keyword);
            var title = TextSimilarity.Normalize(d.Title);

            double score = 0;
            if (title == norm) score += 100;
            else if (title.StartsWith(norm, StringComparison.Ordinal)) score += 60;
            else if (title.Contains(norm, StringComparison.Ordinal)) score += 40;
            else if (TextSimilarity.Similarity(title, norm) >= 0.5) score += 20;

            if (d.Description.Contains(keyword, StringComparison.OrdinalIgnoreCase)) score += 5;
            score += d.Rating;
            score += d.Sources.Count * 3; // 多平台可播放的优先

            return score;
        }
    }
}
