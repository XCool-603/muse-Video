using System;

namespace ShortDrama.Domain.Entities
{
    public class Favorite
    {
        public long Id { get; set; }
        public long UserId { get; set; }
        public long DramaId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public User? User { get; set; }
        public Drama? Drama { get; set; }
    }
}
