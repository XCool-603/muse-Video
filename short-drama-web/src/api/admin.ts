import { request, type PagedResult } from './request'
import type { Dashboard, Drama, PlatformSource } from './types'

export interface DramaSaveRequest {
  title: string
  description: string
  coverUrl: string
  category: string
  status: string
  platformCode: string
  platformDramaId: string
  rating: number
}

export const adminApi = {
  dashboard() {
    return request<Dashboard>({ url: '/admin/dashboard', method: 'get' })
  },

  dramas(params: {
    keyword?: string
    category?: string
    platform?: string
    page?: number
    pageSize?: number
  }) {
    return request<PagedResult<Drama>>({ url: '/admin/dramas', method: 'get', params })
  },

  createDrama(payload: DramaSaveRequest) {
    return request<number>({ url: '/admin/dramas', method: 'post', data: payload })
  },

  updateDrama(id: number, payload: DramaSaveRequest) {
    return request<boolean>({ url: `/admin/dramas/${id}`, method: 'put', data: payload })
  },

  deleteDrama(id: number) {
    return request<boolean>({ url: `/admin/dramas/${id}`, method: 'delete' })
  },

  updateEpisode(
    dramaId: number,
    episode: number,
    payload: { isFree: boolean; isLocked: boolean; videoUrl?: string }
  ) {
    return request<boolean>({
      url: `/admin/dramas/${dramaId}/episodes/${episode}`,
      method: 'put',
      data: payload
    })
  },

  platforms() {
    return request<PlatformSource[]>({ url: '/admin/platforms', method: 'get' })
  },

  togglePlatform(id: number, enabled: boolean) {
    return request<boolean>({
      url: `/admin/platforms/${id}/toggle`,
      method: 'put',
      params: { enabled }
    })
  },

  syncPlatform(platformCode: string) {
    return request<number>({ url: `/admin/platforms/${platformCode}/sync`, method: 'post' })
  }
}
