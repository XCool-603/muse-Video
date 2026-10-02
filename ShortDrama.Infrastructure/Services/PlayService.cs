using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
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
        private readonly PlayabilityTracker _playability;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<PlayService> _logger;

        private static readonly TimeSpan PlayUrlCacheDuration = TimeSpan.FromMinutes(10);

        public PlayService(
            AppDbContext db,
            IAdapterFactory adapters,
            IMemoryCache cache,
            PlayabilityTracker playability,
            IHttpClientFactory httpClientFactory,
            ILogger<PlayService> logger)
        {
            _db = db;
            _adapters = adapters;
            _cache = cache;
            _playability = playability;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        /// <summary>
        /// 判断是「明文 MP4」还是 HLS。
        ///
        /// 不能只看扩展名：红果的字节 CDN 地址形如
        /// https://v26-hgweb.qznovelvod.com/…/video/tos/cn/…?a=8662 —— 没有后缀，
        /// 只看扩展名会被判成 HLS，交给 hls.js 拉必然失败（实测就是这个问题）。
        /// 没有明确后缀时探一次 content-type，结果缓存 30 分钟，避免每次播放都探。
        /// </summary>
        private async Task<bool> IsProgressiveAsync(string url, CancellationToken ct)
        {
            if (url.Contains(".mp4", StringComparison.OrdinalIgnoreCase)) return true;
            if (url.Contains(".m3u8", StringComparison.OrdinalIgnoreCase)) return false;

            var key = $"urlkind:{url}";
            if (_cache.TryGetValue(key, out bool cached)) return cached;

            var progressive = false;
            try
            {
                // 用 Range GET 而不是 HEAD：HEAD 在部分 CDN 上不返回 content-type，
                // 而 Range 只取 1 字节、拿到响应头就断开，代价可以忽略。
                //
                // 必须用**不带默认请求头**的客户端：应用里那个 "stream" 客户端带了一个
                // 假 Referer（https://www.example.com/），而红果的 CDN 会校验 Referer ——
                // 实测外站 Referer 直接 403 text/html，于是内容类型被判成 HLS，
                // 交给 hls.js 去拉必然失败。
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 0);
                using var response = await _httpClientFactory.CreateClient()
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

                var mediaType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
                progressive = mediaType.StartsWith("video/", StringComparison.OrdinalIgnoreCase) &&
                              !mediaType.Contains("mpegurl", StringComparison.OrdinalIgnoreCase);

                _logger.LogInformation("流类型探测：{Type} → {Kind}  ({Url})",
                    string.IsNullOrEmpty(mediaType) ? "(无 content-type)" : mediaType,
                    progressive ? "mp4" : "hls", url);
            }
            catch (Exception ex)
            {
                // 探测失败就按 HLS 处理（保持原有行为），别因为探测挂了就判不可播
                _logger.LogWarning(ex, "探测流类型失败，按 HLS 处理: {Url}", url);
            }

            _cache.Set(key, progressive, TimeSpan.FromMinutes(30));
            return progressive;
        }

        /// <summary>
        /// 该剧所属源出现过的其它 CDN 主机。
        /// 从已入库的分集地址里取样统计 —— 采集站给每部剧的地址可能指向不同主机，
        /// 其中一部分已退役（实测暴风 bfeng10.com 全站 404，fengbao13.com 正常）。
        /// 按「已知可用 → 未知 → 已知被拒」排序，换主机重试时先试靠谱的。
        /// </summary>
        public async Task<List<string>> GetAlternateHostsAsync(long dramaId, CancellationToken ct = default)
        {
            var platformCode = await _db.Dramas.AsNoTracking()
                .Where(d => d.Id == dramaId)
                .Select(d => d.PlatformCode)
                .FirstOrDefaultAsync(ct);

            if (string.IsNullOrWhiteSpace(platformCode)) return new List<string>();

            var cacheKey = $"cdn-hosts:{platformCode}";
            if (_cache.TryGetValue(cacheKey, out List<string>? cached) && cached is not null)
            {
                return cached;
            }

            var dramaIds = _db.Dramas.AsNoTracking()
                .Where(d => d.PlatformCode == platformCode)
                .Select(d => d.Id);

            // 取样即可：一个源的主机就那么几台，400 条地址足够覆盖
            var urls = await _db.Episodes.AsNoTracking()
                .Where(e => dramaIds.Contains(e.DramaId))
                .OrderByDescending(e => e.Id)
                .Select(e => e.VideoUrl)
                .Take(400)
                .ToListAsync(ct);

            var hosts = urls
                .Select(u => Uri.TryCreate(u, UriKind.Absolute, out var uri) ? uri.Host : null)
                .Where(h => !string.IsNullOrWhiteSpace(h))
                .Select(h => h!)
                .ToList();

            // 关键：还要问源站「你现在用哪些主机」。本地库存的是入库当时的地址，
            // 主机可能早就换了 —— 实测暴风资源的本地库主机全是 404 的旧主机，
            // 而源站当前目录里的 fengbao13.com 是好的。少了这一路，换主机只会撞墙。
            if (_adapters.Get(platformCode) is ILiveCatalogAdapter live)
            {
                try
                {
                    hosts.AddRange(await live.GetCdnHostsAsync(ct));
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "取源站 CDN 主机失败：{Platform}", platformCode);
                }
            }

            var ranked = _playability.RankHosts(hosts);
            if (ranked.Count > 0)
            {
                _cache.Set(cacheKey, ranked, TimeSpan.FromMinutes(30));
            }

            return ranked;
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
                    catch (NotSupportedException)
                    {
                        // 适配器明确表示「这个平台无法在服务端播放」（例如红果短剧是
                        // MP4 CENC / AES-128 CTR DRM 加密，密钥不下发到 Web 端）。
                        // 必须原样抛出：绝不能退回 ep.VideoUrl —— 那里存的往往只是
                        // 「官方播放页」地址，拿它当流地址会让上层把网页 HTML 当成播放列表
                        // 返回给播放器（实测红果就是这样，播放列表里全是 HTML 行）。
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "获取平台播放地址失败，回退本地缓存地址: {Url}", rawUrl);

                        // 回退地址也必须看起来是可播放的媒体地址，否则宁可判为不可播，
                        // 也不要让一个网页地址冒充视频流。
                        if (!LooksLikeMediaUrl(rawUrl))
                        {
                            _logger.LogWarning("本地缓存地址不是媒体地址，判定为不可播放: {Url}", rawUrl);
                            return null;
                        }
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
            var isProgressive = await IsProgressiveAsync(rawUrl, ct);

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

        /// <summary>
        /// 判断地址看起来是不是可直接播放的媒体流（HLS / 明文 MP4）。
        /// 只用于「适配器取地址失败后的回退」这一处：此时宁可判为不可播，
        /// 也不能把官方播放页之类的网页地址当成视频流。
        /// </summary>
        private static bool LooksLikeMediaUrl(string? url) =>
            !string.IsNullOrWhiteSpace(url) &&
            (url.Contains(".m3u8", StringComparison.OrdinalIgnoreCase) ||
             url.Contains(".mp4", StringComparison.OrdinalIgnoreCase));

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
