using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace ShortDrama.Api.Access
{
    /// <summary>
    /// 访问口令门中间件。
    ///
    /// 行为：
    ///   - 未通过 → HTML 请求 302 到入口页；API 请求返回 401 JSON
    ///   - 已通过 → 放行
    ///   - 入口页、口令校验接口、静态资源、健康检查、OpenAPI 文档始终放行
    ///
    /// 放在管道最前面（静态文件之前），避免资源被绕过。
    /// </summary>
    public class AccessGateMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly AccessGateOptions _options;

        public AccessGateMiddleware(RequestDelegate next, IOptions<AccessGateOptions> options)
        {
            _next = next;
            _options = options.Value;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (!_options.Enabled || string.IsNullOrEmpty(_options.Password))
            {
                await _next(context);
                return;
            }

            if (IsAlwaysAllowed(context.Request.Path))
            {
                await _next(context);
                return;
            }

            var cookie = context.Request.Cookies[_options.CookieName];
            if (_options.Verify(cookie))
            {
                await _next(context);
                return;
            }

            // API 请求：返回 401，让前端知道要去入口页
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/json; charset=utf-8";
                await context.Response.WriteAsJsonAsync(new
                {
                    code = 4010,
                    message = "需要访问口令",
                    data = (object?)null,
                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                });
                return;
            }

            // 页面请求：跳入口页
            context.Response.Redirect(_options.GatePath);
        }

        /// <summary>无需口令即可访问的路径</summary>
        private bool IsAlwaysAllowed(PathString path)
        {
            if (path.StartsWithSegments(_options.GatePath)) return true;
            if (path.StartsWithSegments(_options.VerifyPath)) return true;
            if (path.StartsWithSegments("/health")) return true;
            if (path.StartsWithSegments("/openapi")) return true;
            if (path.StartsWithSegments("/assets")) return true;
            if (path.StartsWithSegments("/favicon")) return true;

            // 站点图标等零散静态文件
            var value = path.Value ?? string.Empty;
            if (value.EndsWith(".ico", StringComparison.OrdinalIgnoreCase) ||
                value.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                value.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) ||
                value.EndsWith(".webmanifest", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }
    }
}
