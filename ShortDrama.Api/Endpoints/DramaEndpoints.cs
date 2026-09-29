using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
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
            .WithSummary("聚合搜索短剧（跨平台并行 + 去重合并）");

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
            .WithSummary("聚合榜单：推荐 / 热播 / 新剧");

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
            .WithSummary("今日上新");

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
            group.MapGet("/list", async (
                [FromQuery] string? keyword,
                [FromQuery] string? category,
                [FromQuery] string? platform,
                [FromQuery] string? sortBy,
                [FromQuery] int page,
                [FromQuery] int pageSize,
                IDramaService service,
                CancellationToken ct) =>
            {
                var result = await service.QueryAsync(keyword, category, platform, sortBy ?? "hot",
                    page <= 0 ? 1 : page, pageSize <= 0 ? 12 : pageSize, ct);
                return Results.Ok(ApiResponse<PagedResult<DramaDto>>.Success(result));
            })
            .WithName("ListDrama");

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
