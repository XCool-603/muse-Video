using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ShortDrama.Application.Adapters;
using ShortDrama.Application.DTOs;
using ShortDrama.Application.Services;
using ShortDrama.Domain.Entities;
using ShortDrama.Infrastructure.Data;

namespace ShortDrama.Infrastructure.Services
{
    public class AdminService : IAdminService
    {
        private readonly AppDbContext _db;
        private readonly IAdapterFactory _adapters;

        public AdminService(AppDbContext db, IAdapterFactory adapters)
        {
            _db = db;
            _adapters = adapters;
        }

        public async Task<DashboardDto> GetDashboardAsync(CancellationToken ct = default)
        {
            var categoryStats = await _db.Dramas.AsNoTracking()
                .GroupBy(d => d.Category)
                .Select(g => new CategoryStatDto { Category = g.Key, Count = g.Count() })
                .ToListAsync(ct);

            var platformStats = await _db.Dramas.AsNoTracking()
                .GroupBy(d => d.PlatformCode)
                .Select(g => new PlatformStatDto { PlatformCode = g.Key, Count = g.Count() })
                .ToListAsync(ct);

            foreach (var stat in platformStats)
            {
                stat.PlatformName = Mapper.PlatformDisplayName(stat.PlatformCode);
            }

            return new DashboardDto
            {
                DramaCount = await _db.Dramas.CountAsync(ct),
                EpisodeCount = await _db.Episodes.CountAsync(ct),
                UserCount = await _db.Users.CountAsync(ct),
                TotalPlayCount = await _db.Dramas.SumAsync(d => (long?)d.PlayCount, ct) ?? 0,
                PlatformCount = _adapters.GetAll().Count,
                CategoryStats = categoryStats.OrderByDescending(c => c.Count).ToList(),
                PlatformStats = platformStats.OrderByDescending(p => p.Count).ToList()
            };
        }

        public async Task<List<PlatformSourceDto>> GetPlatformSourcesAsync(CancellationToken ct = default)
        {
            var configured = await _db.PlatformSources.AsNoTracking().ToListAsync(ct);
            var configuredCodes = configured.Select(c => c.PlatformCode).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var result = configured.Select(s => new PlatformSourceDto
            {
                Id = s.Id,
                PlatformCode = s.PlatformCode,
                PlatformName = s.PlatformName,
                BaseUrl = s.BaseUrl,
                AdapterType = s.AdapterType,
                IsEnabled = s.IsEnabled,
                LastSyncAt = s.LastSyncAt
            }).ToList();

            // 运行时已注册但未落库的适配器也展示出来
            foreach (var adapter in _adapters.GetAll())
            {
                if (configuredCodes.Contains(adapter.PlatformCode)) continue;

                result.Add(new PlatformSourceDto
                {
                    Id = 0,
                    PlatformCode = adapter.PlatformCode,
                    PlatformName = adapter.PlatformName,
                    BaseUrl = $"https://{adapter.PlatformCode}.example.com",
                    AdapterType = adapter.GetType().Name,
                    IsEnabled = true,
                    LastSyncAt = null
                });
            }

            return result;
        }

        public async Task<bool> TogglePlatformAsync(long id, bool enabled, CancellationToken ct = default)
        {
            var source = await _db.PlatformSources.FirstOrDefaultAsync(s => s.Id == id, ct);
            if (source is null) return false;

            source.IsEnabled = enabled;
            await _db.SaveChangesAsync(ct);
            await _adapters.RefreshAsync();
            return true;
        }

        public async Task<long> CreateDramaAsync(AdminDramaSaveRequest request, CancellationToken ct = default)
        {
            var drama = new Drama
            {
                Title = request.Title,
                Description = request.Description,
                CoverUrl = request.CoverUrl,
                Category = request.Category,
                Status = request.Status,
                Rating = request.Rating,
                PlatformCode = string.IsNullOrWhiteSpace(request.PlatformCode) ? "manual" : request.PlatformCode,
                PlatformDramaId = string.IsNullOrWhiteSpace(request.PlatformDramaId) ? Guid.NewGuid().ToString("N") : request.PlatformDramaId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _db.Dramas.Add(drama);
            await _db.SaveChangesAsync(ct);
            return drama.Id;
        }

        public async Task<bool> UpdateDramaAsync(long id, AdminDramaSaveRequest request, CancellationToken ct = default)
        {
            var drama = await _db.Dramas.FirstOrDefaultAsync(d => d.Id == id, ct);
            if (drama is null) return false;

            drama.Title = request.Title;
            drama.Description = request.Description;
            drama.CoverUrl = request.CoverUrl;
            drama.Category = request.Category;
            drama.Status = request.Status;
            drama.Rating = request.Rating;
            drama.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);
            return true;
        }

        public async Task<bool> DeleteDramaAsync(long id, CancellationToken ct = default)
        {
            var drama = await _db.Dramas.FirstOrDefaultAsync(d => d.Id == id, ct);
            if (drama is null) return false;

            _db.Dramas.Remove(drama);
            await _db.SaveChangesAsync(ct);
            return true;
        }

        public async Task<bool> UpdateEpisodeAsync(long dramaId, int episodeNumber, bool isFree, bool isLocked, string videoUrl, CancellationToken ct = default)
        {
            var episode = await _db.Episodes
                .FirstOrDefaultAsync(e => e.DramaId == dramaId && e.EpisodeNumber == episodeNumber, ct);

            if (episode is null) return false;

            episode.IsFree = isFree;
            episode.IsLocked = isLocked;
            if (!string.IsNullOrWhiteSpace(videoUrl)) episode.VideoUrl = videoUrl;

            await _db.SaveChangesAsync(ct);
            return true;
        }
    }
}
