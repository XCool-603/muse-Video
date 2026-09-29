using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ShortDrama.Application.Adapters;

namespace ShortDrama.Infrastructure.Adapters
{
    /// <summary>
    /// 黄豆短剧 —— **演示用模拟实现**（Demo:Enabled=true 时启用）。
    ///
    /// 返回结构真实的假数据，视频源指向公开测试流，用于无网络环境下演示完整交互。
    /// 真实接入请见 <see cref="HuangDouAdapter"/>（hddj.tv）。
    /// </summary>
    public class HuangDouDemoAdapter : IPlatformAdapter
    {
        public string PlatformName => "黄豆短剧(演示)";
        public string PlatformCode => "demo_huangdou";

        private readonly List<PlatformSearchItem> _mockDramas = new()
        {
            new()
            {
                PlatformDramaId = "hd_101",
                Title = "重生之我在八零当首富",
                Description = "重回1980年，林晚星靠着前世记忆从摆地摊做起，一路逆袭成为名震全国的民营企业家。",
                CoverUrl = "https://images.unsplash.com/photo-1524712245354-2c4e5e7121c0?q=80&w=300&h=400&fit=crop",
                Category = "重生",
                TotalEpisodes = 88,
                Status = "completed",
                Rating = 9.0
            },
            new()
            {
                PlatformDramaId = "hd_102",
                Title = "种田小福女：山里汉宠妻无度",
                Description = "一朝穿越成农家小福女，苏念带着空间灵泉种田养家，顺手把冷面猎户拐回了家。",
                CoverUrl = "https://images.unsplash.com/photo-1500382017468-9049fed747ef?q=80&w=300&h=400&fit=crop",
                Category = "种田",
                TotalEpisodes = 96,
                Status = "ongoing",
                Rating = 8.7
            },
            new()
            {
                PlatformDramaId = "hd_103",
                Title = "总裁的契约甜妻",
                Description = "为救家人她签下三年契约婚姻，却不知这位冷面总裁早已爱她入骨。",
                CoverUrl = "https://images.unsplash.com/photo-1519741497674-611481863552?q=80&w=300&h=400&fit=crop",
                Category = "霸总",
                TotalEpisodes = 72,
                Status = "completed",
                Rating = 8.4
            },
            new()
            {
                PlatformDramaId = "hd_104",
                Title = "镇国战神归来",
                Description = "五年戍边，一朝归来。昔日弃他如敝履的家族，如今跪求他出手相救。",
                CoverUrl = "https://images.unsplash.com/photo-1518709268805-4e9042af9f23?q=80&w=300&h=400&fit=crop",
                Category = "剧情",
                TotalEpisodes = 84,
                Status = "ongoing",
                Rating = 8.9
            }
        };

        public Task<PlatformSearchResult> SearchAsync(string keyword, int page = 1, int pageSize = 10)
        {
            var query = _mockDramas.AsQueryable();
            if (!string.IsNullOrWhiteSpace(keyword))
            {
                query = query.Where(d => d.Title.Contains(keyword) || d.Description.Contains(keyword));
            }

            var total = query.Count();
            var items = query.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            return Task.FromResult(new PlatformSearchResult { Items = items, Total = total });
        }

        public Task<PlatformDramaDetail?> GetDramaDetailAsync(string dramaId)
        {
            var drama = _mockDramas.FirstOrDefault(d => d.PlatformDramaId == dramaId);
            if (drama is null) return Task.FromResult<PlatformDramaDetail?>(null);

            var detail = new PlatformDramaDetail
            {
                PlatformDramaId = drama.PlatformDramaId,
                Title = drama.Title,
                Description = drama.Description,
                CoverUrl = drama.CoverUrl,
                Category = drama.Category,
                TotalEpisodes = drama.TotalEpisodes,
                Status = drama.Status,
                Rating = drama.Rating,
                Episodes = Enumerable.Range(1, drama.TotalEpisodes).Select(num => new PlatformEpisodeItem
                {
                    EpisodeNumber = num,
                    Title = $"第 {num} 集",
                    CoverUrl = drama.CoverUrl,
                    DurationSeconds = 110,
                    VideoUrl = $"https://test-streams.mux.dev/x36xhzz/x36xhzz.m3u8?platform=huangdou&id={dramaId}&ep={num}",
                    IsFree = num <= 8
                }).ToList()
            };

            return Task.FromResult<PlatformDramaDetail?>(detail);
        }

        public Task<string> GetPlayUrlAsync(string dramaId, int episodeNumber)
        {
            return Task.FromResult($"https://test-streams.mux.dev/x36xhzz/x36xhzz.m3u8?platform=huangdou&id={dramaId}&ep={episodeNumber}&clean=true");
        }

        public Task<List<PlatformRankItem>> GetRankAsync(string type, int limit = 20)
        {
            var items = _mockDramas.Select(d => new PlatformRankItem
            {
                PlatformDramaId = d.PlatformDramaId,
                Title = d.Title,
                CoverUrl = d.CoverUrl,
                Category = d.Category,
                PlayCount = 80000 + Math.Abs(d.PlatformDramaId.GetHashCode()) % 400000
            }).Take(limit).ToList();

            return Task.FromResult(items);
        }

        public Task<List<PlatformNewItem>> GetLatestAsync(string category, int limit = 20)
        {
            var query = _mockDramas.AsQueryable();
            if (!string.IsNullOrWhiteSpace(category) && category != "全部")
            {
                query = query.Where(d => d.Category == category);
            }

            var items = query.Select(d => new PlatformNewItem
            {
                PlatformDramaId = d.PlatformDramaId,
                Title = d.Title,
                CoverUrl = d.CoverUrl,
                Category = d.Category
            }).Take(limit).ToList();

            return Task.FromResult(items);
        }
    }
}

