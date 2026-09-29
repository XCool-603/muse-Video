import { request } from './request'
import type { Drama } from './types'

export interface UserInfo {
  id: number
  username: string
  email: string
  phone: string
  avatarUrl: string
  role: string
}

export interface LoginResponse {
  token: string
  user: UserInfo
  expiresIn: number
}

export const userApi = {
  register(payload: { username: string; password: string; email?: string; phone?: string }) {
    return request<LoginResponse>({ url: '/user/register', method: 'post', data: payload })
  },

  login(payload: { username: string; password: string }) {
    return request<LoginResponse>({ url: '/user/login', method: 'post', data: payload })
  },

  profile() {
    return request<UserInfo>({ url: '/user/profile', method: 'get' })
  },

  favorites() {
    return request<Drama[]>({ url: '/user/favorites', method: 'get' })
  },

  addFavorite(dramaId: number) {
    return request<boolean>({ url: `/user/favorites/${dramaId}`, method: 'post' })
  },

  removeFavorite(dramaId: number) {
    return request<boolean>({ url: `/user/favorites/${dramaId}`, method: 'delete' })
  },

  favoriteStatus(dramaId: number) {
    return request<boolean>({ url: `/user/favorites/${dramaId}/status`, method: 'get' })
  }
}
