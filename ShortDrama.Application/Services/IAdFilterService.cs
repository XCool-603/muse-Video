using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ShortDrama.Application.Services
{
    public interface IAdFilterService
    {
        /// <summary>
        /// 拉取原始 m3u8 并执行去广告处理，返回重写后的播放列表内容。
        /// </summary>
        /// <param name="rawUrl">原始播放地址</param>
        /// <param name="segmentProxyBase">
        /// 若提供，则媒体分片地址会被重写为该代理前缀（形如 /api/v1/play/segment?u=xxx），
        /// 由后端统一转发，彻底规避跨域与第三方追踪。
        /// </param>
        /// <param name="ct">取消令牌</param>
        Task<AdFilterResult> BuildCleanPlaylistAsync(string rawUrl, string? segmentProxyBase = null, CancellationToken ct = default);

        /// <summary>
        /// 对已获取的 m3u8 文本执行去广告与地址重写（供分片代理复用，避免二次拉取）。
        /// </summary>
        /// <param name="text">m3u8 原始文本</param>
        /// <param name="baseUrl">该 m3u8 的绝对地址，用于解析相对分片</param>
        /// <param name="segmentProxyBase">分片代理前缀</param>
        AdFilterResult ProcessPlaylist(string text, string baseUrl, string? segmentProxyBase = null);

        /// <summary>把相对分片地址补全为绝对地址，保证浏览器可直接拉取</summary>
        string ResolveUrl(string baseUrl, string relative);
    }

    public class AdFilterResult
    {
        /// <summary>重写后的 m3u8 文本</summary>
        public string Playlist { get; set; } = string.Empty;

        /// <summary>是否成功处理（失败时前端回退直连原始地址）</summary>
        public bool Success { get; set; }

        /// <summary>被识别并剔除的广告分片数量</summary>
        public int RemovedSegmentCount { get; set; }

        /// <summary>被剔除的广告分片地址（用于调试/展示）</summary>
        public List<string> SkippedAdSegments { get; set; } = new();

        public string? Error { get; set; }
    }
}
