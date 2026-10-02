using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace ShortDrama.Infrastructure.Services
{
    /// <summary>
    /// 记录「这台部署机到底能不能播」——按源站 CDN 主机和平台两个维度。
    ///
    /// 为什么按主机而不是按剧：同一部剧的失败在整台主机上是共通的。
    /// 实测采集站给的播放地址可能指向已退役的 CDN 主机（暴风 bfeng10.com 全站 404），
    /// 而同一路径在另一台主机（fengbao13.com）上是 200 —— 所以「哪台主机能用」
    /// 才是可复用的结论，据此可以在失败时换主机重试。
    ///
    /// 数据来源是真实播放请求的结果，不做启动探测（要打几十个 CDN，太重）。
    /// 会话内存住，重启即清零重测。
    /// </summary>
    public class PlayabilityTracker
    {
        private enum Status { Unknown, Ok, Blocked }

        /// <summary>主机名 → 状态。成功即平反；失败只在未曾成功时记为 Blocked</summary>
        private readonly ConcurrentDictionary<string, Status> _byHost = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>平台 → 状态（任一台主机成功即视为该平台可播）</summary>
        private readonly ConcurrentDictionary<string, Status> _byPlatform = new(StringComparer.OrdinalIgnoreCase);

        public void RecordSuccess(string platformCode, string url)
        {
            if (TryHost(url, out var host)) Mark(_byHost, host, ok: true);
            if (!string.IsNullOrWhiteSpace(platformCode)) Mark(_byPlatform, platformCode, ok: true);
        }

        /// <summary>403/404 视为源站拒绝；其他失败（超时等）不改判，避免一次网络抖动拉黑一个源</summary>
        public void RecordFailure(string platformCode, string url, int statusCode)
        {
            var blocked = statusCode == 403 || statusCode == 404;
            if (!blocked) return;

            if (TryHost(url, out var host)) Mark(_byHost, host, ok: false);
            if (!string.IsNullOrWhiteSpace(platformCode)) Mark(_byPlatform, platformCode, ok: false);
        }

        private static void Mark(ConcurrentDictionary<string, Status> map, string key, bool ok)
        {
            map.AddOrUpdate(
                key,
                ok ? Status.Ok : Status.Blocked,
                // 成功永远覆盖失败（CDN 可能只是个别路径失效）；
                // 失败只在从没成功过时才记 Blocked
                (_, existing) => ok ? Status.Ok : existing == Status.Ok ? Status.Ok : Status.Blocked);
        }

        private static bool TryHost(string url, out string host)
        {
            host = string.Empty;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
            host = uri.Host;
            return !string.IsNullOrWhiteSpace(host);
        }

        /// <summary>
        /// 把候选主机排序：已知可用 → 未知 → 已知被拒。
        /// 换主机重试时按这个顺序试，最快命中能用的那台。
        /// </summary>
        public List<string> RankHosts(IEnumerable<string> hosts) => hosts
            .Where(h => !string.IsNullOrWhiteSpace(h))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(Rank)
            .ToList();

        private int Rank(string host) => _byHost.TryGetValue(host, out var s)
            ? s switch { Status.Ok => 0, Status.Unknown => 1, _ => 2 }
            : 1;

        /// <summary>该平台当前是否已知可播（Unknown = 未验证，返回 null）</summary>
        public bool? GetPlatformStatus(string platformCode)
            => _byPlatform.TryGetValue(platformCode, out var s) && s != Status.Unknown
                ? s == Status.Ok
                : null;

        public Dictionary<string, bool?> Snapshot() => _byPlatform.ToDictionary(
            kv => kv.Key,
            kv => (bool?)(kv.Value == Status.Unknown ? null : kv.Value == Status.Ok),
            StringComparer.OrdinalIgnoreCase);
    }
}
