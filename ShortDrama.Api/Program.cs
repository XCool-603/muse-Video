using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using ShortDrama.Api.Access;
using ShortDrama.Api.Endpoints;
using ShortDrama.Infrastructure;
using ShortDrama.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);

// 苹果CMS 采集源配置：增删源只改这个文件，不用动代码
builder.Configuration.AddJsonFile("apple-cms-sources.json", optional: true, reloadOnChange: true);

// ============ 服务注册 ============
builder.Services.AddOpenApi();

// 访问口令门（私有实例入口保护）
builder.Services.Configure<AccessGateOptions>(builder.Configuration.GetSection(AccessGateOptions.SectionName));

// 基础设施：EF Core + 适配器 + 业务服务
builder.Services.AddShortDramaInfrastructure(builder.Configuration);

// JWT 认证
var jwtKey = builder.Configuration["Jwt:Key"] ?? "ShortDrama-Aggregation-Platform-Default-Secret-Key-2025";
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "ShortDrama",
            ValidAudience = builder.Configuration["Jwt:Audience"] ?? "ShortDramaClient",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });

builder.Services.AddAuthorization();

// 限流：保护聚合搜索与播放代理
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddFixedWindowLimiter("api", opt =>
    {
        opt.Window = TimeSpan.FromMinutes(1);
        opt.PermitLimit = 300;
        opt.QueueLimit = 20;
        opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });

    options.AddFixedWindowLimiter("stream", opt =>
    {
        opt.Window = TimeSpan.FromMinutes(1);
        opt.PermitLimit = 3000;
        opt.QueueLimit = 200;
        opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });
});

// CORS：开发期允许前端 dev server 跨域
builder.Services.AddCors(options =>
{
    options.AddPolicy("frontend", policy =>
    {
        policy.WithOrigins(
                "http://localhost:5173", "http://127.0.0.1:5173",
                "http://localhost:4173", "http://127.0.0.1:4173")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

var app = builder.Build();

// ============ 初始化数据库与种子数据 ============
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
    try
    {
        await DbSeeder.SeedAsync(app.Services, logger);
        var factory = scope.ServiceProvider.GetRequiredService<ShortDrama.Application.Adapters.IAdapterFactory>();
        await factory.RefreshAsync();
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "数据库初始化失败");
    }
}

// ============ 请求管道 ============
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

// 口令门放在最前面：静态文件之前，避免资源被绕过
app.UseMiddleware<AccessGateMiddleware>();

app.UseCors("frontend");
app.UseRateLimiter();

// 前端静态资源（发布时把 dist 拷贝到 wwwroot）
//
// 缓存策略必须区分对待，否则每次部署都会让用户撞上
// 「Failed to fetch dynamically imported module」：
//   index.html 引用的是带 hash 的 chunk 名，每次构建都变。浏览器一旦缓存了旧的
//   index.html，就会去请求已经不存在的老 chunk（404），而用户只能靠硬刷新自救。
//   所以 index.html 必须每次回源校验。
//   /assets/* 文件名里带内容 hash，内容变了文件名就变，可以放心长期缓存。
var spaStaticFiles = new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        var headers = ctx.Context.Response.Headers;

        if (ctx.File.Name.Equals("index.html", StringComparison.OrdinalIgnoreCase))
        {
            headers.CacheControl = "no-cache, no-store, must-revalidate";
            headers.Pragma = "no-cache";
            headers.Expires = "0";
        }
        else if (ctx.Context.Request.Path.StartsWithSegments("/assets"))
        {
            headers.CacheControl = "public, max-age=31536000, immutable";
        }
    }
};

app.UseDefaultFiles();
app.UseStaticFiles(spaStaticFiles);

app.UseAuthentication();
app.UseAuthorization();

// ============ 端点映射 ============
app.MapGet("/api/v1/info", () => Results.Ok(new
{
    name = "短剧聚合平台 API",
    version = "1.0.0",
    framework = ".NET 10 Minimal API",
    docs = "/openapi/v1.json",
    time = DateTimeOffset.UtcNow
}))
.WithTags("System")
.ExcludeFromDescription();

app.MapGet("/health", () => Results.Ok(new { status = "healthy", time = DateTimeOffset.UtcNow }))
   .WithTags("System");

app.MapDramaEndpoints();
app.MapPlayEndpoints();
app.MapUserEndpoints();
app.MapAdminEndpoints();
app.MapAccessGateEndpoints();

// SPA 回退：非 API 路径交给前端路由处理。
// 同样套用上面的缓存策略——回退返回的也是 index.html，不设 no-cache 一样会被缓存住。
app.MapFallbackToFile("index.html", spaStaticFiles);

app.Run();
