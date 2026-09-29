using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ShortDrama.Application.Adapters;

namespace ShortDrama.Infrastructure.Adapters
{
    /// <summary>
    /// 黄果短剧（huangguodrama.ai）适配器。
    ///
    /// ============ 接口来源：站点自己公开的，不是逆向 ============
    /// 站点在 robots.txt 里显式 Allow 了 /api/agent/v1/，并发布了 OpenAPI 3.1.0 规范：
    ///     GET /api/agent/v1/openapi/          → 规范本身
    ///     GET /api/agent/v1/search/           → 搜索/列表
    ///     GET /api/agent/v1/titles/{id}/      → 详情
    ///     GET /api/agent/v1/titles/{id}/episodes/ → 分集（含带归因令牌的 watch_url）
    /// 只读，限流 60 请求/IP/分钟。分集接口返回的 attributed_watch_url 带 hg_agent 令牌，
    /// 说明站点是**主动希望**被 agent 接入的。
    ///
    /// ============ 视频：明文 MP4，无任何保护 ============
    /// 播放页 <source src="https://media.huangguodrama.ai/motion/drama-episodes/{sha256}.mp4" type="video/mp4"/>
    /// 实测响应：Content-Type: video/mp4 / Accept-Ranges: bytes / access-control-allow-origin: *
    /// 无 DRM、无加密、无鉴权、无签名 —— 与红果的 MP4 CENC（AES-128 CTR）加密性质完全不同。
    ///
    /// ============ 内容提示 ============
    /// 该站为混合平台：既有常规 AI 短剧，也有成人向作品（站点全站 18+ 门禁，
    /// 应用说明中列有「成人剧场」，分类含 ai-huanlian / ai-mogai）。
    /// 可通过 Options.ExcludeAdult 与 AdultKeywords 控制是否过滤，默认按配置值执行。
    /// </summary>
    public class HuangGuoAiAdapter : IPlatformAdapter, IPagedCatalogAdapter
    {
        private readonly HuangGuoAiOptions _options;
        private readonly HttpClient _http;
        private readonly ILogger _logger;

        /// <summary>简单的客户端限流：站点限制 60 请求/IP/分钟</summary>
        private readonly SemaphoreSlim _throttle = new(1, 1);
        private DateTime _windowStart = DateTime.UtcNow;
        private int _windowCount;

        private static readonly Regex SourceMp4Regex = new(
            @"<source[^>]+src=""(?<v>https://[^""]+\.mp4)""",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex AnyMp4Regex = new(
            @"https://[^""'<>\s]+drama-episodes/[a-f0-9]+\.mp4",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public string PlatformName => _options.PlatformName;
        public string PlatformCode => _options.PlatformCode;

        public HuangGuoAiAdapter(HuangGuoAiOptions options, HttpClient http, ILogger logger)
        {
            _options = options;
            _http = http;
            _logger = logger;
        }

        // ==================== 搜索 ====================

        public async Task<PlatformSearchResult> SearchAsync(string keyword, int page = 1, int pageSize = 10)
        {
            if (string.IsNullOrWhiteSpace(keyword)) return new PlatformSearchResult();

            var url = $"{_options.BaseUrl}/api/agent/v1/search/" +
                      $"?q={Uri.EscapeDataString(keyword)}&page={Math.Max(1, page)}&limit={Math.Clamp(pageSize, 1, 20)}";

            var json = await GetJsonAsync(url);
            if (json is null) return new PlatformSearchResult();

            var items = ReadItems(json)
                .Select(ToSearchItem)
                .Where(PassesFilter)
                .ToList();

            return new PlatformSearchResult
            {
                Items = items,
                Total = ReadInt(json, "total")
            };
        }

        // ==================== 详情 ====================

        public async Task<PlatformDramaDetail?> GetDramaDetailAsync(string dramaId)
        {
            var titleJson = await GetJsonAsync($"{_options.BaseUrl}/api/agent/v1/titles/{Uri.EscapeDataString(dramaId)}/");
            if (titleJson is null) return null;

            var detail = new PlatformDramaDetail
            {
                PlatformDramaId = dramaId,
                Title = ReadString(titleJson, "title"),
                Description = ReadString(titleJson, "synopsis"),
                CoverUrl = string.Empty,
                Category = ReadGenres(titleJson).FirstOrDefault() ?? "短剧",
                Status = ReadString(titleJson, "completeness") == "complete" ? "completed" : "ongoing",
                Rating = 0,
                Episodes = new List<PlatformEpisodeItem>()
            };

            if (string.IsNullOrWhiteSpace(detail.Title)) return null;

            // 分集：官方接口 limit 上限为 20，需要翻页拉全
            var seen = new HashSet<int>();
            var page = 1;
            const int maxPages = 30;   // 安全上限，避免异常数据导致死循环

            while (page <= maxPages)
            {
                var episodesJson = await GetJsonAsync(
                    $"{_options.BaseUrl}/api/agent/v1/titles/{Uri.EscapeDataString(dramaId)}/episodes/?page={page}&limit=20");

                if (episodesJson is null) break;

                var batch = ReadItems(episodesJson).ToList();
                if (batch.Count == 0) break;

                foreach (var item in batch)
                {
                    var number = ReadInt(item, "number");
                    if (number <= 0 || !seen.Add(number)) continue;

                    detail.Episodes.Add(new PlatformEpisodeItem
                    {
                        EpisodeNumber = number,
                        Title = $"第 {number} 集",
                        CoverUrl = string.Empty,
                        DurationSeconds = ReadInt(item, "duration_seconds"),
                        // watch_url 是官方观看页；真正的 MP4 在 GetPlayUrlAsync 里解析
                        VideoUrl = ReadString(item, "watch_url"),
                        IsFree = true
                    });
                }

                var total = ReadInt(episodesJson, "total");
                if (total > 0 && seen.Count >= total) break;

                page++;
            }

            detail.Episodes = detail.Episodes.OrderBy(e => e.EpisodeNumber).ToList();
            detail.TotalEpisodes = detail.Episodes.Count;
            return detail;
        }

        // ==================== 播放地址 ====================

        /// <summary>
        /// 抓官方观看页，从 &lt;source&gt; 标签里取出明文 MP4 直链。
        /// 站点未做任何保护（无 DRM/鉴权/签名），CDN 且 CORS 全开。
        /// </summary>
        public async Task<string> GetPlayUrlAsync(string dramaId, int episodeNumber)
        {
            var candidates = new List<string>();

            // 第 1 集在 /video/{id}/，其余在 /video/{id}/ep-{n}/
            if (episodeNumber == 1)
            {
                candidates.Add($"{_options.BaseUrl}/video/{dramaId}/");
            }
            else
            {
                candidates.Add($"{_options.BaseUrl}/video/{dramaId}/ep-{episodeNumber}/");
            }
            candidates.Add($"{_options.BaseUrl}/video/{dramaId}/ep-{episodeNumber}/");

            foreach (var pageUrl in candidates.Distinct())
            {
                var html = await GetStringAsync(pageUrl);
                if (string.IsNullOrWhiteSpace(html)) continue;

                var match = SourceMp4Regex.Match(html);
                if (!match.Success) match = AnyMp4Regex.Match(html);

                if (match.Success)
                {
                    return match.Groups["v"].Success ? match.Groups["v"].Value : match.Value;
                }
            }

            throw new InvalidOperationException(
                $"黄果短剧：未能从观看页解析出第 {episodeNumber} 集的视频地址（站点结构可能已变）");
        }

        // ==================== 全量目录分页（IPagedCatalogAdapter）====================

        /// <summary>目录总条数</summary>
        public async Task<int> GetCatalogTotalAsync(CancellationToken ct = default)
        {
            var json = await GetJsonAsync($"{_options.BaseUrl}/api/agent/v1/search/?limit=20");
            return json is null ? 0 : ReadInt(json, "total");
        }

        /// <summary>
        /// 按页取全量目录。
        /// 该站标题大量为中英双语，中文关键词搜索命中率低，遍历分页才能拉全。
        /// </summary>
        public async Task<List<PlatformNewItem>> GetCatalogPageAsync(int page, int pageSize, CancellationToken ct = default)
        {
            var limit = Math.Clamp(pageSize, 1, 20);   // 官方上限 20
            var json = await GetJsonAsync($"{_options.BaseUrl}/api/agent/v1/search/?page={Math.Max(1, page)}&limit={limit}");
            if (json is null) return new List<PlatformNewItem>();

            return ReadItems(json)
                .Select(ToSearchItem)
                .Where(PassesFilter)
                .Select(i => new PlatformNewItem
                {
                    PlatformDramaId = i.PlatformDramaId,
                    Title = i.Title,
                    CoverUrl = i.CoverUrl,
                    Category = i.Category,
                    TotalEpisodes = i.TotalEpisodes,
                    Rating = i.Rating,
                    Description = i.Description,
                    Status = i.Status
                })
                .ToList();
        }

        // ==================== 榜单 / 上新 ====================

        public async Task<List<PlatformRankItem>> GetRankAsync(string type, int limit = 20)
        {
            var json = await GetJsonAsync($"{_options.BaseUrl}/api/agent/v1/search/?limit={Math.Clamp(limit, 1, 20)}");
            if (json is null) return new List<PlatformRankItem>();

            return ReadItems(json)
                .Select(ToSearchItem)
                .Where(PassesFilter)
                .Take(limit)
                .Select(i => new PlatformRankItem
                {
                    PlatformDramaId = i.PlatformDramaId,
                    Title = i.Title,
                    CoverUrl = i.CoverUrl,
                    Category = i.Category,
                    PlayCount = 0
                })
                .ToList();
        }

        public async Task<List<PlatformNewItem>> GetLatestAsync(string category, int limit = 20)
        {
            var json = await GetJsonAsync($"{_options.BaseUrl}/api/agent/v1/search/?limit={Math.Clamp(limit, 1, 20)}");
            if (json is null) return new List<PlatformNewItem>();

            return ReadItems(json)
                .Select(ToSearchItem)
                .Where(PassesFilter)
                .Take(limit)
                .Select(i => new PlatformNewItem
                {
                    PlatformDramaId = i.PlatformDramaId,
                    Title = i.Title,
                    CoverUrl = i.CoverUrl,
                    Category = i.Category,
                    TotalEpisodes = i.TotalEpisodes,
                    Rating = i.Rating,
                    Description = i.Description,
                    Status = i.Status
                })
                .ToList();
        }

        // ==================== 字段映射 ====================

        private PlatformSearchItem ToSearchItem(JsonElement e) => new()
        {
            PlatformDramaId = ReadString(e, "id"),
            Title = ReadString(e, "title"),
            Description = ReadString(e, "synopsis"),
            CoverUrl = string.Empty,
            Category = ReadGenres(e).FirstOrDefault() ?? "短剧",
            TotalEpisodes = ReadInt(e, "catalog_episode_count"),
            Status = ReadString(e, "completeness") == "complete" ? "completed" : "ongoing",
            Rating = 0
        };

        /// <summary>成人内容过滤（按标题关键词）</summary>
        private bool PassesFilter(PlatformSearchItem item)
        {
            if (!_options.ExcludeAdult) return true;

            var title = item.Title ?? string.Empty;
            var desc = item.Description ?? string.Empty;

            return !_options.AdultKeywords.Any(k =>
                title.Contains(k, StringComparison.OrdinalIgnoreCase) ||
                desc.Contains(k, StringComparison.OrdinalIgnoreCase));
        }

        private static List<string> ReadGenres(JsonElement? element)
        {
            var result = new List<string>();
            if (element is not { } e ||
                !e.TryGetProperty("genres", out var genres) ||
                genres.ValueKind != JsonValueKind.Array)
            {
                return result;
            }

            foreach (var g in genres.EnumerateArray())
            {
                var label = ReadString(g, "label");
                if (!string.IsNullOrWhiteSpace(label)) result.Add(label);
            }

            return result;
        }

        private static IEnumerable<JsonElement> ReadItems(JsonElement? element)
        {
            if (element is { } e &&
                e.TryGetProperty("items", out var items) &&
                items.ValueKind == JsonValueKind.Array)
            {
                return items.EnumerateArray().ToList();
            }
            return Enumerable.Empty<JsonElement>();
        }

        private static string ReadString(JsonElement? element, string name)
        {
            if (element is not { } e || !e.TryGetProperty(name, out var v)) return string.Empty;

            return v.ValueKind switch
            {
                JsonValueKind.String => v.GetString() ?? string.Empty,
                JsonValueKind.Number => v.ToString(),
                _ => string.Empty
            };
        }

        private static int ReadInt(JsonElement? element, string name)
        {
            if (element is not { } e || !e.TryGetProperty(name, out var v)) return 0;

            return v.ValueKind switch
            {
                JsonValueKind.Number => v.TryGetInt32(out var n) ? n : 0,
                JsonValueKind.String => int.TryParse(v.GetString(), out var s) ? s : 0,
                _ => 0
            };
        }

        // ==================== HTTP ====================

        private async Task<JsonElement?> GetJsonAsync(string url)
        {
            var text = await GetStringAsync(url);
            if (string.IsNullOrWhiteSpace(text)) return null;

            try
            {
                using var doc = JsonDocument.Parse(text);
                return doc.RootElement.Clone();
            }
            catch (JsonException ex)
            {
                _logger.LogWarning("黄果短剧：JSON 解析失败（{Reason}）: {Url}", ex.Message, url);
                return null;
            }
        }

        private async Task<string?> GetStringAsync(string url)
        {
            await ThrottleAsync();
            try
            {
                using var response = await _http.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogDebug("黄果短剧 {Status}: {Url}", (int)response.StatusCode, url);
                    return null;
                }
                return await response.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning("黄果短剧请求失败（{Reason}）: {Url}", ex.GetBaseException().Message, url);
                return null;
            }
        }

        /// <summary>滑动窗口限流：站点限制 60 请求/IP/分钟，这里留出余量按 50 控制</summary>
        private async Task ThrottleAsync()
        {
            if (_options.RequestsPerMinute <= 0) return;

            await _throttle.WaitAsync();
            try
            {
                var now = DateTime.UtcNow;
                if ((now - _windowStart).TotalSeconds >= 60)
                {
                    _windowStart = now;
                    _windowCount = 0;
                }

                if (_windowCount >= _options.RequestsPerMinute)
                {
                    var wait = TimeSpan.FromSeconds(60 - (now - _windowStart).TotalSeconds);
                    if (wait > TimeSpan.Zero)
                    {
                        _logger.LogDebug("黄果短剧：触发本地限流，等待 {Seconds:F0}s", wait.TotalSeconds);
                        await Task.Delay(wait);
                    }
                    _windowStart = DateTime.UtcNow;
                    _windowCount = 0;
                }

                _windowCount++;
            }
            finally
            {
                _throttle.Release();
            }
        }
    }
}

