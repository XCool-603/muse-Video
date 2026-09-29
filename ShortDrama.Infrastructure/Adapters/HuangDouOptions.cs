using System.Collections.Generic;

namespace ShortDrama.Infrastructure.Adapters
{
    /// <summary>黄豆短剧（hddj.tv）适配器配置。</summary>
    public class HuangDouOptions
    {
        public const string SectionName = "HuangDou";

        /// <summary>是否启用</summary>
        public bool Enabled { get; set; } = true;

        public string PlatformCode { get; set; } = "huangdou";
        public string PlatformName { get; set; } = "黄豆短剧";

        /// <summary>站点根地址</summary>
        public string BaseUrl { get; set; } = "https://hddj.tv";

        public int TimeoutSeconds { get; set; } = 15;

        /// <summary>
        /// 详情页路径模板（已由逆向文档确认），{id} 为剧集 ID。
        /// 两个都试，取第一个能解析出剧集的。
        /// </summary>
        public List<string> DetailPathCandidates { get; set; } = new()
        {
            "/watch/details/{id}",
            "/series/details/{id}"
        };

        /// <summary>
        /// 搜索结果卡片里详情链接的前缀（用于从 HTML 反推剧集 ID）。
        /// 默认取详情页路径的公共前缀。
        /// </summary>
        public string DetailLinkPrefix { get; set; } = "/watch/details";

        /// <summary>
        /// 搜索接口候选路径（**未经文档确认，靠探测**）。
        /// 占位符：{kw} 关键词、{page} 页码、{pageSize} 每页条数。
        /// 用浏览器 F12 → Network 搜一次，把真实路径填到这里最前面。
        /// </summary>
        public List<string> SearchPathCandidates { get; set; } = new()
        {
            "/api/search?keyword={kw}&page={page}&pageSize={pageSize}",
            "/api/drama/search?keyword={kw}&page={page}&pageSize={pageSize}",
            "/search?keyword={kw}&page={page}",
            "/search?q={kw}&page={page}",
            "/api/v1/search?kw={kw}&page={page}"
        };

        /// <summary>列表/榜单接口候选路径（**未经文档确认，靠探测**）</summary>
        public List<string> ListPathCandidates { get; set; } = new()
        {
            "/api/drama/list?page=1&pageSize=20",
            "/api/dramas?page=1&pageSize=20",
            "/series",
            "/list"
        };
    }
}
