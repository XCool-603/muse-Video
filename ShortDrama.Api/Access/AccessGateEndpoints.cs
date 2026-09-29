using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using ShortDrama.Application.DTOs;

namespace ShortDrama.Api.Access
{
    public static class AccessGateEndpoints
    {
        public static void MapAccessGateEndpoints(this WebApplication app)
        {
            var options = app.Services.GetRequiredService<IOptions<AccessGateOptions>>().Value;

            var group = app.MapGroup(options.VerifyPath).WithTags("Access");

            // 校验口令并签发 Cookie
            group.MapPost("/", (AccessRequest request, HttpContext http, IOptions<AccessGateOptions> opts) =>
            {
                var o = opts.Value;

                if (string.IsNullOrEmpty(o.Password))
                {
                    return Results.Ok(ApiResponse<bool>.Success(true, "未启用口令门"));
                }

                if (!string.Equals(request?.Password, o.Password, StringComparison.Ordinal))
                {
                    return Results.Json(
                        ApiResponse<bool>.Fail(4010, "口令不正确"),
                        statusCode: StatusCodes.Status401Unauthorized);
                }

                http.Response.Cookies.Append(o.CookieName, o.ComputeToken(), new CookieOptions
                {
                    HttpOnly = true,
                    SameSite = SameSiteMode.Lax,
                    Secure = http.Request.IsHttps,
                    MaxAge = TimeSpan.FromDays(Math.Max(1, o.ValidDays)),
                    Path = "/"
                });

                return Results.Ok(ApiResponse<bool>.Success(true, "口令正确"));
            })
            .WithName("VerifyAccess")
            .AllowAnonymous();

            // 查询当前是否已通过
            group.MapGet("/status", (HttpContext http, IOptions<AccessGateOptions> opts) =>
            {
                var o = opts.Value;

                if (!o.Enabled || string.IsNullOrEmpty(o.Password))
                {
                    return Results.Ok(ApiResponse<bool>.Success(true));
                }

                var passed = o.Verify(http.Request.Cookies[o.CookieName]);
                return Results.Ok(ApiResponse<bool>.Success(passed));
            })
            .WithName("AccessStatus")
            .AllowAnonymous();

            // 主动退出（清除 Cookie）
            group.MapPost("/logout", (HttpContext http, IOptions<AccessGateOptions> opts) =>
            {
                var o = opts.Value;
                http.Response.Cookies.Delete(o.CookieName, new CookieOptions { Path = "/" });
                return Results.Ok(ApiResponse<bool>.Success(true, "已退出"));
            })
            .WithName("AccessLogout")
            .AllowAnonymous();
        }
    }

    public class AccessRequest
    {
        public string? Password { get; set; }
    }
}
