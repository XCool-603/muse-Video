import { request } from './request'

export const accessApi = {
  /** 校验访问口令，通过后后端会签发 HttpOnly Cookie */
  verify(password: string) {
    return request<boolean>({ url: '/access', method: 'post', data: { password } })
  },

  /** 查询当前是否已通过口令门 */
  status() {
    return request<boolean>({ url: '/access/status', method: 'get' })
  },

  /** 清除口令 Cookie */
  logout() {
    return request<boolean>({ url: '/access/logout', method: 'post' })
  }
}
