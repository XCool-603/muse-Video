using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShortDrama.Application.Adapters;
using ShortDrama.Application.Common;
using ShortDrama.Application.DTOs;
using ShortDrama.Application.Services;
using ShortDrama.Domain.Entities;
using ShortDrama.Infrastructure.Data;

namespace ShortDrama.Infrastructure.Services
{
    public class DramaService : IDramaService
    {
        private readonly AppDbContext _db;
        private readonly IAdapterFactory _adapters;
        private readonly PlayabilityTracker _playability;
        private readonly ILogger<DramaService> _logger;

        public DramaService(AppDbContext db, IAdapterFactory adapters, PlayabilityTracker playability, ILogger<DramaService> logger)
        {
            _db = db;
            _adapters = adapters;
            _playability = playability;
            _logger = logger;
        }

        public async Task<PagedResult<DramaDto>> QueryAsync(string? keyword, string? category, string? platformCode, string sortBy, int page, int pageSize, CancellationToken ct = default)
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);

            var query = _db.Dramas.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                query = query.Where(d => EF.Functions.Like(d.Title, $"%{keyword}%") ||
                                         EF.Functions.Like(d.Description, $"%{keyword}%"));
            }

            if (!string.IsNullOrWhiteSpace(category) && category != "全部")
            {
                query = query.Where(d => d.Category == category);
            }

            if (!string.IsNullOrWhiteSpace(platformCode) && platformCode != "all")
            {
                query = query.Where(d => d.PlatformCode == platformCode);
            }

            query = sortBy switch
            {
                "new" => query.OrderByDescending(d => d.UpdatedAt),
                "rating" => query.OrderByDescending(d => d.Rating),
                _ => query.OrderByDescending(d => d.PlayCount)
            };

            var total = await query.CountAsync(ct);
            var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

            return PagedResult<DramaDto>.Create(items.Select(d => Mapper.ToDto(d)).ToList(), total, page, pageSize);
        }

        public async Task<DramaDetailDto?> GetDetailAsync(long id, CancellationToken ct = default)
        {
            var drama = await _db.Dramas
                .Include(d => d.Episodes)
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == id, ct);

            if (drama is null) return null;

            // 分集懒加载：全量目录同步只入库了元数据，分集在首次打开详情页时补齐
            if (drama.Episodes.Count == 0)
            {
                var hydrated = await EnsureEpisodesAsync(drama.Id, ct);
                if (hydrated)
                {
                    drama = await _db.Dramas
                        .Include(d => d.Episodes)
                        .AsNoTracking()
                        .FirstOrDefaultAsync(d => d.Id == id, ct);

                    if (drama is null) return null;
                }
            }

            var dto = Mapper.ToDetailDto(drama);

            // 相关推荐：同分类优先，不足则按热度补足
            var recommends = await _db.Dramas.AsNoTracking()
                .Where(d => d.Id != id && d.Category == drama.Category)
                .OrderByDescending(d => d.PlayCount)
                .Take(8)
                .ToListAsync(ct);

            if (recommends.Count < 8)
            {
                var extra = await _db.Dramas.AsNoTracking()
                    .Where(d => d.Id != id && d.Category != drama.Category)
                    .OrderByDescending(d => d.PlayCount)
                    .Take(8 - recommends.Count)
                    .ToListAsync(ct);
                recommends.AddRange(extra);
            }

            dto.Recommends = recommends.Select(d => Mapper.ToDto(d)).ToList();

            // 聚合同剧的其他平台源
            var normalized = TextSimilarity.Normalize(drama.Title);
            var candidates = await _db.Dramas.AsNoTracking()
                .Where(d => d.Id != id)
                .Select(d => new { d.PlatformCode, d.Title })
                .ToListAsync(ct);

            foreach (var c in candidates)
            {
                if (TextSimilarity.Similarity(normalized, c.Title) >= 0.85 &&
                    !dto.Sources.Contains(c.PlatformCode))
                {
                    dto.Sources.Add(c.PlatformCode);
                }
            }

            return dto;
        }

        /// <summary>
        /// 分集懒加载：库里没有分集时，回源适配器抓取并落库。
        /// 配合「全量目录快速入库」使用——513 部若逐部抓详情，在限流下要跑 20 分钟，
        /// 改成只对用户真正打开过的剧补分集。
        /// </summary>
        public async Task<bool> EnsureEpisodesAsync(long dramaId, CancellationToken ct = default)
        {
            var drama = await _db.Dramas.FirstOrDefaultAsync(d => d.Id == dramaId, ct);
            if (drama is null) return false;

            var adapter = _adapters.Get(drama.PlatformCode);
            if (adapter is null) return false;

            try
            {
                var detail = await adapter.GetDramaDetailAsync(drama.PlatformDramaId);
                if (detail is null || detail.Episodes.Count == 0) return false;

                // 补全元数据
                if (!string.IsNullOrWhiteSpace(detail.Title)) drama.Title = detail.Title;
                if (!string.IsNullOrWhiteSpace(detail.Description)) drama.Description = detail.Description;
                if (!string.IsNullOrWhiteSpace(detail.CoverUrl)) drama.CoverUrl = detail.CoverUrl;
                if (!string.IsNullOrWhiteSpace(detail.Category)) drama.Category = detail.Category;
                if (detail.Rating > 0) drama.Rating = detail.Rating;

                drama.TotalEpisodes = detail.Episodes.Count;
                drama.UpdatedAt = DateTime.UtcNow;

                var existing = await _db.Episodes
                    .Where(e => e.DramaId == dramaId)
                    .ToListAsync(ct);

                var byNumber = existing.ToDictionary(e => e.EpisodeNumber);

                foreach (var ep in detail.Episodes)
                {
                    if (byNumber.TryGetValue(ep.EpisodeNumber, out var entity))
                    {
                        entity.Title = ep.Title;
                        entity.CoverUrl = ep.CoverUrl;
                        entity.DurationSeconds = ep.DurationSeconds;
                        entity.VideoUrl = ep.VideoUrl;
                        entity.IsFree = ep.IsFree;
                        continue;
                    }

                    entity = new Episode
                    {
                        DramaId = dramaId,
                        EpisodeNumber = ep.EpisodeNumber,
                        Title = ep.Title,
                        CoverUrl = ep.CoverUrl,
                        DurationSeconds = ep.DurationSeconds,
                        VideoUrl = ep.VideoUrl,
                        IsFree = ep.IsFree,
                        IsLocked = !ep.IsFree,
                        CreatedAt = DateTime.UtcNow
                    };

                    _db.Episodes.Add(entity);
                    byNumber[ep.EpisodeNumber] = entity;
                }

                await _db.SaveChangesAsync(ct);
                _logger.LogInformation("已懒加载 {Platform}/{DramaId} 的分集，共 {Count} 集",
                    drama.PlatformCode, drama.PlatformDramaId, detail.Episodes.Count);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("懒加载 {Platform}/{DramaId} 分集失败（{Reason}）",
                    drama.PlatformCode, drama.PlatformDramaId, ex.GetBaseException().Message);
                return false;
            }
        }

        public async Task<List<EpisodeDto>> GetEpisodesAsync(long id, CancellationToken ct = default)
        {
            var episodes = await _db.Episodes.AsNoTracking()
                .Where(e => e.DramaId == id)
                .OrderBy(e => e.EpisodeNumber)
                .ToListAsync(ct);

            return episodes.Select(Mapper.ToEpisodeDto).ToList();
        }

        public async Task<List<string>> GetCategoriesAsync(string? platformCode = null, CancellationToken ct = default)
        {
            var query = _db.Dramas.AsNoTracking().AsQueryable();

            // 按平台取分类：分类名是各采集源自己的字段，源与源之间差别很大。
            // 全平台去重能出上百个分类（大量只有一两部的杂项），而按平台浏览时
            // 其中绝大多数在那个源里根本没有内容 —— 前端点下去就是「暂无内容」。
            if (!string.IsNullOrWhiteSpace(platformCode) &&
                !platformCode.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(d => d.PlatformCode == platformCode);
            }

            // 按内容量排序：常用的分类排在前面，长尾杂项自然沉到最后
            var categories = await query
                .Where(d => d.Category != null && d.Category != "")
                .GroupBy(d => d.Category)
                .Select(g => new { Name = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .ThenBy(x => x.Name)
                .Select(x => x.Name)
                .ToListAsync(ct);

            var ordered = new List<string> { "全部" };
            ordered.AddRange(categories.Where(c => !string.IsNullOrWhiteSpace(c)));
            return ordered;
        }

        /// <summary>
        /// 前端平台筛选列表：以运行时已注册的适配器为准（这样新加源立即出现在前端），
        /// 内容量从库里统计，只返回有内容的平台。
        /// </summary>
        public async Task<List<PlatformInfoDto>> GetPlatformsAsync(CancellationToken ct = default)
        {
            var counts = await _db.Dramas.AsNoTracking()
                .GroupBy(d => d.PlatformCode)
                .Select(g => new { Code = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Code, x => x.Count, ct);

            var result = new List<PlatformInfoDto>();

            foreach (var adapter in _adapters.GetAll())
            {
                counts.TryGetValue(adapter.PlatformCode, out var count);

                // 静态的「能不能站内播」（DRM 等）+ 动态的「这台部署机实际可播」
                // （来自真实播放请求的结果：CDN 按 IP/地区整站拒绝时为 false）
                var staticPlayable = IsPlayable(adapter.PlatformCode);
                var measured = _playability.GetPlatformStatus(adapter.PlatformCode);
                var playable = staticPlayable && measured != false;
                var note = PlayNote(adapter.PlatformCode)
                           ?? (measured == false ? "该源 CDN 拒绝本部署机访问（403/404），无法播放" : null);

                result.Add(new PlatformInfoDto
                {
                    PlatformCode = adapter.PlatformCode,
                    PlatformName = adapter.PlatformName,
                    DramaCount = count,
                    Playable = playable,
                    PlayNote = note,
                    Color = PlatformColor(adapter.PlatformCode)
                });
            }

            // 排序：可播的排前面（用户点开就能看），同组内按内容量
            return result
                .OrderBy(p => p.Playable ? 0 : 1)
                .ThenByDescending(p => p.DramaCount > 0)
                .ThenByDescending(p => p.DramaCount)
                .ThenBy(p => p.PlatformName, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>
        /// 该平台是否支持站内直接播放。
        ///
        /// 红果此前按「DRM 加密」被整体判不可播；2026-10-02 实测更正：视频是**明文 MP4**
        /// （MP4 box 里没有任何加密 box），只是官网只公开前几集、其余要登录/会员。
        /// 所以不再按平台判死 —— 能播的集照常播，取不到地址的集由适配器按集抛异常提示。
        /// </summary>
        private static bool IsPlayable(string platformCode) => platformCode switch
        {
            _ => true
        };

        private static string? PlayNote(string platformCode) => platformCode switch
        {
            "hongguo" => "官网只公开前几集（其余需登录或会员），公开的集可直接播放",
            "huangdou" => "付费集有服务端权益校验，免费集可播",
            _ => null
        };

        private static string PlatformColor(string platformCode) => platformCode switch
        {
            "hongguo" => "#ff4d4f",
            "huangguo" => "#fa8c16",
            "huangdou" => "#faad14",
            "juguo" => "#52c41a",
            "yeguo" => "#13c2c2",
            "diguo" => "#722ed1",
            _ => "#8c8c8c"
        };

        public async Task<long> FindLocalIdAsync(string platformCode, string platformDramaId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(platformCode) || string.IsNullOrWhiteSpace(platformDramaId))
            {
                return 0;
            }

            return await _db.Dramas.AsNoTracking()
                .Where(d => d.PlatformCode == platformCode && d.PlatformDramaId == platformDramaId)
                .Select(d => d.Id)
                .FirstOrDefaultAsync(ct);
        }

        public async Task<long> UpsertFromPlatformAsync(string platformCode, string platformDramaId, CancellationToken ct = default)
        {
            var adapter = _adapters.Get(platformCode)
                ?? throw new InvalidOperationException($"未找到平台适配器: {platformCode}");

            var detail = await adapter.GetDramaDetailAsync(platformDramaId)
                ?? throw new InvalidOperationException($"平台 {platformCode} 未找到短剧 {platformDramaId}");

            var drama = await _db.Dramas
                .Include(d => d.Episodes)
                .FirstOrDefaultAsync(d => d.PlatformCode == platformCode && d.PlatformDramaId == platformDramaId, ct);

            if (drama is null)
            {
                drama = new Drama
                {
                    PlatformCode = platformCode,
                    PlatformDramaId = platformDramaId,
                    CreatedAt = DateTime.UtcNow
                };
                _db.Dramas.Add(drama);
            }

            drama.Title = detail.Title;
            drama.Description = detail.Description;
            drama.CoverUrl = detail.CoverUrl;
            drama.Category = detail.Category;
            drama.TotalEpisodes = detail.TotalEpisodes;
            drama.Status = detail.Status;
            drama.Rating = detail.Rating;
            drama.PlatformPlayUrl = $"https://{platformCode}.example.com/drama/{platformDramaId}";
            drama.SourceUrl = drama.PlatformPlayUrl;
            drama.UpdatedAt = DateTime.UtcNow;

            // 剧集 upsert（按集号去重，防止源站重复集号触发唯一约束）
            var byNumber = drama.Episodes.ToDictionary(e => e.EpisodeNumber);
            foreach (var ep in detail.Episodes)
            {
                if (byNumber.TryGetValue(ep.EpisodeNumber, out var entity))
                {
                    entity.Title = ep.Title;
                    entity.CoverUrl = ep.CoverUrl;
                    entity.DurationSeconds = ep.DurationSeconds;
                    entity.VideoUrl = ep.VideoUrl;
                    entity.IsFree = ep.IsFree;
                    continue;
                }

                entity = new Episode
                {
                    EpisodeNumber = ep.EpisodeNumber,
                    Title = ep.Title,
                    CoverUrl = ep.CoverUrl,
                    DurationSeconds = ep.DurationSeconds,
                    VideoUrl = ep.VideoUrl,
                    IsFree = ep.IsFree,
                    IsLocked = !ep.IsFree,
                    CreatedAt = DateTime.UtcNow
                };

                drama.Episodes.Add(entity);
                byNumber[ep.EpisodeNumber] = entity;
            }

            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("已同步 {Platform}/{DramaId} => 本地 Id={Id}，共 {Count} 集",
                platformCode, platformDramaId, drama.Id, detail.Episodes.Count);

            return drama.Id;
        }
    }
}
