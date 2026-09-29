using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ShortDrama.Application.Adapters;

namespace ShortDrama.Infrastructure.Adapters
{
    /// <summary>
    /// 通用适配器：用于「野果」「帝果」等数据格式不统一、以字段映射 + 标准化为主的平台。
    /// 真实接入时通过配置化字段映射表（FieldMapping）把平台原始 JSON 映射为标准化模型，
    /// 并可通过浏览器模拟 / 自动化绕过反爬。
    /// </summary>
    public class GenericAdapter : IPlatformAdapter
    {
        public string PlatformName { get; }
        public string PlatformCode { get; }

        private readonly List<PlatformSearchItem> _dramas;

        public GenericAdapter(string platformCode, string platformName, List<PlatformSearchItem>? seed = null)
        {
            PlatformCode = platformCode;
            PlatformName = platformName;
            _dramas = seed ?? BuildDefaultSeed(platformCode, platformName);
        }

        private static List<PlatformSearchItem> BuildDefaultSeed(string code, string name)
        {
            return new List<PlatformSearchItem>
            {
                new()
                {
                    PlatformDramaId = $"{code}_301",
                    Title = $"{name}·逆袭人生从摆摊开始",
                    Description = $"由{name}出品的都市逆袭短剧，讲述小人物一步步改变命运的故事。",
                    CoverUrl = "https://images.unsplash.com/photo-1441986300917-64674bd600d8?q=80&w=300&h=400&fit=crop",
                    Category = "剧情",
                    TotalEpisodes = 76,
                    Status = "completed",
                    Rating = 8.3
                },
                new()
                {
                    PlatformDramaId = $"{code}_302",
                    Title = $"{name}·王妃她又跑路了",
                    Description = $"由{name}出品的古装甜宠短剧，冷面王爷追妻火葬场。",
                    CoverUrl = "https://images.unsplash.com/photo-1533158307587-828f0a76ef46?q=80&w=300&h=400&fit=crop",
                    Category = "穿越",
                    TotalEpisodes = 90,
                    Status = "ongoing",
                    Rating = 8.8
                }
            };
        }

        public Task<PlatformSearchResult> SearchAsync(string keyword, int page = 1, int pageSize = 10)
        {
            var query = _dramas.AsQueryable();
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
            var drama = _dramas.FirstOrDefault(d => d.PlatformDramaId == dramaId);
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
                    DurationSeconds = 100,
                    VideoUrl = $"https://test-streams.mux.dev/x36xhzz/x36xhzz.m3u8?platform={PlatformCode}&id={dramaId}&ep={num}",
                    IsFree = num <= 4
                }).ToList()
            };

            return Task.FromResult<PlatformDramaDetail?>(detail);
        }

        public Task<string> GetPlayUrlAsync(string dramaId, int episodeNumber)
        {
            return Task.FromResult($"https://test-streams.mux.dev/x36xhzz/x36xhzz.m3u8?platform={PlatformCode}&id={dramaId}&ep={episodeNumber}&clean=true");
        }

        public Task<List<PlatformRankItem>> GetRankAsync(string type, int limit = 20)
        {
            var items = _dramas.Select(d => new PlatformRankItem
            {
                PlatformDramaId = d.PlatformDramaId,
                Title = d.Title,
                CoverUrl = d.CoverUrl,
                Category = d.Category,
                PlayCount = 60000 + Math.Abs(d.PlatformDramaId.GetHashCode()) % 300000
            }).Take(limit).ToList();

            return Task.FromResult(items);
        }

        public Task<List<PlatformNewItem>> GetLatestAsync(string category, int limit = 20)
        {
            var query = _dramas.AsQueryable();
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
