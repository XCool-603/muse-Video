using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ShortDrama.Application.Adapters
{
    /// <summary>
    /// 可选能力：直接按「源站自己的分类」浏览该平台的目录，不经过本地库。
    ///
    /// 为什么需要它：本地库是按关键词播种出来的，只能看到「已经入库的那部分」，
    /// 而且新开的源在同步之前本地是空的 —— 分类只剩「全部」、列表 0~1 条。
    /// 实现本接口后，平台页可以直接问源站要目录：分类来自 ac=list 的 class，
    /// 列表来自 ac=detail[&amp;t=分类]&amp;pg=页码，永远有内容、也永远是最新的。
    ///
    /// 注意各源能力不一致：实测暴风资源的分类分页正常（每页 20 条），
    /// 而无尽资源的分类翻页只给第 1 页、pg=2 起返回 0 条。这属于源站限制，
    /// 调用方要能接受「这一页就是空的」。
    /// </summary>
    public interface ILiveCatalogAdapter
    {
        /// <summary>源站自己的分类表（苹果CMS 的 ac=list 返回的 class 字段）</summary>
        Task<List<PlatformCategoryItem>> GetCategoriesAsync(CancellationToken ct = default);

        /// <summary>
        /// 按源站分类翻页取目录。typeId 为空 = 该源的全站目录（最新在前）。
        /// </summary>
        Task<PlatformCatalogPage> GetCatalogPageAsync(string? typeId, int page, int pageSize, CancellationToken ct = default);

        /// <summary>
        /// 该源**当前**在用的 CDN 主机名（从最近几页目录的播放地址里统计）。
        ///
        /// 为什么需要它：采集站给每部剧的播放地址可能指向不同主机，其中一部分已退役
        /// （实测暴风 bfeng10.com 全站 404，而同一路径在 fengbao13.com 上是 200）。
        /// 本地库里存的是入库当时的地址，主机可能早就换了 —— 换主机重试必须知道
        /// 源站现在用哪些主机，否则只会拿旧主机反复撞墙。
        /// </summary>
        Task<List<string>> GetCdnHostsAsync(CancellationToken ct = default);
    }

    public class PlatformCategoryItem
    {
        public string TypeId { get; set; } = string.Empty;
        public string TypeName { get; set; } = string.Empty;
    }

    public class PlatformCatalogPage
    {
        public List<PlatformNewItem> Items { get; set; } = new();

        /// <summary>源站声明的总数（拿不到时为 0）</summary>
        public int Total { get; set; }
    }
}
