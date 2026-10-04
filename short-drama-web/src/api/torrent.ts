import { request } from './request'

/** 本地种子服务状态。reachable=false 时前端要显示「怎么把它跑起来」，而不是一堆报错 */
export interface TorrentStatus {
  enabled: boolean
  reachable: boolean
  baseUrl: string
  version?: string | null
  downloadDir?: string | null
  activeDownloads: number
  backend?: string | null
  message?: string | null
}

export interface TorrentSearchResult {
  title: string
  /** 站点没提供体积时为 null —— 不要当 0 显示 */
  sizeText?: string | null
  sizeBytes?: number | null
  /** 站点不提供做种数时为 null（例如 dmhy） */
  seeders?: number | null
  leechers?: number | null
  infoHash: string
  magnet: string
  sources: string[]
  publishedAt?: string | null
}

export interface TorrentSourceStatus {
  id: string
  ok: boolean
  count: number
  error?: string | null
}

export interface TorrentSearchResponse {
  query: string
  total: number
  tookMs: number
  cached: boolean
  results: TorrentSearchResult[]
  /** 每个索引源的结果数 —— 用来解释「为什么只有这几条 / 为什么 0 条」 */
  sources: TorrentSourceStatus[]
  /** 个别源失败的原因，如实展示能解释为什么结果比预期少 */
  sourceErrors: string[]
}

export interface TorrentFile {
  index: number
  path: string
  length: number
  contentType: string
  /** 经本平台代理的播放地址 */
  playUrl: string
  /** 浏览器原生能不能播（mkv/avi 之类不能，前端要提前说） */
  browserPlayable: boolean
}

export interface TorrentTask {
  infoHash: string
  name?: string | null
  status: string
  totalBytes: number
  bytesDone: number
  piecesDone: number
  pieceCount: number
  progress: number
  speed: number
  peersConnected: number
  metadataReady: boolean
  files: TorrentFile[]
  error?: string | null
}

export const torrentApi = {
  status() {
    return request<TorrentStatus>({ url: '/torrent/status', method: 'get' })
  },

  search(params: { q: string; limit?: number; minSeeders?: number }) {
    return request<TorrentSearchResponse>({ url: '/torrent/search', method: 'get', params })
  },

  /** 建任务（开始下载）。元数据未就绪时 files 为空，需要轮询 task() */
  prepare(magnet: string) {
    return request<TorrentTask>({ url: '/torrent/prepare', method: 'post', data: { magnet } })
  },

  task(infoHash: string) {
    return request<TorrentTask>({ url: `/torrent/task/${infoHash}`, method: 'get' })
  }
}

/** 任务是否还在推进（决定要不要继续轮询） */
export function isTaskActive(status: string) {
  return ['queued', 'metadata', 'downloading'].includes(status)
}

/** 把字节数格式化成人类可读 */
export function formatBytes(bytes?: number | null) {
  if (!bytes || bytes <= 0) return '—'
  const units = ['B', 'KB', 'MB', 'GB', 'TB']
  let value = bytes
  let index = 0
  while (value >= 1024 && index < units.length - 1) {
    value /= 1024
    index += 1
  }
  return `${value.toFixed(value >= 100 || index === 0 ? 0 : 1)} ${units[index]}`
}

/** 任务状态 → 中文说明 */
export function statusText(status: string) {
  const table: Record<string, string> = {
    queued: '排队中',
    metadata: '正在获取种子元数据',
    downloading: '下载中',
    done: '已下完',
    stopped: '已停止（达到大小上限）',
    failed: '失败',
    cancelled: '已取消',
    interrupted: '已中断',
    paused: '已暂停'
  }
  return table[status] ?? status
}
