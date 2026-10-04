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
            string keyword, int limit = 10, int? minSeeders = null, CancellationToken ct = default)
        {
            var response = new TorrentSearchResponseDto { Query = keyword };

            var query = $"api/search?q={Uri.EscapeDataString(keyword)}&limit={limit}&sort=seeders&order=desc";
            if (minSeeders is > 0) query += $"&min-seeders={minSeeders}";

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

                var size = ReadSize(item.Size, item.SizeText, item.SizeBytes);

                response.Results.Add(new TorrentSearchResultDto
                {
                    Title = item.Title ?? string.Empty,
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
            [JsonPropertyName("sources")] public List<string>? Sources { get; set; }
            [JsonPropertyName("publishedAt")] public string? PublishedAt { get; set; }
        }

        private sealed class DownloadList
        {
            [JsonPropertyName("tasks")] public List<DownloadTask>? Tasks { get; set; }
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
