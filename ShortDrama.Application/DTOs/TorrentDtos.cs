using System.Collections.Generic;

namespace ShortDrama.Application.DTOs
{
    /// <summary>
    /// 本地种子服务（torrent-search）的状态。
    ///
    /// 它是本机另一个独立进程，可能没启动 —— 前端据此决定「种子」入口是否可用，
    /// 而不是让用户点了才发现连不上。
    /// </summary>
    public class TorrentStatusDto
    {
        public bool Enabled { get; set; }
        public bool Reachable { get; set; }
        public string BaseUrl { get; set; } = string.Empty;
        public string? Version { get; set; }
        public string? DownloadDir { get; set; }
        public int ActiveDownloads { get; set; }
        public string? Backend { get; set; }
        /// <summary>不可用时的原因（连不上 / 没启用 / 种子服务自己报的错）</summary>
        public string? Message { get; set; }
    }

    public class TorrentSearchResultDto
    {
        public string Title { get; set; } = string.Empty;
        /// <summary>站点给的体积文本（站点没提供时为 null，不用 0 冒充）</summary>
        public string? SizeText { get; set; }
        public long? SizeBytes { get; set; }
        /// <summary>做种数。为 null 表示该站点不提供（例如 dmhy），不是 0</summary>
        public int? Seeders { get; set; }
        public int? Leechers { get; set; }
        public string InfoHash { get; set; } = string.Empty;
        public string Magnet { get; set; } = string.Empty;
        public List<string> Sources { get; set; } = new();
        public string? PublishedAt { get; set; }
    }

    public class TorrentSearchResponseDto
    {
        public string Query { get; set; } = string.Empty;
        public int Total { get; set; }
        public long TookMs { get; set; }
        public bool Cached { get; set; }
        public List<TorrentSearchResultDto> Results { get; set; } = new();
        /// <summary>
        /// 每个索引源的结果数。**必须给前端**：不然「0 条」无法解释 ——
        /// 是全部源都没命中，还是只有某一个源挂了？
        /// </summary>
        public List<TorrentSourceStatusDto> Sources { get; set; } = new();
        /// <summary>某个源失败时的说明（例如 nyaa 超时）—— 如实转述，这能解释为什么结果比预期少</summary>
        public List<string> SourceErrors { get; set; } = new();

        /// <summary>
        /// 被「与关键词无关」挡掉的条数。
        /// 实测 apibay 对中文查询无效，会返回它自己的默认榜单（做种数还极高），
        /// 这些结果必须挡掉，否则会把真正命中的结果挤下去。
        /// </summary>
        public int FilteredIrrelevant { get; set; }

        /// <summary>被成人内容关键词挡掉的条数（用户可关掉这个过滤）</summary>
        public int FilteredAdult { get; set; }
    }

    public class TorrentSourceStatusDto
    {
        public string Id { get; set; } = string.Empty;
        public bool Ok { get; set; }
        public int Count { get; set; }
        public string? Error { get; set; }
    }

    public class TorrentFileDto
    {
        public int Index { get; set; }
        public string Path { get; set; } = string.Empty;
        public long Length { get; set; }
        public string ContentType { get; set; } = string.Empty;
        /// <summary>本平台的播放地址（经本服务代理，前端不必知道种子服务在哪、也不受跨源限制）</summary>
        public string PlayUrl { get; set; } = string.Empty;
        /// <summary>浏览器能否直接播：mkv/avi/ts 这类容器原生不支持，前端据此给出提示而不是黑屏</summary>
        public bool BrowserPlayable { get; set; }
    }

    public class TorrentTaskDto
    {
        public string InfoHash { get; set; } = string.Empty;
        public string? Name { get; set; }
        /// <summary>queued / metadata / downloading / done / failed / cancelled / stopped …</summary>
        public string Status { get; set; } = string.Empty;
        public long TotalBytes { get; set; }
        public long BytesDone { get; set; }
        public int PiecesDone { get; set; }
        public int PieceCount { get; set; }
        public double Progress { get; set; }
        public long Speed { get; set; }
        public int PeersConnected { get; set; }
        /// <summary>是否已经拿到种子元数据（拿到之后才有文件清单，才能选文件播放）</summary>
        public bool MetadataReady { get; set; }
        public List<TorrentFileDto> Files { get; set; } = new();
        public string? Error { get; set; }
    }

    /// <summary>建任务（开始下载）的请求体</summary>
    public class TorrentPrepareRequest
    {
        /// <summary>磁力链接。也接受裸的 40 位 info hash</summary>
        public string Magnet { get; set; } = string.Empty;
    }
}
