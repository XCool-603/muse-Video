using System.Collections.Generic;
using System.Threading.Tasks;

namespace ShortDrama.Application.Adapters
{
    public interface IAdapterFactory
    {
        /// <summary>获取全部已启用的平台适配器</summary>
        IReadOnlyList<IPlatformAdapter> GetAll();

        /// <summary>按平台编码获取适配器，不存在返回 null</summary>
        IPlatformAdapter? Get(string platformCode);

        /// <summary>刷新适配器启用状态（从平台源配置表同步）</summary>
        Task RefreshAsync();
    }
}
