<template>
  <div class="home-view">
    <!-- 搜索区 -->
    <section class="hero">
      <div class="sd-container hero-inner">
        <h1 class="hero-title">聚合全网短剧，一站搜索观看</h1>
        <p class="hero-sub">红果 · 黄豆 · 剧果 · 野果 · 帝果 —— 多平台内容统一检索与播放</p>

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

        <div class="hero-platforms">
          <span class="sd-muted">平台筛选：</span>
          <a-radio-group v-model:value="platform" size="small" button-style="solid">
            <a-radio-button value="all">全部</a-radio-button>
            <a-radio-button v-for="p in platformStore.withContent()" :key="p.platformCode" :value="p.platformCode">
              {{ p.platformName }}
            </a-radio-button>
          </a-radio-group>
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

      <!-- 榜单 -->
      <div class="sd-section-title">
        <h2>短剧榜单</h2>
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
        <a-empty v-else description="暂无榜单数据" />
      </a-spin>

      <!-- 今日上新 -->
      <div class="sd-section-title">
        <h2>今日上新</h2>
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
        <a-empty v-else description="暂无上新内容" />
      </a-spin>

      <!-- 本地精选 -->
      <div class="sd-section-title">
        <h2>{{ activeCategory === '全部' ? '精选推荐' : `${activeCategory} · 精选` }}</h2>
      </div>

      <a-spin :spinning="listLoading">
        <div v-if="dramaList.length" class="drama-grid">
          <DramaCard v-for="item in dramaList" :key="item.id" :drama="item" />
        </div>
        <a-empty v-else description="暂无内容" />
      </a-spin>

      <div v-if="hasMore" class="load-more">
        <a-button :loading="listLoading" @click="loadMore">加载更多</a-button>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { dramaApi } from '@/api/drama'
import type { Drama } from '@/api/types'
import { usePlatformStore } from '@/stores/platform'
import DramaCard from '@/components/DramaCard.vue'

const router = useRouter()
const platformStore = usePlatformStore()

const keyword = ref('')
const platform = ref('all')
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
  router.push({ path: '/search', query: { q: keyword.value.trim(), platform: platform.value } })
}

async function loadCategories() {
  try {
    categories.value = await dramaApi.categories()
  } catch {
    categories.value = ['全部']
  }
}

async function loadRank() {
  rankLoading.value = true
  try {
    rankList.value = await dramaApi.rank(rankType.value, 10)
  } catch {
    rankList.value = []
  } finally {
    rankLoading.value = false
  }
}

async function loadLatest() {
  latestLoading.value = true
  try {
    latestList.value = await dramaApi.latest(activeCategory.value, 10)
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
  platformStore.load()
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
  align-items: center;
  justify-content: center;
  gap: 10px;
  flex-wrap: wrap;
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
