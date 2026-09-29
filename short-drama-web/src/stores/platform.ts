import { defineStore } from 'pinia'
import { ref } from 'vue'
import { request } from '@/api/request'

export interface PlatformInfo {
  platformCode: string
  platformName: string
  dramaCount: number
  playable: boolean
  playNote: string | null
  color: string
}

/** 平台列表（含内容量与可播性），全局缓存一次 */
export const usePlatformStore = defineStore('platform', () => {
  const platforms = ref<PlatformInfo[]>([])
  const loaded = ref(false)
  const loading = ref(false)

  async function load(force = false) {
    if (loaded.value && !force) return platforms.value
    if (loading.value) return platforms.value

    loading.value = true
    try {
      platforms.value = await request<PlatformInfo[]>({ url: '/drama/platforms', method: 'get' })
      loaded.value = true
    } catch {
      platforms.value = []
    } finally {
      loading.value = false
    }

    return platforms.value
  }

  /** 只保留有内容的平台，用于筛选器 */
  function withContent() {
    return platforms.value.filter((p) => p.dramaCount > 0)
  }

  function nameOf(code: string) {
    return platforms.value.find((p) => p.platformCode === code)?.platformName ?? code
  }

  function colorOf(code: string) {
    return platforms.value.find((p) => p.platformCode === code)?.color ?? '#8c8c8c'
  }

  return { platforms, loaded, loading, load, withContent, nameOf, colorOf }
})
