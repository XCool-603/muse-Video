using System.Collections.Generic;
using System.Linq;
using ShortDrama.Application.Adapters;
using ShortDrama.Application.DTOs;
using ShortDrama.Domain.Entities;

namespace ShortDrama.Infrastructure.Services
{
    internal static class Mapper
    {
        public static DramaDto ToDto(Drama d, string platformName = "")
        {
            return new DramaDto
            {
                Id = d.Id,
                Title = d.Title,
                Description = d.Description,
                CoverUrl = d.CoverUrl,
                Category = d.Category,
                TotalEpisodes = d.TotalEpisodes,
                Status = d.Status,
                PlayCount = d.PlayCount,
                Rating = d.Rating,
                PlatformCode = d.PlatformCode,
                PlatformDramaId = d.PlatformDramaId,
                PlatformName = string.IsNullOrEmpty(platformName) ? PlatformDisplayName(d.PlatformCode) : platformName,
                Sources = new List<string> { d.PlatformCode },
                UpdatedAt = d.UpdatedAt
            };
        }

        public static DramaDetailDto ToDetailDto(Drama d, string platformName = "")
        {
            var dto = new DramaDetailDto
            {
                Id = d.Id,
                Title = d.Title,
                Description = d.Description,
                CoverUrl = d.CoverUrl,
                Category = d.Category,
                TotalEpisodes = d.TotalEpisodes,
                Status = d.Status,
                PlayCount = d.PlayCount,
                Rating = d.Rating,
                PlatformCode = d.PlatformCode,
                PlatformDramaId = d.PlatformDramaId,
                PlatformName = string.IsNullOrEmpty(platformName) ? PlatformDisplayName(d.PlatformCode) : platformName,
                Sources = new List<string> { d.PlatformCode },
                UpdatedAt = d.UpdatedAt,
                Episodes = d.Episodes
                    .OrderBy(e => e.EpisodeNumber)
                    .Select(ToEpisodeDto)
                    .ToList()
            };

            return dto;
        }

        public static EpisodeDto ToEpisodeDto(Episode e) => new()
        {
            Id = e.Id,
            EpisodeNumber = e.EpisodeNumber,
            Title = string.IsNullOrEmpty(e.Title) ? $"第 {e.EpisodeNumber} 集" : e.Title,
            CoverUrl = e.CoverUrl,
            DurationSeconds = e.DurationSeconds,
            IsFree = e.IsFree,
            IsLocked = e.IsLocked
        };

        public static DramaDto FromPlatformItem(PlatformSearchItem item, string platformCode)
        {
            return new DramaDto
            {
                Id = 0,
                Title = item.Title,
                Description = item.Description,
                CoverUrl = item.CoverUrl,
                Category = item.Category,
                TotalEpisodes = item.TotalEpisodes,
                Status = item.Status,
                Rating = item.Rating,
                PlatformCode = platformCode,
                PlatformDramaId = item.PlatformDramaId,
                PlatformName = PlatformDisplayName(platformCode),
                Sources = new List<string> { platformCode },
                UpdatedAt = System.DateTime.UtcNow
            };
        }

        /// <summary>
        /// 平台编码 → 展示名。
        /// 先查启动期注册进来的配置名字（采集源都走这条，例如 acms_bfzy → 暴风资源），
        /// 没注册过再退回内置的少量映射，最后才原样返回编码。
        /// </summary>
        public static string PlatformDisplayName(string code)
        {
            var registered = PlatformNameRegistry.Resolve(code);
            if (!string.IsNullOrEmpty(registered)) return registered;

            return code switch
            {
                "hongguo" => "红果短剧",
                "huangdou" => "黄豆短剧",
                "juguo" => "剧果短剧",
                "yeguo" => "野果短剧",
                "diguo" => "帝果短剧",
                _ => code
            };
        }
    }
}
