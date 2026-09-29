using System;

namespace ShortDrama.Domain.Entities
{
    public class User
    {
        public long Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public string AvatarUrl { get; set; } = string.Empty;
        public string Role { get; set; } = "user"; // user, admin
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
