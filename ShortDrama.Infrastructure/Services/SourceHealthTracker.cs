using System;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace ShortDrama.Infrastructure.Services
{
    /// <summary>
    /// 平台健康状态跟踪：记录各平台最近是否失败/超时，失败后进入一段冷却期。
    ///
    /// 为什么需要：聚合搜索是并行打所有平台，而总耗时取决于最慢的那个。
    /// 实测 22 个采集源里有 7 个的 CDN 按地区拒绝（无尽/最大/百度 403、暴风/量子 404…），
    /// 每次搜索都要为它们等满超时，把整体拖到 10 秒以上。
    /// 冷却期把这些已知不可用的平台暂时摘掉，搜索立刻变快。
    ///
    /// 必须是单例：状态要跨请求共享。
    /// </summary>
    public class SourceHealthTracker
    {
        private readonly ConcurrentDictionary<string, DateTimeOffset> _cooldownUntil = new(StringComparer.OrdinalIgnoreCase);
        private readonly ILogger<SourceHealthTracker> _logger;

        public SourceHealthTracker(ILogger<SourceHealthTracker> logger)
        {
            _logger = logger;
        }

        /// <summary>该平台当前是否在冷却期内（应跳过）。</summary>
        public bool IsCoolingDown(string platformCode) =>
            _cooldownUntil.TryGetValue(platformCode, out var until) && until > DateTimeOffset.UtcNow;

        /// <summary>标记一次失败/超时，进入冷却。</summary>
        public void MarkFailure(string platformCode, TimeSpan cooldown, string? reason = null)
        {
            if (string.IsNullOrWhiteSpace(platformCode)) return;

            var until = DateTimeOffset.UtcNow + cooldown;
            var wasCooling = IsCoolingDown(platformCode);
            _cooldownUntil[platformCode] = until;

            // 只在「刚进入冷却」时打日志，避免刷屏
            if (!wasCooling)
            {
                _logger.LogInformation("平台 {Platform} 进入冷却 {Seconds}s{Reason}",
                    platformCode, (int)cooldown.TotalSeconds,
                    string.IsNullOrWhiteSpace(reason) ? string.Empty : $"（{reason}）");
            }
        }

        /// <summary>一次成功即解除冷却。</summary>
        public void MarkSuccess(string platformCode)
        {
            if (string.IsNullOrWhiteSpace(platformCode)) return;

            if (_cooldownUntil.TryRemove(platformCode, out _))
            {
                _logger.LogInformation("平台 {Platform} 已恢复，解除冷却", platformCode);
            }
        }

        /// <summary>当前处于冷却期的平台数量（供日志/诊断）。</summary>
        public int CoolingDownCount =>
            _cooldownUntil.Count(kv => kv.Value > DateTimeOffset.UtcNow);
    }
}
