import axios, { type AxiosInstance, type AxiosRequestConfig } from 'axios'
import { message } from 'ant-design-vue'

/** 后端统一响应封装 */
export interface ApiResponse<T> {
  code: number
  message: string
  data: T
  timestamp: number
}

export interface PagedResult<T> {
  items: T[]
  total: number
  page: number
  pageSize: number
  hasMore: boolean
}

const http: AxiosInstance = axios.create({
  baseURL: '/api/v1',
  timeout: 30000
})

http.interceptors.request.use((config) => {
  const token = localStorage.getItem('sd_token')
  if (token) {
    config.headers = config.headers ?? {}
    config.headers.Authorization = `Bearer ${token}`
  }
  return config
})

http.interceptors.response.use(
  (response) => response,
  (error) => {
    const status = error?.response?.status
    const body = error?.response?.data
    const code = body?.code

    // 4010 = 未通过访问口令门 → 整页跳到入口页（不能用路由跳转，
    // 因为此时后端会拦截所有请求，SPA 拿不到任何数据）
    if (status === 401 && code === 4010) {
      const here = window.location.pathname + window.location.search
      if (!here.startsWith('/gate')) {
        window.location.replace(`/gate?redirect=${encodeURIComponent(here)}`)
      }
      return Promise.reject(new Error(body?.message || '需要访问口令'))
    }

    if (status === 401) {
      localStorage.removeItem('sd_token')
      message.error('登录已过期，请重新登录')
    } else if (status === 429) {
      message.warning('请求过于频繁，请稍后再试')
    } else if (body?.message) {
      message.error(body.message)
    } else if (status >= 500) {
      message.error('服务器开小差了，请稍后重试')
    }

    return Promise.reject(error)
  }
)

/** 统一解包 ApiResponse.data */
export async function request<T>(config: AxiosRequestConfig): Promise<T> {
  const { data } = await http.request<ApiResponse<T>>(config)
  if (data.code !== 0) {
    throw new Error(data.message || '请求失败')
  }
  return data.data
}

export default http
