using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ShortDrama.Application.Adapters;

namespace ShortDrama.Infrastructure.Adapters
{
    /// <summary>
    /// 红果短剧官网（hongguoduanju.com）适配器。
    ///
    /// ============ 站点性质 ============
    /// 抖音/番茄系官方 H5（React SSR，静态资源托管在字节自有 CDN lf-fe.fqnovelstatic.com，
    /// 页脚举报邮箱为 hongguojubao@bytedance.com）。
    ///
    /// ============ 能做什么 / 不能做什么 ============
    /// ✅ 目录检索：榜单页、分类页、详情页均为**服务端渲染**，数据完整（剧名、封面、集数、
    ///    热度、评分、点赞、收藏、标签、演员、简介、分集列表）。
    /// ❌ 搜索：搜索页是纯前端渲染，服务端不返回结果，且接口未在 bundle 中暴露。
    ///    本适配器 SearchAsync 返回空，由站内已索引的本地库兜底。
    /// ✅ 播放（2026-10-02 实测更正）：视频**没有加密**，是普通明文 MP4。
    ///    取一集下载前 128KB 看 MP4 box：ftyp → moov → mdat，明文 avc1(H.264) + mp4a(AAC)，
    ///    pssh / tenc / senc / encv / sinf / schm / cenc / cbcs 全部不存在。
    ///    地址在播放页 SSR 的 video_player_info.main_url 里，CDN 不需要 Referer
    ///    且 Access-Control-Allow-Origin: *，浏览器可直连。
    ///    真正的限制是**权限**：详情页 SSR 里 episode_cnt=86、accessible_episode_cnt=3，
    ///    官网只公开前 3 集，其余要登录/会员，SSR 里没有它们的播放页链接。
    ///    （此前这里写「MP4 CENC / AES-128 CTR DRM 加密」是错的，已更正。）
    ///
    /// ============ 解析策略 ============
    /// 站点用 CSS Modules，`pc-xxx-{hash}` 的 hash 每次构建都会变，不能作为锚点。
    /// 因此解析只依赖稳定特征：
    ///   - 全局设计系统 class：`m-card` / `m-title` / `m-episode` / `m-img-container`
    ///   - Open Graph 元标签：og:title / og:image / og:description
    ///   - URL 模式：/detail?series_id={id}、/player/{seriesId}/{episodeId}
    ///   - 文本特征：热度、评分、全N集
    /// </summary>
    public class HongGuoWebAdapter : IPlatformAdapter
    {
        private readonly HongGuoWebOptions _options;
        private readonly HttpClient _http;
        private readonly ILogger _logger;

        public string PlatformName => _options.PlatformName;
        public string PlatformCode => _options.PlatformCode;

        // ===== 稳定锚点正则 =====
        private static readonly Regex CardRegex = new(
            @"<a[^>]+class=""[^""]*\bm-card\b[^""]*""[^>]*href=""/detail\?series_id=(?<id>\d+)""(?<body>.*?)</a>",
            RegexOptions.Singleline | RegexOptions.Compiled);

        private static readonly Regex CardImgRegex = new(
            @"<img[^>]+src=""(?<src>[^""]+)""[^>]*alt=""(?<alt>[^""]*)""",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex CardTitleRegex = new(
            @"<p[^>]+class=""[^""]*\bm-title\b[^""]*""[^>]*>(?<v>.*?)</p>",
            RegexOptions.Singleline | RegexOptions.Compiled);

        private static readonly Regex CardEpisodeRegex = new(
            @"<p[^>]+class=""[^""]*\bm-episode\b[^""]*""[^>]*>(?<v>.*?)</p>",
            RegexOptions.Singleline | RegexOptions.Compiled);

        private static readonly Regex H1Regex = new(
            @"<h1[^>]*>(?<v>.*?)</h1>", RegexOptions.Singleline | RegexOptions.Compiled);

        private static readonly Regex HeatRegex = new(
            @"(?<v>[\d.]+万?)热度", RegexOptions.Compiled);

        private static readonly Regex RatingRegex = new(
            @"评分\s*(?<v>[\d.]+)", RegexOptions.Compiled);

        private static readonly Regex LikesRegex = new(
            @"(?<v>[\d.]+万?)\s*次?点赞", RegexOptions.Compiled);

        private static readonly Regex FavoritesRegex = new(
            @"(?<v>[\d.]+万?)\s*收藏", RegexOptions.Compiled);

        private static readonly Regex TotalEpisodesRegex = new(
            @"全\s*(?<v>\d+)\s*集", RegexOptions.Compiled);

        /// <summary>标签：&lt;span class="... m-tag-text-{hash}"&gt;爱情&lt;/span&gt;</summary>
        private static readonly Regex TagTextRegex = new(
            @"<span[^>]+class=""[^""]*m-tag-text[^""]*""[^>]*>(?<v>[^<]{1,10})</span>", RegexOptions.Compiled);

        /// <summary>封面：站点图片托管在 byteimg / douyinpic</summary>
        private static readonly Regex CoverRegex = new(
            @"<img[^>]+src=""(?<v>https://[^""]*(?:byteimg|douyinpic)[^""]*)""", RegexOptions.Compiled);

        /// <summary>
        /// 选集格子。红果只在 SSR 里渲染前几集的 href，其余是无链接的 div：
        ///   &lt;a href="/player/{sid}" class="...pc-episode-item..."&gt;&lt;div&gt;1&lt;/div&gt;&lt;/a&gt;
        ///   &lt;a href="/player/{sid}/{eid}" ...&gt;&lt;div&gt;2&lt;/div&gt;&lt;/a&gt;
        ///   &lt;div class="...pc-episode-item..."&gt;&lt;div&gt;4&lt;/div&gt;&lt;/div&gt;
        /// </summary>
        private static readonly Regex EpisodeCellRegex = new(
            @"<(?<tag>a|div)(?:\s[^>]*?href=""(?<href>[^""]*)""[^>]*?)?\s+class=""[^""]*pc-episode-item[^""]*""[^>]*>\s*<div[^>]*>(?<num>\d+)</div>",
            RegexOptions.Singleline | RegexOptions.Compiled);

        private static readonly Regex HtmlCommentRegex = new(@"<!--.*?-->", RegexOptions.Singleline | RegexOptions.Compiled);

        public HongGuoWebAdapter(HongGuoWebOptions options, HttpClient http, ILogger logger)
        {
            _options = options;
            _http = http;
            _logger = logger;
        }

        // ==================== 榜单 ====================

        public async Task<List<PlatformRankItem>> GetRankAsync(string type, int limit = 20)
        {
            var path = type switch
            {
                "new" => _options.LatestRankPath,
                "recommend" => _options.RealDramaRankPath,
                _ => _options.HotRankPath
            };

            var html = await GetStringAsync($"{_options.BaseUrl}{path}");
            if (html is null) return new List<PlatformRankItem>();

            return ParseCards(html)
                .Take(limit)
                .Select((c, index) => new PlatformRankItem
                {
                    PlatformDramaId = c.Id,
                    Title = c.Title,
                    CoverUrl = c.CoverUrl,
                    Category = c.Category,
                    // 榜单按名次倒推热度，保证排序稳定
                    PlayCount = c.Heat > 0 ? c.Heat : (limit - index) * 10000L
                })
                .ToList();
        }

        // ==================== 上新（分类页）====================

        public async Task<List<PlatformNewItem>> GetLatestAsync(string category, int limit = 20)
        {
            var path = _options.CategoryPaths.TryGetValue(category, out var mapped)
                ? mapped
                : _options.DefaultCategoryPath;

            var html = await GetStringAsync($"{_options.BaseUrl}{path}");
            if (html is null) return new List<PlatformNewItem>();

            return ParseCards(html)
                .Take(limit)
                .Select(c => new PlatformNewItem
                {
                    PlatformDramaId = c.Id,
                    Title = c.Title,
                    CoverUrl = c.CoverUrl,
                    Category = c.Category,
                    TotalEpisodes = c.TotalEpisodes
                })
                .ToList();
        }

        // ==================== 搜索 ====================

        /// <summary>
        /// 站点搜索页是纯前端渲染，服务端不返回结果，且接口未暴露。
        /// 这里返回空集合，聚合搜索由站内已索引的本地库兜底
        /// （SourceBootstrapper 会把榜单/分类页的目录播种进库）。
        /// </summary>
        public Task<PlatformSearchResult> SearchAsync(string keyword, int page = 1, int pageSize = 10)
        {
            _logger.LogDebug("红果短剧：站点搜索为纯前端实现，关键词 \"{Keyword}\" 交由本地索引处理", keyword);
            return Task.FromResult(new PlatformSearchResult());
        }

        // ==================== 详情 ====================

        public async Task<PlatformDramaDetail?> GetDramaDetailAsync(string dramaId)
        {
            var raw = await GetStringAsync($"{_options.BaseUrl}/detail?series_id={Uri.EscapeDataString(dramaId)}");
            if (raw is null) return null;

            // React SSR 会在文本节点之间插入 <!-- --> 分隔符（如「评分<!-- -->9.3」），
            // 先统一剔除，后续所有正则都按连续文本匹配。
            var html = HtmlCommentRegex.Replace(raw, string.Empty);

            var title = CleanText(FirstGroup(H1Regex, html) ?? $"红果短剧 {dramaId}");
            title = Regex.Replace(title, @"[_|｜]\s*(红果短剧|高清完整版全集免费观看).*$", string.Empty).Trim();

            var detail = new PlatformDramaDetail
            {
                PlatformDramaId = dramaId,
                Title = title,
                CoverUrl = FirstGroup(CoverRegex, html) ?? string.Empty,
                Description = ExtractIntro(html),
                Category = "短剧",
                Status = "ongoing",
                Rating = ParseDouble(RatingRegex, html),
                Episodes = new List<PlatformEpisodeItem>()
            };

            // 分类标签（m-tag-text 是设计系统 class，只有后缀 hash 会变）
            var tags = TagTextRegex.Matches(html)
                .Select(m => CleanText(m.Groups["v"].Value))
                .Where(t => t.Length is >= 2 and <= 8)
                .Distinct()
                .Take(4)
                .ToList();

            if (tags.Count > 0) detail.Category = tags[0];

            // 分集：按格子解析，有 href 的记下播放页地址，没有的（懒加载）留空
            var seen = new HashSet<int>();
            foreach (Match m in EpisodeCellRegex.Matches(html))
            {
                if (!int.TryParse(m.Groups["num"].Value, out var number) || number <= 0) continue;
                if (!seen.Add(number)) continue;

                var href = m.Groups["href"].Success ? m.Groups["href"].Value : string.Empty;

                detail.Episodes.Add(new PlatformEpisodeItem
                {
                    EpisodeNumber = number,
                    Title = $"第 {number} 集",
                    CoverUrl = detail.CoverUrl,
                    DurationSeconds = 120,
                    // 这里存的是该集的**官方播放页**地址（SSR 只渲染公开的那几集）。
                    // 真实视频地址在播放页的 SSR JSON 里（video_player_info.main_url），
                    // 由 GetPlayUrlAsync 现取 —— 那些地址带时效签名，不适合入库。
                    VideoUrl = string.IsNullOrWhiteSpace(href)
                        ? string.Empty
                        : $"{_options.BaseUrl}{href}",
                    IsFree = true
                });
            }

            // 若格子解析失败，退回「全N集」补一个纯序号列表
            if (detail.Episodes.Count == 0)
            {
                var total = ParseInt(TotalEpisodesRegex, html);
                for (var n = 1; n <= total; n++)
                {
                    detail.Episodes.Add(new PlatformEpisodeItem
                    {
                        EpisodeNumber = n,
                        Title = $"第 {n} 集",
                        CoverUrl = detail.CoverUrl,
                        DurationSeconds = 120,
                        VideoUrl = string.Empty,
                        IsFree = true
                    });
                }
            }

            detail.Episodes = detail.Episodes.OrderBy(e => e.EpisodeNumber).ToList();
            detail.TotalEpisodes = detail.Episodes.Count;
            return detail;
        }

        // ==================== 播放 ====================

        /// <summary>
        /// 播放页 SSR 里内嵌的明文视频地址。
        /// 形如 video_player_info:{"vid":"…","main_url":"https://v26-hgweb.qznovelvod.com/…/video/tos/cn/…?a=8662&amp;…"}
        /// </summary>
        private static readonly Regex MainUrlRegex = new(
            @"""main_url""\s*:\s*""(?<v>[^""]+)""",
            RegexOptions.Compiled);

        /// <summary>
        /// 取真实播放地址。
        ///
        /// 2026-10-02 实测更正：红果的视频**没有加密**，之前的「MP4 CENC / AES-128 CTR DRM」
        /// 结论是错的。取一集实际下载前 128KB 看 MP4 box 结构：
        ///   ftyp → moov → mdat，明文 avc1(H.264) + mp4a(AAC)，
        ///   pssh / tenc / senc / encv / sinf / schm / cenc / cbcs 全部不存在。
        /// CDN 也不需要 Referer，且 Access-Control-Allow-Origin: *，浏览器可直连播放。
        ///
        /// 真正限制是**权限**而不是加密：详情页 SSR 里
        ///   episode_cnt = 86、accessible_episode_cnt = 3
        /// 即官网只公开前 3 集，其余要登录/会员，SSR 里根本没有它们的播放页链接。
        /// </summary>
        public async Task<string> GetPlayUrlAsync(string dramaId, int episodeNumber)
        {
            var detail = await GetDramaDetailAsync(dramaId);
            var page = detail?.Episodes
                .FirstOrDefault(e => e.EpisodeNumber == episodeNumber)?.VideoUrl;

            if (string.IsNullOrWhiteSpace(page))
            {
                throw new NotSupportedException(
                    $"红果短剧第 {episodeNumber} 集未在官网公开（官网只放出前几集，其余需登录或会员）。" +
                    $"{_options.BaseUrl}/detail?series_id={dramaId}");
            }

            // 播放页里取明文地址；SSR 把 / 转义成 \u002F，要还原
            var html = await _http.GetStringAsync(page);
            var match = MainUrlRegex.Match(html);
            if (!match.Success)
            {
                throw new NotSupportedException(
                    $"红果短剧第 {episodeNumber} 集取不到播放地址（播放页结构可能已变）。可跳转官方播放页观看：{page}");
            }

            return UnescapeSlash(match.Groups["v"].Value);
        }

        /// <summary>SSR JSON 里的 \u002F 与 \/ 还原成 /</summary>
        private static string UnescapeSlash(string value) =>
            value.Replace("\\u002F", "/", StringComparison.OrdinalIgnoreCase)
                 .Replace("\\/", "/", StringComparison.Ordinal);

        // ==================== 解析辅助 ====================

        private List<CardItem> ParseCards(string html)
        {
            var result = new List<CardItem>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (Match m in CardRegex.Matches(html))
            {
                var id = m.Groups["id"].Value;
                if (!seen.Add(id)) continue;

                var body = m.Groups["body"].Value;
                var img = CardImgRegex.Match(body);

                var title = CleanText(StripTags(FirstGroup(CardTitleRegex, body) ?? string.Empty));
                if (string.IsNullOrWhiteSpace(title) && img.Success)
                {
                    title = CleanText(img.Groups["alt"].Value);
                }
                if (string.IsNullOrWhiteSpace(title)) continue;

                var episodeText = CleanText(StripTags(FirstGroup(CardEpisodeRegex, body) ?? string.Empty));

                var category = TagTextRegex.Matches(body)
                    .Select(x => CleanText(x.Groups["v"].Value))
                    .FirstOrDefault(t => t.Length is >= 2 and <= 8) ?? "短剧";

                result.Add(new CardItem
                {
                    Id = id,
                    Title = title,
                    CoverUrl = img.Success ? img.Groups["src"].Value : string.Empty,
                    TotalEpisodes = ParseInt(TotalEpisodesRegex, episodeText),
                    Category = category,
                    Heat = 0
                });
            }

            return result;
        }

        private static string ExtractIntro(string html)
        {
            // 简介块形如 >简介：正文</div>（HTML 注释已在调用前剔除）
            var m = Regex.Match(html, @"简介：\s*(?<v>[^<]{20,2000})", RegexOptions.Singleline);
            return m.Success ? CleanText(m.Groups["v"].Value) : string.Empty;
        }

        private async Task<string?> GetStringAsync(string url)
        {
            try
            {
                using var response = await _http.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogDebug("红果短剧 {Status}: {Url}", (int)response.StatusCode, url);
                    return null;
                }
                return await response.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning("红果短剧请求失败（{Reason}）: {Url}", ex.GetBaseException().Message, url);
                return null;
            }
        }

        private static string? FirstGroup(Regex regex, string input)
        {
            var m = regex.Match(input);
            return m.Success ? m.Groups["v"].Value : null;
        }

        private static int ParseInt(Regex regex, string input)
        {
            var m = regex.Match(input);
            return m.Success && int.TryParse(m.Groups["v"].Value, out var v) ? v : 0;
        }

        private static double ParseDouble(Regex regex, string input)
        {
            var m = regex.Match(input);
            return m.Success && double.TryParse(m.Groups["v"].Value, NumberStyles.Float,
                CultureInfo.InvariantCulture, out var v) ? v : 0;
        }

        private static string StripTags(string value) => Regex.Replace(value, "<[^>]+>", " ");

        private static string CleanText(string value)
            => Regex.Replace(value, @"\s+", " ").Trim();

        private sealed class CardItem
        {
            public string Id { get; init; } = string.Empty;
            public string Title { get; init; } = string.Empty;
            public string CoverUrl { get; init; } = string.Empty;
            public int TotalEpisodes { get; init; }
            public long Heat { get; init; }
            public string Category { get; init; } = "短剧";
        }
    }
}

