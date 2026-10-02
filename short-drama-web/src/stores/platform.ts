import { defineStore } from 'pinia'
import { ref } from 'vue'
import { request } from '@/api/request'
import type { PlatformCategory } from '@/api/drama'
import type { Drama } from '@/api/types'

export interface PlatformInfo {
  platformCode: string
  platformName: string
  dramaCount: number
  playable: boolean
  playNote: string | null
  color: string
}

/**
 * 排序：能播的最先（用户点开就能看），其次按内容量。
 * playable=false 的源点进去大概率 502，压到最后并标灰。
 */
function sortPlatforms(list: PlatformInfo[]) {
  return [...list].sort((a, b) => {
    if (a.playable !== b.playable) return a.playable ? -1 : 1
    return b.dramaCount - a.dramaCount
  })
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

  /**
   * 全部已注册平台，能播的在前、按内容量排序。
   * 刻意不按「本地库有没有内容」过滤：刚打开的源本地库还是空的，
   * 但它的实时榜单/上新是可用的，过滤掉就等于平台凭空消失。
   */
  function all() {
    return sortPlatforms(platforms.value)
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
    const list = all()
    if (!list.length) return selected.value
    if (selected.value && list.some((p) => p.platformCode === selected.value)) return selected.value

    // 默认落点：第一个「可播」的平台（排序已保证能播的在前）；
    // 没有任何平台标记为可播时，all() 的第一个就是内容最多的
    const best = list.find((p) => p.playable && p.dramaCount > 0) ?? list[0]
    select(best.platformCode)
    return selected.value
  }

  function nameOf(code: string) {
    return platforms.value.find((p) => p.platformCode === code)?.platformName ?? code
  }

  function colorOf(code: string) {
    return platforms.value.find((p) => p.platformCode === code)?.color ?? '#8c8c8c'
  }

  return { platforms, loaded, loading, selected, load, all, select, ensureSelected, nameOf, colorOf }
})

/**
 * 会话内缓存：切平台/切分类来回点时不再重复打接口。
 *
 * 为什么要这层：按平台浏览的每个区块都是「实时打源站」（1~4 秒），服务端虽然有缓存，
 * 但浏览器这边每次切换还是得等一个来回。会话内存住结果后，第二次点击是瞬时的；
 * 刷新页面即清空，不会像 localStorage 那样把旧内容留几天。
 *
 * 另外存每个请求的 Promise：快速连点时，同一个 key 的两个并发请求只会真正发一次，
 * 两个调用方共享同一个结果 —— 不会出现「慢的旧响应盖掉新的」。
 */
const categoryCache = new Map<string, Promise<PlatformCategory[]>>()
const catalogCache = new Map<string, Promise<{ items: Drama[]; total: number }>>()
const rankCache = new Map<string, Promise<Drama[]>>()

/** 缓存上限，防长会话内存膨胀；超出就整表清掉（数据都是可以重新拿的） */
const CACHE_LIMIT = 120

function remember<T>(store: Map<string, Promise<T>>, key: string, make: () => Promise<T>): Promise<T> {
  const hit = store.get(key)
  if (hit) return hit
  const p = make().finally(() => {
    // 失败的结果不留在缓存里，下次还能重试
    if (store.get(key) === p) {
      p.catch(() => store.delete(key))
    }
  })
  if (store.size >= CACHE_LIMIT) store.clear()
  store.set(key, p)
  return p
}

export function usePlatformBrowseCache() {
  function loadCategories(platformCode: string) {
    return remember(categoryCache, platformCode, () =>
      request<PlatformCategory[]>({ url: `/drama/live/${platformCode}/categories`, method: 'get' })
    )
  }

  function loadCatalog(platformCode: string, typeId: string | undefined, page: number, pageSize: number) {
    const key = `${platformCode}|${typeId ?? ''}|${page}|${pageSize}`
    return remember(catalogCache, key, () =>
      request<{ items: Drama[]; total: number }>({
        url: `/drama/live/${platformCode}/catalog`,
        method: 'get',
        params: { typeId: typeId || undefined, page, pageSize }
      })
    )
  }

  function loadRank(platformCode: string, type: string, limit: number) {
    const key = `${platformCode}|${type}|${limit}`
    return remember(rankCache, key, () =>
      request<Drama[]>({ url: '/drama/rank', method: 'get', params: { type, limit, platform: platformCode } })
    )
  }

  return { loadCategories, loadCatalog, loadRank }
}
