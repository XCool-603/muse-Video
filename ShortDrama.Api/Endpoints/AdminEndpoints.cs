using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using ShortDrama.Application.Adapters;
using ShortDrama.Application.DTOs;
using ShortDrama.Application.Services;

namespace ShortDrama.Api.Endpoints
{
    public static class AdminEndpoints
    {
        public static void MapAdminEndpoints(this WebApplication app)
        {
            var group = app.MapGroup("/api/v1/admin")
                .WithTags("Admin")
                .RequireAuthorization(policy => policy.RequireRole("admin"));

            // 数据看板
            group.MapGet("/dashboard", async (IAdminService admin, CancellationToken ct) =>
            {
                var data = await admin.GetDashboardAsync(ct);
                return Results.Ok(ApiResponse<DashboardDto>.Success(data));
            })
            .WithName("AdminDashboard");

            // 短剧列表（管理端，含未上架内容）
            group.MapGet("/dramas", async (
                [FromQuery] string? keyword,
                [FromQuery] string? category,
                [FromQuery] string? platform,
                [FromQuery] int page,
                [FromQuery] int pageSize,
                IDramaService dramaService,
                CancellationToken ct) =>
            {
                var result = await dramaService.QueryAsync(keyword, category, platform, "new",
                    page <= 0 ? 1 : page, pageSize <= 0 ? 10 : pageSize, ct);
                return Results.Ok(ApiResponse<PagedResult<DramaDto>>.Success(result));
            })
            .WithName("AdminListDramas");

            // 新增短剧
            group.MapPost("/dramas", async (
                [FromBody] AdminDramaSaveRequest request,
                IAdminService admin,
                CancellationToken ct) =>
            {
                var id = await admin.CreateDramaAsync(request, ct);
                return Results.Ok(ApiResponse<long>.Success(id, "创建成功"));
            })
            .WithName("AdminCreateDrama");

            // 编辑短剧
            group.MapPut("/dramas/{id:long}", async (
                long id,
                [FromBody] AdminDramaSaveRequest request,
                IAdminService admin,
                CancellationToken ct) =>
            {
                var ok = await admin.UpdateDramaAsync(id, request, ct);
                return ok
                    ? Results.Ok(ApiResponse<bool>.Success(true, "更新成功"))
                    : Results.NotFound(ApiResponse<bool>.Fail(404, "短剧不存在"));
            })
            .WithName("AdminUpdateDrama");

            // 删除短剧
            group.MapDelete("/dramas/{id:long}", async (
                long id,
                IAdminService admin,
                CancellationToken ct) =>
            {
                var ok = await admin.DeleteDramaAsync(id, ct);
                return ok
                    ? Results.Ok(ApiResponse<bool>.Success(true, "删除成功"))
                    : Results.NotFound(ApiResponse<bool>.Fail(404, "短剧不存在"));
            })
            .WithName("AdminDeleteDrama");

            // 剧集配置（免费/锁定/播放地址）
            group.MapPut("/dramas/{id:long}/episodes/{episode:int}", async (
                long id,
                int episode,
                [FromBody] EpisodeUpdateRequest request,
                IAdminService admin,
                CancellationToken ct) =>
            {
                var ok = await admin.UpdateEpisodeAsync(id, episode, request.IsFree, request.IsLocked, request.VideoUrl ?? string.Empty, ct);
                return ok
                    ? Results.Ok(ApiResponse<bool>.Success(true, "剧集已更新"))
                    : Results.NotFound(ApiResponse<bool>.Fail(404, "剧集不存在"));
            })
            .WithName("AdminUpdateEpisode");

            // 平台源列表
            group.MapGet("/platforms", async (IAdminService admin, CancellationToken ct) =>
            {
                var list = await admin.GetPlatformSourcesAsync(ct);
                return Results.Ok(ApiResponse<List<PlatformSourceDto>>.Success(list));
            })
            .WithName("AdminListPlatforms");

            // 启用/禁用平台源
            group.MapPut("/platforms/{id:long}/toggle", async (
                long id,
                [FromQuery] bool enabled,
                IAdminService admin,
                CancellationToken ct) =>
            {
                var ok = await admin.TogglePlatformAsync(id, enabled, ct);
                return ok
                    ? Results.Ok(ApiResponse<bool>.Success(true, enabled ? "已启用" : "已禁用"))
                    : Results.NotFound(ApiResponse<bool>.Fail(404, "平台配置不存在"));
            })
            .WithName("AdminTogglePlatform");

            // 触发平台数据同步
            group.MapPost("/platforms/{platformCode}/sync", async (
                string platformCode,
                IAggregationService aggregation,
                CancellationToken ct) =>
            {
                var count = await aggregation.SyncPlatformAsync(platformCode, ct);
                return Results.Ok(ApiResponse<int>.Success(count, $"同步完成，共处理 {count} 部短剧"));
            })
            .WithName("AdminSyncPlatform");
        }
    }

    public class EpisodeUpdateRequest
    {
        public bool IsFree { get; set; } = true;
        public bool IsLocked { get; set; }
        public string? VideoUrl { get; set; }
    }
}
