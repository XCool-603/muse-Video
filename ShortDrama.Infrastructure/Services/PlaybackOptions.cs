namespace ShortDrama.Infrastructure.Services
{
    /// <summary>
    /// 播放代理策略。
    ///
    /// 背景：HLS 去广告是在「播放列表层面」完成的（删掉广告分片行、重写媒体序列号），
    /// 重写分片地址时既可以指向后端代理，也可以指向 CDN 绝对地址 ——
    /// 也就是说「去广告」和「代理带宽」是两件事，不该绑在一起。
    ///
    /// 默认直连：浏览器直接向 CDN 取分片，后端只转发几十 KB 的播放列表，
    /// 视频流量完全不经过服务器。
    /// 代价是要求 CDN 允许跨域。实测主流采集源 CDN 都返回
    /// Access-Control-Allow-Origin: *（它们本就是给别人嵌播放器用的）。
    /// 遇到不发 CORS 头的源，把对应开关打开即可。
    /// </summary>
    public class PlaybackOptions
    {
        public const string SectionName = "Playback";

        /// <summary>
        /// m3u8 分片是否走后端代理。
        /// false（默认）= 播放列表里写 CDN 绝对地址，浏览器直连，后端零视频带宽。
        /// true          = 分片全部经后端转发，任何 CDN 都能播（即使不发 CORS 头），
        ///                 代价是所有视频流量都压在这台服务器上。
        /// </summary>
        public bool ProxySegments { get; set; } = false;

        /// <summary>
        /// 明文 MP4 是否走后端代理。含义同上。
        /// false（默认）= 前端直接用 CDN 直链播放。
        /// </summary>
        public bool ProxyMp4 { get; set; } = false;
    }
}
