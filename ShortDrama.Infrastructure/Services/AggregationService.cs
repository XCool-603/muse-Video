using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
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
        private readonly SourceHealthTracker _health;
        private readonly AggregationOptions _options;
        private readonly IMemoryCache _cache;
        private readonly ILogger<AggregationService> _logger;

        private const double DedupThreshold = 0.85;

        public AggregationService(
            IAdapterFactory adapters,
            AppDbContext db,
            SourceBootstrapper bootstrapper,
            SourceHealthTracker health,
            AggregationOptions options,
            IMemoryCache cache,
            ILogger<AggregationService> logger)
        {
            _adapters = adapters;
            _db = db;
            _bootstrapper = bootstrapper;
            _health = health;
            _options = options;
            _cache = cache;
            _logger = logger;
        }

        public async Task<PagedResult<DramaDto>> SearchAsync(string keyword, int page, int pageSize, string? platformCode = null, CancellationToken ct = default)
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 50);

            // 重复搜索直接命中缓存：翻页、改排序、来回切筛选都会重复打同一组接口
            var cacheKey = $"agg-search:{keyword}|{page}|{pageSize}|{platformCode ?? "all"}";
            if (_options.CacheSeconds > 0 &&
                _cache.TryGetValue(cacheKey, out PagedResult<DramaDto>? cached) && cached is not null)
            {
                return cached;
            }

            var sw = Stopwatch.StartNew();

            var all = _adapters.GetAll();
            var explicitPlatform = !string.IsNullOrWhiteSpace(platformCode) && platformCode != "all";
            if (explicitPlatform)
            {
                all = all.Where(a => a.PlatformCode.Equals(platformCode, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            // 跳过正在冷却的平台（已知失败/超时），不再为它们反复等超时。
            // 显式指定单个平台时不跳过 —— 用户就是要看这个平台，跳过等于什么都不返回。
            var skipped = new List<string>();
            var adapters = new List<Application.Adapters.IPlatformAdapter>();
            foreach (var a in all)
            {
                if (!explicitPlatform && _options.FailureCooldownSeconds > 0 && _health.IsCoolingDown(a.PlatformCode))
                {
                    skipped.Add(a.PlatformCode);
                    continue;
                }
                adapters.Add(a);
            }

            // 安全阀：冷却不能把平台清空。若全部都在冷却中，就忽略冷却照常请求，
            // 否则会出现「所有源都在冷却 → 搜什么都是空的」这种更糟的结果。
            if (adapters.Count == 0 && all.Count > 0)
            {
                _logger.LogWarning("全部 {Count} 个平台都在冷却中，忽略冷却继续请求", all.Count);
                adapters = all.ToList();
                skipped.Clear();
            }

            // 两段式预算（必须在任务之前声明，lambda 要捕获它们）：
            //   软期限 —— 到点且已收到足够多平台的结果就直接返回；
            //   硬上限 —— 无论如何都不再等。
            // 原先 Task.WhenAll 要等所有平台，总耗时等于最慢的那个
            // （实测被几个按地区拒绝的源拖到 10 秒以上）。
            // 采集源实测 P50≈1.4s、P100≈3.5s，所以软期限能覆盖大多数平台，
            // 同时把绝大多数搜索压到 2 秒内。
            var soft = _options.SearchSoftBudgetMs;
            var hard = _options.SearchBudgetMs;
            var minReady = Math.Min(adapters.Count, Math.Max(1, _options.MinPlatformsBeforeReturn));

            // 1) 并行请求各平台（跳过冷却中的）
            var tasks = adapters.Select(async adapter =>
            {
                try
                {
                    var result = await adapter.SearchAsync(keyword, 1, pageSize * 2);

                    // 成功就解除冷却 —— 包括「慢但成功」的。
                    // 慢源不再进冷却：延迟已经由软/硬预算控制住了（到点就返回，不等它），
                    // 再把它冷却掉只会白白丢内容 —— 实测冷却「慢源」会让命中数从 51 掉到 18。
                    // 冷却只留给真正的失败（异常），那种情况重试也是白等。
                    _health.MarkSuccess(adapter.PlatformCode);

                    return (adapter.PlatformCode, Items: result.Items);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "平台 {Platform} 搜索失败，已跳过", adapter.PlatformCode);
                    if (_options.FailureCooldownSeconds > 0)
                    {
                        _health.MarkFailure(adapter.PlatformCode,
                            TimeSpan.FromSeconds(_options.FailureCooldownSeconds), "搜索失败");
                    }
                    return (adapter.PlatformCode, Items: new List<PlatformSearchItem>());
                }
            }).ToList();

            if (hard > 0 && tasks.Count > 0)
            {
                while (true)
                {
                    var done = tasks.Count(t => t.IsCompleted);
                    var elapsed = (int)sw.ElapsedMilliseconds;

                    if (done >= minReady && elapsed >= soft) break;
                    if (elapsed >= hard) break;

                    await Task.Delay(Math.Min(100, Math.Max(1, hard - elapsed)), ct);
                }
            }
            else
            {
                await Task.WhenAll(tasks);
            }

            // 只取已完成的（没等到的结果直接丢弃）
            var platformResults = tasks
                .Where(t => t.IsCompletedSuccessfully)
                .Select(t => t.Result)
                .ToList();

            // 2) 合并
            var merged = new List<DramaDto>();
            foreach (var (code, items) in platformResults)
            {
                merged.AddRange(items.Select(i => Mapper.FromPlatformItem(i, code)));
            }

            // 3) 本地库补充（已同步入库的内容）
            //    注意这里必须跟着 platformCode 过滤：早先漏了，导致按平台筛选时
            //    本地结果会把其他平台的剧一起带出来（筛选形同虚设）。
            if (!string.IsNullOrWhiteSpace(keyword))
            {
                var localQuery = _db.Dramas.AsNoTracking()
                    .Where(d => EF.Functions.Like(d.Title, $"%{keyword}%") || EF.Functions.Like(d.Description, $"%{keyword}%"));

                if (!string.IsNullOrWhiteSpace(platformCode) && platformCode != "all")
                {
                    localQuery = localQuery.Where(d => d.PlatformCode == platformCode);
                }

                var local = await localQuery.Take(pageSize * 2).ToListAsync(ct);
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
            _logger.LogInformation("聚合搜索 \"{Keyword}\" 命中 {Total} 条（{Platforms} 个平台，去重后{Skip}），耗时 {Elapsed}ms",
                keyword, total, adapters.Count,
                skipped.Count > 0 ? $"，跳过冷却中 {skipped.Count} 个" : string.Empty,
                sw.ElapsedMilliseconds);

            var pageResult = PagedResult<DramaDto>.Create(pageItems, total, page, pageSize);

            // 有结果就缓存：翻页、改排序、来回切筛选都会重复打同一组接口，
            // 缓存后这些操作是瞬时的。软期限下拿到的本来就是「已返回平台」的结果，
            // 缓存它不会比用户当下看到的更少；60 秒后自动刷新。
            if (_options.CacheSeconds > 0 && total > 0)
            {
                _cache.Set(cacheKey, pageResult, TimeSpan.FromSeconds(_options.CacheSeconds));
            }

            return pageResult;
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

            // 受预算约束：实测等齐所有平台要 12 秒，而首页挂载时就要用
            var all = (await AwaitWithBudget(tasks, ct, minReadyOverride: 6)).SelectMany(x => x).ToList();

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

            // 受预算约束：实测等齐所有平台要 17 秒，而首页挂载时就要用
            var all = (await AwaitWithBudget(tasks, ct, minReadyOverride: 6)).SelectMany(x => x).ToList();
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
        /// <summary>
        /// 等一批平台任务，但受两段式预算约束：软期限到点且已收到足够多结果就返回，
        /// 硬上限到点无论如何都返回；只有已完成的任务会被计入。
        ///
        /// 榜单与上新原先都是 Task.WhenAll（等所有平台），实测 /drama/rank 要 12 秒、
        /// /drama/latest 要 17 秒 —— 首页挂载时两个都调，用户要等十几秒才看到内容。
        /// 平台之间的差异是「快的一秒内返回、慢的十几秒」，等齐没有任何意义。
        /// </summary>
        private async Task<List<T>> AwaitWithBudget<T>(
            IEnumerable<Task<T>> pending, CancellationToken ct, int? minReadyOverride = null)
        {
            var tasks = pending.ToList();
            if (tasks.Count == 0) return new List<T>();

            var hard = _options.SearchBudgetMs;
            if (hard <= 0)
            {
                await Task.WhenAll(tasks);
                return tasks.Select(t => t.Result).ToList();
            }

            var soft = _options.SearchSoftBudgetMs;
            // 榜单/上新一次只要十几条，没必要等那么多个平台；
            // 门槛放低才能在软期限内收工，否则会一直等到硬上限（实测首页要多等 2 秒）。
            var minReady = Math.Min(tasks.Count, Math.Max(1, minReadyOverride ?? _options.MinPlatformsBeforeReturn));
            var sw = Stopwatch.StartNew();

            while (true)
            {
                var done = tasks.Count(t => t.IsCompleted);
                var elapsed = (int)sw.ElapsedMilliseconds;

                if (done >= minReady && elapsed >= soft) break;
                if (elapsed >= hard) break;

                await Task.Delay(Math.Min(100, Math.Max(1, hard - elapsed)), ct);
            }

            return tasks.Where(t => t.IsCompletedSuccessfully).Select(t => t.Result).ToList();
        }

        private static List<DramaDto> DeduplicateAndMerge(List<DramaDto> items)
        {
            var result = new List<DramaDto>();

            // 性能关键：原先是「对 result 线性扫描 + 逐条算字符串相似度」，即 O(n²)。
            // 25 个平台各 20+ 条会产生十几万次相似度计算，实测占掉搜索耗时的一大半
            // （源本身都在 3.5 秒内返回，应用却又花了 1-2 秒）。
            //
            // 改成两级匹配：
            //   1) 归一化标题完全相同的，用字典 O(1) 命中；
            //   2) 其余只在「同桶」内做模糊比较 —— 桶键取归一化标题的前 2 个字符。
            //      中文短剧标题的近似写法前缀基本一致，同桶足够覆盖绝大多数重复；
            //      跨桶漏掉的近似标题是刻意接受的取舍（换来的是数量级的提速）。
            var exact = new Dictionary<string, DramaDto>(StringComparer.Ordinal);
            var buckets = new Dictionary<string, List<DramaDto>>(StringComparer.Ordinal);

            foreach (var item in items)
            {
                var norm = TextSimilarity.Normalize(item.Title ?? string.Empty);

                DramaDto? match = null;

                if (norm.Length > 0 && exact.TryGetValue(norm, out var same))
                {
                    match = same;
                }
                else if (norm.Length > 0)
                {
                    var key = norm.Length >= 2 ? norm.Substring(0, 2) : norm;
                    if (buckets.TryGetValue(key, out var bucket))
                    {
                        foreach (var candidate in bucket)
                        {
                            if (TextSimilarity.Similarity(candidate.Title, item.Title) >= DedupThreshold)
                            {
                                match = candidate;
                                break;
                            }
                        }
                    }
                }

                if (match is null)
                {
                    result.Add(item);

                    if (norm.Length > 0)
                    {
                        exact[norm] = item;
                        var key = norm.Length >= 2 ? norm.Substring(0, 2) : norm;
                        if (!buckets.TryGetValue(key, out var b)) buckets[key] = b = new List<DramaDto>();
                        b.Add(item);
                    }
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
