using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ShortDrama.Application.Adapters
{
    /// <summary>
    /// 可选能力：支持按页遍历全量目录的适配器。
    ///
    /// 有些站点（如黄果短剧）的目录是中英双语标题，用中文关键词搜索命中率很低，
    /// 但其 catalog 接口支持稳定分页。实现本接口后，SourceBootstrapper 会改为
    /// 逐页遍历全量目录入库，而不是仅按关键词播种。
    /// </summary>
    public interface IPagedCatalogAdapter
    {
        /// <summary>目录总条数（未知时返回 0）</summary>
        Task<int> GetCatalogTotalAsync(CancellationToken ct = default);

        /// <summary>按页取目录。page 从 1 开始</summary>
        Task<List<PlatformNewItem>> GetCatalogPageAsync(int page, int pageSize, CancellationToken ct = default);
    }
}
