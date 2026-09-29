using System.Collections.Generic;

namespace ShortDrama.Infrastructure.Adapters
{
    /// <summary>红果短剧官网（hongguoduanju.com）适配器配置。</summary>
    public class HongGuoWebOptions
    {
        public const string SectionName = "HongGuoWeb";

        public bool Enabled { get; set; } = true;

        public string PlatformCode { get; set; } = "hongguo";
        public string PlatformName { get; set; } = "红果短剧";

        /// <summary>站点根地址（抖音/番茄系官方 H5）</summary>
        public string BaseUrl { get; set; } = "https://hongguoduanju.com";

        public int TimeoutSeconds { get; set; } = 20;

        /// <summary>热播榜路径</summary>
        public string HotRankPath { get; set; } = "/rank/hot-drama";

        /// <summary>真人剧榜路径</summary>
        public string RealDramaRankPath { get; set; } = "/rank/hot-real-drama";

        /// <summary>新剧榜路径</summary>
        public string LatestRankPath { get; set; } = "/rank/hot-ai-drama";

        /// <summary>默认分类页（上新用）</summary>
        public string DefaultCategoryPath { get; set; } = "/category/real-drama";

        /// <summary>站内分类 → 红果分类路径映射</summary>
        public Dictionary<string, string> CategoryPaths { get; set; } = new()
        {
            ["全部"] = "/category/real-drama",
            ["短剧"] = "/category/real-drama",
            ["真人剧"] = "/category/real-drama",
            ["漫剧"] = "/category/comic-drama",
            ["AI剧"] = "/category/ai-drama",
            ["漫画"] = "/category/comic"
        };
    }
}
