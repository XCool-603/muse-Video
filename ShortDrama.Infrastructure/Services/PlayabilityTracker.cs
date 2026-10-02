using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace ShortDrama.Infrastructure.Services
{
    /// <summary>
    /// 按源站 CDN 域名记录「这台部署机到底能不能播它」。
    ///
    /// 为什么按域名而不是按剧：CDN 是按 IP/地区整站拒绝的（docs/SOURCES.md 6.1），
    /// 同一个域名下这一部 403，那一部大概率也是；而按剧记会漏掉「下一部还是坏的」。
    ///
    /// 数据来源是真实播放请求的结果，不是探测 —— 探测要打几十个 CDN，启动太重；
    /// 播放是用户主动触发的，失败一次就知道了。会话内存住，重启即清零（重测）。
    /// </summary>
    public class PlayabilityTracker
    {
        private enum Status { Unknown, Ok, Blocked }

        /// <summary>域名 → 状态。二票否决制：一次成功即 Ok；一次 403/404 即 Blocked</summary>
        private readonly ConcurrentDictionary<string, Status> _byHost = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>按平台聚合（给平台列表用）：平台下任一域名 Blocked 且无 Ok → Blocked</summary>
        private readonly ConcurrentDictionary<string, Status> _byPlatform = new(StringComparer.OrdinalIgnoreCase);

        public void RecordSuccess(string platformCode, string url) => Record(platformCode, url, Status.Ok);

        /// <summary>403/404 视为源站拒绝；其他失败（超时等）不改判，避免一次网络抖动拉黑一个源</summary>
        public void RecordFailure(string platformCode, string url, int statusCode)
        {
            var blocked = statusCode == 403 || statusCode == 404;
            Record(platformCode, url, blocked ? Status.Blocked : Status.Unknown);
        }

        private void Record(string platformCode, string url, Status status)
        {
            try
            {
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                {
                    var host = uri.Host;
                    var existing = _byHost.GetOrAdd(host, status);
                    // 一次成功就平反（CDN 可能只是个别路径失效），但 403/404 之后又成功也允许
                    if (status == Status.Ok || existing != Status.Ok)
                    {
                        _byHost[host] = status == Status.Ok ? Status.Ok : existing == Status.Ok ? Status.Ok : status;
                    }
                }
            }
            catch
            {
                // 地址解析失败不影响播放主流程
            }

            var existingPlatform = _byPlatform.GetOrAdd(platformCode, status);
            if (status == Status.Ok) _byPlatform[platformCode] = Status.Ok;
            else if (existingPlatform != Status.Ok && status == Status.Blocked) _byPlatform[platformCode] = Status.Blocked;
        }

        /// <summary>该平台当前是否已知可播（Unknown 按「未验证」处理，返回 null）</summary>
        public bool? GetPlatformStatus(string platformCode)
            => _byPlatform.TryGetValue(platformCode, out var s) && s != Status.Unknown
                ? s == Status.Ok
                : (bool?)null;

        /// <summary>平台排序键：可播 0 / 未验证 1 / 不可播 2</summary>
        public int SortOrder(string platformCode) => GetPlatformStatus(platformCode) switch
        {
            true => 0,
            null => 1,
            false => 2
        };

        public Dictionary<string, bool?> Snapshot() => _byPlatform.ToDictionary(
            kv => kv.Key, kv => (bool?)(kv.Value == Status.Unknown ? null : kv.Value == Status.Ok), StringComparer.OrdinalIgnoreCase);
    }
}
