using System.Collections.Generic;
using System.Threading.Tasks;

namespace ShortDrama.Application.Adapters
{
    public interface IPlatformAdapter
    {
        string PlatformName { get; }
        string PlatformCode { get; }

        // Search short dramas
        Task<PlatformSearchResult> SearchAsync(string keyword, int page = 1, int pageSize = 10);

        // Get drama detail and episode list
        Task<PlatformDramaDetail?> GetDramaDetailAsync(string dramaId);

        // Get actual play url for specific episode (and strip ads or decrypt)
        Task<string> GetPlayUrlAsync(string dramaId, int episodeNumber);

        // Get rank lists
        Task<List<PlatformRankItem>> GetRankAsync(string type, int limit = 20);

        // Get latest dramas
        Task<List<PlatformNewItem>> GetLatestAsync(string category, int limit = 20);
    }

    public class PlatformSearchResult
    {
        public List<PlatformSearchItem> Items { get; set; } = new();
        public int Total { get; set; }
    }

    public class PlatformSearchItem
    {
        public string PlatformDramaId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string CoverUrl { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public int TotalEpisodes { get; set; }
        public string Status { get; set; } = "ongoing";
        public double Rating { get; set; }
    }

    public class PlatformDramaDetail
    {
        public string PlatformDramaId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string CoverUrl { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public int TotalEpisodes { get; set; }
        public string Status { get; set; } = "ongoing";
        public double Rating { get; set; }
        public List<PlatformEpisodeItem> Episodes { get; set; } = new();
    }

    public class PlatformEpisodeItem
    {
        public int EpisodeNumber { get; set; }
        public string Title { get; set; } = string.Empty;
        public string CoverUrl { get; set; } = string.Empty;
        public int DurationSeconds { get; set; }
        public string VideoUrl { get; set; } = string.Empty;
        public bool IsFree { get; set; } = true;
    }

    public class PlatformRankItem
    {
        public string PlatformDramaId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string CoverUrl { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public long PlayCount { get; set; }
    }

    public class PlatformNewItem
    {
        public string PlatformDramaId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string CoverUrl { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;

        /// <summary>总集数（目录接口能提供时填充，用于列表页展示）</summary>
        public int TotalEpisodes { get; set; }

        /// <summary>评分（可选）</summary>
        public double Rating { get; set; }

        /// <summary>简介（可选）</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>更新状态：ongoing / completed</summary>
        public string Status { get; set; } = "ongoing";
    }
}
