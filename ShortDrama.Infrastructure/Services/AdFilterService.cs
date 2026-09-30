using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ShortDrama.Application.Services;

namespace ShortDrama.Infrastructure.Services
{
    /// <summary>
    /// HLS 去广告处理器。
    /// 策略：
    ///  1) 解析 m3u8，识别广告分片（URL 特征 / 时长异常 / #EXT-X-CUE-OUT 广告标记 / 不连续标记）；
    ///  2) 剔除广告分片及其对应的 #EXTINF 行，重写媒体序列号；
    ///  3) 把分片地址重写为绝对地址，交由后端代理转发，规避跨域与直连追踪参数。
    /// </summary>
    public class AdFilterService : IAdFilterService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<AdFilterService> _logger;

        // 常见广告分片特征
        private static readonly string[] AdUrlPatterns =
        {
            "/ad/", "/ads/", "advert", "ad_", "_ad.", "preroll", "midroll", "postroll",
            "trailer", "promo", "/gg/", "guanggao", "sponsor"
        };

        // 广告标记标签
        private static readonly string[] AdTags =
        {
            "#EXT-X-CUE-OUT", "#EXT-X-AD", "#EXT-X-DATERANGE:CLASS=\"ad\"", "#EXT-OATCLS-SCTE35"
        };

        private static readonly Regex ExtInfRegex = new(@"^#EXTINF:\s*(?<dur>[0-9.]+)\s*(?<title>,.*)?$", RegexOptions.Compiled);

        // 带 URI 属性的标签（#EXT-X-KEY / #EXT-X-MAP / #EXT-X-MEDIA /
        // #EXT-X-I-FRAME-STREAM-INF / #EXT-X-SESSION-KEY / #EXT-X-PART 等）
        private static readonly Regex UriAttrRegex = new(
            "URI\\s*=\\s*(?<q>[\"'])(?<uri>[^\"']*)\\k<q>",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public AdFilterService(IHttpClientFactory httpClientFactory, ILogger<AdFilterService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public async Task<AdFilterResult> BuildCleanPlaylistAsync(string rawUrl, string? segmentProxyBase = null, CancellationToken ct = default)
        {
            var result = new AdFilterResult();
            try
            {
                var client = _httpClientFactory.CreateClient("stream");
                var text = await client.GetStringAsync(rawUrl, ct);
                return ProcessPlaylist(text, rawUrl, segmentProxyBase);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "去广告处理失败，回退原始地址: {Url}", rawUrl);
                result.Success = false;
                result.Error = ex.Message;
                return result;
            }
        }

        public AdFilterResult ProcessPlaylist(string text, string baseUrl, string? segmentProxyBase = null)
        {
            var result = new AdFilterResult();
            try
            {
                // Master playlist（含 #EXT-X-STREAM-INF）不处理广告，直接补全绝对地址
                if (text.Contains("#EXT-X-STREAM-INF", StringComparison.OrdinalIgnoreCase))
                {
                    result.Playlist = RewriteMasterPlaylist(text, baseUrl, segmentProxyBase);
                }
                else
                {
                    result.Playlist = RewriteMediaPlaylist(text, baseUrl, result, segmentProxyBase);
                }

                result.Success = true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "播放列表重写失败: {Url}", baseUrl);
                result.Success = false;
                result.Error = ex.Message;
                result.Playlist = text;
            }

            return result;
        }

        private string RewriteMasterPlaylist(string text, string baseUrl, string? segmentProxyBase)
        {
            var sb = new StringBuilder();
            foreach (var rawLine in text.Split('\n'))
            {
                var line = rawLine.TrimEnd('\r');
                if (line.Length == 0 || line.StartsWith('#'))
                {
                    // 主列表里也有带 URI 的标签，例如
                    //   #EXT-X-MEDIA:TYPE=AUDIO,...,URI="audio.m3u8"
                    //   #EXT-X-I-FRAME-STREAM-INF:...,URI="iframe.m3u8"
                    // 不重写的话播放器会按本列表的 URL 去解析，直接 404
                    sb.AppendLine(RewriteUriAttributes(line, baseUrl, segmentProxyBase));
                }
                else
                {
                    // 子播放列表地址：交给后端代理，保证子列表同样经过去广告处理
                    var absolute = ResolveUrl(baseUrl, line);
                    sb.AppendLine(segmentProxyBase is null
                        ? absolute
                        : $"{segmentProxyBase}?u={Uri.EscapeDataString(absolute)}");
                }
            }
            return sb.ToString();
        }

        private string RewriteMediaPlaylist(string text, string baseUrl, AdFilterResult result, string? segmentProxyBase)
        {
            var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
            var output = new List<string>();
            var pendingExtInf = new List<string>();
            var skipUntilDiscontinuity = false;
            var maxSeq = 0;
            var seqFound = false;

            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i];

                // 广告区间标记：跳过直到 END 或 DISCONTINUITY
                if (AdTags.Any(t => line.StartsWith(t, StringComparison.OrdinalIgnoreCase)))
                {
                    if (line.StartsWith("#EXT-X-CUE-OUT", StringComparison.OrdinalIgnoreCase) ||
                        line.StartsWith("#EXT-X-AD", StringComparison.OrdinalIgnoreCase))
                    {
                        skipUntilDiscontinuity = true;
                        pendingExtInf.Clear();
                        continue;
                    }
                }

                if (line.StartsWith("#EXT-X-CUE-IN", StringComparison.OrdinalIgnoreCase))
                {
                    skipUntilDiscontinuity = false;
                    pendingExtInf.Clear();
                    continue;
                }

                if (skipUntilDiscontinuity && line.StartsWith("#EXT-X-DISCONTINUITY", StringComparison.OrdinalIgnoreCase))
                {
                    skipUntilDiscontinuity = false;
                    continue;
                }

                if (skipUntilDiscontinuity)
                {
                    // 广告区间内的分片与标签一并丢弃，并计入剔除统计
                    if (line.Length > 0 && !line.StartsWith('#'))
                    {
                        result.RemovedSegmentCount++;
                        result.SkippedAdSegments.Add(line);
                        _logger.LogDebug("剔除广告区间分片: {Segment}", line);
                    }
                    continue;
                }

                if (line.Length == 0)
                {
                    output.Add(line);
                    continue;
                }

                if (line.StartsWith('#'))
                {
                    var seqMatch = Regex.Match(line, @"^#EXT-X-MEDIA-SEQUENCE:\s*(?<n>\d+)");
                    if (seqMatch.Success)
                    {
                        seqFound = true;
                        maxSeq = int.Parse(seqMatch.Groups["n"].Value);
                    }

                    if (line.StartsWith("#EXTINF", StringComparison.OrdinalIgnoreCase))
                    {
                        pendingExtInf.Add(line);
                    }
                    else
                    {
                        // 标签行也要重写 URI 属性。最容易漏、后果最严重的是加密密钥：
                        //   #EXT-X-KEY:METHOD=AES-128,URI="enc.key"
                        // 不重写会被解析成 /api/v1/play/stream/{id}/enc.key → 404，
                        // 加密流无法解密，而且 hls.js 会对每个分片反复重试取密钥，
                        // 控制台刷出一整屏 404。实测 3113 就是这样。
                        output.Add(RewriteUriAttributes(line, baseUrl, segmentProxyBase));
                    }
                    continue;
                }

                // 媒体分片行
                var isAd = IsAdSegment(line, pendingExtInf);
                if (isAd)
                {
                    result.RemovedSegmentCount++;
                    result.SkippedAdSegments.Add(line);
                    _logger.LogDebug("剔除广告分片: {Segment}", line);
                    pendingExtInf.Clear();
                    continue;
                }

                output.AddRange(pendingExtInf);
                pendingExtInf.Clear();

                var absoluteSegment = ResolveUrl(baseUrl, line);
                output.Add(segmentProxyBase is null
                    ? absoluteSegment
                    : $"{segmentProxyBase}?u={Uri.EscapeDataString(absoluteSegment)}");
            }

            // 处理尾部悬挂的 EXTINF
            if (pendingExtInf.Count > 0) output.AddRange(pendingExtInf);

            // 重写媒体序列号（剔除分片后序列需保持连续）
            if (seqFound && result.RemovedSegmentCount > 0)
            {
                for (var i = 0; i < output.Count; i++)
                {
                    if (output[i].StartsWith("#EXT-X-MEDIA-SEQUENCE", StringComparison.OrdinalIgnoreCase))
                    {
                        output[i] = $"#EXT-X-MEDIA-SEQUENCE:{maxSeq}";
                        break;
                    }
                }
            }

            return string.Join("\n", output);
        }

        private static bool IsAdSegment(string segmentUrl, List<string> pendingExtInf)
        {
            var lower = segmentUrl.ToLowerInvariant();

            if (AdUrlPatterns.Any(p => lower.Contains(p, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            // 时长异常：短剧正片分片通常 2-10 秒，广告分片常见 15s/30s 整数时长
            foreach (var extInf in pendingExtInf)
            {
                var m = ExtInfRegex.Match(extInf);
                if (m.Success && double.TryParse(m.Groups["dur"].Value, out var dur))
                {
                    if (dur >= 15 && Math.Abs(dur - Math.Round(dur)) < 0.001)
                    {
                        return true;
                    }
                }

                // EXTINF 标题中带广告标识
                if (extInf.Contains("ad", StringComparison.OrdinalIgnoreCase) ||
                    extInf.Contains("广告", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 重写标签里 URI 属性的地址（#EXT-X-KEY / #EXT-X-MAP / #EXT-X-MEDIA /
        /// #EXT-X-I-FRAME-STREAM-INF / #EXT-X-SESSION-KEY / #EXT-X-PART 等）。
        ///
        /// 为什么必须做：播放器把列表里的相对地址按「列表自身的 URL」解析。
        /// 列表是从 /api/v1/play/stream/{id}/{ep}.m3u8 返回的，所以
        /// #EXT-X-KEY:URI="enc.key" 会变成请求 /api/v1/play/stream/{id}/enc.key → 404。
        /// 后果是加密流完全无法解密，且 hls.js 会为每个分片反复重试取密钥，刷屏 404。
        /// 分片行有重写而标签行没有，是最容易漏掉的一类 bug。
        /// </summary>
        private string RewriteUriAttributes(string line, string baseUrl, string? segmentProxyBase)
        {
            if (line.Length == 0 || !line.Contains("URI=", StringComparison.OrdinalIgnoreCase))
            {
                return line;
            }

            return UriAttrRegex.Replace(line, m =>
            {
                var raw = m.Groups["uri"].Value;
                if (string.IsNullOrWhiteSpace(raw)) return m.Value;

                // ResolveUrl 对已是绝对地址的输入原样返回
                var absolute = ResolveUrl(baseUrl, raw);
                var rewritten = segmentProxyBase is null
                    ? absolute
                    : $"{segmentProxyBase}?u={Uri.EscapeDataString(absolute)}";

                var quote = m.Groups["q"].Value;
                return $"URI={quote}{rewritten}{quote}";
            });
        }

        public string ResolveUrl(string baseUrl, string relative)
        {
            if (string.IsNullOrWhiteSpace(relative)) return relative;
            if (relative.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                relative.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return relative;
            }

            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
            {
                return relative;
            }

            return new Uri(baseUri, relative).ToString();
        }
    }
}
