using System;
using System.Collections.Generic;

namespace ShortDrama.Domain.Entities
{
    public class Drama
    {
        public long Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string CoverUrl { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty; // 种田/剧情/穿越/霸总/重生
        public int TotalEpisodes { get; set; }
        public string Status { get; set; } = "ongoing"; // ongoing = 连载中, completed = 已完结
        public long PlayCount { get; set; }
        public double Rating { get; set; }
        public string PlatformCode { get; set; } = string.Empty; // 来源平台 (hongguo, huangdou, etc.)
        public string PlatformDramaId { get; set; } = string.Empty; // 平台原始ID
        public string PlatformPlayUrl { get; set; } = string.Empty; // 平台原始播放页
        public string SourceUrl { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation property
        public List<Episode> Episodes { get; set; } = new();
    }
}
