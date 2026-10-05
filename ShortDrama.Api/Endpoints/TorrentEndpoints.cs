using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using ShortDrama.Application.DTOs;
using ShortDrama.Application.Services;

namespace ShortDrama.Api.Endpoints
{
    /// <summary>
    /// 本地种子（torrent-search）相关接口。
    ///
    /// 定位：给「采集源都放不出来」的剧多一条路 —— 用种子搜索找到资源、边下边播。
    /// 本平台不实现 BT 协议，只把本机那个种子服务的接口转发出来并收在访问口令门之后。
    /// </summary>
    public static class TorrentEndpoints
    {
        public static void MapTorrentEndpoints(this WebApplication app)
        {
            var group = app.MapGroup("/api/v1/torrent").WithTags("Torrent");

            // 种子服务是否可用（前端据此决定「种子」入口能不能点）
            group.MapGet("/status", async (ITorrentService service, CancellationToken ct) =>
            {
                var status = await service.GetStatusAsync(ct);
                return Results.Ok(ApiResponse<TorrentStatusDto>.Success(status));
            })
            .WithName("GetTorrentStatus")
            .WithSummary("本地种子服务状态（它没启动时返回 reachable=false 与原因）");

            // 聚合搜索
            group.MapGet("/search", async (
                [FromQuery] string? q,
                [FromQuery] int limit,
                [FromQuery] int? minSeeders,
                [FromQuery] bool? excludeAdult,
                ITorrentService service,
                ILoggerFactory loggerFactory,
                CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(q))
                {
                    return Results.BadRequest(ApiResponse<TorrentSearchResponseDto>.Fail(400, "缺少搜索关键词"));
                }

                limit = limit is <= 0 or > 50 ? 10 : limit;
                // 默认挡掉疑似成人内容：中文短剧的查询词在成人标题里极常见，
                // 不挡的话第一页基本都是噪声。界面上有开关，关掉即原样返回。
                var exclude = excludeAdult ?? true;

                // 搜索接口原先在成功路径上完全不打日志，导致「用户说搜不出来」时无从判断：
                // 是没发请求、请求失败了、还是真的 0 条。这里把每次搜索都记下来。
                var logger = loggerFactory.CreateLogger("TorrentSearch");
                var startedAt = System.Diagnostics.Stopwatch.StartNew();

                try
                {
                    var result = await service.SearchAsync(q.Trim(), limit, minSeeders, exclude, ct);
                    startedAt.Stop();

                    logger.LogInformation(
                        "种子搜索「{Query}」→ 命中 {Count} 条（源内合计 {Total}，挡掉无关 {Irrelevant} / 成人 {Adult}），用时 {Ms} ms；各源：{Sources}",
                        q.Trim(), result.Results.Count, result.Total,
                        result.FilteredIrrelevant, result.FilteredAdult, startedAt.ElapsedMilliseconds,
                        string.Join(", ", result.Sources.Select(s => $"{s.Id}={(s.Ok ? s.Count.ToString() : "失败")}")));

                    return Results.Ok(ApiResponse<TorrentSearchResponseDto>.Success(result));
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
                {
                    startedAt.Stop();
                    logger.LogWarning(ex, "种子搜索「{Query}」失败（用时 {Ms} ms）：连不上种子服务",
                        q.Trim(), startedAt.ElapsedMilliseconds);

                    return Results.Json(
                        ApiResponse<TorrentSearchResponseDto>.Fail(5030,
                            "连不上本地种子服务。请先启动它：node bin/magnet-search.mjs serve"),
                        statusCode: StatusCodes.Status503ServiceUnavailable);
                }
                catch (JsonException ex)
                {
                    // 上游字段形状变了不该让前端吃 500：如实说明并指出是哪一步
                    logger.LogWarning(ex, "解析种子服务搜索响应失败");
                    return Results.Json(
                        ApiResponse<TorrentSearchResponseDto>.Fail(5001,
                            "本地种子服务返回了预期之外的数据格式（可能是版本不匹配）"),
                        statusCode: StatusCodes.Status502BadGateway);
                }
            })
            .WithName("SearchTorrent")
            .WithSummary("聚合搜索种子（默认按做种数排序）")
            .RequireRateLimiting("api");

            // 建任务：开始下载（边下边播的第一步）
            group.MapPost("/prepare", async (
                [FromBody] TorrentPrepareRequest request,
                ITorrentService service,
                CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(request?.Magnet))
                {
                    return Results.BadRequest(ApiResponse<TorrentTaskDto>.Fail(400, "缺少磁力链接"));
                }

                try
                {
                    var task = await service.PrepareAsync(request.Magnet.Trim(), ct);
                    return Results.Ok(ApiResponse<TorrentTaskDto>.Success(task));
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(ApiResponse<TorrentTaskDto>.Fail(400, ex.Message));
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
                {
                    return Results.Json(
                        ApiResponse<TorrentTaskDto>.Fail(5030, $"无法创建下载任务：{ex.Message}"),
                        statusCode: StatusCodes.Status503ServiceUnavailable);
                }
            })
            .WithName("PrepareTorrent")
            .WithSummary("按磁力创建下载任务并返回文件清单（元数据未就绪时返回空清单，前端轮询）");

            // 任务状态与文件清单
            group.MapGet("/task/{infoHash}", async (
                string infoHash,
                ITorrentService service,
                CancellationToken ct) =>
            {
                if (!IsInfoHash(infoHash))
                {
                    return Results.BadRequest(ApiResponse<TorrentTaskDto>.Fail(400, "info hash 必须是 40 位十六进制"));
                }

                try
                {
                    var task = await service.GetTaskAsync(infoHash, ct);
                    return task is null
                        ? Results.NotFound(ApiResponse<TorrentTaskDto>.Fail(404, "没有这个 info hash 的下载任务"))
                        : Results.Ok(ApiResponse<TorrentTaskDto>.Success(task));
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
                {
                    return Results.Json(
                        ApiResponse<TorrentTaskDto>.Fail(5030, "连不上本地种子服务"),
                        statusCode: StatusCodes.Status503ServiceUnavailable);
                }
                catch (JsonException)
                {
                    return Results.Json(
                        ApiResponse<TorrentTaskDto>.Fail(5001, "本地种子服务返回了预期之外的数据格式"),
                        statusCode: StatusCodes.Status502BadGateway);
                }
            })
            .WithName("GetTorrentTask")
            .WithSummary("下载任务状态 + 文件清单（含播放地址）");

            // 播放代理：透传 Range，转发字节流
            group.MapMethods("/stream/{infoHash}/{fileIndex:int}", new[] { "GET", "HEAD" }, async (
                string infoHash,
                int fileIndex,
                HttpContext http,
                ITorrentService service,
                ILoggerFactory loggerFactory,
                CancellationToken ct) =>
            {
                var logger = loggerFactory.CreateLogger("TorrentStream");

                if (!IsInfoHash(infoHash) || fileIndex < 0)
                {
                    return Results.BadRequest(ApiResponse<object>.Fail(400, "info hash 或文件下标非法"));
                }

                var headOnly = HttpMethods.IsHead(http.Request.Method);
                var range = http.Request.Headers.Range.ToString();

                TorrentStreamResponse? upstream;
                try
                {
                    upstream = await service.OpenStreamAsync(
                        infoHash, fileIndex, string.IsNullOrWhiteSpace(range) ? null : range, headOnly, ct);
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
                {
                    logger.LogWarning("种子播放代理失败（{Hash}/{Index}）：{Message}", infoHash, fileIndex, ex.Message);
                    return Results.Json(
                        ApiResponse<object>.Fail(5030, "连不上本地种子服务"),
                        statusCode: StatusCodes.Status503ServiceUnavailable);
                }

                if (upstream is null)
                {
                    return Results.NotFound(ApiResponse<object>.Fail(404, "种子服务没有返回内容"));
                }

                using (upstream)
                {
                    if (upstream.StatusCode >= 400)
                    {
                        // 把上游的可读原因转给前端（例如「等待数据超时：分片 N 仍未就绪」）
                        var message = ExtractUpstreamMessage(upstream.ErrorBody) ?? "种子服务返回错误";
                        logger.LogInformation("种子播放不可用（{Hash}/{Index}）：{Message}", infoHash, fileIndex, message);
                        return Results.Json(
                            ApiResponse<object>.Fail(upstream.StatusCode, message),
                            statusCode: upstream.StatusCode);
                    }

                    http.Response.StatusCode = upstream.StatusCode;
                    http.Response.ContentType = upstream.ContentType;
                    http.Response.Headers.AcceptRanges = "bytes";

                    if (upstream.ContentLength is long length)
                    {
                        http.Response.ContentLength = length;
                    }

                    if (!string.IsNullOrEmpty(upstream.ContentRange))
                    {
                        http.Response.Headers.ContentRange = upstream.ContentRange;
                    }

                    // HEAD 只回头部：向上游也发的 HEAD，本来就没有正文
                    if (headOnly)
                    {
                        return Results.Empty;
                    }

                    // 流式转发，不整段读进内存 —— 种子文件可能有几个 GB
                    await upstream.Content.CopyToAsync(http.Response.Body, ct);
                    return Results.Empty;
                }
            })
            .WithName("StreamTorrent")
            .WithSummary("边下边播：代理本地种子服务的 Range 流（数据没到会等待）");
        }

        private static bool IsInfoHash(string value)
            => !string.IsNullOrWhiteSpace(value) && value.Length == 40 && value.All(Uri.IsHexDigit);

        /// <summary>从种子服务的错误 JSON 里取出 message 字段（取不到就返回原文）</summary>
        private static string? ExtractUpstreamMessage(string? body)
        {
            if (string.IsNullOrWhiteSpace(body)) return null;

            try
            {
                using var document = JsonDocument.Parse(body);
                if (document.RootElement.TryGetProperty("error", out var error) &&
                    error.TryGetProperty("message", out var message))
                {
                    return message.GetString();
                }
            }
            catch (JsonException)
            {
                // 不是 JSON 就退回原文
            }

            return body.Length > 300 ? body[..300] + "…" : body;
        }
    }
}
