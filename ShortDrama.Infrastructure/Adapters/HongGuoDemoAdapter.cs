using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ShortDrama.Application.Adapters;

namespace ShortDrama.Infrastructure.Adapters
{
    public class HongGuoDemoAdapter : IPlatformAdapter
    {
        public string PlatformName => "红果短剧(演示)";
        public string PlatformCode => "demo_hongguo";

        private readonly List<PlatformSearchItem> _mockDramas;

        public HongGuoDemoAdapter()
        {
            // Set up some realistic sounding mock short dramas
            _mockDramas = new List<PlatformSearchItem>
            {
                new() {
                    PlatformDramaId = "hg_001",
                    Title = "巅峰狂少：我有神豪系统",
                    Description = "穷学生林凡意外获得神豪系统，从此开启逆袭人生。看他如何惩恶扬善，一步步登上世界巅峰！",
                    CoverUrl = "https://images.unsplash.com/photo-1594909122845-11baa439b7bf?q=80&w=300&h=400&fit=crop",
                    Category = "霸总",
                    TotalEpisodes = 80,
                    Status = "completed",
                    Rating = 9.2
                },
                new() {
                    PlatformDramaId = "hg_002",
                    Title = "穿越古代当首富：我有现代超市",
                    Description = "现代超市老板意外穿越成古代破产少爷，利用现代经营理念和无限超市空间，玩转大梁王朝，成为天下首富！",
                    CoverUrl = "https://images.unsplash.com/photo-1618005182384-a83a8bd57fbe?q=80&w=300&h=400&fit=crop",
                    Category = "穿越",
                    TotalEpisodes = 100,
                    Status = "ongoing",
                    Rating = 8.8
                },
                new() {
                    PlatformDramaId = "hg_003",
                    Title = "隐世医仙在都市",
                    Description = "神医传人隐入都市成为上门女婿，面对各方白眼，他医武双绝，银针渡人，最终让世界为之颤抖！",
                    CoverUrl = "https://images.unsplash.com/photo-1579783900882-c0d3dad7b119?q=80&w=300&h=400&fit=crop",
                    Category = "剧情",
                    TotalEpisodes = 75,
                    Status = "completed",
                    Rating = 9.5
                },
                new() {
                    PlatformDramaId = "hg_004",
                    Title = "开局退婚：我竟成了赘婿至尊",
                    Description = "被未婚妻无情退婚，却不知他正是掌握全球万亿财富的‘赘婿至尊’，退婚之日，战神齐拜，财阀俯首！",
                    CoverUrl = "https://images.unsplash.com/photo-1506157786151-b8491531f063?q=80&w=300&h=400&fit=crop",
                    Category = "重生",
                    TotalEpisodes = 90,
                    Status = "ongoing",
                    Rating = 8.5
                }
            };
        }

        public Task<PlatformSearchResult> SearchAsync(string keyword, int page = 1, int pageSize = 10)
        {
            var query = _mockDramas.AsQueryable();
            if (!string.IsNullOrWhiteSpace(keyword))
            {
                query = query.Where(d => d.Title.Contains(keyword) || d.Description.Contains(keyword));
            }

            var total = query.Count();
            var items = query.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            return Task.FromResult(new PlatformSearchResult
            {
                Items = items,
                Total = total
            });
        }

        public Task<PlatformDramaDetail?> GetDramaDetailAsync(string dramaId)
        {
            var drama = _mockDramas.FirstOrDefault(d => d.PlatformDramaId == dramaId);
            if (drama == null) return Task.FromResult<PlatformDramaDetail?>(null);

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
                    DurationSeconds = 120, // Typical short drama episode length
                    // We supply ad-free m3u8 playlist links pointing to big buck bunny or other standard test streams, 
                    // or simulate real-looking streaming links
                    VideoUrl = $"https://test-streams.mux.dev/x36xhzz/x36xhzz.m3u8?platform=hongguo&drama_id={dramaId}&ep={num}&ad=0", // We append ad=0 parameter simulating ad-free m3u8
                    IsFree = num <= 5 // First 5 episodes are free
                }).ToList()
            };

            return Task.FromResult<PlatformDramaDetail?>(detail);
        }

        public Task<string> GetPlayUrlAsync(string dramaId, int episodeNumber)
        {
            // Anti-Ad / 去广告 strategy: 
            // Here we strip out tracking parameters or simulate clean playback link fetch
            // Let's return a clean playback stream.
            string cleanStream = $"https://test-streams.mux.dev/x36xhzz/x36xhzz.m3u8?platform=hongguo&id={dramaId}&ep={episodeNumber}&clean=true";
            return Task.FromResult(cleanStream);
        }

        public Task<List<PlatformRankItem>> GetRankAsync(string type, int limit = 20)
        {
            var items = _mockDramas.Select(d => new PlatformRankItem
            {
                PlatformDramaId = d.PlatformDramaId,
                Title = d.Title,
                CoverUrl = d.CoverUrl,
                Category = d.Category,
                PlayCount = 100000 + new Random().Next(10000, 500000)
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


