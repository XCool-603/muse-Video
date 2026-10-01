import { request, type PagedResult } from './request'
import type { Drama, DramaDetail, Episode, PlayInfo, PlayProgress } from './types'

export const dramaApi = {
  /** 聚合搜索：跨平台并行 + 去重合并 */
  search(params: { q: string; page?: number; pageSize?: number; platform?: string }) {
    return request<PagedResult<Drama>>({ url: '/drama/search', method: 'get', params })
  },

  /**
   * 短剧列表。
   * live=true 时后端会把分类/关键词交给聚合搜索，并行打各平台接口，
   * 返回「实时结果 + 本地库」的合集；不传则只查本地库。
   */
  list(params: {
    keyword?: string
    category?: string
    platform?: string
    sortBy?: string
    page?: number
    pageSize?: number
    live?: boolean
  }) {
    return request<PagedResult<Drama>>({ url: '/drama/list', method: 'get', params })
  },

  detail(id: number) {
    return request<DramaDetail>({ url: `/drama/${id}`, method: 'get' })
  },

  episodes(id: number) {
    return request<Episode[]>({ url: `/drama/${id}/episodes`, method: 'get' })
  },

  /**
   * 榜单。传 platform 只查那一个平台（一个源一次请求）；
   * 不传才是全平台聚合 —— 全平台要并行打十几个源，是首页卡顿的来源。
   */
  rank(type = 'hot', limit = 20, platform?: string) {
    return request<Drama[]>({ url: '/drama/rank', method: 'get', params: { type, limit, platform } })
  },

  /** 今日上新。platform 语义同 rank */
  latest(category = '全部', limit = 20, platform?: string) {
    return request<Drama[]>({ url: '/drama/latest', method: 'get', params: { category, limit, platform } })
  },

  categories() {
    return request<string[]>({ url: '/drama/categories', method: 'get' })
  },

  /** 把平台原始剧集导入本地库（管理员） */
  sync(platform: string, dramaId: string) {
    return request<number>({ url: '/drama/sync', method: 'post', params: { platform, dramaId } })
  },

  /**
   * 按需入库：聚合搜索返回的实时结果还没落库（id=0），
   * 点开时调这个把它落到本地库并拿到本地 Id，之后就能进播放页。
   * 已在库中时后端直接返回本地 Id，不会再打上游。
   */
  resolve(platform: string, dramaId: string) {
    return request<number>({ url: '/drama/resolve', method: 'post', params: { platform, dramaId } })
  }
}

export const playApi = {
  playInfo(dramaId: number, episode: number) {
    return request<PlayInfo>({ url: `/play/${dramaId}/${episode}`, method: 'get' })
  },

  reportProgress(payload: { dramaId: number; episode: number; position: number }) {
    return request<boolean>({ url: '/play/progress', method: 'post', data: payload })
  },

  getProgress(dramaId: number) {
    return request<PlayProgress | null>({ url: `/play/progress/${dramaId}`, method: 'get' })
  },

  history(limit = 20) {
    return request<PlayProgress[]>({ url: '/play/history', method: 'get', params: { limit } })
  }
}
