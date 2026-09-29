using System.Collections.Generic;

namespace ShortDrama.Infrastructure.Adapters
{
    /// <summary>黄果短剧（huangguodrama.ai）适配器配置。</summary>
    public class HuangGuoAiOptions
    {
        public const string SectionName = "HuangGuoAi";

        public bool Enabled { get; set; } = true;

        public string PlatformCode { get; set; } = "huangguo";
        public string PlatformName { get; set; } = "黄果短剧";

        /// <summary>站点根地址（官方 API 与观看页都在这个域）</summary>
        public string BaseUrl { get; set; } = "https://huangguodrama.ai";

        /// <summary>视频 CDN（明文 MP4，无需特殊处理，仅作记录）</summary>
        public string MediaBaseUrl { get; set; } = "https://media.huangguodrama.ai";

        public int TimeoutSeconds { get; set; } = 20;

        /// <summary>本地限流（站点官方限制 60 请求/IP/分钟，这里默认留余量）</summary>
        public int RequestsPerMinute { get; set; } = 50;

        /// <summary>
        /// 是否过滤成人向内容。
        /// 该站为混合平台（全站 18+ 门禁，含 ai-huanlian / ai-mogai 分类），
        /// 按标题与简介关键词过滤。默认 false = 不过滤。
        /// </summary>
        public bool ExcludeAdult { get; set; } = false;

        /// <summary>成人向内容过滤关键词（仅在 ExcludeAdult=true 时生效）</summary>
        public List<string> AdultKeywords { get; set; } = new()
        {
            "诱惑", "艳", "情欲", "肉欲", "性爱", "色情", "成人", "私房",
            "占有", "调教", "娇喘", "淫", "脱衣", "禁忌", "情事", "艳遇",
            "易子而交", "干妈", "兽人", "换脸"
        };
    }
}
