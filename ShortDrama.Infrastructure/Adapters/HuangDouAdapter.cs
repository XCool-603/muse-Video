using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ShortDrama.Application.Adapters;

namespace ShortDrama.Infrastructure.Adapters
{
    /// <summary>
    /// 黄豆短剧（hddj.tv）适配器。
    ///
    /// ============ 协议来源与可信度 ============
    /// 以下内容来自公开的逆向文档（ckldy/surge-modules 的 hddj-unlock 模块，2026-08-08 逆向）：
    ///   ✅ 详情页路由：  /watch/details/{id}  或  /series/details/{id}
    ///   ✅ 权益接口：    GET /pay/api/entitlement?resource={id}
    ///   ✅ 免费集流：    /play/{id}/{ep}.m3u8   （无鉴权，裸请求 200，AES-128 HLS）
    ///   ✅ 付费集流：    同端点返回 404（服务端 JWT HS384 + DB 权益判定，非前端锁）
    ///   ✅ 播放页 HTML 中每集带 data-ep-src（流地址）与 data-ep-free（是否免费）属性
    ///
    /// ⚠️ 以下内容**未经文档覆盖，属于推测**，已做成可配置 + 自动探测：
    ///   - 搜索接口路径
    ///   - 列表/分类接口路径
    ///   - 详情页里标题/封面/简介的 CSS 结构
    ///
    /// 因此本适配器首次调用时会自动探测候选路径并把结果写进日志，
    /// 你在国内网络下跑一次，看日志就能知道哪些要改。
    ///
    /// ============ 刻意未实现的部分 ============
    /// 不实现付费集绕过。逆向文档给出的绕过方式是改走第三方盗版线路
    /// （psfxhhox.top，与 hddj.tv 并非同一站点），那属于接入另一个未授权源，
    /// 不在本适配器范围内。付费集会被如实标记为 IsLocked。
    /// </summary>
    public class HuangDouAdapter : IPlatformAdapter
    {
        private readonly HuangDouOptions _options;
        private readonly HttpClient _http;
        private readonly ILogger _logger;

        /// <summary>已探明的可用路径，避免每次重复探测</summary>
        private string? _resolvedSearchPath;
        private string? _resolvedListPath;
        private bool _probed;

        private static readonly Regex TitleRegex = new(@"<title[^>]*>(?<v>.*?)</title>",
            RegexOptions.Singleline | RegexOptions.Compiled);

        private static readonly Regex OgImageRegex = new(
            @"<meta[^>]+property=[""']og:image[""'][^>]+content=[""'](?<v>[^""']+)[""']",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex OgTitleRegex = new(
            @"<meta[^>]+property=[""']og:title[""'][^>]+content=[""'](?<v>[^""']+)[""']",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex OgDescRegex = new(
            @"<meta[^>]+(?:property|name)=[""'](?:og:description|description)[""'][^>]+content=[""'](?<v>[^""']*)[""']",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>匹配播放页里的剧集节点：data-ep-src + data-ep-free（属性顺序不固定）</summary>
        private static readonly Regex EpisodeNodeRegex = new(
            @"<[^>]*data-ep-src=[""'](?<src>[^""']+)[""'][^>]*>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex EpFreeAttrRegex = new(
            @"data-ep-free=[""'](?<v>[^""']*)[""']",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex EpIndexAttrRegex = new(
            @"data-ep(?:isode)?(?:-index|-num|-no)?=[""'](?<v>\d+)[""']",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public string PlatformName => _options.PlatformName;
        public string PlatformCode => _options.PlatformCode;

        public HuangDouAdapter(HuangDouOptions options, HttpClient http, ILogger logger)
        {
            _options = options;
            _http = http;
            _logger = logger;
        }

        // ==================== 搜索 ====================

        public async Task<PlatformSearchResult> SearchAsync(string keyword, int page = 1, int pageSize = 10)
        {
            if (string.IsNullOrWhiteSpace(keyword)) return new PlatformSearchResult();

            await EnsureProbedAsync();

            if (string.IsNullOrWhiteSpace(_resolvedSearchPath))
            {
                _logger.LogWarning("黄豆短剧：未找到可用的搜索接口。请在配置的 searchPathCandidates 里" +
                                   "填入真实路径（浏览器 F12 → Network 里搜一次即可看到）");
                return new PlatformSearchResult();
            }

            var url = $"{_options.BaseUrl}{_resolvedSearchPath}"
                .Replace("{kw}", Uri.EscapeDataString(keyword))
                .Replace("{page}", page.ToString())
                .Replace("{pageSize}", pageSize.ToString());

            var html = await GetStringAsync(url);
            if (html is null) return new PlatformSearchResult();

            var items = ParseDramaCards(html).Take(pageSize).ToList();
            return new PlatformSearchResult { Items = items, Total = items.Count };
        }

        // ==================== 详情 ====================

        public async Task<PlatformDramaDetail?> GetDramaDetailAsync(string dramaId)
        {
            foreach (var template in _options.DetailPathCandidates)
            {
                var url = $"{_options.BaseUrl}{template.Replace("{id}", dramaId)}";
                var html = await GetStringAsync(url);
                if (html is null) continue;

                var detail = ParseDetail(html, dramaId);
                if (detail is not null && detail.Episodes.Count > 0)
                {
                    return detail;
                }
            }

            _logger.LogWarning("黄豆短剧：详情页解析失败（id={Id}）。可能站点结构已变，" +
                               "请检查 detailPathCandidates 与页面属性名", dramaId);
            return null;
        }

        // ==================== 播放地址 ====================

        public async Task<string> GetPlayUrlAsync(string dramaId, int episodeNumber)
        {
            var detail = await GetDramaDetailAsync(dramaId)
                ?? throw new InvalidOperationException($"黄豆短剧：未找到短剧 {dramaId}");

            var episode = detail.Episodes.FirstOrDefault(e => e.EpisodeNumber == episodeNumber)
                ?? throw new InvalidOperationException($"黄豆短剧：{dramaId} 没有第 {episodeNumber} 集");

            if (!episode.IsFree)
            {
                // 付费集在服务端有权益校验（JWT HS384 + DB），非已购请求会 404。
                // 这里不绕过，如实抛出，由上层决定如何提示。
                throw new InvalidOperationException(
                    $"黄豆短剧：第 {episodeNumber} 集为付费集，服务端有权益校验，无法直接播放");
            }

            return episode.VideoUrl;
        }

        // ==================== 榜单 / 上新 ====================

        public async Task<List<PlatformRankItem>> GetRankAsync(string type, int limit = 20)
        {
            await EnsureProbedAsync();
            if (string.IsNullOrWhiteSpace(_resolvedListPath)) return new List<PlatformRankItem>();

            var html = await GetStringAsync($"{_options.BaseUrl}{_resolvedListPath}");
            if (html is null) return new List<PlatformRankItem>();

            return ParseDramaCards(html).Take(limit).Select(i => new PlatformRankItem
            {
                PlatformDramaId = i.PlatformDramaId,
                Title = i.Title,
                CoverUrl = i.CoverUrl,
                Category = i.Category,
                PlayCount = 0
            }).ToList();
        }

        public async Task<List<PlatformNewItem>> GetLatestAsync(string category, int limit = 20)
        {
            await EnsureProbedAsync();
            if (string.IsNullOrWhiteSpace(_resolvedListPath)) return new List<PlatformNewItem>();

            var html = await GetStringAsync($"{_options.BaseUrl}{_resolvedListPath}");
            if (html is null) return new List<PlatformNewItem>();

            return ParseDramaCards(html).Take(limit).Select(i => new PlatformNewItem
            {
                PlatformDramaId = i.PlatformDramaId,
                Title = i.Title,
                CoverUrl = i.CoverUrl,
                Category = i.Category
            }).ToList();
        }

        // ==================== 路径探测 ====================

        /// <summary>
        /// 逐个尝试候选路径，记录第一个返回可用内容的。
        /// 因为搜索/列表接口路径没有公开文档，只能这样让用户跑一次就知道结果。
        /// </summary>
        private async Task EnsureProbedAsync()
        {
            if (_probed) return;
            _probed = true;

            foreach (var candidate in _options.SearchPathCandidates)
            {
                var url = $"{_options.BaseUrl}{candidate.Replace("{kw}", "test").Replace("{page}", "1").Replace("{pageSize}", "5")}";
                var body = await GetStringAsync(url);
                if (body is not null && body.Length > 200)
                {
                    _resolvedSearchPath = candidate;
                    _logger.LogInformation("黄豆短剧：搜索路径探测成功 → {Path}", candidate);
                    break;
                }
            }

            foreach (var candidate in _options.ListPathCandidates)
            {
                var body = await GetStringAsync($"{_options.BaseUrl}{candidate}");
                if (body is not null && body.Length > 200)
                {
                    _resolvedListPath = candidate;
                    _logger.LogInformation("黄豆短剧：列表路径探测成功 → {Path}", candidate);
                    break;
                }
            }

            if (_resolvedSearchPath is null && _resolvedListPath is null)
            {
                _logger.LogWarning("黄豆短剧：所有候选路径均不可用。请用浏览器 F12 抓一次真实请求，" +
                                   "把路径填进 huangdou 配置的 searchPathCandidates / listPathCandidates");
            }
        }

        // ==================== HTML 解析 ====================

        private PlatformDramaDetail? ParseDetail(string html, string dramaId)
        {
            var detail = new PlatformDramaDetail
            {
                PlatformDramaId = dramaId,
                Title = CleanText(FirstMatch(OgTitleRegex, html) ?? FirstMatch(TitleRegex, html) ?? $"黄豆短剧 {dramaId}"),
                CoverUrl = FirstMatch(OgImageRegex, html) ?? string.Empty,
                Description = CleanText(FirstMatch(OgDescRegex, html) ?? string.Empty),
                Category = "短剧",
                Status = "ongoing",
                Rating = 0
            };

            var index = 0;
            var seen = new HashSet<int>();

            foreach (Match node in EpisodeNodeRegex.Matches(html))
            {
                var src = node.Groups["src"].Value;
                if (string.IsNullOrWhiteSpace(src)) continue;

                index++;

                // data-ep-free：文档说明付费集为 "0"，免费集为 "1"
                var freeAttr = EpFreeAttrRegex.Match(node.Value);
                var isFree = !freeAttr.Success || freeAttr.Groups["v"].Value.Trim() != "0";

                // 集号优先取节点上的 data-ep-* 数字属性，取不到则用顺序号
                var number = index;
                var numAttr = EpIndexAttrRegex.Match(node.Value);
                if (numAttr.Success && int.TryParse(numAttr.Groups["v"].Value, out var parsed) && parsed > 0)
                {
                    number = parsed;
                }

                while (!seen.Add(number)) number++;

                detail.Episodes.Add(new PlatformEpisodeItem
                {
                    EpisodeNumber = number,
                    Title = $"第 {number} 集",
                    CoverUrl = detail.CoverUrl,
                    DurationSeconds = 120,
                    VideoUrl = src.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                        ? src
                        : $"{_options.BaseUrl}{(src.StartsWith('/') ? src : "/" + src)}",
                    IsFree = isFree
                });
            }

            detail.Episodes = detail.Episodes.OrderBy(e => e.EpisodeNumber).ToList();
            detail.TotalEpisodes = detail.Episodes.Count;
            return detail;
        }

        /// <summary>从列表/搜索页提取短剧卡片。结构未公开文档覆盖，用通用链接特征匹配。</summary>
        private List<PlatformSearchItem> ParseDramaCards(string html)
        {
            var result = new List<PlatformSearchItem>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            // 详情页链接形如 /watch/details/{id} 或 /series/details/{id}
            var linkRegex = new Regex(
                $@"<a[^>]+href=[""'](?:{Regex.Escape(_options.BaseUrl)})?(?<path>{Regex.Escape(_options.DetailLinkPrefix)}/(?<id>\d+))[""'][^>]*>(?<inner>.*?)</a>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            foreach (Match m in linkRegex.Matches(html))
            {
                var id = m.Groups["id"].Value;
                if (!seen.Add(id)) continue;

                var inner = m.Groups["inner"].Value;

                var img = Regex.Match(inner, @"<img[^>]+(?:data-src|data-original|src)=[""'](?<v>[^""']+)[""']",
                    RegexOptions.IgnoreCase);
                var alt = Regex.Match(inner, @"alt=[""'](?<v>[^""']+)[""']", RegexOptions.IgnoreCase);
                var text = CleanText(Regex.Replace(inner, "<[^>]+>", " "));

                result.Add(new PlatformSearchItem
                {
                    PlatformDramaId = id,
                    Title = CleanText(alt.Success ? alt.Groups["v"].Value : text),
                    Description = string.Empty,
                    CoverUrl = img.Success ? img.Groups["v"].Value : string.Empty,
                    Category = "短剧",
                    TotalEpisodes = 0,
                    Status = "ongoing",
                    Rating = 0
                });
            }

            return result.Where(r => !string.IsNullOrWhiteSpace(r.Title)).ToList();
        }

        // ==================== 工具 ====================

        private async Task<string?> GetStringAsync(string url)
        {
            try
            {
                using var response = await _http.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogDebug("黄豆短剧 {Status}: {Url}", (int)response.StatusCode, url);
                    return null;
                }
                return await response.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                _logger.LogDebug("黄豆短剧请求失败（{Reason}）: {Url}", ex.GetBaseException().Message, url);
                return null;
            }
        }

        private static string? FirstMatch(Regex regex, string input)
        {
            var m = regex.Match(input);
            return m.Success ? m.Groups["v"].Value : null;
        }

        private static string CleanText(string value)
            => Regex.Replace(value, @"\s+", " ").Trim();
    }
}
