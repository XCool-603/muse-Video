using System;
using System.Collections.Generic;

namespace ShortDrama.Application.DTOs
{
    /// <summary>统一响应封装</summary>
    public class ApiResponse<T>
    {
        public int Code { get; set; }
        public string Message { get; set; } = "ok";
        public T? Data { get; set; }
        public long Timestamp { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        public static ApiResponse<T> Success(T data, string message = "ok")
            => new() { Code = 0, Message = message, Data = data };

        public static ApiResponse<T> Fail(int code, string message)
            => new() { Code = code, Message = message, Data = default };
    }

    public class PagedResult<T>
    {
        public List<T> Items { get; set; } = new();
        public int Total { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public bool HasMore => Page * PageSize < Total;

        public static PagedResult<T> Create(List<T> items, int total, int page, int pageSize)
            => new() { Items = items, Total = total, Page = page, PageSize = pageSize };
    }

    public class DramaDto
    {
        public long Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string CoverUrl { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public int TotalEpisodes { get; set; }
        public string Status { get; set; } = "ongoing";
        public long PlayCount { get; set; }
        public double Rating { get; set; }
        public string PlatformCode { get; set; } = string.Empty;
        public string PlatformName { get; set; } = string.Empty;
        public string PlatformDramaId { get; set; } = string.Empty;
        public List<string> Sources { get; set; } = new();
        public DateTime UpdatedAt { get; set; }
    }

    public class DramaDetailDto : DramaDto
    {
        public List<EpisodeDto> Episodes { get; set; } = new();
        public List<DramaDto> Recommends { get; set; } = new();
    }

    public class EpisodeDto
    {
        public long Id { get; set; }
        public int EpisodeNumber { get; set; }
        public string Title { get; set; } = string.Empty;
        public string CoverUrl { get; set; } = string.Empty;
        public int DurationSeconds { get; set; }
        public bool IsFree { get; set; }
        public bool IsLocked { get; set; }
    }

    public class PlayInfoDto
    {
        public long DramaId { get; set; }
        public string DramaTitle { get; set; } = string.Empty;
        public int EpisodeNumber { get; set; }
        public string PlayUrl { get; set; } = string.Empty;
        public string PlatformCode { get; set; } = string.Empty;
        public string PlatformName { get; set; } = string.Empty;
        public int DurationSeconds { get; set; }
        public bool AdFree { get; set; }
        public int ResumePosition { get; set; }
        public List<string> SkippedAdSegments { get; set; } = new();

        /// <summary>
        /// 流类型：hls（走 hls.js 播放，地址为后端去广告代理）
        /// 或 mp4（明文渐进式文件，直接给 &lt;video&gt; 播）。
        /// 前端据此选择播放方式。
        /// </summary>
        public string StreamType { get; set; } = "hls";

        /// <summary>该集是否需要跳转官方页面观看（DRM 等无法服务端播放的情况）</summary>
        public bool RequiresExternalPlayer { get; set; }
    }

    public class PlayProgressDto
    {
        public long DramaId { get; set; }
        public int EpisodeNumber { get; set; }
        public int PositionSeconds { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class ReportProgressRequest
    {
        public long DramaId { get; set; }
        public int Episode { get; set; }
        public int Position { get; set; }
    }

    public class LoginRequest
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    public class RegisterRequest
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
    }

    public class LoginResponse
    {
        public string Token { get; set; } = string.Empty;
        public UserDto User { get; set; } = new();
        public long ExpiresIn { get; set; }
    }

    public class UserDto
    {
        public long Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string AvatarUrl { get; set; } = string.Empty;
        public string Role { get; set; } = "user";
    }

    public class PlatformSourceDto
    {
        public long Id { get; set; }
        public string PlatformCode { get; set; } = string.Empty;
        public string PlatformName { get; set; } = string.Empty;
        public string BaseUrl { get; set; } = string.Empty;
        public string AdapterType { get; set; } = string.Empty;
        public bool IsEnabled { get; set; }
        public DateTime? LastSyncAt { get; set; }
    }

    /// <summary>前端用的平台信息（含内容量统计）</summary>
    public class PlatformInfoDto
    {
        public string PlatformCode { get; set; } = string.Empty;
        public string PlatformName { get; set; } = string.Empty;

        /// <summary>库内短剧数</summary>
        public int DramaCount { get; set; }

        /// <summary>是否支持站内直接播放</summary>
        public bool Playable { get; set; }

        /// <summary>不可播放时的说明（如 DRM 加密）</summary>
        public string? PlayNote { get; set; }

        /// <summary>前端展示色（用于来源圆点/标签）</summary>
        public string Color { get; set; } = "#888888";
    }

    public class AdminDramaSaveRequest
    {
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string CoverUrl { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Status { get; set; } = "ongoing";
        public string PlatformCode { get; set; } = string.Empty;
        public string PlatformDramaId { get; set; } = string.Empty;
        public double Rating { get; set; }
    }

    public class DashboardDto
    {
        public int DramaCount { get; set; }
        public int EpisodeCount { get; set; }
        public int UserCount { get; set; }
        public long TotalPlayCount { get; set; }
        public int PlatformCount { get; set; }
        public List<CategoryStatDto> CategoryStats { get; set; } = new();
        public List<PlatformStatDto> PlatformStats { get; set; } = new();
    }

    public class CategoryStatDto
    {
        public string Category { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    public class PlatformStatDto
    {
        public string PlatformCode { get; set; } = string.Empty;
        public string PlatformName { get; set; } = string.Empty;
        public int Count { get; set; }
    }
}
