using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ShortDrama.Application.DTOs;
using ShortDrama.Application.Services;
using ShortDrama.Domain.Entities;
using ShortDrama.Infrastructure.Data;

namespace ShortDrama.Infrastructure.Services
{
    public class FavoriteService : IFavoriteService
    {
        private readonly AppDbContext _db;

        public FavoriteService(AppDbContext db) => _db = db;

        public async Task<List<DramaDto>> ListAsync(string userId, CancellationToken ct = default)
        {
            if (!long.TryParse(userId, out var uid)) return new List<DramaDto>();

            var dramas = await _db.Favorites.AsNoTracking()
                .Where(f => f.UserId == uid)
                .OrderByDescending(f => f.CreatedAt)
                .Select(f => f.Drama!)
                .ToListAsync(ct);

            return dramas.Select(d => Mapper.ToDto(d)).ToList();
        }

        public async Task<bool> AddAsync(string userId, long dramaId, CancellationToken ct = default)
        {
            if (!long.TryParse(userId, out var uid)) return false;

            var exists = await _db.Favorites.AnyAsync(f => f.UserId == uid && f.DramaId == dramaId, ct);
            if (exists) return true;

            var dramaExists = await _db.Dramas.AnyAsync(d => d.Id == dramaId, ct);
            if (!dramaExists) return false;

            _db.Favorites.Add(new Favorite { UserId = uid, DramaId = dramaId, CreatedAt = DateTime.UtcNow });
            await _db.SaveChangesAsync(ct);
            return true;
        }

        public async Task<bool> RemoveAsync(string userId, long dramaId, CancellationToken ct = default)
        {
            if (!long.TryParse(userId, out var uid)) return false;

            var favorite = await _db.Favorites.FirstOrDefaultAsync(f => f.UserId == uid && f.DramaId == dramaId, ct);
            if (favorite is null) return false;

            _db.Favorites.Remove(favorite);
            await _db.SaveChangesAsync(ct);
            return true;
        }

        public async Task<bool> IsFavoriteAsync(string userId, long dramaId, CancellationToken ct = default)
        {
            if (!long.TryParse(userId, out var uid)) return false;
            return await _db.Favorites.AnyAsync(f => f.UserId == uid && f.DramaId == dramaId, ct);
        }
    }
}
