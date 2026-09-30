using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ShortDrama.Application.Adapters;
using ShortDrama.Application.Services;
using ShortDrama.Infrastructure.Adapters;
using ShortDrama.Infrastructure.Data;
using ShortDrama.Infrastructure.Services;

namespace ShortDrama.Infrastructure
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddShortDramaInfrastructure(this IServiceCollection services, IConfiguration configuration)
        {
            // 数据库：默认 SQLite（零依赖本地运行），可通过配置切换到 PostgreSQL / MySQL
            var provider = configuration["Database:Provider"] ?? "Sqlite";
            var connectionString = configuration.GetConnectionString("Default")
                                   ?? "Data Source=shortdrama.db";

            services.AddDbContext<AppDbContext>(options =>
            {
                switch (provider.ToLowerInvariant())
                {
                    case "postgresql":
                    case "postgres":
                        options.UseNpgsql(connectionString);
                        break;
                    case "mysql":
                        options.UseMySQL(connectionString);
                        break;
                    default:
                        options.UseSqlite(connectionString);
                        break;
                }
            });

            // 内存缓存（生产可替换为 Redis）
            services.AddMemoryCache();

            // 苹果CMS 采集源配置（apple-cms-sources.json）
            var appleCms = configuration.GetSection(AppleCmsOptions.SectionName).Get<AppleCmsOptions>()
                           ?? new AppleCmsOptions();
            services.AddSingleton(appleCms);

            // 播放代理策略（appsettings.json 的 Playback 段）
            // 默认直连 CDN，视频流量不经过服务器；遇到不发 CORS 头的源再打开代理
            var playback = configuration.GetSection(PlaybackOptions.SectionName).Get<PlaybackOptions>()
                           ?? new PlaybackOptions();
            services.AddSingleton(playback);

            // 采集源抓取用的 HttpClient
            services.AddHttpClient("applecms", client =>
            {
                client.Timeout = TimeSpan.FromSeconds(Math.Clamp(appleCms.TimeoutSeconds, 3, 60));
                client.DefaultRequestHeaders.UserAgent.ParseAdd(
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36");
                client.DefaultRequestHeaders.Accept.ParseAdd("application/json, text/plain, */*");
            });

            // ===== 黄豆短剧 hddj.tv（真实站点，协议来自公开逆向文档）=====
            var huangDou = configuration.GetSection(HuangDouOptions.SectionName).Get<HuangDouOptions>()
                           ?? new HuangDouOptions();

            if (huangDou.Enabled && !string.IsNullOrWhiteSpace(huangDou.BaseUrl))
            {
                services.AddSingleton(huangDou);
                services.AddHttpClient("huangdou", client =>
                {
                    client.Timeout = TimeSpan.FromSeconds(Math.Clamp(huangDou.TimeoutSeconds, 3, 60));
                    client.DefaultRequestHeaders.UserAgent.ParseAdd(
                        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36");
                    client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/json,*/*");
                });
                services.AddSingleton<IPlatformAdapter>(sp =>
                {
                    var factory = sp.GetRequiredService<IHttpClientFactory>();
                    var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("HuangDou");
                    return new HuangDouAdapter(huangDou, factory.CreateClient("huangdou"), logger);
                });
            }

            // ===== 红果短剧官网 hongguoduanju.com（抖音/番茄系官方 H5）=====
            // 目录（榜单/分类/详情）为服务端渲染，可索引；视频为 DRM 加密，不可服务端播放。
            var hongGuo = configuration.GetSection(HongGuoWebOptions.SectionName).Get<HongGuoWebOptions>()
                          ?? new HongGuoWebOptions();

            if (hongGuo.Enabled && !string.IsNullOrWhiteSpace(hongGuo.BaseUrl))
            {
                services.AddSingleton(hongGuo);
                services.AddHttpClient("hongguoweb", client =>
                {
                    client.Timeout = TimeSpan.FromSeconds(Math.Clamp(hongGuo.TimeoutSeconds, 3, 60));
                    client.DefaultRequestHeaders.UserAgent.ParseAdd(
                        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36");
                    client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,*/*");
                });
                services.AddSingleton<IPlatformAdapter>(sp =>
                {
                    var factory = sp.GetRequiredService<IHttpClientFactory>();
                    var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("HongGuoWeb");
                    return new HongGuoWebAdapter(hongGuo, factory.CreateClient("hongguoweb"), logger);
                });
            }

            // ===== 黄果短剧 huangguodrama.ai =====
            // 接口是站点自己在 robots.txt 里 Allow 并发布 OpenAPI 规范的公开 catalog API（只读、限流 60/min）；
            // 视频为明文 MP4（无 DRM/鉴权/签名，CORS 全开）。
            var huangGuoAi = configuration.GetSection(HuangGuoAiOptions.SectionName).Get<HuangGuoAiOptions>()
                             ?? new HuangGuoAiOptions();

            if (huangGuoAi.Enabled && !string.IsNullOrWhiteSpace(huangGuoAi.BaseUrl))
            {
                services.AddSingleton(huangGuoAi);
                services.AddHttpClient("huangguoai", client =>
                {
                    client.Timeout = TimeSpan.FromSeconds(Math.Clamp(huangGuoAi.TimeoutSeconds, 3, 60));
                    client.DefaultRequestHeaders.UserAgent.ParseAdd(
                        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36");
                    client.DefaultRequestHeaders.Accept.ParseAdd("application/json, text/html, */*");
                });
                services.AddSingleton<IPlatformAdapter>(sp =>
                {
                    var factory = sp.GetRequiredService<IHttpClientFactory>();
                    var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("HuangGuoAi");
                    return new HuangGuoAiAdapter(huangGuoAi, factory.CreateClient("huangguoai"), logger);
                });
            }

            // ===== 演示用模拟适配器（Demo:Enabled=true 时才注册）=====
            // 关闭后仅使用真实源
            if (configuration.GetValue("Demo:Enabled", false))
            {
                services.AddSingleton<HongGuoDemoAdapter>();
                services.AddSingleton<HuangDouDemoAdapter>();
                services.AddSingleton<JuGuoAdapter>();
                services.AddSingleton<IPlatformAdapter>(sp => sp.GetRequiredService<HongGuoDemoAdapter>());
                services.AddSingleton<IPlatformAdapter>(sp => sp.GetRequiredService<HuangDouDemoAdapter>());
                services.AddSingleton<IPlatformAdapter>(sp => sp.GetRequiredService<JuGuoAdapter>());
                services.AddSingleton<IPlatformAdapter>(sp => new GenericAdapter("demo_yeguo", "野果短剧(演示)"));
                services.AddSingleton<IPlatformAdapter>(sp => new GenericAdapter("demo_diguo", "帝果短剧(演示)"));
            }

            // ===== 苹果CMS 真实采集源：配置里加一条即接入一个新平台 =====
            if (appleCms.Enabled)
            {
                var enabledSources = appleCms.Sources
                    .Where(s => s.Enabled && !string.IsNullOrWhiteSpace(s.Api) && !string.IsNullOrWhiteSpace(s.PlatformCode))
                    .ToList();

                foreach (var source in enabledSources)
                {
                    var captured = source;
                    services.AddSingleton<IPlatformAdapter>(sp =>
                    {
                        var factory = sp.GetRequiredService<IHttpClientFactory>();
                        var logger = sp.GetRequiredService<ILoggerFactory>()
                            .CreateLogger($"AppleCms.{captured.PlatformCode}");
                        return new AppleCmsAdapter(captured, appleCms, factory.CreateClient("applecms"), logger);
                    });
                }
            }

            services.AddSingleton<IAdapterFactory, AdapterFactory>();

            // 业务服务
            services.AddScoped<SourceBootstrapper>();
            services.AddScoped<IAggregationService, AggregationService>();
            services.AddScoped<IDramaService, DramaService>();
            services.AddScoped<IPlayService, PlayService>();
            services.AddScoped<IAuthService, AuthService>();
            services.AddScoped<IFavoriteService, FavoriteService>();
            services.AddScoped<IAdminService, AdminService>();
            services.AddSingleton<IAdFilterService, AdFilterService>();

            // 流媒体抓取用的 HttpClient
            services.AddHttpClient("stream", client =>
            {
                client.Timeout = TimeSpan.FromSeconds(20);
                client.DefaultRequestHeaders.UserAgent.ParseAdd(
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36");
                client.DefaultRequestHeaders.Referrer = new Uri("https://www.example.com/");
            });

            return services;
        }
    }
}
