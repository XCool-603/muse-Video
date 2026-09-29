using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace ShortDrama.Tests
{
    /// <summary>测试用 HttpClientFactory 桩，ProcessPlaylist 路径不会发起真实请求。</summary>
    internal sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new NoopHandler(), disposeHandler: true);

        private sealed class NoopHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
                => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
        }
    }
}
