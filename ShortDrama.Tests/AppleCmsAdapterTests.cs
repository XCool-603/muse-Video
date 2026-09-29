using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using ShortDrama.Infrastructure.Adapters;
using Xunit;

namespace ShortDrama.Tests
{
    /// <summary>苹果CMS 适配器测试：字段映射、剧集解析、以及集号去重的边界情况。</summary>
    public class AppleCmsAdapterTests
    {
        /// <summary>构造一个返回固定 JSON 的适配器</summary>
        private static AppleCmsAdapter CreateAdapter(string json, string format = "json")
        {
            var source = new AppleCmsSource
            {
                PlatformCode = "test_acms",
                PlatformName = "测试采集源",
                Api = "https://example.com/api.php/provide/vod/",
                Format = format
            };

            var http = new HttpClient(new StubHandler(json));
            return new AppleCmsAdapter(source, http, NullLogger.Instance);
        }

        private static string BuildResponse(params (string Id, string Name, string PlayUrl)[] items)
        {
            var list = items.Select(i => new Dictionary<string, object>
            {
                ["vod_id"] = i.Id,
                ["vod_name"] = i.Name,
                ["vod_pic"] = "https://example.com/cover.jpg",
                ["vod_blurb"] = "简介",
                ["vod_remarks"] = "已完结",
                ["vod_score"] = "8.5",
                ["vod_hits"] = "12345",
                ["type_name"] = "短剧大全",
                ["vod_play_url"] = i.PlayUrl
            }).ToList();

            return JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["code"] = 1,
                ["msg"] = "数据列表",
                ["page"] = 1,
                ["pagecount"] = 1,
                ["total"] = list.Count,
                ["list"] = list
            });
        }

        [Fact]
        public async Task GetDramaDetailAsync_MapsAppleCmsFields()
        {
            var json = BuildResponse(("107394", "宴律，你的白月光回国了",
                "第1集$https://cdn.example.com/a/第1集/index.m3u8#第2集$https://cdn.example.com/a/第2集/index.m3u8"));

            var adapter = CreateAdapter(json);
            var detail = await adapter.GetDramaDetailAsync("107394");

            Assert.NotNull(detail);
            Assert.Equal("宴律，你的白月光回国了", detail!.Title);
            Assert.Equal("https://example.com/cover.jpg", detail.CoverUrl);
            Assert.Equal("completed", detail.Status);
            Assert.Equal(8.5, detail.Rating);
            Assert.Equal("短剧", detail.Category);          // type_name 含「短」→ 归一为短剧
            Assert.Equal(2, detail.Episodes.Count);
            Assert.Equal(1, detail.Episodes[0].EpisodeNumber);
            Assert.Equal("https://cdn.example.com/a/第1集/index.m3u8", detail.Episodes[0].VideoUrl);
        }

        [Fact]
        public async Task ParseEpisodes_MultiSourcePrefersM3u8()
        {
            // 多播放源用 $$$ 分隔，第一个源是 mp4、第二个是 m3u8，应优先选 m3u8
            var playUrl = "第1集$https://cdn.example.com/a/1.mp4" +
                          "$$$" +
                          "第1集$https://cdn.example.com/a/1.m3u8#第2集$https://cdn.example.com/a/2.m3u8";

            var adapter = CreateAdapter(BuildResponse(("1", "测试剧", playUrl)));
            var detail = await adapter.GetDramaDetailAsync("1");

            Assert.NotNull(detail);
            Assert.Equal(2, detail!.Episodes.Count);
            Assert.All(detail.Episodes, e => Assert.Contains(".m3u8", e.VideoUrl));
        }

        [Fact]
        public async Task ParseEpisodes_DuplicateNumbers_AreMadeUnique()
        {
            // 源站重复集号：必须去重，否则写库时触发 (DramaId, EpisodeNumber) 唯一约束
            var playUrl = "第1集$https://c/1.m3u8#第1集$https://c/2.m3u8#第1集$https://c/3.m3u8";

            var adapter = CreateAdapter(BuildResponse(("1", "测试剧", playUrl)));
            var detail = await adapter.GetDramaDetailAsync("1");

            Assert.NotNull(detail);
            var numbers = detail!.Episodes.Select(e => e.EpisodeNumber).ToList();
            Assert.Equal(numbers.Count, numbers.Distinct().Count());
        }

        [Fact]
        public async Task ParseEpisodes_SparseNumbers_DoNotHang()
        {
            // 回归测试：集号集合稀疏时（只含 2、3 而没有 1），
            // 早期实现用「跳到 Count+1」去重会原地打转形成死循环。
            // 这里用超时兜底：如果逻辑再次退化，测试会失败而不是永久挂起。
            var playUrl = "第2集$https://c/2.m3u8#第3集$https://c/3.m3u8#第2集$https://c/2b.m3u8";

            var adapter = CreateAdapter(BuildResponse(("1", "测试剧", playUrl)));

            var task = adapter.GetDramaDetailAsync("1");
            var finished = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(5)));

            Assert.True(finished == task, "集号去重逻辑疑似死循环（5 秒未返回）");

            var detail = await task;
            Assert.NotNull(detail);
            var numbers = detail!.Episodes.Select(e => e.EpisodeNumber).ToList();
            Assert.Equal(numbers.Count, numbers.Distinct().Count());
        }

        [Fact]
        public async Task ParseEpisodes_TitlesWithoutNumbers_FallBackToSequence()
        {
            var playUrl = "上集$https://c/1.m3u8#中集$https://c/2.m3u8#下集$https://c/3.m3u8";

            var adapter = CreateAdapter(BuildResponse(("1", "测试剧", playUrl)));
            var detail = await adapter.GetDramaDetailAsync("1");

            Assert.NotNull(detail);
            Assert.Equal(new[] { 1, 2, 3 }, detail!.Episodes.Select(e => e.EpisodeNumber).ToArray());
        }

        [Fact]
        public async Task SearchAsync_MapsResults()
        {
            var json = BuildResponse(
                ("1", "霸总剧一", "第1集$https://c/1.m3u8"),
                ("2", "穿越剧二", "第1集$https://c/1.m3u8#第2集$https://c/2.m3u8"));

            var adapter = CreateAdapter(json);
            var result = await adapter.SearchAsync("短剧");

            Assert.Equal(2, result.Items.Count);
            Assert.Equal(2, result.Items[1].TotalEpisodes);
            Assert.Equal("completed", result.Items[0].Status);
        }

        [Fact]
        public async Task GetDramaDetailAsync_MalformedJson_ReturnsNullWithoutThrowing()
        {
            var adapter = CreateAdapter("<html>502 Bad Gateway</html>");
            var detail = await adapter.GetDramaDetailAsync("1");

            Assert.Null(detail);
        }

        [Fact]
        public async Task GetPlayUrlAsync_UnknownEpisode_Throws()
        {
            var adapter = CreateAdapter(BuildResponse(("1", "测试剧", "第1集$https://c/1.m3u8")));

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => adapter.GetPlayUrlAsync("1", 99));
        }

        /// <summary>返回固定响应的 HttpMessageHandler</summary>
        private sealed class StubHandler : HttpMessageHandler
        {
            private readonly string _body;

            public StubHandler(string body) => _body = body;

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_body, Encoding.UTF8, "application/json")
                });
            }
        }
    }
}
