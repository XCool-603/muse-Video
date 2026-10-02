using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using ShortDrama.Application.Adapters;

namespace ShortDrama.Infrastructure.Adapters
{
    /// <summary>
    /// 苹果CMS V10 通用采集适配器。
    ///
    /// 苹果CMS 是国内影视站最常用的开源 CMS，其采集接口协议是事实标准，
    /// 接口形如 {api}?ac=detail&amp;wd=关键词 / {api}?ac=detail&amp;ids=ID。
    /// 返回 JSON（或 XML），其中：
    ///   vod_id        短剧 ID
    ///   vod_name      标题
    ///   vod_pic       封面
    ///   vod_blurb     简介
    ///   vod_remarks   更新状态（"已完结" / "更新至第 30 集"）
    ///   vod_score     评分（0-10）
    ///   vod_hits      播放量
    ///   vod_play_from 播放源标识，多源用 $$$ 分隔
    ///   vod_play_url  剧集列表，格式 "第1集$url1#第2集$url2"，多源用 $$$ 分隔
    ///
    /// 只需在配置里填一个 api 地址即可接入一个新源，无需改动任何上层代码。
    /// </summary>
    public class AppleCmsAdapter : IPlatformAdapter, ILiveCatalogAdapter
    {
        private readonly AppleCmsSource _source;
        private readonly AppleCmsOptions _options;
        private readonly HttpClient _http;
        private readonly ILogger _logger;

        /// <summary>详情缓存，避免取播放地址时重复请求</summary>
        private readonly Dictionary<string, PlatformDramaDetail> _detailCache = new();
        private readonly object _cacheLock = new();

        public string PlatformName => _source.PlatformName;
        public string PlatformCode => _source.PlatformCode;

        public AppleCmsAdapter(AppleCmsSource source, AppleCmsOptions options, HttpClient http, ILogger logger)
        {
            _source = source;
            _options = options;
            _http = http;
            _logger = logger;
        }

        // ==================== 搜索 ====================

        public async Task<PlatformSearchResult> SearchAsync(string keyword, int page = 1, int pageSize = 10)
        {
            if (string.IsNullOrWhiteSpace(keyword))
            {
                return new PlatformSearchResult();
            }

            // 每页条数由源站决定（苹果CMS 一般固定 20 条），请求方只能裁剪。
            // 这里必须真的按 pageSize 裁：早先没传 take，导致 BootstrapLimitPerKeyword
            // 这个配置项对苹果CMS 源完全不起作用（写了但不做事）。
            var query = $"ac=detail&wd={Uri.EscapeDataString(keyword)}&pg={Math.Max(1, page)}";
            var items = await FetchListAsync(query, Math.Clamp(pageSize, 1, 100));

            return new PlatformSearchResult
            {
                Items = items.Where(PassesFilter).Select(ToSearchItem).ToList(),
                Total = items.Count
            };
        }

        // ==================== 详情 ====================

        public async Task<PlatformDramaDetail?> GetDramaDetailAsync(string dramaId)
        {
            lock (_cacheLock)
            {
                if (_detailCache.TryGetValue(dramaId, out var cached)) return cached;
            }

            var items = await FetchListAsync($"ac=detail&ids={Uri.EscapeDataString(dramaId)}");
            var raw = items.FirstOrDefault();
            if (raw is null) return null;

            var detail = ToDetail(raw);

            lock (_cacheLock)
            {
                _detailCache[dramaId] = detail;
            }

            return detail;
        }

        // ==================== 播放地址 ====================

        public async Task<string> GetPlayUrlAsync(string dramaId, int episodeNumber)
        {
            var detail = await GetDramaDetailAsync(dramaId);
            var episode = detail?.Episodes.FirstOrDefault(e => e.EpisodeNumber == episodeNumber);

            if (episode is null || string.IsNullOrWhiteSpace(episode.VideoUrl))
            {
                throw new InvalidOperationException($"采集源 {PlatformName} 未找到 {dramaId} 第 {episodeNumber} 集");
            }

            // 采集源提供的即为直链 m3u8，无贴片广告；
            // 仍然返回给上层的播放代理，由 AdFilterService 做分片净化与转发。
            return episode.VideoUrl;
        }

        // ==================== 榜单 ====================

        public async Task<List<PlatformRankItem>> GetRankAsync(string type, int limit = 20)
        {
            // 优先给「短剧维度」的榜：把该源所有短剧分类的目录合并、按播放量排序。
            // 之前用的是 h=9/h=8 全站日榜/周榜，但那拿到的是该源全站内容（综艺/美剧/动漫混杂），
            // 而且实测「t=分类 & h=9」的组合没有任何源支持 —— 全部返回 0 条。
            if (!string.Equals(type, "new", StringComparison.OrdinalIgnoreCase))
            {
                var ranked = await GetShortDramaRankAsync(limit);
                if (ranked.Count > 0) return ranked;
                // 该源没有可用短剧分类（如那批 AV 源）时落到下面的全站榜
            }

            // 「新剧榜」= 该源全站最新；其他类型在没有短剧分类时也退化到这里
            var items = await FetchListAsync("ac=detail&pg=1", limit);

            // 有的源站根本不支持 h 排序参数：实测辣椒资源 h=9 / h=8 都直接返回空列表，
            // 于是热播榜、推荐榜整块是空的。退化成「该源最新」，总比空着强。
            if (items.Count == 0)
            {
                items = await FetchListAsync("ac=detail&pg=1&h=9", limit);
            }

            return items
                .Where(PassesFilter)
                .OrderByDescending(i => GetLong(i, "vod_hits"))
                .Take(limit)
                .Select(i => new PlatformRankItem
                {
                    PlatformDramaId = GetString(i, "vod_id"),
                    Title = GetString(i, "vod_name"),
                    CoverUrl = GetString(i, "vod_pic"),
                    Category = MapCategory(GetString(i, "type_name")),
                    PlayCount = GetLong(i, "vod_hits")
                })
                .ToList();
        }

        /// <summary>
        /// 短剧维度的热榜：该源所有短剧分类的目录合并后按播放量排序。
        ///
        /// 短剧分类的判定：配置的 shortDramaTypeIds 优先；但实测速播/金鹰资源配置里
        /// 指定的 type_id 已失效（返回 0 条），所以查空时退回按分类名匹配（短剧/爽剧/逆袭…）。
        /// 每个分类只取第 1 页 —— 苹果CMS 每页固定 20 条，合并后再排序截断，
        /// 请求次数 = 短剧分类数（3~10 个），串行打源站。
        /// </summary>
        private async Task<List<PlatformRankItem>> GetShortDramaRankAsync(int limit)
        {
            var typeIds = new List<string>();

            foreach (var id in _source.ShortDramaTypeIds)
            {
                typeIds.Add(id.ToString());
            }

            if (typeIds.Count == 0)
            {
                // 没配 shortDramaTypeIds：按分类表的名字找
                var classes = await GetCategoriesInternalAsync();
                typeIds = classes
                    .Where(c => ShortDramaHints.Any(h => c.TypeName.Contains(h, StringComparison.Ordinal)))
                    .Select(c => c.TypeId)
                    .ToList();
            }
            else
            {
                // 配置了：先验证是否还有效，全部失效就退回按名字匹配
                var probe = await FetchListAsync($"ac=detail&t={typeIds[0]}&pg=1", 1);
                if (probe.Count == 0)
                {
                    var classes = await GetCategoriesInternalAsync();
                    var byName = classes
                        .Where(c => ShortDramaHints.Any(h => c.TypeName.Contains(h, StringComparison.Ordinal)))
                        .Select(c => c.TypeId)
                        .ToList();
                    if (byName.Count > 0) typeIds = byName;
                }
            }

            if (typeIds.Count == 0) return new List<PlatformRankItem>();

            // 并行拉、只取前 6 个分类：串行打 10 次 = 实测 9.5 秒，首页等不起；
            // 每类第 1 页 20 条，6 类就有 120 个候选，按播放量排序后足够选 10 条。
            // 并发 6 对单源是可承受的（搜索路径本来就是 25 个源同时打）。
            var tasks = typeIds.Take(6)
                .Select(typeId => FetchListAsync($"ac=detail&t={typeId}&pg=1", limit))
                .ToList();

            var merged = new List<Dictionary<string, JsonElement>>();
            foreach (var page in await Task.WhenAll(tasks))
            {
                merged.AddRange(page);
            }

            return merged
                .Where(PassesFilter)
                .GroupBy(i => GetString(i, "vod_id"))
                .Select(g => g.First())
                .OrderByDescending(i => GetLong(i, "vod_hits"))
                .Take(limit)
                .Select(i => new PlatformRankItem
                {
                    PlatformDramaId = GetString(i, "vod_id"),
                    Title = GetString(i, "vod_name"),
                    CoverUrl = GetString(i, "vod_pic"),
                    Category = MapCategory(GetString(i, "type_name")),
                    PlayCount = GetLong(i, "vod_hits")
                })
                .ToList();
        }

        /// <summary>判定「短剧相关分类」用的词。与前端分类排序用的是同一套语义</summary>
        private static readonly string[] ShortDramaHints =
        {
            "短剧", "爽剧", "逆袭", "重生", "霸总", "穿越", "甜宠", "闪婚", "漫剧"
        };

        /// <summary>取源站分类表（不走缓存，调用方决定是否缓存）</summary>
        private async Task<List<PlatformCategoryItem>> GetCategoriesInternalAsync()
        {
            var envelope = await FetchEnvelopeAsync("ac=list", 0, default);
            return envelope.Classes;
        }

        // ==================== 上新 ====================

        public async Task<List<PlatformNewItem>> GetLatestAsync(string category, int limit = 20)
        {
            var items = new List<Dictionary<string, JsonElement>>();

            if (_source.ShortDramaTypeIds.Count > 0)
            {
                // 配置了短剧分类：逐个分类拉取。
                // 注意不同采集站对分类参数的实现不一致：多数用 t，少数用 type_id，也有直接忽略的。
                // 因此这里逐个尝试，任一方式拿到数据即止。
                foreach (var typeId in _source.ShortDramaTypeIds)
                {
                    var batch = await FetchListAsync($"ac=detail&pg=1&t={typeId}", limit);
                    if (batch.Count == 0)
                    {
                        batch = await FetchListAsync($"ac=detail&pg=1&type_id={typeId}", limit);
                    }

                    items.AddRange(batch);
                    if (items.Count >= limit) break;
                }
            }
            else
            {
                items = await FetchListAsync("ac=detail&pg=1", limit);
            }

            // 分类过滤完全不被支持时，退化为全站最新（由上层按关键词播种兜底）
            if (items.Count == 0)
            {
                items = await FetchListAsync("ac=detail&pg=1", limit);
            }

            return items
                .Where(PassesFilter)
                .GroupBy(i => GetString(i, "vod_id"))
                .Select(g => g.First())
                .Take(limit)
                .Select(i => new PlatformNewItem
                {
                    PlatformDramaId = GetString(i, "vod_id"),
                    Title = GetString(i, "vod_name"),
                    CoverUrl = GetString(i, "vod_pic"),
                    Category = MapCategory(GetString(i, "type_name"))
                })
                .ToList();
        }

        // ==================== 源站目录直读（ILiveCatalogAdapter）====================

        /// <summary>
        /// 源站自己的分类表。苹果CMS 的 ac=list 会在 class 字段里返回全部分类，
        /// 这是「按平台浏览」时分类的来源 —— 不再依赖本地库统计。
        /// </summary>
        public async Task<List<PlatformCategoryItem>> GetCategoriesAsync(CancellationToken ct = default)
        {
            var envelope = await FetchEnvelopeAsync("ac=list", 0, ct);
            return envelope.Classes;
        }

        /// <summary>
        /// 按源站分类翻页取目录。typeId 为空 = 该源的全站目录。
        /// 过滤规则与搜索/榜单/上新一致（成人内容开关对目录同样生效）。
        /// </summary>
        public async Task<PlatformCatalogPage> GetCatalogPageAsync(
            string? typeId, int page, int pageSize, CancellationToken ct = default)
        {
            page = Math.Max(1, page);
            var query = string.IsNullOrWhiteSpace(typeId)
                ? $"ac=detail&pg={page}"
                : $"ac=detail&t={Uri.EscapeDataString(typeId)}&pg={page}";

            var envelope = await FetchEnvelopeAsync(query, Math.Clamp(pageSize, 1, 100), ct);

            return new PlatformCatalogPage
            {
                Total = envelope.Total,
                Items = envelope.Items
                    .Where(PassesFilter)
                    .GroupBy(i => GetString(i, "vod_id"))
                    .Select(g => g.First())
                    .Select(i => new PlatformNewItem
                    {
                        PlatformDramaId = GetString(i, "vod_id"),
                        Title = GetString(i, "vod_name"),
                        CoverUrl = GetString(i, "vod_pic"),
                        Category = MapCategory(GetString(i, "type_name")),
                        Description = FirstNonEmpty(GetString(i, "vod_blurb"), GetString(i, "vod_content")),
                        Status = GetString(i, "vod_remarks")
                    })
                    .ToList()
            };
        }

        // ==================== 内部实现 ====================

        /// <summary>接口返回的信封：list（条目）、total（总数）、class（分类表）</summary>
        private sealed class CmsEnvelope
        {
            public List<Dictionary<string, JsonElement>> Items { get; } = new();
            public int Total { get; set; }
            public List<PlatformCategoryItem> Classes { get; } = new();
        }

        /// <summary>拉取列表；失败返回空集合，绝不抛出（聚合搜索不能因单源失败而整体失败）</summary>
        private async Task<List<Dictionary<string, JsonElement>>> FetchListAsync(string query, int take = 0)
        {
            var envelope = await FetchEnvelopeAsync(query, take, default);
            return envelope.Items;
        }

        /// <summary>
        /// 取接口原始信封。比 FetchListAsync 多带出 total 与 class ——
        /// 「按平台浏览」需要总数（分页）和分类表（筛选项）。
        /// </summary>
        private async Task<CmsEnvelope> FetchEnvelopeAsync(string query, int take, CancellationToken ct)
        {
            var envelope = new CmsEnvelope();
            var url = BuildUrl(query);
            try
            {
                using var response = await _http.GetAsync(url, ct);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("采集源 {Platform} 返回 {Status}: {Url}", PlatformName, (int)response.StatusCode, url);
                    return envelope;
                }

                var body = await response.Content.ReadAsStringAsync(ct);

                if (_source.Format.Equals("xml", StringComparison.OrdinalIgnoreCase))
                {
                    envelope.Items.AddRange(ParseXml(body));
                    return envelope;
                }

                if (string.IsNullOrWhiteSpace(body) || body.TrimStart().StartsWith('<'))
                {
                    _logger.LogDebug("采集源 {Platform} 返回非 JSON 内容: {Url}", PlatformName, url);
                    return envelope;
                }

                using var doc = JsonDocument.Parse(body);

                if (doc.RootElement.TryGetProperty("list", out var list) && list.ValueKind == JsonValueKind.Array)
                {
                    foreach (var element in list.EnumerateArray())
                    {
                        if (element.ValueKind != JsonValueKind.Object) continue;
                        if (take > 0 && envelope.Items.Count >= take) break;

                        var dict = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
                        foreach (var prop in element.EnumerateObject())
                        {
                            dict[prop.Name] = prop.Value.Clone();
                        }
                        envelope.Items.Add(dict);
                    }
                }

                if (doc.RootElement.TryGetProperty("total", out var total) &&
                    total.ValueKind == JsonValueKind.Number && total.TryGetInt32(out var totalValue))
                {
                    envelope.Total = totalValue;
                }

                if (doc.RootElement.TryGetProperty("class", out var classes) && classes.ValueKind == JsonValueKind.Array)
                {
                    foreach (var element in classes.EnumerateArray())
                    {
                        if (element.ValueKind != JsonValueKind.Object) continue;

                        var id = element.TryGetProperty("type_id", out var tid) ? tid.ToString() : string.Empty;
                        var name = element.TryGetProperty("type_name", out var tname) ? tname.ToString() : string.Empty;
                        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name)) continue;

                        envelope.Classes.Add(new PlatformCategoryItem { TypeId = id, TypeName = name });
                    }
                }

                return envelope;
            }
            catch (Exception ex)
            {
                // 采集源不稳定是常态（超时/断连/被限流），这里只记一行摘要，
                // 不打完整堆栈，否则日志会被单个坏源淹没。
                _logger.LogWarning("采集源 {Platform} 请求失败（{Reason}）: {Url}",
                    PlatformName, ex.GetBaseException().Message, url);
                return envelope;
            }
        }

        private string BuildUrl(string query)
        {
            var api = _source.Api;
            var separator = api.Contains('?') ? '&' : '?';
            return $"{api}{separator}{query}";
        }

        /// <summary>兼容 /at/xml/ 接口格式</summary>
        private List<Dictionary<string, JsonElement>> ParseXml(string body)
        {
            var result = new List<Dictionary<string, JsonElement>>();
            if (string.IsNullOrWhiteSpace(body)) return result;

            try
            {
                var doc = XDocument.Parse(body);
                foreach (var video in doc.Descendants("video"))
                {
                    var dict = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
                    foreach (var node in video.Elements())
                    {
                        using var jsonDoc = JsonDocument.Parse(JsonSerializer.Serialize(node.Value));
                        dict[node.Name.LocalName] = jsonDoc.RootElement.Clone();
                    }
                    result.Add(dict);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "采集源 {Platform} XML 解析失败", PlatformName);
            }

            return result;
        }

        private PlatformSearchItem ToSearchItem(Dictionary<string, JsonElement> raw) => new()
        {
            PlatformDramaId = GetString(raw, "vod_id"),
            Title = GetString(raw, "vod_name"),
            Description = FirstNonEmpty(GetString(raw, "vod_blurb"), GetString(raw, "vod_content")),
            CoverUrl = GetString(raw, "vod_pic"),
            Category = MapCategory(GetString(raw, "type_name")),
            TotalEpisodes = ParseEpisodes(raw).Count,
            Status = ParseStatus(GetString(raw, "vod_remarks")),
            Rating = GetDouble(raw, "vod_score")
        };

        private PlatformDramaDetail ToDetail(Dictionary<string, JsonElement> raw) => new()
        {
            PlatformDramaId = GetString(raw, "vod_id"),
            Title = GetString(raw, "vod_name"),
            Description = FirstNonEmpty(GetString(raw, "vod_blurb"), GetString(raw, "vod_content")),
            CoverUrl = GetString(raw, "vod_pic"),
            Category = MapCategory(GetString(raw, "type_name")),
            TotalEpisodes = ParseEpisodes(raw).Count,
            Status = ParseStatus(GetString(raw, "vod_remarks")),
            Rating = GetDouble(raw, "vod_score"),
            Episodes = ParseEpisodes(raw)
                .Select(e => new PlatformEpisodeItem
                {
                    EpisodeNumber = e.Number,
                    Title = e.Title,
                    CoverUrl = GetString(raw, "vod_pic"),
                    DurationSeconds = 120,
                    VideoUrl = e.Url,
                    IsFree = true
                })
                .ToList()
        };

        /// <summary>解析 vod_play_url：多播放源用 $$$ 分隔，剧集用 # 分隔，集名与地址用 $ 分隔</summary>
        private static List<(int Number, string Title, string Url)> ParseEpisodes(Dictionary<string, JsonElement> raw)
        {
            var result = new List<(int, string, string)>();
            var playUrl = GetString(raw, "vod_play_url");
            if (string.IsNullOrWhiteSpace(playUrl)) return result;

            // 多播放源：优先取第一个包含 m3u8 的源
            var groups = playUrl.Split("$$$", StringSplitOptions.RemoveEmptyEntries);
            var chosen = groups.FirstOrDefault(g => g.Contains(".m3u8", StringComparison.OrdinalIgnoreCase))
                         ?? groups.FirstOrDefault();

            if (string.IsNullOrWhiteSpace(chosen)) return result;

            var seenNumbers = new HashSet<int>();
            var index = 0;
            foreach (var entry in chosen.Split('#', StringSplitOptions.RemoveEmptyEntries))
            {
                var separator = entry.LastIndexOf('$');
                if (separator <= 0 || separator == entry.Length - 1) continue;

                var title = entry[..separator].Trim();
                var url = entry[(separator + 1)..].Trim();

                if (string.IsNullOrWhiteSpace(url)) continue;

                index++;
                var number = ExtractEpisodeNumber(title, index);

                // 部分采集源的标题不含集号，或存在重复/跳号集号；
                // 这里强制保证集号唯一，否则写入 Episodes 时会触发 (DramaId, EpisodeNumber) 唯一约束冲突。
                // 注意：必须是「已被占用就递增」而不是「跳到 Count+1」——
                // 后者在集号集合稀疏时（例如只含 2 和 3）会原地打转，形成死循环。
                while (!seenNumbers.Add(number))
                {
                    number++;
                }

                result.Add((number, string.IsNullOrWhiteSpace(title) ? $"第 {number} 集" : title, url));
            }

            return result.OrderBy(e => e.Item1).ToList();
        }

        /// <summary>从「第12集」这类标题里提取集号，失败则用顺序号</summary>
        private static int ExtractEpisodeNumber(string title, int fallback)
        {
            var match = System.Text.RegularExpressions.Regex.Match(title, @"\d+");
            if (match.Success && int.TryParse(match.Value, out var number) && number > 0)
            {
                return number;
            }
            return fallback;
        }

        private static string ParseStatus(string remarks)
        {
            if (string.IsNullOrWhiteSpace(remarks)) return "ongoing";
            if (remarks.Contains('完')) return "completed";
            return "ongoing";
        }

        /// <summary>
        /// 成人内容过滤：标题或简介命中任一关键词即剔除。
        /// 默认开启（<see cref="AppleCmsOptions.ExcludeAdult"/>），可在配置里关掉；
        /// 与黄果适配器的 ExcludeAdult 是同一套思路。
        /// </summary>
        private bool PassesFilter(string title, string description)
        {
            if (!_options.ExcludeAdult) return true;

            var keywords = _options.AdultKeywords;
            if (keywords is null || keywords.Count == 0) return true;

            foreach (var keyword in keywords)
            {
                if (string.IsNullOrWhiteSpace(keyword)) continue;

                if (title.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                    description.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>按原始条目过滤（取 vod_name / vod_blurb / vod_content）</summary>
        private bool PassesFilter(Dictionary<string, JsonElement> raw)
            => PassesFilter(
                GetString(raw, "vod_name"),
                FirstNonEmpty(GetString(raw, "vod_blurb"), GetString(raw, "vod_content")));

        /// <summary>把采集源的分类名归一到站内分类</summary>
        private static string MapCategory(string typeName)        {
            if (string.IsNullOrWhiteSpace(typeName)) return "剧情";

            if (typeName.Contains('短')) return "短剧";

            foreach (var known in new[] { "霸总", "穿越", "重生", "种田", "悬疑", "古装", "都市", "甜宠", "逆袭", "战神" })
            {
                if (typeName.Contains(known)) return known;
            }

            return typeName.Length <= 6 ? typeName : "剧情";
        }

        private static string GetString(Dictionary<string, JsonElement> raw, string key)
        {
            if (!raw.TryGetValue(key, out var value)) return string.Empty;

            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString() ?? string.Empty,
                JsonValueKind.Number => value.ToString(),
                _ => string.Empty
            };
        }

        private static long GetLong(Dictionary<string, JsonElement> raw, string key)
        {
            var text = GetString(raw, key);
            return long.TryParse(text, out var value) ? value : 0;
        }

        private static double GetDouble(Dictionary<string, JsonElement> raw, string key)
        {
            var text = GetString(raw, key);
            return double.TryParse(text, out var value) ? value : 0;
        }

        private static string FirstNonEmpty(params string[] values)
            => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;
    }
}
