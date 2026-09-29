using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ShortDrama.Application.Adapters;

namespace ShortDrama.Infrastructure.Adapters
{
    /// <summary>
    /// 剧果短剧适配器。
    /// 真实接入时：接口签名 + 频率限制，需分布式采集与代理池。
    /// </summary>
    public class JuGuoAdapter : IPlatformAdapter
    {
        public string PlatformName => "剧果短剧";
        public string PlatformCode => "demo_juguo";

        private readonly List<PlatformSearchItem> _mockDramas = new()
        {
            new()
            {
                PlatformDramaId = "jg_201",
                Title = "闪婚老公竟是隐藏首富",
                Description = "相亲被放鸽子，她随手拉了个路人闪婚，没想到对方是身家千亿的隐形首富。",
                CoverUrl = "https://images.unsplash.com/photo-1522673607200-164d1b6ce486?q=80&w=300&h=400&fit=crop",
                Category = "霸总",
                TotalEpisodes = 68,
                Status = "completed",
                Rating = 8.6
            },
            new()
            {
                PlatformDramaId = "jg_202",
                Title = "全家读我心后杀疯了",
                Description = "小丫头心声被全家听见，从此炮灰一家开启逆天改命之路，反派集体破防。",
                CoverUrl = "https://images.unsplash.com/photo-1503454537195-1dcabb73ffb9?q=80&w=300&h=400&fit=crop",
                Category = "穿越",
                TotalEpisodes = 92,
                Status = "ongoing",
                Rating = 9.1
            },
            new()
            {
                PlatformDramaId = "jg_203",
                Title = "荒年种田：我带全村吃饱饭",
                Description = "灾荒之年，她凭一手好农艺和现代知识，带全村人开荒种田，度过饥荒迎来丰收。",
                CoverUrl = "https://images.unsplash.com/photo-1464226184884-fa280b87c399?q=80&w=300&h=400&fit=crop",
                Category = "种田",
                TotalEpisodes = 105,
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
                    DurationSeconds = 130,
                    VideoUrl = $"https://test-streams.mux.dev/x36xhzz/x36xhzz.m3u8?platform=juguo&id={dramaId}&ep={num}",
                    IsFree = num <= 6
                }).ToList()
            };

            return Task.FromResult<PlatformDramaDetail?>(detail);
        }

        public Task<string> GetPlayUrlAsync(string dramaId, int episodeNumber)
        {
            return Task.FromResult($"https://test-streams.mux.dev/x36xhzz/x36xhzz.m3u8?platform=juguo&id={dramaId}&ep={episodeNumber}&clean=true");
        }

        public Task<List<PlatformRankItem>> GetRankAsync(string type, int limit = 20)
        {
            var items = _mockDramas.Select(d => new PlatformRankItem
            {
                PlatformDramaId = d.PlatformDramaId,
                Title = d.Title,
                CoverUrl = d.CoverUrl,
                Category = d.Category,
                PlayCount = 120000 + Math.Abs(d.PlatformDramaId.GetHashCode()) % 500000
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

