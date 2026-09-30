using System.Collections.Generic;

namespace ShortDrama.Infrastructure.Adapters
{
    /// <summary>
    /// 苹果CMS V10 采集源配置。
    /// 一个采集源 = 一个 IPlatformAdapter 实例，聚合搜索会自动纳入调度。
    /// </summary>
    public class AppleCmsOptions
    {
        public const string SectionName = "AppleCms";

        /// <summary>是否启用苹果CMS 采集源</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>单次请求超时（秒）</summary>
        public int TimeoutSeconds { get; set; } = 12;

        /// <summary>
        /// 初始入库时用于播种的关键词。
        /// 部分采集源不支持分类过滤（t / type_id 参数被忽略），返回的是全站最新（多为动漫、综艺），
        /// 因此用短剧相关关键词走搜索接口播种，入库内容更贴合本站定位。
        /// </summary>
        public List<string> BootstrapKeywords { get; set; } = new()
        {
            "短剧", "霸总", "重生", "穿越", "战神", "甜宠", "闪婚", "逆袭"
        };

        /// <summary>每个关键词每源最多入库多少部（每页上限）</summary>
        public int BootstrapLimitPerKeyword { get; set; } = 10;

        /// <summary>
        /// 每个播种关键词翻多少页。
        /// 采集接口每页固定返回约 20 条，只读第 1 页会严重漏内容：
        /// 实测「短剧」一个词，无尽 648 部、暴风 546 部、最大 492 部，原先每个源只取到约 20 部。
        /// 翻页会让首次播种的请求数与耗时成倍增加，按需调整。
        /// </summary>
        public int BootstrapPagesPerKeyword { get; set; } = 5;

        /// <summary>
        /// 翻页时每页之间的间隔（毫秒）。
        /// 8 个关键词并行翻 5 页会在短时间内对同一源打出约 40 个请求，
        /// 实测会触发限流：暴风返回 429，无尽直接超时并把该源整轮播种打废。
        /// 加间隔是为了把请求摊开，别把源惹毛。
        /// </summary>
        public int BootstrapPageDelayMs { get; set; } = 300;

        /// <summary>单个源初始入库的候选上限（防止个别大源拖慢启动）</summary>
        public int BootstrapMaxPerSource { get; set; } = 80;

        /// <summary>
        /// 单个源最多收集多少候选。
        /// 0 = 沿用旧算法 max(bootstrapMaxPerSource, maxCatalogPages × 20)。
        /// 旧算法把「目录页数」当成「候选上限」来算，语义混乱，且实际只给到 600 部；
        /// 实测 20 个关键词 × 5 页时单源可收集 1150~1684 部，600 会成为瓶颈，所以单独给一个显式开关。
        /// </summary>
        public int BootstrapMaxCandidatesPerSource { get; set; } = 0;

        /// <summary>播种时抓取详情的并发数（采集站多有频率限制，不宜过高）</summary>
        public int BootstrapConcurrency { get; set; } = 8;

        /// <summary>
        /// 全量目录模式：适配器实现 IPagedCatalogAdapter 时，逐页遍历全量目录而非按关键词播种。
        /// 适用于标题中英双语、中文关键词命中率低的站点。
        /// </summary>
        public bool FullCatalogSync { get; set; } = true;

        /// <summary>全量目录最多遍历多少页（每页 20）</summary>
        public int MaxCatalogPages { get; set; } = 30;

        /// <summary>
        /// 全量目录模式下是否跳过逐部抓详情（只入库目录元数据，分集在首次访问详情页时懒加载）。
        /// 513 部 × 2 次请求在 50/分钟的限流下要跑 20 分钟；只拉目录约 1 分钟。
        /// </summary>
        public bool CatalogOnlySync { get; set; } = true;

        /// <summary>采集源列表</summary>
        public List<AppleCmsSource> Sources { get; set; } = new();
    }

    public class AppleCmsSource
    {
        /// <summary>平台编码，需全局唯一，用作 platform_sources.platform_code</summary>
        public string PlatformCode { get; set; } = string.Empty;

        /// <summary>展示名称</summary>
        public string PlatformName { get; set; } = string.Empty;

        /// <summary>采集接口地址，形如 https://xxx.com/api.php/provide/vod/</summary>
        public string Api { get; set; } = string.Empty;

        /// <summary>是否启用</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// 短剧分类 ID 列表（苹果CMS 的 type_id）。
        /// 留空则全站检索；填写后榜单/上新只拉这些分类，能显著减少无关内容。
        /// </summary>
        public List<int> ShortDramaTypeIds { get; set; } = new();

        /// <summary>备注（不参与逻辑）</summary>
        public string? Note { get; set; }

        /// <summary>接口响应格式：json（默认）或 xml</summary>
        public string Format { get; set; } = "json";
    }
}
