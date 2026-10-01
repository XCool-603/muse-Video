using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ShortDrama.Application.Adapters;
using ShortDrama.Application.DTOs;

namespace ShortDrama.Application.Services
{
    public interface IAggregationService
    {
        /// <summary>跨平台聚合搜索：并行请求各适配器 + 本地库，去重合并后统一排序分页</summary>
        Task<PagedResult<DramaDto>> SearchAsync(string keyword, int page, int pageSize, string? platformCode = null, CancellationToken ct = default);

        /// <summary>
        /// 聚合榜单。platformCode 为空或 "all" = 全部平台；
        /// 传具体平台则只打那一个源（按平台浏览时的快路径）。
        /// </summary>
        Task<List<DramaDto>> GetRankAsync(string type, int limit = 20, string? platformCode = null, CancellationToken ct = default);

        /// <summary>聚合今日上新。platformCode 语义同 <see cref="GetRankAsync"/></summary>
        Task<List<DramaDto>> GetLatestAsync(string category = "全部", int limit = 20, string? platformCode = null, CancellationToken ct = default);

        /// <summary>把某平台的短剧数据同步入库（用于本地化与加速后续查询）</summary>
        Task<int> SyncPlatformAsync(string platformCode, CancellationToken ct = default);

        /// <summary>
        /// 某个平台自己的分类表，直接读源站接口（苹果CMS 的 ac=list → class）。
        /// 不依赖本地库：新开的源本地是空的，本地统计出来的分类只剩「全部」。
        /// </summary>
        Task<List<PlatformCategoryItem>> GetLiveCategoriesAsync(string platformCode, CancellationToken ct = default);

        /// <summary>
        /// 某个平台的源站目录分页，直接读源站接口（ac=detail[&amp;t=分类]&amp;pg=页码）。
        /// typeId 为空 = 该源的全站目录。同样不经过本地库。
        /// </summary>
        Task<PagedResult<DramaDto>> GetLiveCatalogAsync(
            string platformCode, string? typeId, int page, int pageSize, CancellationToken ct = default);
    }

    public interface IDramaService
    {
        Task<PagedResult<DramaDto>> QueryAsync(string? keyword, string? category, string? platformCode, string sortBy, int page, int pageSize, CancellationToken ct = default);

        Task<DramaDetailDto?> GetDetailAsync(long id, CancellationToken ct = default);

        Task<List<EpisodeDto>> GetEpisodesAsync(long id, CancellationToken ct = default);

        /// <summary>库里没有分集时回源补齐（配合全量目录快速入库）</summary>
        Task<bool> EnsureEpisodesAsync(long dramaId, CancellationToken ct = default);

        /// <summary>
        /// 分类列表。传 platformCode 只返回该平台真的有的分类（按内容量从多到少），
        /// 不传则返回全平台去重后的分类。
        /// 分类是各采集源自己的字段，同一个分类名在不同源里差别很大：全平台能出上百个，
        /// 而按平台浏览时其中绝大多数在那个源里是空的 —— 所以分类列表必须跟着平台走。
        /// </summary>
        Task<List<string>> GetCategoriesAsync(string? platformCode = null, CancellationToken ct = default);

        /// <summary>前端平台筛选列表（只返回启用的、且有内容的平台）</summary>
        Task<List<PlatformInfoDto>> GetPlatformsAsync(CancellationToken ct = default);

        /// <summary>把适配器返回的剧集详情落库（幂等 upsert）</summary>
        Task<long> UpsertFromPlatformAsync(string platformCode, string platformDramaId, CancellationToken ct = default);

        /// <summary>
        /// 按平台原始 ID 查本地库，返回本地 Id；库里没有则返回 0。
        /// 用于「按需入库」：实时聚合搜索的结果（Id=0）在用户点开时先查这里，
        /// 命中就直接播放，不命中才回源拉取，避免每次点开都打上游。
        /// </summary>
        Task<long> FindLocalIdAsync(string platformCode, string platformDramaId, CancellationToken ct = default);
    }

    public interface IPlayService
    {
        Task<PlayInfoDto?> GetPlayInfoAsync(long dramaId, int episode, string? userId, CancellationToken ct = default);

        Task SaveProgressAsync(string userId, long dramaId, int episode, int position, CancellationToken ct = default);

        Task<PlayProgressDto?> GetProgressAsync(string userId, long dramaId, CancellationToken ct = default);

        Task<List<PlayProgressDto>> GetHistoryAsync(string userId, int limit = 20, CancellationToken ct = default);
    }

    public interface IAuthService
    {
        Task<LoginResponse?> LoginAsync(LoginRequest request, CancellationToken ct = default);

        Task<LoginResponse?> RegisterAsync(RegisterRequest request, CancellationToken ct = default);

        Task<UserDto?> GetProfileAsync(string userId, CancellationToken ct = default);

        string GenerateToken(long userId, string username, string role);
    }

    public interface IFavoriteService
    {
        Task<List<DramaDto>> ListAsync(string userId, CancellationToken ct = default);

        Task<bool> AddAsync(string userId, long dramaId, CancellationToken ct = default);

        Task<bool> RemoveAsync(string userId, long dramaId, CancellationToken ct = default);

        Task<bool> IsFavoriteAsync(string userId, long dramaId, CancellationToken ct = default);
    }

    public interface IAdminService
    {
        Task<DashboardDto> GetDashboardAsync(CancellationToken ct = default);

        Task<List<PlatformSourceDto>> GetPlatformSourcesAsync(CancellationToken ct = default);

        Task<bool> TogglePlatformAsync(long id, bool enabled, CancellationToken ct = default);

        Task<long> CreateDramaAsync(AdminDramaSaveRequest request, CancellationToken ct = default);

        Task<bool> UpdateDramaAsync(long id, AdminDramaSaveRequest request, CancellationToken ct = default);

        Task<bool> DeleteDramaAsync(long id, CancellationToken ct = default);

        Task<bool> UpdateEpisodeAsync(long dramaId, int episodeNumber, bool isFree, bool isLocked, string videoUrl, CancellationToken ct = default);
    }
}
