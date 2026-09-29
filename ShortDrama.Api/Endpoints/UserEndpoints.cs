using System.Collections.Generic;
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
    public static class UserEndpoints
    {
        public static void MapUserEndpoints(this WebApplication app)
        {
            var group = app.MapGroup("/api/v1/user").WithTags("User");

            // 注册
            group.MapPost("/register", async (
                [FromBody] RegisterRequest request,
                IAuthService auth,
                CancellationToken ct) =>
            {
                var result = await auth.RegisterAsync(request, ct);
                return result is null
                    ? Results.BadRequest(ApiResponse<LoginResponse>.Fail(4001, "用户名已存在或参数不合法"))
                    : Results.Ok(ApiResponse<LoginResponse>.Success(result, "注册成功"));
            })
            .WithName("Register");

            // 登录
            group.MapPost("/login", async (
                [FromBody] LoginRequest request,
                IAuthService auth,
                CancellationToken ct) =>
            {
                var result = await auth.LoginAsync(request, ct);
                return result is null
                    ? Results.Json(ApiResponse<LoginResponse>.Fail(4002, "用户名或密码错误"), statusCode: 401)
                    : Results.Ok(ApiResponse<LoginResponse>.Success(result, "登录成功"));
            })
            .WithName("Login");

            // 个人信息
            group.MapGet("/profile", async (
                IAuthService auth,
                HttpContext http,
                CancellationToken ct) =>
            {
                var userId = http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrWhiteSpace(userId)) return Results.Unauthorized();

                var profile = await auth.GetProfileAsync(userId, ct);
                return profile is null
                    ? Results.NotFound(ApiResponse<UserDto>.Fail(404, "用户不存在"))
                    : Results.Ok(ApiResponse<UserDto>.Success(profile));
            })
            .WithName("GetProfile")
            .RequireAuthorization();

            // 收藏列表
            group.MapGet("/favorites", async (
                IFavoriteService favorites,
                HttpContext http,
                CancellationToken ct) =>
            {
                var userId = http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrWhiteSpace(userId)) return Results.Unauthorized();

                var list = await favorites.ListAsync(userId, ct);
                return Results.Ok(ApiResponse<List<DramaDto>>.Success(list));
            })
            .WithName("ListFavorites")
            .RequireAuthorization();

            // 添加收藏
            group.MapPost("/favorites/{dramaId:long}", async (
                long dramaId,
                IFavoriteService favorites,
                HttpContext http,
                CancellationToken ct) =>
            {
                var userId = http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrWhiteSpace(userId)) return Results.Unauthorized();

                var ok = await favorites.AddAsync(userId, dramaId, ct);
                return ok
                    ? Results.Ok(ApiResponse<bool>.Success(true, "收藏成功"))
                    : Results.BadRequest(ApiResponse<bool>.Fail(4004, "收藏失败"));
            })
            .WithName("AddFavorite")
            .RequireAuthorization();

            // 取消收藏
            group.MapDelete("/favorites/{dramaId:long}", async (
                long dramaId,
                IFavoriteService favorites,
                HttpContext http,
                CancellationToken ct) =>
            {
                var userId = http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrWhiteSpace(userId)) return Results.Unauthorized();

                var ok = await favorites.RemoveAsync(userId, dramaId, ct);
                return Results.Ok(ApiResponse<bool>.Success(ok, ok ? "已取消收藏" : "未收藏该短剧"));
            })
            .WithName("RemoveFavorite")
            .RequireAuthorization();

            // 是否已收藏
            group.MapGet("/favorites/{dramaId:long}/status", async (
                long dramaId,
                IFavoriteService favorites,
                HttpContext http,
                CancellationToken ct) =>
            {
                var userId = http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrWhiteSpace(userId)) return Results.Ok(ApiResponse<bool>.Success(false));

                var isFav = await favorites.IsFavoriteAsync(userId, dramaId, ct);
                return Results.Ok(ApiResponse<bool>.Success(isFav));
            })
            .WithName("FavoriteStatus");
        }
    }
}
