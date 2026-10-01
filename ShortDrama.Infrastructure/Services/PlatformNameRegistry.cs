using System;
using System.Collections.Concurrent;

namespace ShortDrama.Infrastructure.Services
{
    /// <summary>
    /// 平台编码 → 展示名的全局注册表。
    ///
    /// 为什么需要它：采集源的展示名（acms_bfzy = 暴风资源）只写在 apple-cms-sources.json 里，
    /// 而 <see cref="Mapper"/> 是静态帮助类，注入不进配置。于是凡是从本地库映射出来的内容
    /// （/drama/list、收藏、详情、推荐）platformName 一直是平台编码本身，
    /// 前端 <c>DramaCard</c> 的角标上就直接显示「acms_bfzy」这种内部编码。
    /// 启动绑定配置时把名字注册进来，所有映射点即可统一取到，不必逐个改调用方。
    ///
    /// 写入只发生在启动期（DependencyInjection 绑定配置那一步，单线程），之后只读。
    /// </summary>
    internal static class PlatformNameRegistry
    {
        private static readonly ConcurrentDictionary<string, string> Names =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>注册一个平台的展示名。编码或名字为空则忽略。</summary>
        public static void Register(string? platformCode, string? platformName)
        {
            if (string.IsNullOrWhiteSpace(platformCode) || string.IsNullOrWhiteSpace(platformName)) return;
            Names[platformCode.Trim()] = platformName.Trim();
        }

        /// <summary>取展示名；没注册过返回 null，由调用方决定回退方式。</summary>
        public static string? Resolve(string? platformCode)
        {
            if (string.IsNullOrWhiteSpace(platformCode)) return null;
            return Names.TryGetValue(platformCode.Trim(), out var name) ? name : null;
        }
    }
}
