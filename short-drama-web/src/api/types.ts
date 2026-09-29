export interface Drama {
  id: number
  title: string
  description: string
  coverUrl: string
  category: string
  totalEpisodes: number
  status: string
  playCount: number
  rating: number
  platformCode: string
  platformName: string
  platformDramaId: string
  sources: string[]
  updatedAt: string
}

export interface Episode {
  id: number
  episodeNumber: number
  title: string
  coverUrl: string
  durationSeconds: number
  isFree: boolean
  isLocked: boolean
}

export interface DramaDetail extends Drama {
  episodes: Episode[]
  recommends: Drama[]
}

export interface PlayInfo {
  dramaId: number
  dramaTitle: string
  episodeNumber: number
  playUrl: string
  platformCode: string
  platformName: string
  durationSeconds: number
  adFree: boolean
  resumePosition: number
  skippedAdSegments: string[]
  /** hls = m3u8（走 hls.js + 后端去广告代理）；mp4 = 明文文件（原生播放） */
  streamType: 'hls' | 'mp4'
  /** 该集是否只能跳官方页面观看（如 DRM 加密） */
  requiresExternalPlayer: boolean
}

export interface PlayProgress {
  dramaId: number
  episodeNumber: number
  positionSeconds: number
  updatedAt: string
}

export interface PlatformSource {
  id: number
  platformCode: string
  platformName: string
  baseUrl: string
  adapterType: string
  isEnabled: boolean
  lastSyncAt: string | null
}

export interface Dashboard {
  dramaCount: number
  episodeCount: number
  userCount: number
  totalPlayCount: number
  platformCount: number
  categoryStats: { category: string; count: number }[]
  platformStats: { platformCode: string; platformName: string; count: number }[]
}
