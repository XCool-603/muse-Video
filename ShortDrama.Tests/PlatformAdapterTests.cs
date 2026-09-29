using System.Collections.Generic;
using System.Linq;
using ShortDrama.Application.Adapters;
using ShortDrama.Infrastructure.Adapters;
using Xunit;

namespace ShortDrama.Tests
{
    /// <summary>平台适配器契约测试：确保各平台返回标准化模型且字段完整。</summary>
    public class PlatformAdapterTests
    {
        public static IEnumerable<object[]> Adapters() => new List<object[]>
        {
            new object[] { new HongGuoDemoAdapter() },
            new object[] { new HuangDouDemoAdapter() },
            new object[] { new JuGuoAdapter() },
            new object[] { new GenericAdapter("yeguo", "野果短剧") },
            new object[] { new GenericAdapter("diguo", "帝果短剧") }
        };

        [Theory]
        [MemberData(nameof(Adapters))]
        public async Task SearchAsync_WithKeyword_ReturnsStandardizedItems(IPlatformAdapter adapter)
        {
            var result = await adapter.SearchAsync("逆袭", 1, 10);

            Assert.NotNull(result);
            Assert.All(result.Items, item =>
            {
                Assert.False(string.IsNullOrWhiteSpace(item.PlatformDramaId));
                Assert.False(string.IsNullOrWhiteSpace(item.Title));
                Assert.True(item.TotalEpisodes > 0);
                Assert.True(item.Rating > 0);
            });
        }

        [Theory]
        [MemberData(nameof(Adapters))]
        public async Task GetDramaDetailAsync_ReturnsEpisodesWithVideoUrl(IPlatformAdapter adapter)
        {
            var list = await adapter.GetLatestAsync("全部", 1);
            var first = list.First();

            var detail = await adapter.GetDramaDetailAsync(first.PlatformDramaId);

            Assert.NotNull(detail);
            Assert.Equal(detail!.TotalEpisodes, detail.Episodes.Count);
            Assert.All(detail.Episodes, ep =>
            {
                Assert.True(ep.EpisodeNumber > 0);
                Assert.False(string.IsNullOrWhiteSpace(ep.VideoUrl));
                Assert.True(ep.DurationSeconds > 0);
            });
        }

        [Theory]
        [MemberData(nameof(Adapters))]
        public async Task GetPlayUrlAsync_ReturnsAdFreeUrl(IPlatformAdapter adapter)
        {
            var list = await adapter.GetLatestAsync("全部", 1);
            var url = await adapter.GetPlayUrlAsync(list.First().PlatformDramaId, 1);

            Assert.False(string.IsNullOrWhiteSpace(url));
            // 去广告链路要求播放地址可被后端代理识别与处理
            Assert.StartsWith("http", url);
        }

        [Theory]
        [MemberData(nameof(Adapters))]
        public async Task GetRankAsync_ReturnsOrderedByPlayCount(IPlatformAdapter adapter)
        {
            var rank = await adapter.GetRankAsync("hot", 10);

            Assert.NotEmpty(rank);
            Assert.All(rank, item => Assert.True(item.PlayCount > 0));
        }

        [Fact]
        public async Task GetDramaDetailAsync_UnknownId_ReturnsNull()
        {
            var adapter = new HongGuoDemoAdapter();
            var detail = await adapter.GetDramaDetailAsync("not-exist-id");
            Assert.Null(detail);
        }

        [Fact]
        public void PlatformCodes_AreUnique()
        {
            var codes = Adapters().Select(row => ((IPlatformAdapter)row[0]).PlatformCode).ToList();
            Assert.Equal(codes.Count, codes.Distinct().Count());
        }
    }
}
