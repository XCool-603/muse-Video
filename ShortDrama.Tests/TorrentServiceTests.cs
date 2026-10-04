using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using ShortDrama.Infrastructure.Services;
using Xunit;

namespace ShortDrama.Tests
{
    /// <summary>
    /// 本地种子服务封装的测试。
    ///
    /// 重点覆盖**字段形状**：种子服务有两个不同的输出形状 ——
    /// HTTP API 的 size 是数字（字节）+ sizeText 是字符串，而技能脚本输出的 JSON
    /// 是 size 字符串 + sizeBytes。按一种写死就会 500，这里把两种都钉住。
    /// </summary>
    public class TorrentServiceTests
    {
        private static TorrentService CreateService(params (string Fragment, string Body)[] routes)
            => new(new CannedHttpClientFactory(routes), new TorrentOptions(), NullLogger<TorrentService>.Instance);

        [Theory]
        [InlineData("magnet:?xt=urn:btih:04F7386BD83F2D5A3C891EBDDB211835C39D16C6&dn=x", "04f7386bd83f2d5a3c891ebddb211835c39d16c6")]
        [InlineData("04f7386bd83f2d5a3c891ebddb211835c39d16c6", "04f7386bd83f2d5a3c891ebddb211835c39d16c6")]
        [InlineData("magnet:?xt=urn:btih:ZZZZ", null)]
        [InlineData("", null)]
        [InlineData("magnet:?dn=没有hash", null)]
        public void ExtractInfoHash_从磁力或裸hash里取出小写hash(string input, string? expected)
            => Assert.Equal(expected, TorrentService.ExtractInfoHash(input));

        [Fact]
        public async Task SearchAsync_HTTP接口形状_size是数字_sizeText是字符串()
        {
            const string body = """
            {
              "ok": true, "query": "ubuntu", "total": 1, "tookMs": 12, "cached": false,
              "sources": [{ "id": "bitsearch", "ok": true, "count": 1, "error": null }],
              "results": [{
                "title": "ubuntu-24.04.iso", "infoHash": "A7838B75C42B612DA3B6CC99BEED4ECB2D04CFF2",
                "magnet": "magnet:?xt=urn:btih:a7838b75c42b612da3b6cc99beed4ecb2d04cff2",
                "size": 4927586304, "sizeText": "4.59 GiB",
                "seeders": 177, "leechers": 331, "sources": ["bitsearch"],
                "publishedAt": "2026-10-04T04:00:27.179Z"
              }]
            }
            """;

            var service = CreateService(("api/search", body));
            var result = await service.SearchAsync("ubuntu");

            var item = Assert.Single(result.Results);
            Assert.Equal("4.59 GiB", item.SizeText);
            Assert.Equal(4927586304, item.SizeBytes);
            Assert.Equal(177, item.Seeders);
            // info hash 统一转小写，避免同一资源因大小写被当成两个
            Assert.Equal("a7838b75c42b612da3b6cc99beed4ecb2d04cff2", item.InfoHash);
            Assert.Equal(new[] { "bitsearch" }, item.Sources);
        }

        [Fact]
        public async Task SearchAsync_技能脚本形状_size是字符串_也能解析()
        {
            const string body = """
            {
              "total": 1, "tookMs": 5, "cached": true, "sources": [],
              "results": [{
                "title": "x.iso", "infoHash": "a7838b75c42b612da3b6cc99beed4ecb2d04cff2",
                "magnet": "magnet:?xt=urn:btih:a7838b75c42b612da3b6cc99beed4ecb2d04cff2",
                "size": "3.40 GiB", "sizeBytes": 3654957056, "seeders": null
              }]
            }
            """;

            var service = CreateService(("api/search", body));
            var result = await service.SearchAsync("x");

            var item = Assert.Single(result.Results);
            Assert.Equal("3.40 GiB", item.SizeText);
            Assert.Equal(3654957056, item.SizeBytes);
            // 站点没提供做种数就是 null，不能当 0
            Assert.Null(item.Seeders);
            Assert.True(result.Cached);
        }

        [Fact]
        public async Task SearchAsync_失败的数据源要如实转述_否则解释不了结果为什么少()
        {
            const string body = """
            {
              "total": 0, "tookMs": 900, "cached": false,
              "sources": [
                { "id": "apibay", "ok": true, "count": 0, "error": null },
                { "id": "nyaa", "ok": false, "count": 0, "error": "timeout" }
              ],
              "results": []
            }
            """;

            var service = CreateService(("api/search", body));
            var result = await service.SearchAsync("x");

            Assert.Empty(result.Results);
            Assert.Single(result.SourceErrors);
            Assert.Contains("nyaa", result.SourceErrors[0]);
            Assert.Contains("timeout", result.SourceErrors[0]);
        }

        [Fact]
        public async Task SearchAsync_跳过没有磁力或缺hash的结果()
        {
            const string body = """
            {
              "total": 2, "tookMs": 1, "cached": false, "sources": [],
              "results": [
                { "title": "无磁力", "infoHash": "a7838b75c42b612da3b6cc99beed4ecb2d04cff2", "magnet": null, "size": 1 },
                { "title": "缺hash", "infoHash": "", "magnet": "magnet:?xt=urn:btih:x", "size": 1 }
              ]
            }
            """;

            var service = CreateService(("api/search", body));
            var result = await service.SearchAsync("x");

            Assert.Empty(result.Results);
        }

        [Fact]
        public async Task GetStatusAsync_连不上时给出可读原因_而不是抛异常()
        {
            var service = new TorrentService(
                new CannedHttpClientFactory(Array.Empty<(string, string)>(), throwOnRequest: true),
                new TorrentOptions { BaseUrl = "http://127.0.0.1:8787" },
                NullLogger<TorrentService>.Instance);

            var status = await service.GetStatusAsync();

            Assert.True(status.Enabled);
            Assert.False(status.Reachable);
            Assert.Contains("magnet-search.mjs serve", status.Message);
        }

        [Fact]
        public async Task GetStatusAsync_服务正常时带上版本与下载目录()
        {
            const string body = """
            {
              "ok": true, "version": "1.0.0", "uptimeSec": 10,
              "downloads": { "enabled": true, "dir": "C:/dl", "active": 2, "backend": "builtin" }
            }
            """;

            var service = CreateService(("api/health", body));
            var status = await service.GetStatusAsync();

            Assert.True(status.Reachable);
            Assert.Equal("1.0.0", status.Version);
            Assert.Equal("C:/dl", status.DownloadDir);
            Assert.Equal(2, status.ActiveDownloads);
        }

        /// <summary>按 URL 片段返回预设响应；没有匹配就 404。可配置成直接抛连接异常。</summary>
        private sealed class CannedHttpClientFactory : IHttpClientFactory
        {
            private readonly (string Fragment, string Body)[] _routes;
            private readonly bool _throwOnRequest;

            public CannedHttpClientFactory((string Fragment, string Body)[] routes, bool throwOnRequest = false)
            {
                _routes = routes;
                _throwOnRequest = throwOnRequest;
            }

            public HttpClient CreateClient(string name)
            {
                // BaseAddress 必须给：TorrentService 用的是相对 URI（生产里由 DI 配置 BaseAddress）
                return new HttpClient(new Handler(_routes, _throwOnRequest), disposeHandler: true)
                {
                    BaseAddress = new Uri("http://127.0.0.1:8787/")
                };
            }

            private sealed class Handler : HttpMessageHandler
            {
                private readonly (string Fragment, string Body)[] _routes;
                private readonly bool _throwOnRequest;

                public Handler((string Fragment, string Body)[] routes, bool throwOnRequest)
                {
                    _routes = routes;
                    _throwOnRequest = throwOnRequest;
                }

                protected override Task<HttpResponseMessage> SendAsync(
                    HttpRequestMessage request, CancellationToken cancellationToken)
                {
                    if (_throwOnRequest)
                    {
                        throw new HttpRequestException("Connection refused (模拟种子服务没启动)");
                    }

                    var url = request.RequestUri?.ToString() ?? string.Empty;

                    foreach (var (fragment, body) in _routes)
                    {
                        if (url.Contains(fragment, StringComparison.OrdinalIgnoreCase))
                        {
                            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                            {
                                Content = new StringContent(body, Encoding.UTF8, "application/json")
                            });
                        }
                    }

                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
                }
            }
        }
    }
}
