using System.Security.Claims;
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
    public static class DramaEndpoints
    {
        public static void MapDramaEndpoints(this WebApplication app)
        {
            var group = app.MapGroup("/api/v1/drama").WithTags("Drama");

            // 聚合搜索
            group.MapGet("/search", async (
                [FromQuery] string? q,
                [FromQuery] int page,
                [FromQuery] int pageSize,
                [FromQuery] string? platform,
                IAggregationService service,
                CancellationToken ct) =>
            {
                page = page <= 0 ? 1 : page;
                pageSize = pageSize <= 0 ? 12 : pageSize;
                var result = await service.SearchAsync(q ?? string.Empty, page, pageSize, platform, ct);
                return Results.Ok(ApiResponse<PagedResult<DramaDto>>.Success(result));
            })
            .WithName("SearchDrama")
            .WithSummary("聚合搜索短剧（跨平台并行 + 去重合并）")
            // 一次搜索会并行打十几个上游，必须限流：原先 "api" 策略只注册没挂到任何端点，实际是空转
            .RequireRateLimiting("api");

            // 榜单
            group.MapGet("/rank", async (
                [FromQuery] string? type,
                [FromQuery] int limit,
                IAggregationService service,
                CancellationToken ct) =>
            {
                var result = await service.GetRankAsync(type ?? "hot", limit <= 0 ? 20 : limit, ct);
                return Results.Ok(ApiResponse<System.Collections.Generic.List<DramaDto>>.Success(result));
            })
            .WithName("GetRank")
            .WithSummary("聚合榜单：推荐 / 热播 / 新剧")
            .RequireRateLimiting("api");

            // 今日上新
            group.MapGet("/latest", async (
                [FromQuery] string? category,
                [FromQuery] int limit,
                IAggregationService service,
                CancellationToken ct) =>
            {
                var result = await service.GetLatestAsync(category ?? "全部", limit <= 0 ? 20 : limit, ct);
                return Results.Ok(ApiResponse<System.Collections.Generic.List<DramaDto>>.Success(result));
            })
            .WithName("GetLatest")
            .WithSummary("今日上新")
            .RequireRateLimiting("api");

            // 分类列表
            group.MapGet("/categories", async (IDramaService service, CancellationToken ct) =>
            {
                var categories = await service.GetCategoriesAsync(ct);
                return Results.Ok(ApiResponse<System.Collections.Generic.List<string>>.Success(categories));
            })
            .WithName("GetCategories");

            // 平台列表（前端筛选用，含内容量与可播性）
            group.MapGet("/platforms", async (IDramaService service, CancellationToken ct) =>
            {
                var platforms = await service.GetPlatformsAsync(ct);
                return Results.Ok(ApiResponse<System.Collections.Generic.List<PlatformInfoDto>>.Success(platforms));
            })
            .WithName("GetPlatforms")
            .WithSummary("平台筛选列表");

            // 本地库列表（支持关键词/分类/平台/排序/分页）
            // live=true 时把分类/关键词交给聚合搜索，会并行打各平台接口，
            // 返回「实时结果 + 本地库」去重后的合集；否则只查本地库。
            group.MapGet("/list", async (
                [FromQuery] string? keyword,
                [FromQuery] string? category,
                [FromQuery] string? platform,
                [FromQuery] string? sortBy,
                [FromQuery] int page,
                [FromQuery] int pageSize,
                // 用可空 bool 而不是 bool + 默认值：C# 要求可选参数排在必填参数之后，
                // 而这里后面还有 service/aggregation/ct，带默认值会编译不过（CS1737）。
                // 可空类型天然可选，不传就是 null。
                [FromQuery] bool? live,
                IDramaService service,
                IAggregationService aggregation,
                CancellationToken ct) =>
            {
                page = page <= 0 ? 1 : page;
                pageSize = pageSize <= 0 ? 12 : pageSize;

                // 实时模式：分类即搜索词。
                // 采集源没有可靠的分类查询接口（实测 t/type_id 常被忽略），
                // 但把分类名当关键词搜索是有效的（「霸总」「穿越」这类词命中率很高）。
                if (live == true)
                {
                    var q = !string.IsNullOrWhiteSpace(keyword) ? keyword : category;
                    if (!string.IsNullOrWhiteSpace(q) && q != "全部")
                    {
                        // 必须「合并」而不是「替换」：
                        //   本地是按 Category 字段查（权威，例如「穿越」有 626 部）
                        //   实时是按标题关键词查（只能命中标题里带该词的）
                        // 直接替换会让本地那批分类正确、标题不含关键词的剧凭空消失。
                        var take = Math.Clamp(page * pageSize, pageSize, 100);

                        var local = await service.QueryAsync(keyword, category, platform,
                            sortBy ?? "hot", 1, take, ct);
                        var liveResult = await aggregation.SearchAsync(q, 1, take, platform, ct);

                        // 本地结果排在前面（分类字段权威，且已在库、可直接播）
                        var merged = new List<DramaDto>(local.Items);
                        var seen = new HashSet<string>(merged.Select(d => d.Title), StringComparer.Ordinal);
                        var liveOnly = 0;
                        foreach (var item in liveResult.Items)
                        {
                            if (seen.Add(item.Title))
                            {
                                merged.Add(item);
                                liveOnly++;
                            }
                        }

                        // 总数不能用 merged.Count（那只是本次取到的条数，会把 63 报成 18）。
                        // 本地有内容 → 本地总数 + 实时新增；本地为空 → 以实时总数为准（它才是能翻页的那个）。
                        var total = local.Total > 0
                            ? local.Total + liveOnly
                            : Math.Max(liveResult.Total, merged.Count);

                        var paged = merged.Skip((page - 1) * pageSize).Take(pageSize).ToList();
                        return Results.Ok(ApiResponse<PagedResult<DramaDto>>.Success(
                            PagedResult<DramaDto>.Create(paged, total, page, pageSize)));
                    }
                }

                var result = await service.QueryAsync(keyword, category, platform, sortBy ?? "hot",
                    page, pageSize, ct);
                return Results.Ok(ApiResponse<PagedResult<DramaDto>>.Success(result));
            })
            .WithName("ListDrama")
            .RequireRateLimiting("api");

            // 短剧详情
            group.MapGet("/{id:long}", async (long id, IDramaService service, CancellationToken ct) =>
            {
                var drama = await service.GetDetailAsync(id, ct);
                return drama is null
                    ? Results.NotFound(ApiResponse<DramaDetailDto>.Fail(404, "短剧不存在"))
                    : Results.Ok(ApiResponse<DramaDetailDto>.Success(drama));
            })
            .WithName("GetDramaDetail");

            // 剧集列表
            group.MapGet("/{id:long}/episodes", async (long id, IDramaService service, CancellationToken ct) =>
            {
                var episodes = await service.GetEpisodesAsync(id, ct);
                return Results.Ok(ApiResponse<System.Collections.Generic.List<EpisodeDto>>.Success(episodes));
            })
            .WithName("GetEpisodes");

            // 按需入库：本地没有就把这部剧从平台接口拉下来落库，返回本地 Id。
            //
            // 这是「本地只存一部分，其余用接口实时找」这套模式的关键一环：
            // 聚合搜索返回的实时结果 Id=0（还没落库），前端点开时先查本地，
            // 命中直接播，不命中才回源拉取并落库，下次就命中本地了。
            //
            // 匿名可用：搜索本身就是匿名的，点开搜索结果不该要求先登录。
            // 站点外面还有访问口令门，加上 api 限流，避免被刷着灌库。
            group.MapPost("/resolve", async (
                [FromQuery] string? platform,
                [FromQuery] string? dramaId,
                IDramaService service,
                ILoggerFactory loggerFactory,
                CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(platform) || string.IsNullOrWhiteSpace(dramaId))
                {
                    return Results.BadRequest(ApiResponse<long>.Fail(400, "platform 与 dramaId 必填"));
                }

                // 先查本地，命中就不打上游
                var localId = await service.FindLocalIdAsync(platform, dramaId, ct);
                if (localId > 0)
                {
                    return Results.Ok(ApiResponse<long>.Success(localId, "已在库中"));
                }

                try
                {
                    var id = await service.UpsertFromPlatformAsync(platform, dramaId, ct);
                    loggerFactory.CreateLogger("DramaResolve")
                        .LogInformation("按需入库 {Platform}/{DramaId} -> {Id}", platform, dramaId, id);
                    return Results.Ok(ApiResponse<long>.Success(id, "已入库"));
                }
                catch (Exception ex)
                {
                    loggerFactory.CreateLogger("DramaResolve")
                        .LogWarning("按需入库失败 {Platform}/{DramaId}（{Reason}）",
                            platform, dramaId, ex.GetBaseException().Message);
                    return Results.NotFound(ApiResponse<long>.Fail(404, "该平台未找到此短剧，或平台暂时不可用"));
                }
            })
            .WithName("ResolveDrama")
            .WithSummary("按需入库：把实时搜索结果落库并返回本地 Id")
            .AllowAnonymous()
            .RequireRateLimiting("api");

            // 按平台原始 ID 导入/同步到本地库
            group.MapPost("/sync", async (
                [FromQuery] string platform,
                [FromQuery] string dramaId,
                IDramaService service,
                CancellationToken ct) =>
            {
                var id = await service.UpsertFromPlatformAsync(platform, dramaId, ct);
                return Results.Ok(ApiResponse<long>.Success(id, "同步成功"));
            })
            .WithName("SyncDrama")
            .RequireAuthorization(policy => policy.RequireRole("admin"));
        }
    }
}
