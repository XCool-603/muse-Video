using System;

namespace ShortDrama.Domain.Entities
{
    public class PlatformSource
    {
        public long Id { get; set; }
        public string PlatformCode { get; set; } = string.Empty; // e.g., hongguo, huangdou
        public string PlatformName { get; set; } = string.Empty;
        public string BaseUrl { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
        public string AdapterType { get; set; } = string.Empty;
        public bool IsEnabled { get; set; } = true;
        public DateTime? LastSyncAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
