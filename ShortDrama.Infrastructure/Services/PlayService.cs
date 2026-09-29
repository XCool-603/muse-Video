using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using ShortDrama.Application.Adapters;
using ShortDrama.Application.DTOs;
using ShortDrama.Application.Services;
using ShortDrama.Domain.Entities;
using ShortDrama.Infrastructure.Data;

namespace ShortDrama.Infrastructure.Services
{
    public class PlayService : IPlayService
    {
        private readonly AppDbContext _db;
        private readonly IAdapterFactory _adapters;
        private readonly IMemoryCache _cache;
        private readonly ILogger<PlayService> _logger;

        private static readonly TimeSpan PlayUrlCacheDuration = TimeSpan.FromMinutes(10);

        public PlayService(AppDbContext db, IAdapterFactory adapters, IMemoryCache cache, ILogger<PlayService> logger)
        {
            _db = db;
            _adapters = adapters;
            _cache = cache;
            _logger = logger;
        }

        public async Task<PlayInfoDto?> GetPlayInfoAsync(long dramaId, int episode, string? userId, CancellationToken ct = default)
        {
            var drama = await _db.Dramas.AsNoTracking().FirstOrDefaultAsync(d => d.Id == dramaId, ct);
            if (drama is null) return null;

            var ep = await _db.Episodes.AsNoTracking()
                .FirstOrDefaultAsync(e => e.DramaId == dramaId && e.EpisodeNumber == episode, ct);

            // 本地无剧集记录时，回源适配器补齐
            if (ep is null)
            {
                var adapter = _adapters.Get(drama.PlatformCode);
                if (adapter is null) return null;

                var detail = await adapter.GetDramaDetailAsync(drama.PlatformDramaId);
                var remote = detail?.Episodes.FirstOrDefault(x => x.EpisodeNumber == episode);
                if (remote is null) return null;

                ep = new Episode
                {
                    DramaId = dramaId,
                    EpisodeNumber = remote.EpisodeNumber,
                    Title = remote.Title,
                    CoverUrl = remote.CoverUrl,
                    DurationSeconds = remote.DurationSeconds,
                    VideoUrl = remote.VideoUrl,
                    IsFree = remote.IsFree,
                    IsLocked = !remote.IsFree
                };
            }

            // 播放地址：优先缓存，其次适配器实时获取（走无广告源）
            var cacheKey = $"playurl:{drama.PlatformCode}:{drama.PlatformDramaId}:{episode}";
            var rawUrl = ep.VideoUrl;

            if (!_cache.TryGetValue(cacheKey, out string? cachedUrl) || string.IsNullOrWhiteSpace(cachedUrl))
            {
                var adapter = _adapters.Get(drama.PlatformCode);
                if (adapter is not null)
                {
                    try
                    {
                        // 多源切换 / 会员鉴权绕过策略在适配器内部实现，这里拿到的是干净地址
                        rawUrl = await adapter.GetPlayUrlAsync(drama.PlatformDramaId, episode);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "获取平台播放地址失败，回退本地缓存地址: {Url}", rawUrl);
                    }
                }

                if (!string.IsNullOrWhiteSpace(rawUrl))
                {
                    _cache.Set(cacheKey, rawUrl, PlayUrlCacheDuration);
                }
            }
            else
            {
                rawUrl = cachedUrl!;
            }

            // 续播位置
            var resume = 0;
            if (!string.IsNullOrWhiteSpace(userId) && long.TryParse(userId, out var uid))
            {
                var progress = await _db.PlayProgresses.AsNoTracking()
                    .FirstOrDefaultAsync(p => p.UserId == uid && p.DramaId == dramaId, ct);

                if (progress is not null && progress.EpisodeNumber == episode)
                {
                    resume = progress.PositionSeconds;
                }
            }

            // 播放计数（异步累加，不阻塞返回）
            _ = IncrementPlayCountAsync(dramaId);

            // 判定流类型：明文 MP4 直接交给 <video>，m3u8 走 hls.js + 去广告代理
            var isProgressive = rawUrl.Contains(".mp4", StringComparison.OrdinalIgnoreCase);

            return new PlayInfoDto
            {
                DramaId = dramaId,
                DramaTitle = drama.Title,
                EpisodeNumber = episode,
                PlayUrl = rawUrl,
                PlatformCode = drama.PlatformCode,
                PlatformName = Mapper.PlatformDisplayName(drama.PlatformCode),
                DurationSeconds = ep.DurationSeconds,
                AdFree = true,
                ResumePosition = resume,
                StreamType = isProgressive ? "mp4" : "hls"
            };
        }

        private async Task IncrementPlayCountAsync(long dramaId)
        {
            try
            {
                var drama = await _db.Dramas.FirstOrDefaultAsync(d => d.Id == dramaId);
                if (drama is not null)
                {
                    drama.PlayCount += 1;
                    await _db.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "播放计数更新失败 DramaId={DramaId}", dramaId);
            }
        }

        public async Task SaveProgressAsync(string userId, long dramaId, int episode, int position, CancellationToken ct = default)
        {
            if (!long.TryParse(userId, out var uid)) return;

            var progress = await _db.PlayProgresses
                .FirstOrDefaultAsync(p => p.UserId == uid && p.DramaId == dramaId, ct);

            if (progress is null)
            {
                progress = new PlayProgress { UserId = uid, DramaId = dramaId };
                _db.PlayProgresses.Add(progress);
            }

            progress.EpisodeNumber = episode;
            progress.PositionSeconds = Math.Max(0, position);
            progress.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);
        }

        public async Task<PlayProgressDto?> GetProgressAsync(string userId, long dramaId, CancellationToken ct = default)
        {
            if (!long.TryParse(userId, out var uid)) return null;

            var progress = await _db.PlayProgresses.AsNoTracking()
                .FirstOrDefaultAsync(p => p.UserId == uid && p.DramaId == dramaId, ct);

            if (progress is null) return null;

            return new PlayProgressDto
            {
                DramaId = progress.DramaId,
                EpisodeNumber = progress.EpisodeNumber,
                PositionSeconds = progress.PositionSeconds,
                UpdatedAt = progress.UpdatedAt
            };
        }

        public async Task<List<PlayProgressDto>> GetHistoryAsync(string userId, int limit = 20, CancellationToken ct = default)
        {
            if (!long.TryParse(userId, out var uid)) return new List<PlayProgressDto>();

            return await _db.PlayProgresses.AsNoTracking()
                .Where(p => p.UserId == uid)
                .OrderByDescending(p => p.UpdatedAt)
                .Take(limit)
                .Select(p => new PlayProgressDto
                {
                    DramaId = p.DramaId,
                    EpisodeNumber = p.EpisodeNumber,
                    PositionSeconds = p.PositionSeconds,
                    UpdatedAt = p.UpdatedAt
                })
                .ToListAsync(ct);
        }
    }
}
