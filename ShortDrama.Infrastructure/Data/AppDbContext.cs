using Microsoft.EntityFrameworkCore;
using ShortDrama.Domain.Entities;

namespace ShortDrama.Infrastructure.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        public DbSet<Drama> Dramas => Set<Drama>();
        public DbSet<Episode> Episodes => Set<Episode>();
        public DbSet<User> Users => Set<User>();
        public DbSet<PlayProgress> PlayProgresses => Set<PlayProgress>();
        public DbSet<Favorite> Favorites => Set<Favorite>();
        public DbSet<PlatformSource> PlatformSources => Set<PlatformSource>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure Drama
            modelBuilder.Entity<Drama>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => new { e.PlatformCode, e.PlatformDramaId }).IsUnique();
                entity.HasIndex(e => e.Category);
            });

            // Configure Episode
            modelBuilder.Entity<Episode>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => new { e.DramaId, e.EpisodeNumber }).IsUnique();
                entity.HasOne(e => e.Drama)
                      .WithMany(d => d.Episodes)
                      .HasForeignKey(e => e.DramaId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // Configure User
            modelBuilder.Entity<User>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.Username).IsUnique();
                entity.HasIndex(e => e.Email).IsUnique();
            });

            // Configure PlayProgress
            modelBuilder.Entity<PlayProgress>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => new { e.UserId, e.DramaId }).IsUnique();
            });

            // Configure Favorite
            modelBuilder.Entity<Favorite>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => new { e.UserId, e.DramaId }).IsUnique();
            });

            // Configure PlatformSource
            modelBuilder.Entity<PlatformSource>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.PlatformCode).IsUnique();
            });
        }
    }
}
