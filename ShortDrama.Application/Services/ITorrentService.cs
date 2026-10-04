using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ShortDrama.Application.DTOs;

namespace ShortDrama.Application.Services
{
    /// <summary>
    /// 本地种子服务（torrent-search，默认 127.0.0.1:8787）的封装。
    ///
    /// 搜索、下载、边下边播都由那个进程做，本服务只负责三件事：
    ///   1. 转发请求，顺便把这些接口收在本平台的访问口令门之后；
    ///   2. 把它的字段映射成前端友好的形状（补播放地址、判断浏览器能否播）；
    ///   3. 它没启动时给出可读原因，而不是把连接失败直接抛给前端。
    /// </summary>
    public interface ITorrentService
    {
        /// <summary>探测本地种子服务是否可用</summary>
        Task<TorrentStatusDto> GetStatusAsync(CancellationToken ct = default);

        /// <summary>聚合搜索（默认按做种数排序，做种数高的才下得动）</summary>
        Task<TorrentSearchResponseDto> SearchAsync(
            string keyword, int limit = 10, int? minSeeders = null, CancellationToken ct = default);

        /// <summary>确保该磁力有下载任务（没有就创建），返回任务状态与文件清单</summary>
        Task<TorrentTaskDto> PrepareAsync(string magnet, CancellationToken ct = default);

        /// <summary>查任务状态；没有该任务时返回 null</summary>
        Task<TorrentTaskDto?> GetTaskAsync(string infoHash, CancellationToken ct = default);

        /// <summary>
        /// 代理播放：把 Range 头透传给种子服务，再把它的响应（状态码 / 关键头 / 字节流）原样转发。
        ///
        /// 为什么代理而不是让前端直连 127.0.0.1:8787：
        ///   · 前端只需认本平台一个源，不受跨源与 Referer 影响；
        ///   · 访问口令门同时保护了这个接口（种子服务本身没有鉴权）；
        ///   · 平台部署在服务器上、用户从别的设备访问时，直连 127.0.0.1 根本不成立。
        /// 代价是多一跳本机回环，对自托管场景可以忽略。
        /// </summary>
        /// <param name="headOnly">
        /// true 时只取头部。必须真的向上游发 HEAD：种子服务对 GET 会等数据就绪（最多 30 秒），
        /// 而 HEAD 是立刻返回的 —— 播放器的探测请求不该被拖住。
        /// </param>
        Task<TorrentStreamResponse?> OpenStreamAsync(
            string infoHash, int fileIndex, string? rangeHeader, bool headOnly = false, CancellationToken ct = default);
    }

    /// <summary>
    /// 代理播放的响应。调用方负责 Dispose（会连带释放上游流）。
    /// </summary>
    public sealed class TorrentStreamResponse : IDisposable
    {
        public int StatusCode { get; init; }
        public string ContentType { get; init; } = "application/octet-stream";
        public long? ContentLength { get; init; }
        public string? ContentRange { get; init; }
        public bool AcceptRanges { get; init; } = true;
        public Stream Content { get; init; } = Stream.Null;
        /// <summary>非 2xx 时的响应体（种子服务返回的 JSON 错误说明）</summary>
        public string? ErrorBody { get; init; }

        public void Dispose() => Content.Dispose();
    }
}
