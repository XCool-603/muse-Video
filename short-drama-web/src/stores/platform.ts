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

/** 记住上次浏览的平台，刷新/重进后还停在那儿 */
const STORAGE_KEY = 'sd.browse.platform'

/** 平台列表（含内容量与可播性），全局缓存一次 */
export const usePlatformStore = defineStore('platform', () => {
  const platforms = ref<PlatformInfo[]>([])
  const loaded = ref(false)
  const loading = ref(false)

  /** 当前浏览的平台。浏览路径上没有「全部」，所以它始终是一个具体平台 */
  const selected = ref<string>(readStored())

  function readStored(): string {
    try {
      return localStorage.getItem(STORAGE_KEY) ?? ''
    } catch {
      // 隐私模式 / 禁用存储：读不到就当没选过
      return ''
    }
  }

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

  /** 切平台：同时记住，刷新后还停在这个平台 */
  function select(code: string) {
    selected.value = code
    try {
      localStorage.setItem(STORAGE_KEY, code)
    } catch {
      // 写不了就算了，不影响本次浏览
    }
  }

  /**
   * 落定一个平台。浏览路径上不再有「全部」，所以必须有确定的一个：
   * 优先沿用上次选的（前提是它现在还有内容），否则选剧数最多的那个。
   * 平台列表是异步加载的，所以要在 load() 之后再调。
   */
  function ensureSelected() {
    const list = withContent()
    if (!list.length) return selected.value
    if (selected.value && list.some((p) => p.platformCode === selected.value)) return selected.value

    const best = [...list].sort((a, b) => b.dramaCount - a.dramaCount)[0]
    select(best.platformCode)
    return selected.value
  }

  function nameOf(code: string) {
    return platforms.value.find((p) => p.platformCode === code)?.platformName ?? code
  }

  function colorOf(code: string) {
    return platforms.value.find((p) => p.platformCode === code)?.color ?? '#8c8c8c'
  }

  return { platforms, loaded, loading, selected, load, withContent, select, ensureSelected, nameOf, colorOf }
})
