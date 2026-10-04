using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using ShortDrama.Application.DTOs;
using ShortDrama.Application.Services;
using ShortDrama.Infrastructure.Services;

namespace ShortDrama.Api.Endpoints
{
    public static class PlayEndpoints
    {
        public static void MapPlayEndpoints(this WebApplication app)
        {
            var group = app.MapGroup("/api/v1/play").WithTags("Play");

            // 获取播放信息（含无广告播放地址）
            group.MapGet("/{dramaId:long}/{episode:int}", async (
                long dramaId,
                int episode,
                IPlayService service,
                PlaybackOptions playback,
                HttpContext http,
                CancellationToken ct) =>
            {
                var userId = http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                PlayInfoDto? info;
                try
                {
                    info = await service.GetPlayInfoAsync(dramaId, episode, userId, ct);
                }
                catch (NotSupportedException ex)
                {
                    // 适配器明确表示该平台无法在服务端播放（例如红果的 DRM 加密）。
                    // 如实返回原因，不要伪装成「播放源不可用」这种含糊提示。
                    return Results.Json(
                        ApiResponse<PlayInfoDto>.Fail(4090, ex.Message),
                        statusCode: StatusCodes.Status409Conflict);
                }

                if (info is null)
                {
                    return Results.NotFound(ApiResponse<PlayInfoDto>.Fail(404, "短剧或剧集不存在"));
                }

                // 播放地址策略由 Playback 配置决定，默认「浏览器直连 CDN」：
                //   m3u8 → /play/stream/...  后端只转发几十 KB 的播放列表（去广告重写），
                //                            分片默认让浏览器直连 CDN，视频流量不经过服务器
                //   mp4  → 默认给 CDN 直链；Playback:ProxyMp4=true 时改为 /play/segment?u=
                //
                // 实测主流采集源 CDN 都返回 Access-Control-Allow-Origin: *（它们本就是
                // 给别人嵌播放器用的），所以直连通常没问题。遇到不发 CORS 头的源，
                // 打开 ProxySegments / ProxyMp4 即可，代价是视频流量全部压在这台服务器上。
                if (string.Equals(info.StreamType, "mp4", StringComparison.OrdinalIgnoreCase))
                {
                    if (playback.ProxyMp4)
                    {
                        info.PlayUrl = $"/api/v1/play/segment?u={Uri.EscapeDataString(info.PlayUrl)}";
                    }
                    // 否则保持 CDN 直链
                }
                else
                {
                    info.PlayUrl = $"/api/v1/play/stream/{dramaId}/{episode}.m3u8";
                }

                return Results.Ok(ApiResponse<PlayInfoDto>.Success(info));
            })
            .WithName("GetPlayInfo")
            .WithSummary("获取播放地址（去广告；分片默认直连 CDN）");

            // 播放列表代理：拉取原始 m3u8 → 剔除广告分片 → 重写分片地址 → 返回干净列表
            group.MapGet("/stream/{dramaId:long}/{episode:int}.m3u8", async (
                long dramaId,
                int episode,
                [FromQuery] bool? proxy,
                IPlayService playService,
                IAdFilterService adFilter,
                IMemoryCache cache,
                PlaybackOptions playback,
                IHttpClientFactory httpClientFactory,
                PlayabilityTracker playability,
                HttpContext http,
                ILoggerFactory loggerFactory,
                CancellationToken ct) =>
            {
                var logger = loggerFactory.CreateLogger("PlayStream");

                // 播放列表是动态生成的（去广告、分片直连/代理两种模式），绝不能进浏览器缓存。
                // 不设这个头时浏览器会按启发式规则缓存，于是修好之后用户仍然拿到旧内容：
                // 实测出现过「服务端已返回绝对密钥地址，浏览器却还在请求旧的相对地址 enc.key → 404」，
                // 以及「服务端已改为 502，浏览器仍在重放旧的 302 到源站 → 跨域报错」。
                http.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
                http.Response.Headers.Pragma = "no-cache";

                // 分片是否经后端转发：
                //   默认 false → 播放列表里写 CDN 绝对地址，浏览器直连，后端零视频带宽
                //   配置开启或请求带 ?proxy=1 → 分片走后端代理（前端直连失败时的兜底）
                var proxySegments = proxy == true || playback.ProxySegments;
                // 两种模式的播放列表内容不同，缓存键必须区分，否则会互相串
                var cacheKey = $"clean-m3u8:{dramaId}:{episode}:{(proxySegments ? "p" : "d")}";
                var failKey = $"play-fail:{dramaId}:{episode}:{(proxySegments ? "p" : "d")}";

                if (cache.TryGetValue(cacheKey, out string? cached) && !string.IsNullOrWhiteSpace(cached))
                {
                    return Results.Text(cached!, "application/vnd.apple.mpegurl", Encoding.UTF8);
                }

                // 上一次已经失败过？60 秒内直接把同样的失败返回，不再打源站。
                // 上游 403/404 是按 IP/地区的整站拒绝，60 秒内不会变；而播放器对同一集
                // 会重试好几次，不缓存的话每次重试都完整走一遍「去广告失败 → 回退原始 → 再失败」。
                if (cache.TryGetValue(failKey, out string? knownFail) && !string.IsNullOrWhiteSpace(knownFail))
                {
                    return Results.Json(
                        ApiResponse<string>.Fail(5020,
                            $"源站播放列表拉取失败（刚试过，60 秒内不重试）：{knownFail}。" +
                            "可能是该源在服务器所在地不可达，或被源站按地区拒绝"),
                        statusCode: StatusCodes.Status502BadGateway);
                }

                PlayInfoDto? info;
                try
                {
                    info = await playService.GetPlayInfoAsync(dramaId, episode, null, ct);
                }
                catch (NotSupportedException ex)
                {
                    // 同 /{dramaId}/{episode}：平台本身不可播（DRM 等），如实说明
                    return Results.Json(
                        ApiResponse<string>.Fail(4090, ex.Message),
                        statusCode: StatusCodes.Status409Conflict);
                }

                if (info is null || string.IsNullOrWhiteSpace(info.PlayUrl))
                {
                    return Results.NotFound("播放源不可用");
                }

                // 明文 MP4 不是播放列表。ProxyMp4 未开启时直接 302 到 CDN 直链
                //（浏览器直连，零带宽；实测黄果的 CDN 开了 CORS）。
                if (string.Equals(info.StreamType, "mp4", StringComparison.OrdinalIgnoreCase))
                {
                    if (playback.ProxyMp4 || proxy == true)
                    {
                        return Results.Redirect(
                            $"/api/v1/play/segment?u={Uri.EscapeDataString(info.PlayUrl)}");
                    }

                    // 同样要转成 ASCII：MP4 地址里也可能带未编码中文
                    return Results.Redirect(ToAsciiUrl(info.PlayUrl));
                }

                // 用相对路径，不能用 $"{Scheme}://{Request.Host}/..." 拼绝对地址。
                // 播放器会把 m3u8 里的相对地址按「播放列表自身的 URL」解析，因此始终与页面同源；
                // 而用 Request.Host 拼绝对地址时，只要中间隔了一层代理就会出错：
                // 前端 dev server 的 changeOrigin:true 会把 Host 改写成 localhost:5080，
                // 于是页面在 127.0.0.1:5173、分片地址却是 localhost:5080 —— 浏览器判定为跨域。
                // 反向代理（Nginx/Caddy）下同理。
                var segmentProxy = proxySegments ? "/api/v1/play/segment" : null;
                var filtered = await adFilter.BuildCleanPlaylistAsync(info.PlayUrl, segmentProxy, ct);

                // 换主机重试：采集站给的播放地址可能指向已退役的 CDN 主机
                // （实测暴风 bfeng10.com 全站 404，而同一路径在 fengbao13.com 上是 200，
                //  4143 条相对路径分片、无加密）。同一路径换该源其它主机，多数能救回来。
                //
                // 但**整站级封锁**换主机是没用的：实测无尽资源 12 台主机全部 403、
                // 天堂资源 9 台全部 403。这种源已经被 PlayabilityTracker 记为不可播，
                // 再逐个试只是白等 5~10 秒 —— 所以此时跳过整轮换主机。
                // 注意原始地址上面已经试过一次：网络恢复后第一次播放就能成功并解除标记。
                if (!filtered.Success && playability.GetPlatformStatus(info.PlatformCode) == false)
                {
                    logger.LogInformation("平台 {Platform} 已判定本机不可达，跳过换主机重试", info.PlatformName);
                }
                else if (!filtered.Success)
                {
                    var alternates = await playService.GetAlternateHostsAsync(dramaId, ct);
                    foreach (var altUrl in SwapHosts(info.PlayUrl, alternates))
                    {
                        var retry = await adFilter.BuildCleanPlaylistAsync(altUrl, segmentProxy, ct);
                        if (!retry.Success) continue;

                        logger.LogInformation("换 CDN 主机成功：{From} → {To}",
                            HostOf(info.PlayUrl), HostOf(altUrl));

                        playability.RecordSuccess(info.PlatformCode, altUrl);
                        cache.Set(cacheKey, retry.Playlist, TimeSpan.FromMinutes(5));
                        return Results.Text(retry.Playlist, "application/vnd.apple.mpegurl", Encoding.UTF8);
                    }
                }

                if (!filtered.Success)
                {
                    // 失败也缓存 60 秒（检查在上面）：上游 403/404 是按 IP/地区的整站拒绝，
                    // 60 秒内不会变，重试同样的请求只会白白再打源站。
                    cache.Set(failKey, (filtered.Error ?? "未知原因").TrimEnd('.', '。', ' '), TimeSpan.FromSeconds(60));

                    // 记录该源站 CDN 在这台部署机上的可播性（403/404 → 按地区整站拒绝）
                    var upstream = ExtractUpstreamStatus(filtered.Error);
                    playability.RecordFailure(info.PlatformCode, info.PlayUrl, upstream);

                    logger.LogWarning("拉取播放列表失败（Drama={DramaId}, Ep={Episode}, 源={Platform}）: {Error}",
                        dramaId, episode, info.PlatformName, filtered.Error);

                    // 兜底：后端拉不到，改让浏览器自己去拉。
                    // 后端多在机房，IP 常被源站按地区拒绝；用户浏览器的 IP 往往没被拒。
                    // 分片本来就直连 CDN，所以不会引入新的跨域风险，代价是这条路径不做去广告。
                    //
                    // 但调用方已经显式要求代理时（?proxy=true，说明前端直连也失败了）不再兜底，
                    // 否则会在「直连失败 → 代理失败 → 又转回直连」之间绕圈。
                    if (playback.FallbackToDirectPlaylist && proxy != true)
                    {
                        // 兜底前先探一下源站到底允不允许浏览器直连。
                        // 不发 Access-Control-Allow-Origin 的源，重定向过去只会让浏览器
                        // 报一个看不懂的跨域错误（用户反馈过），还不如直接给出带源名的 502。
                        if (await AllowsBrowserDirectAsync(httpClientFactory, info.PlayUrl, ct))
                        {
                            // 地址里可能带未编码中文，必须先转成 ASCII 才能放进 Location 头
                            var directUrl = ToAsciiUrl(info.PlayUrl);
                            logger.LogInformation("改由浏览器直连源站播放列表（不去广告）：{Url}", directUrl);
                            return Results.Redirect(directUrl);
                        }

                        logger.LogInformation("源站未返回 CORS 头，浏览器直连也会被拦，不做兜底：{Url}",
                            info.PlayUrl);
                    }

                    // 异常消息本身带英文句号，去掉避免出现「(Not Found).。」
                    var reason = (filtered.Error ?? "未知原因").TrimEnd('.', '。', ' ');
                    return Results.Json(
                        ApiResponse<string>.Fail(5020,
                            $"源站播放列表拉取失败（{info.PlatformName}）：{reason}。" +
                            "可能是该源在服务器所在地不可达，或被源站按地区拒绝（60 秒内不重试）"),
                        statusCode: StatusCodes.Status502BadGateway);
                }

                if (filtered.RemovedSegmentCount > 0)
                {
                    logger.LogInformation("已剔除 {Count} 个广告分片 (Drama={DramaId}, Ep={Episode})",
                        filtered.RemovedSegmentCount, dramaId, episode);
                }

                playability.RecordSuccess(info.PlatformCode, info.PlayUrl);
                cache.Set(cacheKey, filtered.Playlist, TimeSpan.FromMinutes(5));
                return Results.Text(filtered.Playlist, "application/vnd.apple.mpegurl", Encoding.UTF8);
            })
            .WithName("PlayStream")
            .WithSummary("m3u8 去广告代理");

            // 分片代理：转发 ts/fmp4 分片；若目标是子播放列表则再次执行去广告与地址重写
            group.MapGet("/segment", async (
                [FromQuery] string u,
                IHttpClientFactory httpClientFactory,
                IAdFilterService adFilter,
                IMemoryCache cache,
                HttpContext http,
                CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(u) || !Uri.TryCreate(u, UriKind.Absolute, out var uri))
                {
                    return Results.BadRequest("非法的分片地址");
                }

                // 只允许 http/https，避免 SSRF 到本地协议
                if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                {
                    return Results.BadRequest("不支持的协议");
                }

                var client = httpClientFactory.CreateClient("stream");
                using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
                if (!response.IsSuccessStatusCode)
                {
                    return Results.StatusCode((int)response.StatusCode);
                }

                var bytes = await response.Content.ReadAsByteArrayAsync(ct);
                var contentType = response.Content.Headers.ContentType?.MediaType ?? "video/mp2t";

                // 子播放列表：文本重写（去广告 + 分片走代理）
                var isPlaylist = contentType.Contains("mpegurl", StringComparison.OrdinalIgnoreCase) ||
                                 (bytes.Length > 7 && Encoding.UTF8.GetString(bytes, 0, 7) == "#EXTM3U");

                if (isPlaylist)
                {
                    // 子播放列表同样不能进浏览器缓存（原因同主列表）
                    http.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";

                    var cacheKey = $"clean-seg:{u}";
                    if (cache.TryGetValue(cacheKey, out string? cachedText) && !string.IsNullOrWhiteSpace(cachedText))
                    {
                        return Results.Text(cachedText!, "application/vnd.apple.mpegurl", Encoding.UTF8);
                    }

                    var text = Encoding.UTF8.GetString(bytes);
                    // 同上：相对路径，避免代理改写 Host 后生成跨域地址
                    var segmentProxy = "/api/v1/play/segment";
                    var processed = adFilter.ProcessPlaylist(text, uri.ToString(), segmentProxy);

                    cache.Set(cacheKey, processed.Playlist, TimeSpan.FromMinutes(5));
                    return Results.Text(processed.Playlist, "application/vnd.apple.mpegurl", Encoding.UTF8);
                }

                // 二进制内容（ts 分片 / mp4）：开启 Range 支持，
                // 否则 MP4 走代理后浏览器无法拖动进度条
                return Results.File(bytes, contentType, enableRangeProcessing: true);
            })
            .WithName("PlaySegment")
            .WithSummary("媒体分片 / 子播放列表代理");

            // 上报播放进度
            group.MapPost("/progress", async (
                [FromBody] ReportProgressRequest request,
                IPlayService service,
                HttpContext http,
                CancellationToken ct) =>
            {
                var userId = http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrWhiteSpace(userId))
                {
                    return Results.Unauthorized();
                }

                await service.SaveProgressAsync(userId, request.DramaId, request.Episode, request.Position, ct);
                return Results.Ok(ApiResponse<bool>.Success(true, "进度已保存"));
            })
            .WithName("ReportProgress")
            .RequireAuthorization();

            // 查询续播位置
            group.MapGet("/progress/{dramaId:long}", async (
                long dramaId,
                IPlayService service,
                HttpContext http,
                CancellationToken ct) =>
            {
                var userId = http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrWhiteSpace(userId)) return Results.Unauthorized();

                var progress = await service.GetProgressAsync(userId, dramaId, ct);
                return Results.Ok(ApiResponse<PlayProgressDto?>.Success(progress));
            })
            .WithName("GetProgress")
            .RequireAuthorization();

            // 观看历史
            group.MapGet("/history", async (
                [FromQuery] int limit,
                IPlayService service,
                HttpContext http,
                CancellationToken ct) =>
            {
                var userId = http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrWhiteSpace(userId)) return Results.Unauthorized();

                var history = await service.GetHistoryAsync(userId, limit <= 0 ? 20 : limit, ct);
                return Results.Ok(ApiResponse<List<PlayProgressDto>>.Success(history));
            })
            .WithName("PlayHistory")
            .RequireAuthorization();
        }

        /// <summary>
        /// 把 URL 的主机名换成候选主机，路径与查询串原样保留。
        /// 刻意不用 UriBuilder：它会「双向归一化」，把已编码的中文路径解回原文，
        /// 而部分源站地址里带未编码中文（见 ToAsciiUrl 的注释），换了会被 Kestrel 拒绝。
        /// </summary>
        private static IEnumerable<string> SwapHosts(string url, IReadOnlyList<string> hosts)
        {
            var schemeEnd = url.IndexOf("://", StringComparison.Ordinal);
            if (schemeEnd <= 0 || hosts.Count == 0) yield break;

            var scheme = url[..schemeEnd];
            var rest = url[(schemeEnd + 3)..];
            var slash = rest.IndexOf('/');
            if (slash <= 0) yield break;

            var originalHost = rest[..slash];
            var pathAndQuery = rest[slash..];

            foreach (var host in hosts)
            {
                if (string.IsNullOrWhiteSpace(host)) continue;
                if (host.Equals(originalHost, StringComparison.OrdinalIgnoreCase)) continue;
                yield return $"{scheme}://{host}{pathAndQuery}";
            }
        }

        private static string HostOf(string url)
            => Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : url;

        /// <summary>从去广告失败的异常消息里抽上游状态码，识别失败用于可播性跟踪。
        /// 消息形如 "Response status code does not indicate success: 404 (Not Found)." ——
        /// 状态码在括号**外面**（括号里是原因短语），所以正则要匹配「数字 + 左括号」。</summary>
        private static int ExtractUpstreamStatus(string? error)
        {
            if (string.IsNullOrWhiteSpace(error)) return 0;
            var match = System.Text.RegularExpressions.Regex.Match(error, @"(\d{3})\s*\(");
            return match.Success ? int.Parse(match.Groups[1].Value) : 0;
        }

        /// <summary>
        /// 探测源站是否允许浏览器跨域直连：带 Origin 头请求一次，看响应里有没有
        /// Access-Control-Allow-Origin。
        ///
        /// 注意要看「即使是 4xx/5xx 的响应」的头 —— 有些 CDN 在错误响应上也会带 ACAO，
        /// 那说明浏览器至少能拿到响应，重定向过去是有意义的。
        /// 完全不带的（例如 bfeng10.com）重定向过去只会得到跨域报错，就不该兜底。
        /// </summary>
        private static async Task<bool> AllowsBrowserDirectAsync(
            IHttpClientFactory factory, string url, CancellationToken ct)
        {
            try
            {
                var client = factory.CreateClient("stream");
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.TryAddWithoutValidation("Origin", "http://localhost");
                using var response = await client.SendAsync(
                    request, HttpCompletionOption.ResponseHeadersRead, ct);
                return response.Headers.Contains("Access-Control-Allow-Origin");
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 把 URL 里的非 ASCII 字符按 UTF-8 做百分号编码，使其能放进 HTTP 头。
        ///
        /// HTTP 头（Location）只允许 ASCII，而部分采集源给的地址里带未编码的中文，
        /// 实测暴风资源：https://v.fengbao8.com/video/nvzhanshenlailin/第1集/index.m3u8
        /// 直接塞进 Location 会让 Kestrel 抛 InvalidOperationException
        /// （Invalid non-ASCII or control character in header: 0x7B2C）→ 500。
        ///
        /// 注意不能用 Uri.AbsoluteUri：它会「双向归一化」，把已经编码的
        /// %E7%AC%AC 反向解回中文，等于没修。这里按 UTF-8 字节逐个处理，
        /// 只转义 >= 0x80 的字节，已有的 %XX 原样保留。
        /// </summary>
        private static string ToAsciiUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return url;

            var bytes = Encoding.UTF8.GetBytes(url);
            var sb = new StringBuilder(bytes.Length);
            foreach (var b in bytes)
            {
                if (b < 0x80) sb.Append((char)b);
                else sb.Append('%').Append(b.ToString("X2"));
            }

            return sb.ToString();
        }
    }
}
