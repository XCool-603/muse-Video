namespace ShortDrama.Infrastructure.Services
{
    /// <summary>
    /// 聚合搜索的性能与容错策略。
    ///
    /// 背景：聚合搜索会并行请求所有平台，总耗时取决于最慢的那个。
    /// 实测 22 个采集源里有 7 个的 CDN 按地区拒绝，每次搜索都要为它们等满超时，
    /// 把整体拖到 10 秒以上。下面三个开关分别解决「等慢源」「反复等死源」「重复搜」。
    /// </summary>
    public class AggregationOptions
    {
        public const string SectionName = "Aggregation";

        /// <summary>
        /// 软期限（毫秒）：到点且已收到 MinPlatformsBeforeReturn 个平台的结果，就直接返回。
        /// 实测采集源的搜索延迟 P50≈1.4s、P80≈2.7s、P100≈3.5s，
        /// 所以 2000ms 能覆盖约七成平台，同时把绝大多数搜索压到 2 秒内。
        /// </summary>
        public int SearchSoftBudgetMs { get; set; } = 2000;

        /// <summary>
        /// 硬上限（毫秒）：无论如何都不再等，直接返回已收到的结果。
        /// 设为 0 表示不限制（退回旧行为：等所有平台，包括超时的）。
        /// </summary>
        public int SearchBudgetMs { get; set; } = 5000;

        /// <summary>
        /// 软期限到达时至少要有多少个平台返回才提前收工；不够就继续等到硬上限。
        /// 防止「刚好只回来一两个平台」就返回，导致结果过少。
        /// </summary>
        public int MinPlatformsBeforeReturn { get; set; } = 8;

        /// <summary>
        /// 平台失败或超时后的冷却时间（秒）。冷却期内不再向它发请求，直接跳过。
        /// 一次成功即解除。设为 0 表示禁用冷却。
        /// </summary>
        public int FailureCooldownSeconds { get; set; } = 120;

        /// <summary>
        /// 搜索结果的缓存时长（秒）。相同关键词/分页/平台的重复搜索直接命中缓存。
        /// 设为 0 表示不缓存。
        /// </summary>
        public int CacheSeconds { get; set; } = 60;
    }
}
