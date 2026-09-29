using System;

namespace ShortDrama.Domain.Entities
{
    public class PlayProgress
    {
        public long Id { get; set; }
        public long UserId { get; set; }
        public long DramaId { get; set; }
        public int EpisodeNumber { get; set; }
        public int PositionSeconds { get; set; }
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public User? User { get; set; }
        public Drama? Drama { get; set; }
    }
}
