using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ShortDrama.Application.Adapters;
using ShortDrama.Application.Services;
using ShortDrama.Domain.Entities;
using ShortDrama.Infrastructure.Adapters;
using ShortDrama.Infrastructure.Services;

namespace ShortDrama.Infrastructure.Data
{
    /// <summary>数据库初始化与种子数据（平台源配置、管理员账号、首批短剧同步）。</summary>
    public static class DbSeeder
    {
        /// <summary>
        /// 演示用模拟适配器的平台编码，关闭演示模式时会被清理。
        /// 必须与真实适配器的编码区分开 —— 早期两者都叫 "hongguo"，
        /// 导致每次启动都会把真实红果数据一并删掉。
        /// </summary>
        private static readonly string[] DemoPlatformCodes =
            { "demo_hongguo", "demo_huangdou", "demo_juguo", "demo_yeguo", "demo_diguo" };

        public static async Task SeedAsync(IServiceProvider services, ILogger logger)
        {
            using var scope = services.CreateScope();
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<AppDbContext>();

            await db.Database.EnsureCreatedAsync();

            var config = sp.GetRequiredService<IConfiguration>();
            var demoEnabled = config.GetValue("Demo:Enabled", false);

            // 1) 平台源配置
            var platforms = new List<PlatformSource>();

            if (demoEnabled)
            {
                // 注意：以下 BaseUrl 是占位符，不是真实官网（红果官网为 hongguoduanju.com，
                // 其余几个平台的真实地址未经验证）。演示模式只使用内置假数据，不会请求这些地址。
                platforms.AddRange(new List<PlatformSource>
                {
                    new() { PlatformCode = "demo_hongguo", PlatformName = "红果短剧(演示)", BaseUrl = "(内置模拟数据)", AdapterType = nameof(HongGuoDemoAdapter), IsEnabled = true },
                    new() { PlatformCode = "demo_huangdou", PlatformName = "黄豆短剧(演示)", BaseUrl = "(内置模拟数据)", AdapterType = nameof(HuangDouDemoAdapter), IsEnabled = true },
                    new() { PlatformCode = "demo_juguo", PlatformName = "剧果短剧(演示)", BaseUrl = "(内置模拟数据)", AdapterType = nameof(JuGuoAdapter), IsEnabled = true },
                    new() { PlatformCode = "demo_yeguo", PlatformName = "野果短剧(演示)", BaseUrl = "(内置模拟数据)", AdapterType = nameof(GenericAdapter), IsEnabled = true },
                    new() { PlatformCode = "demo_diguo", PlatformName = "帝果短剧(演示)", BaseUrl = "(内置模拟数据)", AdapterType = nameof(GenericAdapter), IsEnabled = true }
                });
            }
            else
            {
                // 关闭演示模式：清理历史遗留的模拟数据，避免污染真实搜索结果
                var demoDramas = await db.Dramas.Where(d => DemoPlatformCodes.Contains(d.PlatformCode)).ToListAsync();
                if (demoDramas.Count > 0)
                {
                    db.Dramas.RemoveRange(demoDramas);
                    logger.LogInformation("演示模式已关闭，清理 {Count} 部模拟短剧", demoDramas.Count);
                }

                var demoPlatforms = await db.PlatformSources.Where(s => DemoPlatformCodes.Contains(s.PlatformCode)).ToListAsync();
                if (demoPlatforms.Count > 0)
                {
                    db.PlatformSources.RemoveRange(demoPlatforms);
                }

                await db.SaveChangesAsync();
            }

            // 苹果CMS 采集源（来自 apple-cms-sources.json），一并落库以便管理后台开关
            var appleCms = sp.GetService<AppleCmsOptions>();
            if (appleCms is { Enabled: true })
            {
                foreach (var source in appleCms.Sources.Where(s => !string.IsNullOrWhiteSpace(s.PlatformCode)))
                {
                    platforms.Add(new PlatformSource
                    {
                        PlatformCode = source.PlatformCode,
                        PlatformName = source.PlatformName,
                        BaseUrl = source.Api,
                        AdapterType = nameof(AppleCmsAdapter),
                        IsEnabled = source.Enabled
                    });
                }
            }

            // 红果短剧官网 hongguoduanju.com（真实站点，仅目录可索引）
            var hongGuo = sp.GetService<HongGuoWebOptions>();
            if (hongGuo is { Enabled: true } && !string.IsNullOrWhiteSpace(hongGuo.BaseUrl))
            {
                platforms.Add(new PlatformSource
                {
                    PlatformCode = hongGuo.PlatformCode,
                    PlatformName = hongGuo.PlatformName,
                    BaseUrl = hongGuo.BaseUrl,
                    AdapterType = nameof(HongGuoWebAdapter),
                    IsEnabled = true
                });
            }

            // 黄果短剧 huangguodrama.ai（站点公开 catalog API + 明文 MP4）
            var huangGuoAi = sp.GetService<HuangGuoAiOptions>();
            if (huangGuoAi is { Enabled: true } && !string.IsNullOrWhiteSpace(huangGuoAi.BaseUrl))
            {
                platforms.Add(new PlatformSource
                {
                    PlatformCode = huangGuoAi.PlatformCode,
                    PlatformName = huangGuoAi.PlatformName,
                    BaseUrl = huangGuoAi.BaseUrl,
                    AdapterType = nameof(HuangGuoAiAdapter),
                    IsEnabled = true
                });
            }

            // 黄豆短剧 hddj.tv（真实站点）
            var huangDou = sp.GetService<HuangDouOptions>();
            if (huangDou is { Enabled: true } && !string.IsNullOrWhiteSpace(huangDou.BaseUrl))
            {
                platforms.Add(new PlatformSource
                {
                    PlatformCode = huangDou.PlatformCode,
                    PlatformName = huangDou.PlatformName,
                    BaseUrl = huangDou.BaseUrl,
                    AdapterType = nameof(HuangDouAdapter),
                    IsEnabled = true
                });
            }

            foreach (var platform in platforms)
            {
                if (!await db.PlatformSources.AnyAsync(s => s.PlatformCode == platform.PlatformCode))
                {
                    db.PlatformSources.Add(platform);
                }
            }
            await db.SaveChangesAsync();

            // 2) 默认管理员
            if (!await db.Users.AnyAsync())
            {
                db.Users.Add(new User
                {
                    Username = "admin",
                    Email = "admin@shortdrama.local",
                    Phone = "13800000000",
                    PasswordHash = AuthService.HashPassword("admin123"),
                    Role = "admin",
                    AvatarUrl = "https://api.dicebear.com/7.x/initials/svg?seed=admin",
                    CreatedAt = DateTime.UtcNow
                });
                await db.SaveChangesAsync();
                logger.LogInformation("已创建默认管理员账号 admin / admin123");
            }

            // 3) 首次启动同步各平台短剧数据
            //    放到后台任务执行：采集源播种要跑几分钟，不能把 API 启动挂住。
            //    期间前端可以正常访问，只是内容库还在陆续填充。
            if (!await db.Dramas.AnyAsync())
            {
                var platformCodes = platforms.Select(p => (p.PlatformCode, p.PlatformName)).ToList();
                var timeoutSeconds = config.GetValue("AppleCms:BootstrapTotalTimeoutSeconds", 600);
                var rootProvider = services;

                _ = Task.Run(async () =>
                {
                    using var bgScope = rootProvider.CreateScope();
                    var bgSp = bgScope.ServiceProvider;
                    var aggregation = bgSp.GetRequiredService<IAggregationService>();
                    var bgLogger = bgSp.GetRequiredService<ILoggerFactory>().CreateLogger("Bootstrap");

                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
                    bgLogger.LogInformation("后台播种开始，共 {Count} 个平台", platformCodes.Count);

                    foreach (var (code, name) in platformCodes)
                    {
                        if (cts.IsCancellationRequested)
                        {
                            bgLogger.LogWarning("后台播种总超时，剩余平台跳过；可在管理后台手动触发同步");
                            break;
                        }

                        try
                        {
                            var count = await aggregation.SyncPlatformAsync(code, cts.Token);
                            bgLogger.LogInformation("播种 {Platform}: {Count} 部短剧", name, count);
                        }
                        catch (OperationCanceledException)
                        {
                            bgLogger.LogWarning("播种 {Platform} 超时，已跳过", name);
                            break;
                        }
                        catch (Exception ex)
                        {
                            bgLogger.LogWarning("播种 {Platform} 失败：{Reason}", name, ex.GetBaseException().Message);
                        }
                    }

                    bgLogger.LogInformation("后台播种结束");
                });
            }

            var dramaCount = await db.Dramas.CountAsync();
            var episodeCount = await db.Episodes.CountAsync();
            logger.LogInformation("数据库就绪：{DramaCount} 部短剧，{EpisodeCount} 集", dramaCount, episodeCount);
        }
    }
}
