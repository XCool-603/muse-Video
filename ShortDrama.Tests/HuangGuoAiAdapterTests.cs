using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using ShortDrama.Infrastructure.Adapters;
using Xunit;

namespace ShortDrama.Tests
{
    /// <summary>
    /// 黄果短剧（huangguodrama.ai）适配器测试。
    ///
    /// 接口是站点公开的（robots.txt Allow + OpenAPI 规范），因此这里按真实响应结构构造桩数据。
    /// 重点覆盖：limit 上限（官方上限 20，超了返回 400）、分集翻页、MP4 直链提取、成人内容过滤。
    /// </summary>
    public class HuangGuoAiAdapterTests
    {
        private const string SearchJson = """
            {"request_id":"r1","items":[
              {"id":"110003","title":"My Sister's Boyfriend Arrested Me｜姐姐男友陷害我",
               "synopsis":"姐姐的男友设局让她锒铛入狱。","genres":[{"id":"dushi","label":"都市"},{"id":"xuanyi","label":"悬疑"}],
               "published_episode_count":8,"catalog_episode_count":30,"full_episode_count":3,
               "completeness":"partial","media_types":["opening_clip","episode"]},
              {"id":"1085","title":"干妈的诱惑","synopsis":"一段禁忌的关系。",
               "genres":[{"id":"dushi","label":"都市"}],
               "published_episode_count":12,"catalog_episode_count":12,"full_episode_count":0,
               "completeness":"complete","media_types":["opening_clip"]}
            ],"total":513,"page":1,"limit":20}
            """;

        private const string TitleJson = """
            {"request_id":"r2","id":"110003",
             "title":"My Sister's Boyfriend Arrested Me｜姐姐男友陷害我",
             "synopsis":"姐姐的男友设局让她锒铛入狱，出狱后她带着证据归来。",
             "genres":[{"id":"dushi","label":"都市"},{"id":"xuanyi","label":"悬疑"}],
             "published_episode_count":8,"catalog_episode_count":30,"full_episode_count":3,
             "completeness":"partial"}
            """;

        // 真实响应里 items 混着多种 media_type：片头 30 秒、正片 80-100 秒。
        // 只有 media_type=episode 的才算正片。
        private const string EpisodesPage1 = """
            {"request_id":"r3","id":"110003","total":4,"page":1,"limit":20,"items":[
              {"number":1,"media_type":"opening_clip","duration_seconds":30,"watch_url":"https://huangguodrama.ai/video/110003/"},
              {"number":2,"media_type":"opening_clip","duration_seconds":30,"watch_url":"https://huangguodrama.ai/video/110003/ep-2/"},
              {"number":3,"media_type":"episode","duration_seconds":85,"watch_url":"https://huangguodrama.ai/video/110003/ep-3/"}
            ]}
            """;

        private const string EpisodesPage2 = """
            {"request_id":"r4","id":"110003","total":4,"page":2,"limit":20,"items":[
              {"number":4,"media_type":"episode","duration_seconds":92,"watch_url":"https://huangguodrama.ai/video/110003/ep-4/"}
            ]}
            """;

        private const string WatchPageHtml = """
            <!DOCTYPE html><html><body>
              <video data-drama-id="110003" data-episode="1" data-media-kind="episode" class="hg-web-play__native" poster="/x.jpg">
                <source src="https://media.huangguodrama.ai/motion/drama-episodes/72060645be88c12dee1fa9518b189f07628e2247ac153e8fb028497245047cfa.mp4" type="video/mp4"/>
              </video>
            </body></html>
            """;

        /// <summary>按 URL 特征路由的桩处理器</summary>
        private sealed class RoutingHandler : HttpMessageHandler
        {
            public List<string> Requests { get; } = new();
            private readonly bool _rejectLargeLimit;

            public RoutingHandler(bool rejectLargeLimit = true) => _rejectLargeLimit = rejectLargeLimit;

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var url = request.RequestUri!.ToString();
                Requests.Add(url);

                // 复刻官方行为：limit > 20 返回 400 INVALID_INPUT
                if (_rejectLargeLimit)
                {
                    var m = Regex.Match(url, @"[?&]limit=(\d+)");
                    if (m.Success && int.Parse(m.Groups[1].Value) > 20)
                    {
                        return Json(HttpStatusCode.BadRequest,
                            """{"error":"INVALID_INPUT","issues":[{"path":["limit"],"message":"Too big: expected number to be <=20"}]}""");
                    }
                }

                if (url.Contains("/episodes/"))
                {
                    return Json(HttpStatusCode.OK, url.Contains("page=2") ? EpisodesPage2 : EpisodesPage1);
                }
                if (url.Contains("/api/agent/v1/search/"))
                {
                    return Json(HttpStatusCode.OK, SearchJson);
                }
                if (url.Contains("/api/agent/v1/titles/"))
                {
                    return Json(HttpStatusCode.OK, TitleJson);
                }
                if (url.Contains("/video/"))
                {
                    return Html(WatchPageHtml);
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            private static Task<HttpResponseMessage> Json(HttpStatusCode code, string body) =>
                Task.FromResult(new HttpResponseMessage(code)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                });

            private static Task<HttpResponseMessage> Html(string body) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "text/html")
                });
        }

        private static (HuangGuoAiAdapter Adapter, RoutingHandler Handler) CreateAdapter(
            bool excludeAdult = false, bool rejectLargeLimit = true)
        {
            var options = new HuangGuoAiOptions
            {
                PlatformCode = "huangguo",
                PlatformName = "黄果短剧",
                BaseUrl = "https://huangguodrama.ai",
                RequestsPerMinute = 0,     // 测试里关掉限流，避免等待
                ExcludeAdult = excludeAdult
            };

            var handler = new RoutingHandler(rejectLargeLimit);
            return (new HuangGuoAiAdapter(options, new HttpClient(handler), NullLogger.Instance), handler);
        }

        // ==================== 搜索 ====================

        [Fact]
        public async Task SearchAsync_ParsesItemsAndGenres()
        {
            var (adapter, _) = CreateAdapter();
            var result = await adapter.SearchAsync("姐姐");

            Assert.Equal(2, result.Items.Count);
            Assert.Equal("110003", result.Items[0].PlatformDramaId);
            Assert.Contains("姐姐男友陷害我", result.Items[0].Title);
            Assert.Equal("都市", result.Items[0].Category);
            // full_episode_count（真实正片集数）优先于 catalog_episode_count（声明集数 30）
            Assert.Equal(3, result.Items[0].TotalEpisodes);
            Assert.Equal(513, result.Total);
        }

        [Fact]
        public async Task SearchAsync_ClipOnlyTitle_ReportsZeroEpisodes()
        {
            // 只有片头/预告的剧，正片集数是 0；不能用声明集数 12 冒充
            var (adapter, _) = CreateAdapter();
            var result = await adapter.SearchAsync("干妈");

            var item = result.Items.First(i => i.PlatformDramaId == "1085");
            Assert.Equal(0, item.TotalEpisodes);
        }

        [Fact]
        public async Task SearchAsync_EmptyKeyword_ReturnsEmptyWithoutRequest()
        {
            var (adapter, handler) = CreateAdapter();
            var result = await adapter.SearchAsync("");

            Assert.Empty(result.Items);
            Assert.Empty(handler.Requests);
        }

        [Fact]
        public async Task SearchAsync_NeverExceedsOfficialLimitOf20()
        {
            // 回归测试：官方 limit 上限是 20，超了直接 400。
            // 早期实现 clamp 到 50，导致所有请求被拒。
            var (adapter, handler) = CreateAdapter();
            await adapter.SearchAsync("测试", page: 1, pageSize: 100);

            var searchRequest = handler.Requests.First(r => r.Contains("/search/"));
            var limit = int.Parse(Regex.Match(searchRequest, @"limit=(\d+)").Groups[1].Value);

            Assert.True(limit <= 20, $"limit 必须 ≤ 20，实际 {limit}");
        }

        // ==================== 详情与分集翻页 ====================

        [Fact]
        public async Task GetDramaDetailAsync_ParsesTitleAndMetadata()
        {
            var (adapter, _) = CreateAdapter();
            var detail = await adapter.GetDramaDetailAsync("110003");

            Assert.NotNull(detail);
            Assert.Contains("姐姐男友陷害我", detail!.Title);
            Assert.Contains("出狱后她带着证据归来", detail.Description);
            Assert.Equal("都市", detail.Category);
            Assert.Equal("ongoing", detail.Status);      // completeness=partial
        }

        [Fact]
        public async Task GetDramaDetailAsync_PaginatesEpisodes()
        {
            // 分集接口 limit 上限 20，超过 20 集必须翻页
            var (adapter, handler) = CreateAdapter();
            var detail = await adapter.GetDramaDetailAsync("110003");

            Assert.NotNull(detail);
            // 第 1、2 条是片头（opening_clip），只保留正片 3、4
            Assert.Equal(new[] { 3, 4 }, detail!.Episodes.Select(e => e.EpisodeNumber).ToArray());

            var episodeRequests = handler.Requests.Where(r => r.Contains("/episodes/")).ToList();
            Assert.True(episodeRequests.Count >= 2, "应当翻页请求第二页");
            Assert.All(episodeRequests, r =>
            {
                var limit = int.Parse(Regex.Match(r, @"limit=(\d+)").Groups[1].Value);
                Assert.True(limit <= 20);
            });
        }

        [Fact]
        public async Task GetDramaDetailAsync_SkipsOpeningClipsAndTrailers()
        {
            // 回归：站点分集接口把 30 秒片头/预告混在 items 里返回。
            // 早先不加区分，一部只有 106 条片头的剧会被展示成「106 集正片」。
            var (adapter, _) = CreateAdapter();
            var detail = await adapter.GetDramaDetailAsync("110003");

            Assert.NotNull(detail);
            Assert.DoesNotContain(detail!.Episodes, e => e.EpisodeNumber is 1 or 2);
            Assert.All(detail.Episodes, e => Assert.True(e.DurationSeconds > 60, "只应保留正片（时长明显长于 30 秒片头）"));
            Assert.Equal(2, detail.TotalEpisodes);   // 与 Episodes.Count 一致
        }

        [Fact]
        public async Task GetDramaDetailAsync_KeepsEpisodeNumberUnchanged()
        {
            // 集号必须沿用接口原值：GetPlayUrlAsync 用它拼 /video/{id}/ep-{n}/
            var (adapter, _) = CreateAdapter();
            var detail = await adapter.GetDramaDetailAsync("110003");

            Assert.NotNull(detail);
            Assert.Equal(3, detail!.Episodes[0].EpisodeNumber);
            Assert.Contains("/ep-3/", detail.Episodes[0].VideoUrl);
        }

        [Fact]
        public async Task GetDramaDetailAsync_MissingMediaType_TreatedAsRealEpisode()
        {
            // 站点若去掉 media_type 字段，不能因此把内容全过滤掉
            var options = new HuangGuoAiOptions { BaseUrl = "https://huangguodrama.ai", RequestsPerMinute = 0 };
            var http = new HttpClient(new NoMediaTypeHandler());
            var adapter = new HuangGuoAiAdapter(options, http, NullLogger.Instance);

            var detail = await adapter.GetDramaDetailAsync("110003");

            Assert.NotNull(detail);
            Assert.Equal(new[] { 1, 2 }, detail!.Episodes.Select(e => e.EpisodeNumber).ToArray());
        }

        [Fact]
        public async Task GetDramaDetailAsync_AllRequestsStayWithinLimit()
        {
            var (adapter, handler) = CreateAdapter();
            await adapter.GetDramaDetailAsync("110003");

            Assert.NotEmpty(handler.Requests);
            Assert.All(handler.Requests, r =>
            {
                var m = Regex.Match(r, @"[?&]limit=(\d+)");
                if (m.Success)
                {
                    Assert.True(int.Parse(m.Groups[1].Value) <= 20, $"越界请求: {r}");
                }
            });
        }

        // ==================== 播放地址 ====================

        [Fact]
        public async Task GetPlayUrlAsync_ExtractsPlainMp4FromWatchPage()
        {
            var (adapter, _) = CreateAdapter();
            var url = await adapter.GetPlayUrlAsync("110003", 1);

            Assert.StartsWith("https://media.huangguodrama.ai/motion/drama-episodes/", url);
            Assert.EndsWith(".mp4", url);
        }

        [Fact]
        public async Task GetPlayUrlAsync_NonFirstEpisode_UsesEpPath()
        {
            var (adapter, handler) = CreateAdapter();
            await adapter.GetPlayUrlAsync("110003", 5);

            Assert.Contains(handler.Requests, r => r.Contains("/video/110003/ep-5/"));
        }

        [Fact]
        public async Task GetPlayUrlAsync_NoSourceOnPage_Throws()
        {
            var options = new HuangGuoAiOptions { BaseUrl = "https://huangguodrama.ai", RequestsPerMinute = 0 };
            var http = new HttpClient(new EmptyHtmlHandler());
            var adapter = new HuangGuoAiAdapter(options, http, NullLogger.Instance);

            await Assert.ThrowsAsync<InvalidOperationException>(() => adapter.GetPlayUrlAsync("1", 1));
        }

        // ==================== 成人内容过滤 ====================

        [Fact]
        public async Task SearchAsync_WithoutFilter_KeepsAllItems()
        {
            var (adapter, _) = CreateAdapter(excludeAdult: false);
            var result = await adapter.SearchAsync("x");

            Assert.Equal(2, result.Items.Count);
        }

        [Fact]
        public async Task SearchAsync_WithFilter_DropsAdultTitles()
        {
            var (adapter, _) = CreateAdapter(excludeAdult: true);
            var result = await adapter.SearchAsync("x");

            Assert.Single(result.Items);
            Assert.DoesNotContain(result.Items, i => i.Title.Contains("干妈"));
        }

        // ==================== 全量目录分页（IPagedCatalogAdapter）====================

        [Fact]
        public async Task GetCatalogTotalAsync_ReturnsTotal()
        {
            var (adapter, _) = CreateAdapter();
            Assert.Equal(513, await adapter.GetCatalogTotalAsync());
        }

        [Fact]
        public async Task GetCatalogPageAsync_ReturnsItemsWithEpisodeCount()
        {
            var (adapter, _) = CreateAdapter();
            var page = await adapter.GetCatalogPageAsync(1, 20);

            Assert.Equal(2, page.Count);
            Assert.Equal("110003", page[0].PlatformDramaId);
            // 真实正片集数（full_episode_count），不是声明集数 30
            Assert.Equal(3, page[0].TotalEpisodes);
            Assert.Equal("都市", page[0].Category);
        }

        [Fact]
        public async Task GetCatalogPageAsync_RespectsOfficialLimit()
        {
            // 回归：官方 limit 上限 20，超了返回 400，会让整页目录拉空
            var (adapter, handler) = CreateAdapter();
            await adapter.GetCatalogPageAsync(1, 500);

            var request = handler.Requests.Last(r => r.Contains("/search/"));
            var limit = int.Parse(Regex.Match(request, @"limit=(\d+)").Groups[1].Value);

            Assert.True(limit <= 20, $"limit 必须 ≤ 20，实际 {limit}");
        }

        [Fact]
        public async Task GetCatalogPageAsync_ForwardsPageNumber()
        {
            var (adapter, handler) = CreateAdapter();
            await adapter.GetCatalogPageAsync(7, 20);

            Assert.Contains(handler.Requests, r => r.Contains("/search/") && r.Contains("page=7"));
        }

        // ==================== 容错 ====================

        [Fact]
        public async Task SearchAsync_ServerError_ReturnsEmptyWithoutThrowing()
        {
            var options = new HuangGuoAiOptions { BaseUrl = "https://huangguodrama.ai", RequestsPerMinute = 0 };
            var http = new HttpClient(new FailingHandler());
            var adapter = new HuangGuoAiAdapter(options, http, NullLogger.Instance);

            Assert.Empty((await adapter.SearchAsync("x")).Items);
            Assert.Null(await adapter.GetDramaDetailAsync("1"));
        }

        [Fact]
        public async Task GetDramaDetailAsync_MalformedJson_ReturnsNull()
        {
            var options = new HuangGuoAiOptions { BaseUrl = "https://huangguodrama.ai", RequestsPerMinute = 0 };
            var http = new HttpClient(new EmptyHtmlHandler());
            var adapter = new HuangGuoAiAdapter(options, http, NullLogger.Instance);

            Assert.Null(await adapter.GetDramaDetailAsync("1"));
        }

        private sealed class EmptyHtmlHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
                => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("<html><body>no video here</body></html>", Encoding.UTF8, "text/html")
                });
        }

        /// <summary>分集响应里没有 media_type 字段（模拟站点改字段，此时应按正片处理）</summary>
        private sealed class NoMediaTypeHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var url = request.RequestUri!.ToString();
                var body = url.Contains("/episodes/")
                    ? """
                      {"request_id":"r","id":"110003","total":2,"page":1,"limit":20,"items":[
                        {"number":1,"duration_seconds":85,"watch_url":"https://huangguodrama.ai/video/110003/"},
                        {"number":2,"duration_seconds":90,"watch_url":"https://huangguodrama.ai/video/110003/ep-2/"}]}
                      """
                    : TitleJson;

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                });
            }
        }

        private sealed class FailingHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
                => throw new HttpRequestException("connection refused");
        }
    }
}
