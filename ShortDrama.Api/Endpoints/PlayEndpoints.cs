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
                HttpContext http,
                CancellationToken ct) =>
            {
                var userId = http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                var info = await service.GetPlayInfoAsync(dramaId, episode, userId, ct);

                if (info is null)
                {
                    return Results.NotFound(ApiResponse<PlayInfoDto>.Fail(404, "短剧或剧集不存在"));
                }

                // 播放地址一律走本机代理，让浏览器只跟自己的源说话，彻底不依赖源站的 CORS：
                //   m3u8 → /play/stream/...  （去广告重写 + 分片代理）
                //   mp4  → /play/segment?u=  （原先是源站直链，浏览器直连第三方 CDN，
                //                            只有开了 CORS 的源能播，没开的必然被拦）
                if (string.Equals(info.StreamType, "mp4", StringComparison.OrdinalIgnoreCase))
                {
                    info.PlayUrl = $"/api/v1/play/segment?u={Uri.EscapeDataString(info.PlayUrl)}";
                }
                else
                {
                    info.PlayUrl = $"/api/v1/play/stream/{dramaId}/{episode}.m3u8";
                }

                return Results.Ok(ApiResponse<PlayInfoDto>.Success(info));
            })
            .WithName("GetPlayInfo")
            .WithSummary("获取播放地址（后端代理 + 去广告）");

            // 播放列表代理：拉取原始 m3u8 → 剔除广告分片 → 重写分片地址 → 返回干净列表
            group.MapGet("/stream/{dramaId:long}/{episode:int}.m3u8", async (
                long dramaId,
                int episode,
                IPlayService playService,
                IAdFilterService adFilter,
                IMemoryCache cache,
                HttpContext http,
                ILoggerFactory loggerFactory,
                CancellationToken ct) =>
            {
                var logger = loggerFactory.CreateLogger("PlayStream");
                var cacheKey = $"clean-m3u8:{dramaId}:{episode}";

                if (cache.TryGetValue(cacheKey, out string? cached) && !string.IsNullOrWhiteSpace(cached))
                {
                    return Results.Text(cached!, "application/vnd.apple.mpegurl", Encoding.UTF8);
                }

                var info = await playService.GetPlayInfoAsync(dramaId, episode, null, ct);
                if (info is null || string.IsNullOrWhiteSpace(info.PlayUrl))
                {
                    return Results.NotFound("播放源不可用");
                }

                // 明文 MP4 不是播放列表：转到本机的分片代理（同源），
                // 不再重定向到源站——重定向会让浏览器直连第三方 CDN，没开 CORS 就必然被拦。
                if (string.Equals(info.StreamType, "mp4", StringComparison.OrdinalIgnoreCase))
                {
                    return Results.Redirect(
                        $"/api/v1/play/segment?u={Uri.EscapeDataString(info.PlayUrl)}");
                }

                // 必须用相对路径，不能用 $"{Scheme}://{Request.Host}/..." 拼绝对地址。
                // 播放器会把 m3u8 里的相对地址按「播放列表自身的 URL」解析，因此始终与页面同源；
                // 而用 Request.Host 拼绝对地址时，只要中间隔了一层代理就会出错：
                // 前端 dev server 的 changeOrigin:true 会把 Host 改写成 localhost:5080，
                // 于是页面在 127.0.0.1:5173、分片地址却是 localhost:5080 —— 浏览器判定为跨域。
                // 反向代理（Nginx/Caddy）下同理。
                var segmentProxy = "/api/v1/play/segment";
                var filtered = await adFilter.BuildCleanPlaylistAsync(info.PlayUrl, segmentProxy, ct);

                if (!filtered.Success)
                {
                    // 这里绝不能重定向到源站原始地址。
                    // 采集源的 CDN 基本都不发 Access-Control-Allow-Origin，
                    // 浏览器必然以 CORS 拦下，用户只会看到一个看不懂的跨域报错
                    // （实测 v5.ppqrrs.com 等：应用 403 拉不到 → 302 到源站 → 浏览器 CORS 拦截）。
                    // 拉不到就如实返回 502，让前端给出可理解的提示。
                    logger.LogWarning("拉取播放列表失败（Drama={DramaId}, Ep={Episode}）: {Error}",
                        dramaId, episode, filtered.Error);

                    return Results.Json(
                        ApiResponse<string>.Fail(5020,
                            "源站播放列表拉取失败：可能是该源在服务器所在地不可达，或被源站按地区拒绝"),
                        statusCode: StatusCodes.Status502BadGateway);
                }

                if (filtered.RemovedSegmentCount > 0)
                {
                    logger.LogInformation("已剔除 {Count} 个广告分片 (Drama={DramaId}, Ep={Episode})",
                        filtered.RemovedSegmentCount, dramaId, episode);
                }

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
    }
}
