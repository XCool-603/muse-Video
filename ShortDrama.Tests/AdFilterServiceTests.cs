using System;
using System.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using ShortDrama.Infrastructure.Services;
using Xunit;

namespace ShortDrama.Tests
{
    /// <summary>HLS 去广告处理器测试：验证广告分片识别、剔除与地址重写。</summary>
    public class AdFilterServiceTests
    {
        private static AdFilterService CreateService() =>
            new(new StubHttpClientFactory(), NullLogger<AdFilterService>.Instance);

        private const string MediaPlaylistWithAds = """
            #EXTM3U
            #EXT-X-VERSION:3
            #EXT-X-TARGETDURATION:10
            #EXT-X-MEDIA-SEQUENCE:100
            #EXTINF:3.000,
            seg_100.ts
            #EXTINF:3.000,
            seg_101.ts
            #EXT-X-CUE-OUT:30.000
            #EXTINF:15.000,
            https://cdn.example.com/ad/preroll_001.ts
            #EXTINF:15.000,
            https://cdn.example.com/ad/preroll_002.ts
            #EXT-X-CUE-IN
            #EXT-X-DISCONTINUITY
            #EXTINF:3.000,
            seg_102.ts
            #EXTINF:3.000,
            seg_103.ts
            #EXTINF:30.000,
            https://cdn.example.com/video/midroll_003.ts
            #EXTINF:3.000,
            seg_104.ts
            """;

        [Fact]
        public void ProcessPlaylist_RemovesAdSegments_MarkedByCueOut()
        {
            var service = CreateService();

            var result = service.ProcessPlaylist(MediaPlaylistWithAds, "https://cdn.example.com/hls/index.m3u8");

            Assert.True(result.Success);
            Assert.DoesNotContain("preroll_001.ts", result.Playlist);
            Assert.DoesNotContain("preroll_002.ts", result.Playlist);
            // 正片分片必须保留
            Assert.Contains("seg_100.ts", result.Playlist);
            Assert.Contains("seg_102.ts", result.Playlist);
            Assert.Contains("seg_104.ts", result.Playlist);
        }

        [Fact]
        public void ProcessPlaylist_RemovesAdSegments_MarkedByUrlPattern()
        {
            var service = CreateService();

            var result = service.ProcessPlaylist(MediaPlaylistWithAds, "https://cdn.example.com/hls/index.m3u8");

            Assert.DoesNotContain("midroll_003.ts", result.Playlist);
            Assert.True(result.RemovedSegmentCount >= 3, $"应至少剔除 3 个广告分片，实际 {result.RemovedSegmentCount}");
            Assert.NotEmpty(result.SkippedAdSegments);
        }

        [Fact]
        public void ProcessPlaylist_RemovesAdSegments_MarkedByAbnormalDuration()
        {
            var service = CreateService();

            const string playlist = """
                #EXTM3U
                #EXT-X-VERSION:3
                #EXTINF:4.000,
                normal_1.ts
                #EXTINF:30.000,
                https://cdn.example.com/media/slot_2.ts
                #EXTINF:4.000,
                normal_3.ts
                """;

            var result = service.ProcessPlaylist(playlist, "https://cdn.example.com/hls/index.m3u8");

            Assert.DoesNotContain("slot_2.ts", result.Playlist);
            Assert.Contains("normal_1.ts", result.Playlist);
            Assert.Contains("normal_3.ts", result.Playlist);
        }

        [Fact]
        public void ProcessPlaylist_KeepsCleanPlaylistIntact()
        {
            var service = CreateService();

            const string playlist = """
                #EXTM3U
                #EXT-X-VERSION:3
                #EXT-X-TARGETDURATION:10
                #EXTINF:4.000,
                seg_1.ts
                #EXTINF:4.000,
                seg_2.ts
                #EXTINF:4.000,
                seg_3.ts
                """;

            var result = service.ProcessPlaylist(playlist, "https://cdn.example.com/hls/index.m3u8");

            Assert.Equal(0, result.RemovedSegmentCount);
            Assert.Contains("seg_1.ts", result.Playlist);
            Assert.Contains("seg_2.ts", result.Playlist);
            Assert.Contains("seg_3.ts", result.Playlist);
        }

        [Fact]
        public void ProcessPlaylist_ResolvesRelativeSegmentUrlsToAbsolute()
        {
            var service = CreateService();

            const string playlist = """
                #EXTM3U
                #EXTINF:4.000,
                sub/seg_1.ts
                """;

            var result = service.ProcessPlaylist(playlist, "https://cdn.example.com/hls/720p/index.m3u8");

            Assert.Contains("https://cdn.example.com/hls/720p/sub/seg_1.ts", result.Playlist);
        }

        [Fact]
        public void ProcessPlaylist_RewritesSegmentsToProxyWhenProvided()
        {
            var service = CreateService();
            const string proxy = "http://localhost:5080/api/v1/play/segment";

            const string playlist = """
                #EXTM3U
                #EXTINF:4.000,
                seg_1.ts
                """;

            var result = service.ProcessPlaylist(playlist, "https://cdn.example.com/hls/index.m3u8", proxy);

            Assert.Contains($"{proxy}?u=", result.Playlist);
            Assert.Contains(Uri.EscapeDataString("https://cdn.example.com/hls/seg_1.ts"), result.Playlist);
        }

        [Fact]
        public void ProcessPlaylist_MasterPlaylist_KeepsStreamInfAndRewritesVariants()
        {
            var service = CreateService();

            const string master = """
                #EXTM3U
                #EXT-X-STREAM-INF:BANDWIDTH=1000000,RESOLUTION=1280x720
                url_0/index.m3u8
                #EXT-X-STREAM-INF:BANDWIDTH=500000,RESOLUTION=640x360
                url_1/index.m3u8
                """;

            var result = service.ProcessPlaylist(master, "https://cdn.example.com/hls/master.m3u8");

            Assert.True(result.Success);
            Assert.Contains("#EXT-X-STREAM-INF", result.Playlist);
            Assert.Contains("https://cdn.example.com/hls/url_0/index.m3u8", result.Playlist);
            Assert.Contains("https://cdn.example.com/hls/url_1/index.m3u8", result.Playlist);
        }

        [Fact]
        public void ProcessPlaylist_RewritesMediaSequenceAfterRemoval()
        {
            var service = CreateService();

            var result = service.ProcessPlaylist(MediaPlaylistWithAds, "https://cdn.example.com/hls/index.m3u8");

            // 剔除广告后媒体序列号需保持一致（不能出现断裂）
            var seqLine = result.Playlist
                .Split('\n')
                .FirstOrDefault(l => l.StartsWith("#EXT-X-MEDIA-SEQUENCE", StringComparison.Ordinal));

            Assert.NotNull(seqLine);
            Assert.Equal("#EXT-X-MEDIA-SEQUENCE:100", seqLine);
        }

        // ==================== 带 URI 属性的标签（加密密钥等） ====================

        [Fact]
        public void ProcessPlaylist_RewritesEncryptionKeyUriToAbsolute()
        {
            // 加密流的密钥地址不重写就会被解析成
            // /api/v1/play/stream/{id}/enc.key → 404，直接无法解密
            var service = CreateService();

            const string playlist = """
                #EXTM3U
                #EXT-X-VERSION:3
                #EXT-X-KEY:METHOD=AES-128,URI="enc.key",IV=0x1234567890ABCDEF1234567890ABCDEF
                #EXTINF:4.000,
                seg_1.ts
                """;

            var result = service.ProcessPlaylist(playlist, "https://cdn.example.com/hls/index.m3u8");

            Assert.True(result.Success);
            Assert.Contains("URI=\"https://cdn.example.com/hls/enc.key\"", result.Playlist);
            Assert.DoesNotContain("URI=\"enc.key\"", result.Playlist);
            // IV 等其它属性必须原样保留
            Assert.Contains("IV=0x1234567890ABCDEF1234567890ABCDEF", result.Playlist);
            Assert.Contains("METHOD=AES-128", result.Playlist);
        }

        [Fact]
        public void ProcessPlaylist_RewritesEncryptionKeyUriToProxyWhenProvided()
        {
            var service = CreateService();
            const string proxy = "http://localhost:5080/api/v1/play/segment";

            const string playlist = """
                #EXTM3U
                #EXT-X-KEY:METHOD=AES-128,URI="enc.key"
                #EXTINF:4.000,
                seg_1.ts
                """;

            var result = service.ProcessPlaylist(playlist, "https://cdn.example.com/hls/index.m3u8", proxy);

            Assert.Contains($"{proxy}?u={Uri.EscapeDataString("https://cdn.example.com/hls/enc.key")}", result.Playlist);
        }

        [Fact]
        public void ProcessPlaylist_KeepsAlreadyAbsoluteKeyUriUsable()
        {
            // 已是绝对地址时不能把它改坏（例如二次编码）
            var service = CreateService();

            const string playlist = """
                #EXTM3U
                #EXT-X-KEY:METHOD=AES-128,URI="https://keys.example.com/abc.key"
                #EXTINF:4.000,
                seg_1.ts
                """;

            var result = service.ProcessPlaylist(playlist, "https://cdn.example.com/hls/index.m3u8");

            Assert.Contains("URI=\"https://keys.example.com/abc.key\"", result.Playlist);
        }

        [Fact]
        public void ProcessPlaylist_RewritesMapUri()
        {
            // fMP4 的初始化分片同样带 URI 属性
            var service = CreateService();

            const string playlist = """
                #EXTM3U
                #EXT-X-MAP:URI="init.mp4"
                #EXTINF:4.000,
                seg_1.m4s
                """;

            var result = service.ProcessPlaylist(playlist, "https://cdn.example.com/hls/index.m3u8");

            Assert.Contains("URI=\"https://cdn.example.com/hls/init.mp4\"", result.Playlist);
        }

        [Fact]
        public void ProcessPlaylist_MasterPlaylist_RewritesMediaUri()
        {
            // 主列表里的备用音轨/字幕轨同样带 URI 属性
            var service = CreateService();

            const string master = """
                #EXTM3U
                #EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID="aud",NAME="中文",URI="audio/index.m3u8"
                #EXT-X-STREAM-INF:BANDWIDTH=1000000,AUDIO="aud"
                video/index.m3u8
                """;

            var result = service.ProcessPlaylist(master, "https://cdn.example.com/hls/master.m3u8");

            Assert.Contains("URI=\"https://cdn.example.com/hls/audio/index.m3u8\"", result.Playlist);
            Assert.Contains("https://cdn.example.com/hls/video/index.m3u8", result.Playlist);
        }

        [Fact]
        public void ProcessPlaylist_RewritesSingleQuotedKeyUri()
        {
            // 规范要求双引号，但实测有源用单引号，一并处理
            var service = CreateService();

            const string playlist = """
                #EXTM3U
                #EXT-X-KEY:METHOD=AES-128,URI='enc.key'
                #EXTINF:4.000,
                seg_1.ts
                """;

            var result = service.ProcessPlaylist(playlist, "https://cdn.example.com/hls/index.m3u8");

            Assert.Contains("URI='https://cdn.example.com/hls/enc.key'", result.Playlist);
        }
    }
}
