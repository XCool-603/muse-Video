using System;

namespace ShortDrama.Domain.Entities
{
    public class Episode
    {
        public long Id { get; set; }
        public long DramaId { get; set; }
        public int EpisodeNumber { get; set; }
        public string Title { get; set; } = string.Empty;
        public string CoverUrl { get; set; } = string.Empty;
        public int DurationSeconds { get; set; }
        public string VideoUrl { get; set; } = string.Empty; // m3u8 or mp4
        public bool IsFree { get; set; } = true;
        public bool IsLocked { get; set; } = false;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation property
        public Drama? Drama { get; set; }
    }
}
