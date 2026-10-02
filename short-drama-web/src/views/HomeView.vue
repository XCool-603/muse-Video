<template>
  <div class="home-view">
    <!-- 搜索区 -->
    <section class="hero">
      <div class="sd-container hero-inner">
        <h1 class="hero-title">聚合全网短剧，一站搜索观看</h1>
        <p class="hero-sub">
          {{ platformStore.all().length }} 个平台 · 按平台浏览（内容实时取自各平台接口），搜索仍可全网检索
        </p>

        <div class="search-box">
          <a-input-search
            v-model:value="keyword"
            size="large"
            placeholder="搜索短剧名称，如：逆袭、重生、霸总"
            enter-button="搜索"
            allow-clear
            @search="handleSearch"
          />
        </div>

        <!-- 平台优先：浏览路径上没有「全部」，一次只打一个源 -->
        <div class="hero-platforms">
          <span class="sd-muted">平台：</span>
          <div class="platform-tabs">
            <button
              v-for="p in platformStore.all()"
              :key="p.platformCode"
              class="platform-tab"
              :class="{ active: p.platformCode === platform }"
              :title="p.playNote || p.platformName"
              @click="selectPlatform(p.platformCode)"
            >
              {{ p.platformName }}
              <span class="platform-count">{{ p.dramaCount }}</span>
            </button>
          </div>
        </div>
      </div>
    </section>

    <div class="sd-container">
      <!--
        分类来自源站自己的分类表（ac=list），不再从本地库统计：
        本地库是按关键词播种出来的，新开的源本地是空的，统计出来只剩「全部」一条。
      -->
      <div v-if="sortedCategories.length" class="category-chips">
        <button class="chip" :class="{ active: typeId === '' }" @click="selectType('')">全部</button>
        <button
          v-for="c in visibleCategories"
          :key="c.typeId"
          class="chip"
          :class="{ active: typeId === c.typeId, short: isShortCategory(c) }"
          @click="selectType(c.typeId)"
        >
          {{ c.typeName }}
        </button>
        <button v-if="sortedCategories.length > CHIP_LIMIT" class="chip chip-more" @click="chipsExpanded = !chipsExpanded">
          {{ chipsExpanded ? '收起' : `更多分类 +${sortedCategories.length - CHIP_LIMIT}` }}
        </button>
      </div>

      <!-- 该平台的内容：源站目录分页 -->
      <div class="sd-section-title">
        <h2>{{ platformName }} · {{ currentTypeName }}</h2>
        <span class="sd-muted">{{ total > 0 ? `共 ${total} 部 · ` : '' }}实时取自该平台接口</span>
      </div>

      <a-spin :spinning="listLoading && list.length > 0">
        <div v-if="list.length" class="drama-grid">
          <DramaCard
            v-for="item in list"
            :key="`${item.platformCode}-${item.platformDramaId}`"
            :drama="item"
          />
        </div>
        <!-- 首次加载没有旧内容可留：铺一排占位卡片，避免整块白掉再突然弹开 -->
        <div v-else-if="listLoading" class="drama-grid">
          <CardSkeleton v-for="i in pageSize" :key="i" />
        </div>
        <a-empty v-else description="暂时取不到内容：可能是源站这个分类翻页不全，也可能是内容被过滤规则剔除，或该源本身没有内容" />
      </a-spin>

      <div v-if="hasMore" class="load-more">
        <a-button :loading="listLoading" @click="loadMore">加载更多</a-button>
      </div>

      <!-- 榜单：同样是实时打该平台 -->
      <div class="sd-section-title">
        <h2>{{ platformName }} · 榜单</h2>
      </div>

      <a-tabs v-model:activeKey="rankType" @change="loadRank">
        <a-tab-pane key="hot" tab="热播榜" />
        <a-tab-pane key="recommend" tab="推荐榜" />
        <a-tab-pane key="new" tab="新剧榜" />
      </a-tabs>

      <a-spin :spinning="rankLoading">
        <div v-if="rankList.length" class="drama-grid">
          <DramaCard v-for="item in rankList" :key="`${item.platformCode}-${item.platformDramaId}`" :drama="item" />
        </div>
        <a-empty v-else description="该平台暂无榜单数据" />
      </a-spin>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import type { PlatformCategory } from '@/api/drama'
import type { Drama } from '@/api/types'
import { usePlatformStore, usePlatformBrowseCache } from '@/stores/platform'
import DramaCard from '@/components/DramaCard.vue'
import CardSkeleton from '@/components/CardSkeleton.vue'

const router = useRouter()
const platformStore = usePlatformStore()
const browse = usePlatformBrowseCache()

/** 分类一多就是一整面墙，先显示这么多个，其余折起来 */
const CHIP_LIMIT = 14

/** 这些词出现在源站分类名里，说明是短剧相关的分类，排到前面去 */
const SHORT_DRAMA_HINTS = ['短剧', '爽剧', '逆袭', '重生', '霸总', '穿越', '甜宠', '闪婚', '漫剧']

const keyword = ref('')
/** 当前浏览的平台。始终是某一个具体平台，没有「全部」 */
const platform = ref('')
const platformName = computed(() => platformStore.nameOf(platform.value) || '该平台')

const categories = ref<PlatformCategory[]>([])
/** 当前选中的源站分类；空 = 该源的全站目录 */
const typeId = ref('')
const chipsExpanded = ref(false)

const list = ref<Drama[]>([])
const listLoading = ref(false)
const page = ref(1)
const pageSize = 18
const total = ref(0)
const hasMore = ref(false)

const rankType = ref('hot')
const rankList = ref<Drama[]>([])
const rankLoading = ref(false)

function isShortCategory(c: PlatformCategory) {
  return SHORT_DRAMA_HINTS.some((h) => c.typeName.includes(h))
}

/** 短剧相关分类排前面，其余按源站顺序 */
const sortedCategories = computed(() => {
  return [...categories.value].sort((a, b) => {
    const sa = isShortCategory(a) ? 0 : 1
    const sb = isShortCategory(b) ? 0 : 1
    if (sa !== sb) return sa - sb
    return (Number(a.typeId) || 0) - (Number(b.typeId) || 0)
  })
})

const visibleCategories = computed(() =>
  chipsExpanded.value ? sortedCategories.value : sortedCategories.value.slice(0, CHIP_LIMIT)
)

const currentTypeName = computed(() => {
  if (!typeId.value) return '全部内容'
  return categories.value.find((c) => c.typeId === typeId.value)?.typeName ?? '全部内容'
})

function handleSearch() {
  if (!keyword.value.trim()) return
  // 搜索保持全网：这是聚合站的核心能力，要缩小范围可以在搜索页自己选平台
  router.push({ path: '/search', query: { q: keyword.value.trim() } })
}

function selectPlatform(code: string) {
  if (code === platform.value) return
  platform.value = code
  platformStore.select(code)

  // 换平台后原来的分类在新区里多半不存在，直接回到「全部」
  typeId.value = ''
  chipsExpanded.value = false

  loadCategories()
  loadList(true)
  loadRank()
}

function selectType(id: string) {
  if (id === typeId.value) return
  typeId.value = id
  loadList(true)
}

async function loadCategories() {
  try {
    categories.value = await browse.loadCategories(platform.value)
  } catch {
    categories.value = []
  }
}

/**
 * 请求序号：快速连点时（切平台 A → B → C），慢的旧响应不能盖掉新请求的结果。
 * 响应回来先对号，不是当前这轮的直接丢弃。
 */
let listSeq = 0

async function loadList(reset = true) {
  const seq = ++listSeq
  listLoading.value = true
  if (reset) page.value = 1
  // 注意：reset 时不清空旧列表 —— 先留着，等新数据到了原地替换，
  // 否则切平台/切分类时网格会先塌掉再弹回来，视觉上就是一卡。

  try {
    const result = await browse.loadCatalog(
      platform.value,
      typeId.value || undefined,
      page.value,
      pageSize
    )
    if (seq !== listSeq) return // 已经有更新的请求了，这份作废

    list.value = reset ? result.items : [...list.value, ...result.items]
    total.value = result.total
    // 源站声明的 total 常常远大于它真正肯给的页数（有的源翻到第 2 页就是空的），
    // 所以「还有更多」要看这一页到底有没有拿到东西。
    hasMore.value = result.items.length > 0 && list.value.length < result.total
  } catch {
    if (seq === listSeq) hasMore.value = false
  } finally {
    if (seq === listSeq) listLoading.value = false
  }
}

/** 榜单同样有请求序号问题，且切 tab 来回点也该走缓存 */
let rankSeq = 0

async function loadRank() {
  const seq = ++rankSeq
  rankLoading.value = true
  try {
    const result = await browse.loadRank(platform.value, rankType.value, 10)
    if (seq !== rankSeq) return
    rankList.value = result
  } catch {
    if (seq === rankSeq) rankList.value = []
  } finally {
    if (seq === rankSeq) rankLoading.value = false
  }
}

function loadMore() {
  page.value += 1
  loadList(false)
}

onMounted(async () => {
  await platformStore.load()
  // 平台列表拿到之后才能落定默认平台（上次选的，或内容最多的那个）
  platform.value = platformStore.ensureSelected()

  loadCategories()
  loadList(true)
  loadRank()
})
</script>

<style scoped>
.hero {
  padding: 40px 0 28px;
  background:
    radial-gradient(1200px 320px at 50% -80px, rgba(255, 77, 109, 0.22), transparent),
    linear-gradient(180deg, #16161c, var(--sd-bg));
  border-bottom: 1px solid var(--sd-border);
  margin-bottom: 8px;
}

.hero-inner {
  text-align: center;
}

.hero-title {
  font-size: 28px;
  font-weight: 700;
  margin: 0 0 10px;
  background: linear-gradient(90deg, #fff, #ffb3c1);
  -webkit-background-clip: text;
  background-clip: text;
  color: transparent;
}

.hero-sub {
  margin: 0 0 22px;
  font-size: 13px;
  color: var(--sd-text-secondary);
}

.search-box {
  max-width: 620px;
  margin: 0 auto 16px;
}

.hero-platforms {
  display: flex;
  align-items: flex-start;
  justify-content: center;
  gap: 10px;
  flex-wrap: wrap;
}

.platform-tabs {
  display: flex;
  flex-wrap: wrap;
  justify-content: center;
  gap: 8px;
  max-width: 900px;
}

.platform-tab {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 6px 14px;
  border-radius: 999px;
  border: 1px solid var(--sd-border);
  background: var(--sd-bg-elevated);
  color: var(--sd-text-secondary);
  font-size: 13px;
  cursor: pointer;
  transition: all 0.18s;
}

.platform-tab:hover {
  color: var(--sd-text);
  border-color: #3d3d48;
}

.platform-tab.active {
  color: #fff;
  background: linear-gradient(135deg, #ff4d6d, #ff8a5b);
  border-color: transparent;
}

.platform-count {
  font-size: 11px;
  opacity: 0.7;
}

.category-chips {
  display: flex;
  gap: 10px;
  flex-wrap: wrap;
  margin-top: 22px;
}

.chip {
  padding: 6px 16px;
  border-radius: 999px;
  border: 1px solid var(--sd-border);
  background: var(--sd-bg-elevated);
  color: var(--sd-text-secondary);
  font-size: 13px;
  cursor: pointer;
  transition: all 0.18s;
}

.chip:hover {
  color: var(--sd-text);
  border-color: #3d3d48;
}

.chip.active {
  color: #fff;
  background: linear-gradient(135deg, #ff4d6d, #ff8a5b);
  border-color: transparent;
}

/* 源站分类名里带「短剧」等词的，描边highlight一下，方便一眼找到 */
.chip.short:not(.active) {
  color: #ffb3c1;
  border-color: rgba(255, 77, 109, 0.5);
}

.chip-more {
  border-style: dashed;
}

.load-more {
  display: flex;
  justify-content: center;
  margin: 28px 0 10px;
}

@media (max-width: 640px) {
  .hero-title {
    font-size: 20px;
  }

  .hero {
    padding: 26px 0 20px;
  }
}
</style>
