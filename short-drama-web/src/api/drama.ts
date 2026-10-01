import { request, type PagedResult } from './request'
import type { Drama, DramaDetail, Episode, PlayInfo, PlayProgress } from './types'

/** 源站自己的分类（苹果CMS 的 class：type_id / type_name） */
export interface PlatformCategory {
  typeId: string
  typeName: string
}

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

  /**
   * 分类列表。传 platform 只返回该平台真的有的分类（按内容量排序）——
   * 分类是各采集源自己的字段，不跟平台走的话，选出来的分类点进去大多是空的。
   */
  categories(platform?: string) {
    return request<string[]>({ url: '/drama/categories', method: 'get', params: { platform } })
  },

  /**
   * 该平台自己的分类表，直接读源站接口（苹果CMS 的 ac=list）。
   * 与 categories() 的区别：那个是「本地库统计出来的」，新开的源本地是空的；
   * 这个是源站真实分类，任何已启用的源都能拿到。
   */
  liveCategories(platform: string) {
    return request<PlatformCategory[]>({ url: `/drama/live/${platform}/categories`, method: 'get' })
  },

  /**
   * 该平台的目录分页，直接读源站接口（ac=detail[&t=分类]&pg=页码）。
   * typeId 不传 = 该源的全站目录（最新在前）。
   */
  liveCatalog(params: { platform: string; typeId?: string; page?: number; pageSize?: number }) {
    const { platform, ...rest } = params
    return request<PagedResult<Drama>>({ url: `/drama/live/${platform}/catalog`, method: 'get', params: rest })
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
