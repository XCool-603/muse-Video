namespace ShortDrama.Infrastructure.Services
{
    /// <summary>
    /// 本地种子服务（torrent-search）的连接配置。
    ///
    /// 它默认只监听 127.0.0.1:8787，是**另一个独立进程**：需要用户自己把它跑起来
    /// （`node bin/magnet-search.mjs serve`）。没跑起来时本平台的「种子」入口会显示
    /// 不可用并说明原因，而不是给一堆连接失败。
    /// </summary>
    public class TorrentOptions
    {
        public const string SectionName = "Torrent";

        /// <summary>总开关。关掉之后 /api/v1/torrent/* 一律返回未启用</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>种子服务地址。它默认只绑回环，所以这里也是回环地址</summary>
        public string BaseUrl { get; set; } = "http://127.0.0.1:8787";

        /// <summary>调用种子服务的超时（秒）。搜索要并行打多个索引站，给宽一点</summary>
        public int TimeoutSeconds { get; set; } = 30;

        /// <summary>
        /// 播放代理的单次读取缓冲（字节）。
        /// 代理是流式转发的，不会把整个文件读进内存；这个值只影响单次拷贝的大小。
        /// </summary>
        public int CopyBufferBytes { get; set; } = 64 * 1024;

        /// <summary>
        /// 边下边播时允许服务端等待数据的最长时间（秒）。
        /// 起播时等首片是正常的；等太久说明这个种子下不动，应当让播放器稍后重试而不是一直挂着。
        /// </summary>
        public int StreamWaitSeconds { get; set; } = 30;
    }
}
