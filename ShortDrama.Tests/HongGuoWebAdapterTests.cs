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
    /// 红果短剧官网（hongguoduanju.com）适配器测试。
    ///
    /// HTML 片段取自真实页面结构（2026-09 抓取）。注意站点用 CSS Modules，
    /// `pc-xxx-{hash}` 与 `m-xxx-{hash}` 的后缀 hash 每次构建都会变，
    /// 因此断言重点是「不依赖 hash 也能解析」，避免站点改版后测试仍假通过。
    /// </summary>
    public class HongGuoWebAdapterTests
    {
        /// <summary>分类页卡片：class 后缀故意用与真实页面不同的 hash</summary>
        private const string CategoryHtml = """
            <html><body>
            <div class="pc-card-container-LFCrL3 m-card-container-GwMtLP">
              <a class="pc-card-DQXf3W m-card-V7QopP" href="/detail?series_id=7687919221593885758">
                <div class="pc-img-container-lewSpn m-img-container-w8VcQ5">
                  <div class="pc-img-zYP4Wp m-img-snzjMw" style="opacity:1">
                    <picture class="picture-FtO8FS ">
                      <img class="image-PWlIcn" src="https://p6-novel.byteimg.com/novel-pic/abc~tplv-shrink:640:0.image" alt="二嫁有喜" />
                    </picture>
                  </div>
                  <div class="pc-bottom-cover-xkICHs m-bottom-cover-_4Vibe">
                    <p class="pc-episode-TGdnks m-episode-Y4cUAR">全77集</p>
                  </div>
                </div>
                <p class="pc-title-l_s3n8 m-title-F3bkRB">二嫁有喜</p>
                <div class="pc-tags-XTafh6 m-tags-hiz3Zq">
                  <div class="pc-tag-wtQfY1 m-tag-gcFeXh"><span class="pc-tag-text-jRGaMo m-tag-text-BOHGFG">爱情</span></div>
                  <div class="pc-tag-wtQfY1 m-tag-gcFeXh"><span class="pc-tag-text-jRGaMo m-tag-text-BOHGFG">古风爱情</span></div>
                </div>
              </a>
              <a class="pc-card-ZZZZZZ m-card-AAAAAA" href="/detail?series_id=7683196130645003288">
                <div class="pc-img-container-MHaxoa m-img-container-U8Q9z7">
                  <img class="image-PWlIcn" src="https://p3-novel.byteimg.com/novel-pic/def~tplv-shrink:640:0.image" alt="东北话事人" />
                </div>
                <p class="pc-episode-XXXXXX m-episode-YYYYYY">全71集</p>
                <p class="pc-title-GGGGGG m-title-HHHHHH">东北话事人</p>
                <div class="m-tags-IIIIII"><span class="m-tag-text-JJJJJJ">成长</span></div>
              </a>
            </div>
            </body></html>
            """;

        /// <summary>
        /// 详情页：含 React SSR 的 &lt;!-- --&gt; 注释分隔符、h1 标题、评分/点赞/收藏、
        /// 以及选集格子（前 3 集有 href，其余是无链接的 div）。
        /// </summary>
        private const string DetailHtml = """
            <html><head>
              <title>二嫁有喜_高清完整版全集免费观看_红果短剧</title>
            </head><body>
            <h1 class="pc-hero-title-OTsGrt">二嫁有喜</h1>
            <span class="pc-hero-heat-banAWb">6764万热度</span>
            <div class="pc-hero-tags-ib8DCw">
              <div class="pc-hero-rating-qo1olY"><span class="pc-hero-rating-text-uqSNEF">评分<!-- -->9.6</span></div>
              <div class="pc-hero-social-tag-EsvU5u">196.8万<!-- -->次点赞</div>
              <div class="pc-hero-social-tag-EsvU5u">87.9万<!-- -->收藏</div>
            </div>
            <div class="pc-hero-tags-ib8DCw">
              <div class="pc-tag-wtQfY1 m-tag-gcFeXh"><span class="pc-tag-text-jRGaMo m-tag-text-BOHGFG">爱情</span></div>
              <div class="pc-tag-wtQfY1 m-tag-gcFeXh"><span class="pc-tag-text-jRGaMo m-tag-text-BOHGFG">古风爱情</span></div>
              <div class="pc-tag-wtQfY1 m-tag-gcFeXh"><span class="pc-tag-text-jRGaMo m-tag-text-BOHGFG">女性成长</span></div>
            </div>
            <img class="image-PWlIcn" src="https://p6-novel.byteimg.com/novel-pic/05438bf540034a849cd2dbd~tplv-shrink:640:0.image" alt="二嫁有喜" />
            <div class="pc-hero-desc-tooltip-l4Pqi2">简介：<!-- -->江芝出身书香世家，娘家是有名的旺子家族，唯独江芝嫁给永宁侯谢景渊七年，始终无所出。三天后江芝二嫁鳏夫周晋，婚后却发现周晋总是在不经意间展现对她的敬重和爱护。</div>
            <a href="/player/7687919221593885758" class="pc-hero-btn-play-pQT8cU"><span>播放正片</span></a>
            <div class="pc-episode-container-zCK1Ng">
              <div class="pc-episode-grid-wLEnar ">
                <a href="/player/7687919221593885758" class="pc-episode-cell-TUQIDr pc-episode-item-7KBv6v"><div class="pc-episode-cell-text-JYe7Xg">1</div></a>
                <a href="/player/7687919221593885758/7687921081989991486" rel="nofollow" class="pc-episode-cell-TUQIDr pc-episode-item-7KBv6v"><div class="pc-episode-cell-text-JYe7Xg">2</div></a>
                <a href="/player/7687919221593885758/7687921189049601086" rel="nofollow" class="pc-episode-cell-TUQIDr pc-episode-item-7KBv6v"><div class="pc-episode-cell-text-JYe7Xg">3</div></a>
                <div class="pc-episode-cell-TUQIDr pc-episode-item-7KBv6v"><div class="pc-episode-cell-text-JYe7Xg">4</div></div>
                <div class="pc-episode-cell-TUQIDr pc-episode-item-7KBv6v"><div class="pc-episode-cell-text-JYe7Xg">5</div></div>
              </div>
            </div>
            </body></html>
            """;

        private static HongGuoWebAdapter CreateAdapter(string html)
        {
            var options = new HongGuoWebOptions
            {
                PlatformCode = "hongguo",
                PlatformName = "红果短剧",
                BaseUrl = "https://hongguoduanju.com"
            };
            return new HongGuoWebAdapter(options, new HttpClient(new StubHandler(html)), NullLogger.Instance);
        }

        /// <summary>
        /// 播放页：SSR JSON 里的明文视频地址。注意 / 被转义成 \u002F（与真实页面一致），
        /// 这是必须处理的坑 —— 不还原的话地址直接不可用。
        /// </summary>
        private const string PlayerHtml = """
            <html><body>
            <script>window.__INITIAL_STATE__={"series_id":"7687919221593885758","vid":"7687919221593885758","video_player_info":{"duration":98.1,"height":"1280","width":"720","main_url":"https:\u002F\u002Fv26-hgweb.qznovelvod.com\u002Fabc123\u002Fvideo\u002Ftos\u002Fcn\u002Fxxx\u002Findex.mp4?a=8662&br=1261"}}</script>
            </body></html>
            """;

        /// <summary>按 URL 片段路由的桩：详情页与播放页返回不同内容</summary>
        private static HongGuoWebAdapter CreateRoutedAdapter(params (string Fragment, string Body)[] routes)
        {
            var options = new HongGuoWebOptions
            {
                PlatformCode = "hongguo",
                PlatformName = "红果短剧",
                BaseUrl = "https://hongguoduanju.com"
            };
            return new HongGuoWebAdapter(options, new HttpClient(new RoutingStubHandler(routes)), NullLogger.Instance);
        }

        private sealed class RoutingStubHandler : HttpMessageHandler
        {
            private readonly (string Fragment, string Body)[] _routes;

            public RoutingStubHandler((string Fragment, string Body)[] routes) => _routes = routes;

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var url = request.RequestUri?.ToString() ?? string.Empty;

                foreach (var (fragment, body) in _routes)
                {
                    if (url.Contains(fragment, StringComparison.OrdinalIgnoreCase))
                    {
                        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent(body, Encoding.UTF8, "text/html")
                        });
                    }
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }
        }

        // ==================== 目录解析 ====================

        [Fact]
        public async Task GetLatestAsync_ParsesCards_WithoutRelyingOnClassHash()
        {
            var adapter = CreateAdapter(CategoryHtml);
            var list = await adapter.GetLatestAsync("全部", 10);

            Assert.Equal(2, list.Count);
            Assert.Equal("二嫁有喜", list[0].Title);
            Assert.Equal("7687919221593885758", list[0].PlatformDramaId);
            Assert.Contains("byteimg.com", list[0].CoverUrl);
            Assert.Equal("爱情", list[0].Category);
            Assert.Equal("成长", list[1].Category);
        }

        [Fact]
        public async Task GetRankAsync_ReturnsCards()
        {
            var adapter = CreateAdapter(CategoryHtml);
            var rank = await adapter.GetRankAsync("hot", 10);

            Assert.Equal(2, rank.Count);
            Assert.Equal("二嫁有喜", rank[0].Title);
        }

        [Fact]
        public async Task GetRankAsync_AssignsStableOrdering()
        {
            var adapter = CreateAdapter(CategoryHtml);
            var rank = await adapter.GetRankAsync("hot", 10);

            // 榜单没有热度数值时，用名次倒推，保证排序稳定（不能全是 0）
            Assert.All(rank, r => Assert.True(r.PlayCount > 0));
            Assert.True(rank[0].PlayCount >= rank[1].PlayCount);
        }

        // ==================== 详情解析 ====================

        [Fact]
        public async Task GetDramaDetailAsync_ParsesTitleRatingCover()
        {
            var adapter = CreateAdapter(DetailHtml);
            var detail = await adapter.GetDramaDetailAsync("7687919221593885758");

            Assert.NotNull(detail);
            Assert.Equal("二嫁有喜", detail!.Title);
            Assert.Equal(9.6, detail.Rating);                     // 跨过 React 的 <!-- --> 注释
            Assert.Equal("爱情", detail.Category);
            Assert.Contains("byteimg.com", detail.CoverUrl);
            Assert.Contains("江芝出身书香世家", detail.Description);
        }

        [Fact]
        public async Task GetDramaDetailAsync_ParsesEpisodeGrid()
        {
            var adapter = CreateAdapter(DetailHtml);
            var detail = await adapter.GetDramaDetailAsync("7687919221593885758");

            Assert.NotNull(detail);
            Assert.Equal(5, detail!.Episodes.Count);
            Assert.Equal(new[] { 1, 2, 3, 4, 5 }, detail.Episodes.Select(e => e.EpisodeNumber).ToArray());
        }

        [Fact]
        public async Task GetDramaDetailAsync_OnlyFirstEpisodesHavePlayerUrl()
        {
            // 红果 SSR 只渲染前几集的 href，其余格子是无链接的 div。
            // 这是站点的真实行为，适配器如实反映：拿不到的就不编造。
            var adapter = CreateAdapter(DetailHtml);
            var detail = await adapter.GetDramaDetailAsync("7687919221593885758");

            Assert.NotNull(detail);
            Assert.Equal("https://hongguoduanju.com/player/7687919221593885758", detail!.Episodes[0].VideoUrl);
            Assert.Contains("/7687921081989991486", detail.Episodes[1].VideoUrl);
            Assert.Contains("/7687921189049601086", detail.Episodes[2].VideoUrl);
            Assert.Equal(string.Empty, detail.Episodes[3].VideoUrl);
            Assert.Equal(string.Empty, detail.Episodes[4].VideoUrl);
        }

        // ==================== 搜索与播放的已知限制 ====================

        [Fact]
        public async Task SearchAsync_ReturnsEmpty_BecauseSiteSearchIsClientSide()
        {
            // 站点搜索页是纯前端渲染，服务端不返回结果；由本地索引兜底
            var adapter = CreateAdapter(CategoryHtml);
            var result = await adapter.SearchAsync("二嫁");

            Assert.Empty(result.Items);
        }

        [Fact]
        public async Task GetPlayUrlAsync_ExtractsPlainMp4Url_FromPlayerPage()
        {
            // 2026-10-02 实测更正：红果视频没有加密，播放页 SSR 的
            // video_player_info.main_url 就是明文 MP4 地址（CDN 还开了 CORS）。
            var adapter = CreateRoutedAdapter(
                ("/detail", DetailHtml),
                ("/player/", PlayerHtml));

            var url = await adapter.GetPlayUrlAsync("7687919221593885758", 1);

            Assert.StartsWith("https://v26-hgweb.qznovelvod.com/", url);
            Assert.Contains(".mp4", url);
            // SSR 把 / 转义成 \u002F，必须还原，否则地址不可用
            Assert.DoesNotContain("\\u002F", url);
        }

        [Fact]
        public async Task GetPlayUrlAsync_EpisodeNotPublished_ExplainsPermission_NotDrm()
        {
            // 第 4 集在 SSR 里没有播放页链接：官网只公开前几集（accessible_episode_cnt=3），
            // 其余要登录/会员。这是权限问题，不是加密问题。
            var adapter = CreateRoutedAdapter(
                ("/detail", DetailHtml),
                ("/player/", PlayerHtml));

            var ex = await Assert.ThrowsAsync<NotSupportedException>(
                () => adapter.GetPlayUrlAsync("7687919221593885758", 4));

            Assert.Contains("未在官网公开", ex.Message);
            Assert.DoesNotContain("DRM", ex.Message);
        }

        // ==================== 容错 ====================

        [Fact]
        public async Task GetDramaDetailAsync_SiteUnreachable_ReturnsNull()
        {
            var options = new HongGuoWebOptions { BaseUrl = "https://hongguoduanju.com" };
            var adapter = new HongGuoWebAdapter(options, new HttpClient(new FailingHandler()), NullLogger.Instance);

            Assert.Null(await adapter.GetDramaDetailAsync("1"));
        }

        [Fact]
        public async Task GetDramaDetailAsync_UnparsablePage_DoesNotThrow()
        {
            var adapter = CreateAdapter("<html><body>502 Bad Gateway</body></html>");
            var detail = await adapter.GetDramaDetailAsync("1");

            // 解析不出内容时返回一个空壳而不是抛异常，避免拖垮聚合搜索
            Assert.NotNull(detail);
            Assert.Empty(detail!.Episodes);
        }

        private sealed class StubHandler : HttpMessageHandler
        {
            private readonly string _body;
            public StubHandler(string body) => _body = body;

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
                => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_body, Encoding.UTF8, "text/html")
                });
        }

        private sealed class FailingHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
                => throw new HttpRequestException("connection refused");
        }
    }
}
