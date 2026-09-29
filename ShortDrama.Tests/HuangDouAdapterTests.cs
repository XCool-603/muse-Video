using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using ShortDrama.Infrastructure.Adapters;
using Xunit;

namespace ShortDrama.Tests
{
    /// <summary>
    /// 黄豆短剧（hddj.tv）适配器测试。
    ///
    /// 注意：站点本身从开发环境不可达，因此这里只测试**解析逻辑**——
    /// 用按逆向文档结构构造的 HTML 验证 data-ep-src / data-ep-free 的解析、
    /// 免费/付费集标记、集号去重等行为。真实连通性需要在国内网络下验证。
    /// </summary>
    public class HuangDouAdapterTests
    {
        private const string DetailHtml = """
            <!DOCTYPE html>
            <html>
            <head>
              <title>宴律，你的白月光回国了 - 黄豆短剧</title>
              <meta property="og:title" content="宴律，你的白月光回国了" />
              <meta property="og:image" content="https://cdn.hddj.tv/cover/yanlv.jpg" />
              <meta property="og:description" content="申浩男,余茵主演的都市虐恋短剧。" />
            </head>
            <body>
              <div class="ep-list">
                <a class="ep" data-ep-src="/play/15297/1.m3u8" data-ep-free="1" data-ep-index="1">第1集</a>
                <a class="ep" data-ep-src="/play/15297/2.m3u8" data-ep-free="1" data-ep-index="2">第2集</a>
                <a class="ep" data-ep-src="/play/15297/3.m3u8" data-ep-free="0" data-ep-index="3">第3集</a>
                <a class="ep" data-ep-free="0" data-ep-src="/play/15297/4.m3u8" data-ep-index="4">第4集</a>
              </div>
            </body>
            </html>
            """;

        private static HuangDouAdapter CreateAdapter(string detailHtml, string? searchHtml = null)
        {
            var options = new HuangDouOptions
            {
                PlatformCode = "huangdou",
                PlatformName = "黄豆短剧",
                BaseUrl = "https://hddj.tv",
                DetailPathCandidates = new() { "/watch/details/{id}" },
                SearchPathCandidates = new() { "/api/search?keyword={kw}&page={page}&pageSize={pageSize}" },
                ListPathCandidates = new() { "/api/drama/list?page=1&pageSize=20" }
            };

            var handler = new RoutingHandler(detailHtml, searchHtml);
            return new HuangDouAdapter(options, new HttpClient(handler), NullLogger.Instance);
        }

        [Fact]
        public async Task GetDramaDetailAsync_ParsesDocumentedAttributes()
        {
            var adapter = CreateAdapter(DetailHtml);
            var detail = await adapter.GetDramaDetailAsync("15297");

            Assert.NotNull(detail);
            Assert.Equal("宴律，你的白月光回国了", detail!.Title);
            Assert.Equal("https://cdn.hddj.tv/cover/yanlv.jpg", detail.CoverUrl);
            Assert.Equal(4, detail.Episodes.Count);
            Assert.Equal(1, detail.Episodes[0].EpisodeNumber);
            Assert.Equal("https://hddj.tv/play/15297/1.m3u8", detail.Episodes[0].VideoUrl);
        }

        [Fact]
        public async Task GetDramaDetailAsync_MarksFreeAndPaidEpisodes()
        {
            var adapter = CreateAdapter(DetailHtml);
            var detail = await adapter.GetDramaDetailAsync("15297");

            Assert.NotNull(detail);
            Assert.True(detail!.Episodes[0].IsFree);   // data-ep-free="1"
            Assert.True(detail.Episodes[1].IsFree);
            Assert.False(detail.Episodes[2].IsFree);   // data-ep-free="0"
            Assert.False(detail.Episodes[3].IsFree);   // 属性顺序颠倒也要能识别
        }

        [Fact]
        public async Task GetPlayUrlAsync_FreeEpisode_ReturnsUrl()
        {
            var adapter = CreateAdapter(DetailHtml);
            var url = await adapter.GetPlayUrlAsync("15297", 1);

            Assert.Equal("https://hddj.tv/play/15297/1.m3u8", url);
        }

        [Fact]
        public async Task GetPlayUrlAsync_PaidEpisode_ThrowsInsteadOfBypassing()
        {
            // 付费集在服务端有 JWT 权益校验，适配器刻意不做绕过，必须如实抛出
            var adapter = CreateAdapter(DetailHtml);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => adapter.GetPlayUrlAsync("15297", 3));

            Assert.Contains("付费", ex.Message);
        }

        [Fact]
        public async Task GetDramaDetailAsync_DuplicateEpisodeIndex_IsMadeUnique()
        {
            const string html = """
                <html><head><title>测试</title></head><body>
                  <a data-ep-src="/play/1/1.m3u8" data-ep-free="1" data-ep-index="5">A</a>
                  <a data-ep-src="/play/1/2.m3u8" data-ep-free="1" data-ep-index="5">B</a>
                  <a data-ep-src="/play/1/3.m3u8" data-ep-free="1" data-ep-index="5">C</a>
                </body></html>
                """;

            var adapter = CreateAdapter(html);
            var detail = await adapter.GetDramaDetailAsync("1");

            Assert.NotNull(detail);
            var numbers = detail!.Episodes.Select(e => e.EpisodeNumber).ToList();
            Assert.Equal(numbers.Count, numbers.Distinct().Count());
        }

        [Fact]
        public async Task GetDramaDetailAsync_MissingTitle_FallsBackToPlaceholder()
        {
            const string html = """
                <html><body>
                  <a data-ep-src="/play/9/1.m3u8" data-ep-free="1" data-ep-index="1">第1集</a>
                </body></html>
                """;

            var adapter = CreateAdapter(html);
            var detail = await adapter.GetDramaDetailAsync("9");

            Assert.NotNull(detail);
            Assert.False(string.IsNullOrWhiteSpace(detail!.Title));
            Assert.Single(detail.Episodes);
        }

        [Fact]
        public async Task GetDramaDetailAsync_SiteUnreachable_ReturnsNullWithoutThrowing()
        {
            // 站点不可达时必须优雅返回 null，不能把聚合搜索整体带崩
            var options = new HuangDouOptions
            {
                BaseUrl = "https://hddj.tv",
                DetailPathCandidates = new() { "/watch/details/{id}" }
            };
            var adapter = new HuangDouAdapter(options, new HttpClient(new FailingHandler()), NullLogger.Instance);

            var detail = await adapter.GetDramaDetailAsync("1");

            Assert.Null(detail);
        }

        [Fact]
        public async Task SearchAsync_EmptyKeyword_ReturnsEmpty()
        {
            var adapter = CreateAdapter(DetailHtml);
            var result = await adapter.SearchAsync("");

            Assert.Empty(result.Items);
        }

        /// <summary>按 URL 路由到不同响应体的处理器</summary>
        private sealed class RoutingHandler : HttpMessageHandler
        {
            private readonly string _detailHtml;
            private readonly string? _searchHtml;

            public RoutingHandler(string detailHtml, string? searchHtml)
            {
                _detailHtml = detailHtml;
                _searchHtml = searchHtml;
            }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var path = request.RequestUri!.AbsolutePath;
                var body = path.Contains("search", StringComparison.OrdinalIgnoreCase)
                    ? _searchHtml
                    : _detailHtml;

                if (body is null)
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "text/html")
                });
            }
        }

        /// <summary>始终连接失败，用于验证容错</summary>
        private sealed class FailingHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
                => throw new HttpRequestException("connection refused");
        }
    }
}
