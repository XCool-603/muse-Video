using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ShortDrama.Application.DTOs;
using ShortDrama.Application.Services;

namespace ShortDrama.Infrastructure.Services
{
    /// <summary>
    /// 通过 HTTP 调用本机种子服务（torrent-search）。
    ///
    /// 设计取舍：**本平台不实现 BT 协议**。种子搜索与边下边播都是那个项目的强项
    /// （自研 BT 引擎 + 分片校验 + HTTP Range 流式），这里只做转发与映射。
    /// 重复实现一遍引擎既没必要，也会把两边的坑各踩一遍。
    /// </summary>
    public class TorrentService : ITorrentService
    {
        /// <summary>浏览器原生能播的容器。mkv/avi/ts 这些即便内容没问题也播不了，要提前告诉用户</summary>
        private static readonly HashSet<string> BrowserPlayableTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "video/mp4", "video/webm", "audio/mpeg", "audio/mp4", "audio/aac", "audio/wav", "audio/ogg", "audio/flac"
        };

        private static readonly Regex InfoHashRegex =
            new(@"(?:urn:btih:)?([0-9a-fA-F]{40})", RegexOptions.Compiled);

        private readonly IHttpClientFactory _factory;
        private readonly TorrentOptions _options;
        private readonly ILogger<TorrentService> _logger;

        /// <summary>数据源清单缓存：用来按 kinds 排除成人站。10 分钟足够，源不会频繁变</summary>
        private List<SourceInfo>? _sourceCache;
        private DateTimeOffset _sourceCacheAt = DateTimeOffset.MinValue;
        private static readonly TimeSpan SourceCacheTtl = TimeSpan.FromMinutes(10);

        public TorrentService(IHttpClientFactory factory, TorrentOptions options, ILogger<TorrentService> logger)
        {
            _factory = factory;
            _options = options;
            _logger = logger;
        }

        /// <summary>
        /// 控制面客户端（搜索/状态/任务）：短超时，连不上就尽快失败，好让前端提示「种子服务没启动」。
        /// </summary>
        private HttpClient Control => _factory.CreateClient("torrent");

        /// <summary>
        /// 数据面客户端（播放代理）：**超时必须无限**。
        ///
        /// HttpClient.Timeout 覆盖的不只是拿到响应头，还包括读取响应体的整个过程；
        /// 用控制面那个 30 秒超时去代理视频，播到 30 秒就会被掐断 —— 这个坑很隐蔽，
        /// 因为短片段测试根本发现不了。
        /// 这里靠调用方的 CancellationToken（客户端断开）来结束，不设整体超时。
        /// </summary>
        private HttpClient DataPlane => _factory.CreateClient("torrent-stream");

        public async Task<TorrentStatusDto> GetStatusAsync(CancellationToken ct = default)
        {
            var status = new TorrentStatusDto
            {
                Enabled = _options.Enabled,
                BaseUrl = _options.BaseUrl,
                Reachable = false
            };

            if (!_options.Enabled)
            {
                status.Message = "已在配置里关闭（Torrent:Enabled=false）";
                return status;
            }

            try
            {
                var health = await Control.GetFromJsonAsync<HealthResponse>("api/health", ct);
                if (health is null)
                {
                    status.Message = "种子服务返回了空响应";
                    return status;
                }

                status.Reachable = health.Ok;
                status.Version = health.Version;
                status.DownloadDir = health.Downloads?.Dir;
                status.ActiveDownloads = health.Downloads?.Active ?? 0;
                status.Backend = health.Downloads?.Backend;
                status.Message = health.Ok ? null : "种子服务报告自身不健康";
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                // 最常见的正常情况：用户还没把种子服务跑起来。给可读的原因，不要抛异常。
                status.Message = $"连不上本地种子服务（{_options.BaseUrl}）。先启动它：node bin/magnet-search.mjs serve";
                _logger.LogInformation("本地种子服务不可达：{Message}", ex.Message);
            }

            return status;
        }

        public async Task<TorrentSearchResponseDto> SearchAsync(
            string keyword, int limit = 10, int? minSeeders = null, bool excludeAdult = true, CancellationToken ct = default)
        {
            var response = new TorrentSearchResponseDto { Query = keyword };

            // 用种子服务的默认排序（relevance，按相关度）。
            //
            // **不要改成 sort=seeders**：实测 apibay 对中文查询是无效的（它不处理中文，
            // 直接返回自己的默认榜单：MobLand / South Park 之类），而那批无关结果的做种数
            // 极高（3 万+）。一旦按做种排序，它们会稳定霸占前几条，表现为
            // 「不管搜什么，结果都一样」——这个坑我踩过。
            // 相关度排序下这些无关结果会沉下去，中文查询的首条才是真命中。
            var query = $"api/search?q={Uri.EscapeDataString(keyword)}&limit={limit}&sort=relevance";
            if (minSeeders is > 0) query += $"&min-seeders={minSeeders}";

            // 排除成人站：**按源的 kinds 排除，而不是按标题猜词**。
            // 工具里有 sukebei（Nyaa 成人分站，kinds=[adult]），它只放成人内容；
            // 实测工具的 safe=true 对这些结果并不生效（开了 safe 结果照旧），
            // 所以直接在请求里把这类源去掉才是可靠的。
            if (excludeAdult)
            {
                var allowed = await GetAllowedSourceIdsAsync(ct);
                if (allowed is { Count: > 0 })
                {
                    query += $"&sources={Uri.EscapeDataString(string.Join(",", allowed))}";
                }
            }

            var payload = await Control.GetFromJsonAsync<SearchResponse>(query, ct);
            if (payload is null) return response;

            response.Total = payload.Total;
            response.TookMs = payload.TookMs;
            response.Cached = payload.Cached;

            // 失败的源要如实转述：这能解释为什么结果比预期少
            foreach (var source in payload.Sources ?? new List<SourceStatus>())
            {
                response.Sources.Add(new TorrentSourceStatusDto
                {
                    Id = source.Id,
                    Ok = source.Ok,
                    Count = source.Count,
                    Error = source.Error
                });

                if (!source.Ok && !string.IsNullOrWhiteSpace(source.Error))
                {
                    response.SourceErrors.Add($"{source.Id}：{source.Error}");
                }
            }

            foreach (var item in payload.Results ?? new List<SearchItem>())
            {
                if (string.IsNullOrWhiteSpace(item.InfoHash) || string.IsNullOrWhiteSpace(item.Magnet)) continue;

                var title = item.Title ?? string.Empty;

                // 与关键词无关的结果直接挡掉。
                // 实测 apibay 对中文查询无效，会返回它自己的默认榜单（MobLand / South Park 之类），
                // 这些结果不但没用，做种数还极高，会把真命中的结果挤下去。
                if (!IsRelevant(keyword, title))
                {
                    response.FilteredIrrelevant += 1;
                    continue;
                }

                // 成人内容过滤。**首选工具自带的 adult 标记**：它来自站点自己的分类
                // （实测准：sukebei 的 Real Life - Videos 全部 adult=true，正常动漫条目 false）。
                // 关键词判断只作为兜底 —— 有些站点不标分类，那时只能按标题猜，会漏也会误伤，
                // 所以过滤条数如实报给前端，界面上也允许关掉。
                if (excludeAdult && (item.Adult || LooksAdult(title)))
                {
                    response.FilteredAdult += 1;
                    continue;
                }

                var size = ReadSize(item.Size, item.SizeText, item.SizeBytes);

                response.Results.Add(new TorrentSearchResultDto
                {
                    Title = title,
                    SizeText = size.Text,
                    SizeBytes = size.Bytes,
                    Seeders = item.Seeders,
                    Leechers = item.Leechers,
                    InfoHash = item.InfoHash.ToLowerInvariant(),
                    Magnet = item.Magnet,
                    Sources = item.Sources ?? new List<string>(),
                    PublishedAt = item.PublishedAt
                });
            }

            return response;
        }

        /// <summary>
        /// 允许参与搜索的源 id：默认启用、在线、且 kinds 里没有 adult。
        ///
        /// 取不到清单时返回 null，调用方就不要传 sources 参数（退回「全部源 + 标题关键词兜底」），
        /// 而不是让搜索直接失败。
        /// </summary>
        private async Task<List<string>?> GetAllowedSourceIdsAsync(CancellationToken ct)
        {
            if (_sourceCache is not null && DateTimeOffset.UtcNow - _sourceCacheAt < SourceCacheTtl)
            {
                return BuildAllowedIds(_sourceCache);
            }

            try
            {
                var payload = await Control.GetFromJsonAsync<SourceList>("api/sources", ct);
                if (payload?.Sources is { Count: > 0 })
                {
                    _sourceCache = payload.Sources;
                    _sourceCacheAt = DateTimeOffset.UtcNow;
                    return BuildAllowedIds(_sourceCache);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                _logger.LogDebug("取种子服务数据源清单失败，本次退回全部源：{Message}", ex.Message);
            }

            return null;
        }

        private static List<string> BuildAllowedIds(List<SourceInfo> sources)
            => sources
                .Where(s => s.DefaultEnabled && !s.Offline)
                .Where(s => s.Kinds is null || !s.Kinds.Contains("adult", StringComparer.OrdinalIgnoreCase))
                .Select(s => s.Id)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToList();

        /// <summary>
        /// 标题是否与关键词相关。
        ///
        /// 中文查询：要求标题命中的「查询词字符」达到 60%（至少 2 个）。
        ///   下限取 2 而不是 1 很关键：2 字查询（三体、狂飙）若只要求 1 个字命中，
        ///   任何含「三」或「体」的标题都会通过（实测把《无职转生 第三季》也放进来了）。
        /// 英文查询：要求标题包含查询的全部词元（长度 ≥2 的词元），否则视为无关。
        /// </summary>
        internal static bool IsRelevant(string keyword, string title)
        {
            if (string.IsNullOrWhiteSpace(keyword) || string.IsNullOrWhiteSpace(title)) return false;

            var query = keyword.Trim();
            var target = title.ToLowerInvariant();
            var needle = query.ToLowerInvariant();

            var queryChars = query.Where(IsCjk).Distinct().ToArray();

            // 中文查询：按字符命中率判断
            if (queryChars.Length > 0)
            {
                var hit = queryChars.Count(c => title.Contains(c));
                // 单字查询只能要求 1；其余至少 2，且不低于 60%
                var need = queryChars.Length == 1
                    ? 1
                    : Math.Max(2, (int)Math.Ceiling(queryChars.Length * 0.6));
                return hit >= need;
            }

            // 英文/数字查询：整串命中，或所有词元都命中
            if (target.Contains(needle)) return true;

            var tokens = needle.Split(new[] { ' ', '.', '-', '_', '(', ')', '[', ']' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(t => t.Length >= 2)
                .ToArray();

            return tokens.Length > 0 && tokens.All(t => target.Contains(t));
        }

        private static bool IsCjk(char c)
            => (c >= 0x4E00 && c <= 0x9FFF)      // 基本区
            || (c >= 0x3400 && c <= 0x4DBF)      // 扩展 A
            || (c >= 0xF900 && c <= 0xFAFF);     // 兼容表意

        /// <summary>
        /// 疑似成人内容的标题关键词（**兜底用**，首选工具自带的 adult 标记）。
        ///
        /// 只收「明确指向成人作品」的词：制片厂名、露骨行为词。
        /// 刻意不收「美女 / 性感 / 偷拍 / 调教」这类会大量误伤的泛词。
        /// </summary>
        private static readonly string[] AdultKeywords =
        {
            // 国内成人制片厂 / 平台
            "麻豆", "精东", "香蕉秀", "蜜桃影像", "天美传媒", "果冻传媒", "星空传媒", "乌鸦传媒", "91制片",
            "swag", "皇家华人", "爱豆传媒", "国产自拍", "国产AV",
            // 露骨行为词
            "无码", "里番", "H漫", "内射", "口交", "口爆", "颜射", "中出", "无套", "群交", "肛交",
            "巨乳", "爆乳", "淫", "肉便器", "痴女", "援交", "自慰", "做爱", "性爱", "换妻", "约啪",
            "嫩穴", "骚穴", "肉棒", "鸡巴", "精液", "喷精", "吞精", "高潮喷", "母狗", "母畜",
            "呻吟", "浪叫", "荡妇", "少妇", "后入", "灌满", "白浆", "打桩", "喷神", "白虎",
            "丝足", "足交", "痴汉", "偷窥", "爆菊", "破处", "迷奸", "轮奸", "乱伦", "春药",
            // 常见标题特征词（成人资源圈常用）
            "寻花", "探花", "外围", "楼凤", "会所", "桑拿", "学生妹", "兼职妹", "大屌", "巨屌",
            "情色", "三级片", "黄片", "黄版", "五十人斩", "调教开发",
            // 英文
            "porn", "xxx", "hentai", "jav", "nsfw", "uncensored"
        };

        private static bool LooksAdult(string title)
        {
            var lower = title.ToLowerInvariant();
            foreach (var word in AdultKeywords)
            {
                if (lower.Contains(word, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        public async Task<TorrentTaskDto> PrepareAsync(string magnet, CancellationToken ct = default)
        {
            var infoHash = ExtractInfoHash(magnet)
                ?? throw new ArgumentException("磁力链接里没有 40 位 info hash", nameof(magnet));

            // 让种子服务建任务（已有进行中的同 hash 任务时它会直接返回既有的那个）
            using var response = await Control.PostAsJsonAsync(
                "api/downloads", new { input = magnet, backend = "builtin" }, ct);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                throw new InvalidOperationException($"种子服务拒绝创建任务（{(int)response.StatusCode}）：{Truncate(body, 200)}");
            }

            // 刚建的任务通常还在解析元数据，文件清单要等一会儿才有 —— 返回当前状态，前端轮询
            return await GetTaskAsync(infoHash, ct)
                ?? new TorrentTaskDto { InfoHash = infoHash, Status = "queued" };
        }

        public async Task<TorrentTaskDto?> GetTaskAsync(string infoHash, CancellationToken ct = default)
        {
            var hash = infoHash.ToLowerInvariant();

            var list = await Control.GetFromJsonAsync<DownloadList>("api/downloads", ct);
            var task = list?.Tasks?.FirstOrDefault(t =>
                string.Equals(t.InfoHash, hash, StringComparison.OrdinalIgnoreCase));

            if (task is null) return null;

            var dto = new TorrentTaskDto
            {
                InfoHash = hash,
                Name = task.Name,
                Status = task.Status ?? "unknown",
                TotalBytes = task.TotalBytes,
                BytesDone = task.BytesDone,
                PiecesDone = task.PiecesDone,
                PieceCount = task.PieceCount,
                Progress = task.Progress,
                Speed = task.Speed,
                PeersConnected = task.PeersConnected,
                Error = task.Error
            };

            // 文件清单从 /api/stream 拿（那里带 content-type 与下标）；还没到那一步就用任务里的粗清单
            try
            {
                var meta = await Control.GetFromJsonAsync<StreamMeta>($"api/stream/{hash}", ct);
                if (meta?.Files is { Count: > 0 })
                {
                    dto.MetadataReady = true;
                    foreach (var file in meta.Files)
                    {
                        dto.Files.Add(new TorrentFileDto
                        {
                            Index = file.Index,
                            Path = file.Path ?? string.Empty,
                            Length = file.Length,
                            ContentType = file.ContentType ?? "application/octet-stream",
                            PlayUrl = $"/api/v1/torrent/stream/{hash}/{file.Index}",
                            BrowserPlayable = BrowserPlayableTypes.Contains(file.ContentType ?? string.Empty)
                        });
                    }
                }
            }
            catch (HttpRequestException ex)
            {
                // 409 = 还没进入可分片阶段（元数据未就绪或任务已结束），不是错误
                _logger.LogDebug("取文件清单失败（可能元数据未就绪）：{Message}", ex.Message);
            }

            if (dto.Files.Count == 0 && task.Files is { Count: > 0 })
            {
                dto.MetadataReady = true;
                for (var i = 0; i < task.Files.Count; i++)
                {
                    var file = task.Files[i];
                    dto.Files.Add(new TorrentFileDto
                    {
                        Index = i,
                        Path = file.Path ?? string.Empty,
                        Length = file.Length,
                        ContentType = GuessContentType(file.Path),
                        PlayUrl = $"/api/v1/torrent/stream/{hash}/{i}",
                        BrowserPlayable = BrowserPlayableTypes.Contains(GuessContentType(file.Path))
                    });
                }
            }

            return dto;
        }

        public async Task<TorrentStreamResponse?> OpenStreamAsync(
            string infoHash, int fileIndex, string? rangeHeader, bool headOnly = false, CancellationToken ct = default)
        {
            var hash = infoHash.ToLowerInvariant();
            // HEAD 必须真的向上游发 HEAD：种子服务对 GET 会等数据就绪（最多 30 秒），
            // HEAD 则是立刻返回的 —— 播放器的探测请求不该被拖住。
            var request = new HttpRequestMessage(
                headOnly ? HttpMethod.Head : HttpMethod.Get, $"api/stream/{hash}/{fileIndex}");

            // Range 必须透传：播放器靠它做 seek，代理不能把它吞掉
            if (!string.IsNullOrWhiteSpace(rangeHeader) &&
                System.Net.Http.Headers.RangeHeaderValue.TryParse(rangeHeader, out var range))
            {
                request.Headers.Range = range;
            }

            var response = await DataPlane.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

            var stream = await response.Content.ReadAsStreamAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                // 把上游的 JSON 错误说明读出来给前端（例如「等待数据超时」）
                using var reader = new StreamReader(stream);
                var body = await reader.ReadToEndAsync(ct);
                stream.Dispose();

                return new TorrentStreamResponse
                {
                    StatusCode = (int)response.StatusCode,
                    ErrorBody = Truncate(body, 500)
                };
            }

            return new TorrentStreamResponse
            {
                StatusCode = (int)response.StatusCode,
                ContentType = response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream",
                ContentLength = response.Content.Headers.ContentLength,
                ContentRange = response.Content.Headers.ContentRange?.ToString(),
                AcceptRanges = response.Headers.AcceptRanges.Contains("bytes"),
                Content = stream
            };
        }

        /// <summary>从磁力链接（或裸 hash）里取 info hash</summary>
        public static string? ExtractInfoHash(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return null;
            var match = InfoHashRegex.Match(input);
            return match.Success ? match.Groups[1].Value.ToLowerInvariant() : null;
        }

        /// <summary>按扩展名猜类型：任务里只有 path，没有 content-type</summary>
        private static string GuessContentType(string? filePath)
        {
            var ext = Path.GetExtension(filePath ?? string.Empty).ToLowerInvariant();
            return ext switch
            {
                ".mp4" or ".m4v" => "video/mp4",
                ".webm" => "video/webm",
                ".mkv" => "video/x-matroska",
                ".mov" => "video/quicktime",
                ".avi" => "video/x-msvideo",
                ".ts" or ".m2ts" => "video/mp2t",
                ".mp3" => "audio/mpeg",
                ".m4a" => "audio/mp4",
                ".aac" => "audio/aac",
                ".flac" => "audio/flac",
                ".wav" => "audio/wav",
                _ => "application/octet-stream"
            };
        }

        /// <summary>
        /// 解析体积。**必须同时认两种形状**（实测两种都遇到过）：
        ///   · HTTP API：size 是数字（字节）+ sizeText 是 "4.59 GiB"
        ///   · 技能脚本输出：size 是 "3.40 GiB" 字符串 + sizeBytes 是数字
        /// 只按一种写，另一种要么 500、要么把体积显示成空。
        /// </summary>
        private static (string? Text, long? Bytes) ReadSize(JsonElement? size, string? sizeText, long? sizeBytes)
        {
            var text = sizeText;
            var bytes = sizeBytes;

            if (size is not JsonElement value) return (text, bytes);

            switch (value.ValueKind)
            {
                case JsonValueKind.Number when value.TryGetInt64(out var numeric):
                    bytes ??= numeric;
                    text ??= FormatSize(numeric);
                    break;

                case JsonValueKind.String:
                    var raw = value.GetString();
                    if (long.TryParse(raw, out var parsed))
                    {
                        bytes ??= parsed;
                        text ??= FormatSize(parsed);
                    }
                    else if (!string.IsNullOrWhiteSpace(raw))
                    {
                        // size 直接就是人类可读文本
                        text ??= raw;
                    }
                    break;
            }

            return (text, bytes);
        }

        /// <summary>把字节数格式化成人类可读（与种子服务 sizeText 的风格保持一致）</summary>
        private static string FormatSize(long bytes)
        {
            string[] units = { "B", "KiB", "MiB", "GiB", "TiB" };
            double value = bytes;
            var index = 0;
            while (value >= 1024 && index < units.Length - 1)
            {
                value /= 1024;
                index += 1;
            }
            return index == 0 ? $"{value:0} {units[index]}" : $"{value:0.00} {units[index]}";
        }

        private static string Truncate(string value, int max)
            => value.Length <= max ? value : value[..max] + "…";

        // ---- 种子服务的响应模型（只声明用得到的字段，避免上游加字段就崩） ----

        private sealed class HealthResponse
        {
            [JsonPropertyName("ok")] public bool Ok { get; set; }
            [JsonPropertyName("version")] public string? Version { get; set; }
            [JsonPropertyName("downloads")] public HealthDownloads? Downloads { get; set; }
        }

        private sealed class HealthDownloads
        {
            [JsonPropertyName("dir")] public string? Dir { get; set; }
            [JsonPropertyName("active")] public int Active { get; set; }
            [JsonPropertyName("backend")] public string? Backend { get; set; }
        }

        private sealed class SearchResponse
        {
            [JsonPropertyName("total")] public int Total { get; set; }
            [JsonPropertyName("tookMs")] public long TookMs { get; set; }
            [JsonPropertyName("cached")] public bool Cached { get; set; }
            [JsonPropertyName("sources")] public List<SourceStatus>? Sources { get; set; }
            [JsonPropertyName("results")] public List<SearchItem>? Results { get; set; }
        }

        private sealed class SourceStatus
        {
            [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
            [JsonPropertyName("ok")] public bool Ok { get; set; }
            [JsonPropertyName("count")] public int Count { get; set; }
            [JsonPropertyName("error")] public string? Error { get; set; }
        }

        private sealed class SearchItem
        {
            [JsonPropertyName("title")] public string? Title { get; set; }
            /// <summary>
            /// 体积。**这里必须是宽容类型**：HTTP API 给的是数字（字节），而技能脚本
            /// 输出的 JSON 给的是字符串（"4.59 GiB"）—— 两个形状不一样，写死一种就会 500。
            /// </summary>
            [JsonPropertyName("size")] public JsonElement? Size { get; set; }
            [JsonPropertyName("sizeText")] public string? SizeText { get; set; }
            [JsonPropertyName("sizeBytes")] public long? SizeBytes { get; set; }
            [JsonPropertyName("seeders")] public int? Seeders { get; set; }
            [JsonPropertyName("leechers")] public int? Leechers { get; set; }
            [JsonPropertyName("infoHash")] public string? InfoHash { get; set; }
            [JsonPropertyName("magnet")] public string? Magnet { get; set; }
            /// <summary>工具按站点分类给出的成人标记。比按标题猜词可靠，优先用它</summary>
            [JsonPropertyName("adult")] public bool Adult { get; set; }
            [JsonPropertyName("sources")] public List<string>? Sources { get; set; }
            [JsonPropertyName("publishedAt")] public string? PublishedAt { get; set; }
        }

        private sealed class DownloadList
        {
            [JsonPropertyName("tasks")] public List<DownloadTask>? Tasks { get; set; }
        }

        private sealed class SourceList
        {
            [JsonPropertyName("sources")] public List<SourceInfo>? Sources { get; set; }
        }

        private sealed class SourceInfo
        {
            [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
            [JsonPropertyName("kinds")] public List<string>? Kinds { get; set; }
            [JsonPropertyName("defaultEnabled")] public bool DefaultEnabled { get; set; } = true;
            [JsonPropertyName("offline")] public bool Offline { get; set; }
        }

        private sealed class DownloadTask
        {
            [JsonPropertyName("infoHash")] public string? InfoHash { get; set; }
            [JsonPropertyName("name")] public string? Name { get; set; }
            [JsonPropertyName("status")] public string? Status { get; set; }
            [JsonPropertyName("totalBytes")] public long TotalBytes { get; set; }
            [JsonPropertyName("bytesDone")] public long BytesDone { get; set; }
            [JsonPropertyName("piecesDone")] public int PiecesDone { get; set; }
            [JsonPropertyName("pieceCount")] public int PieceCount { get; set; }
            [JsonPropertyName("progress")] public double Progress { get; set; }
            [JsonPropertyName("speed")] public long Speed { get; set; }
            [JsonPropertyName("peersConnected")] public int PeersConnected { get; set; }
            [JsonPropertyName("error")] public string? Error { get; set; }
            [JsonPropertyName("files")] public List<DownloadFile>? Files { get; set; }
        }

        private sealed class DownloadFile
        {
            [JsonPropertyName("path")] public string? Path { get; set; }
            [JsonPropertyName("length")] public long Length { get; set; }
        }

        private sealed class StreamMeta
        {
            [JsonPropertyName("files")] public List<StreamFile>? Files { get; set; }
        }

        private sealed class StreamFile
        {
            [JsonPropertyName("index")] public int Index { get; set; }
            [JsonPropertyName("path")] public string? Path { get; set; }
            [JsonPropertyName("length")] public long Length { get; set; }
            [JsonPropertyName("contentType")] public string? ContentType { get; set; }
        }
    }
}
