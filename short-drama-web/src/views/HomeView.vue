<template>
  <div class="home-view">
    <!-- 搜索区 -->
    <section class="hero">
      <div class="sd-container hero-inner">
        <h1 class="hero-title">聚合全网短剧，一站搜索观看</h1>
        <p class="hero-sub">
          {{ platformStore.withContent().length }} 个采集源 · 按平台浏览（一次只查一个源），搜索仍可全网检索
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

        <!--
          平台优先：浏览路径上不再有「全部」。
          「全部」意味着并行打十几个源、等最慢的那个，这正是之前首页卡的原因；
          选定一个平台后，下面的榜单/上新/列表都只查这一个源。
        -->
        <div class="hero-platforms">
          <span class="sd-muted">平台：</span>
          <div class="platform-tabs">
            <button
              v-for="p in platformStore.withContent()"
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
      <!-- 分类快捷入口 -->
      <div class="category-chips">
        <button
          v-for="cat in categories"
          :key="cat"
          class="chip"
          :class="{ active: activeCategory === cat }"
          @click="selectCategory(cat)"
        >
          {{ cat }}
        </button>
      </div>

      <!--
        本地库放最前面：它只查本地（实测 16ms），切平台/翻页都是瞬时的。
        榜单和上新要打源站（单平台 0.5~2 秒），放后面让它们各自转圈，
        不要挡住首屏 —— 之前首屏是两个全平台聚合区块，用户要等 3 秒才看到东西。
      -->
      <div class="sd-section-title">
        <h2>{{ platformName }} · {{ activeCategory === '全部' ? '全部内容' : activeCategory }}</h2>
        <span class="sd-muted">{{ total }} 部</span>
      </div>

      <a-spin :spinning="listLoading">
        <div v-if="dramaList.length" class="drama-grid">
          <DramaCard v-for="item in dramaList" :key="item.id" :drama="item" />
        </div>
        <a-empty v-else description="该平台暂无内容" />
      </a-spin>

      <div v-if="hasMore" class="load-more">
        <a-button :loading="listLoading" @click="loadMore">加载更多</a-button>
      </div>

      <!-- 榜单 -->
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

      <!-- 今日上新 -->
      <div class="sd-section-title">
        <h2>{{ platformName }} · 今日上新</h2>
        <a-button type="link" @click="router.push('/category')">查看更多</a-button>
      </div>

      <a-spin :spinning="latestLoading">
        <div v-if="latestList.length" class="drama-grid">
          <DramaCard
            v-for="item in latestList"
            :key="`latest-${item.platformCode}-${item.platformDramaId}`"
            :drama="item"
          />
        </div>
        <a-empty v-else description="该平台暂无上新内容" />
      </a-spin>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { dramaApi } from '@/api/drama'
import type { Drama } from '@/api/types'
import { usePlatformStore } from '@/stores/platform'
import DramaCard from '@/components/DramaCard.vue'

const router = useRouter()
const platformStore = usePlatformStore()

const keyword = ref('')
/** 当前浏览的平台。始终是某一个具体平台，没有「全部」 */
const platform = ref('')
const platformName = computed(() => platformStore.nameOf(platform.value) || '该平台')
const activeCategory = ref('全部')
const rankType = ref('hot')

const categories = ref<string[]>(['全部'])

const rankList = ref<Drama[]>([])
const latestList = ref<Drama[]>([])
const dramaList = ref<Drama[]>([])
const rankLoading = ref(false)
const latestLoading = ref(false)
const listLoading = ref(false)

const page = ref(1)
const pageSize = 12
const total = ref(0)
const hasMore = ref(false)

function handleSearch() {
  if (!keyword.value.trim()) return
  // 搜索保持全网：这是聚合站的核心能力，要缩小范围可以在搜索页自己选平台
  router.push({ path: '/search', query: { q: keyword.value.trim() } })
}

function selectPlatform(code: string) {
  if (code === platform.value) return
  platform.value = code
  platformStore.select(code)

  // 分类是各采集源自己的字段：换平台后原来的分类可能在新平台里根本不存在，
  // 所以先按新平台重取分类（选中的那个不在其中就退回「全部」），再刷新各区块
  loadCategories().then(() => {
    loadRank()
    loadLatest()
    loadList(true)
  })
}

/**
 * 按当前平台取分类。分类列表必须跟着平台走 ——
 * 全平台去重能出上百个分类，而其中绝大多数在某个具体源里是空的，
 * 用户点下去只会看到「暂无内容」。
 * 换平台后原来选中的分类可能在新平台里不存在，就退回「全部」。
 */
async function loadCategories() {
  try {
    categories.value = await dramaApi.categories(platform.value)
  } catch {
    categories.value = ['全部']
  }

  if (!categories.value.includes(activeCategory.value)) {
    activeCategory.value = '全部'
  }
}

async function loadRank() {
  rankLoading.value = true
  try {
    rankList.value = await dramaApi.rank(rankType.value, 10, platform.value)
  } catch {
    rankList.value = []
  } finally {
    rankLoading.value = false
  }
}

async function loadLatest() {
  latestLoading.value = true
  try {
    latestList.value = await dramaApi.latest(activeCategory.value, 10, platform.value)
  } catch {
    latestList.value = []
  } finally {
    latestLoading.value = false
  }
}

async function loadList(reset = true) {
  listLoading.value = true
  if (reset) {
    page.value = 1
    dramaList.value = []
  }

  try {
    const result = await dramaApi.list({
      category: activeCategory.value,
      platform: platform.value,
      sortBy: 'hot',
      page: page.value,
      pageSize
    })

    dramaList.value = reset ? result.items : [...dramaList.value, ...result.items]
    total.value = result.total
    hasMore.value = dramaList.value.length < result.total
  } catch {
    hasMore.value = false
  } finally {
    listLoading.value = false
  }
}

function loadMore() {
  page.value += 1
  loadList(false)
}

function selectCategory(cat: string) {
  activeCategory.value = cat
  loadLatest()
  loadList(true)
}

onMounted(async () => {
  await platformStore.load()
  // 平台列表拿到之后才能落定默认平台（上次选的，或剧数最多的那个）
  platform.value = platformStore.ensureSelected()

  await loadCategories()
  loadRank()
  loadLatest()
  loadList(true)
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
  max-width: 860px;
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
