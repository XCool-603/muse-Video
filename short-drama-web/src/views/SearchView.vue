<template>
  <div class="search-view sd-container">
    <div class="search-header">
      <a-input-search
        v-model:value="keyword"
        size="large"
        placeholder="搜索短剧名称"
        enter-button="搜索"
        allow-clear
        :loading="loading"
        @search="handleSearch"
      />
    </div>

    <div class="filters">
      <a-radio-group v-model:value="platform" size="small" button-style="solid" @change="handleSearch">
        <a-radio-button value="all">全部平台</a-radio-button>
        <a-radio-button v-for="p in platforms" :key="p.platformCode" :value="p.platformCode">
          {{ p.platformName }}
        </a-radio-button>
      </a-radio-group>

      <span v-if="searched" class="sd-muted">
        共找到 <b class="highlight">{{ total }}</b> 部相关短剧（已跨平台去重合并）
      </span>
    </div>

    <a-spin :spinning="loading">
      <div v-if="results.length" class="drama-grid">
        <DramaCard v-for="item in results" :key="`${item.platformCode}-${item.platformDramaId}`" :drama="item" />
      </div>

      <a-empty v-else-if="searched" description="没有找到相关短剧，试试其他关键词" />
      <div v-else class="hint">
        <SearchOutlined class="hint-icon" />
        <p>输入关键词开始搜索，支持跨平台聚合检索</p>
        <div class="hot-words">
          <span class="sd-muted">热门搜索：</span>
          <a-tag
            v-for="word in hotWords"
            :key="word"
            color="processing"
            class="hot-tag"
            @click="quickSearch(word)"
          >
            {{ word }}
          </a-tag>
        </div>
      </div>
    </a-spin>

    <div v-if="hasMore" class="load-more">
      <a-button :loading="loading" @click="loadMore">加载更多</a-button>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { SearchOutlined } from '@ant-design/icons-vue'
import { dramaApi } from '@/api/drama'
import type { Drama } from '@/api/types'
import { usePlatformStore } from '@/stores/platform'
import DramaCard from '@/components/DramaCard.vue'

const route = useRoute()
const router = useRouter()
const platformStore = usePlatformStore()
/** 只算一次：模板里直接调 all() 会在每次重渲染时重新排序 */
const platforms = computed(() => platformStore.all())

const keyword = ref('')
const platform = ref('all')
const results = ref<Drama[]>([])
const loading = ref(false)
const searched = ref(false)
const page = ref(1)
const pageSize = 12
const total = ref(0)
const hasMore = ref(false)

const hotWords = ['逆袭', '重生', '霸总', '穿越', '种田', '首富', '战神']

async function doSearch(reset = true) {
  if (!keyword.value.trim()) {
    results.value = []
    searched.value = false
    return
  }

  loading.value = true
  searched.value = true

  if (reset) {
    page.value = 1
    results.value = []
  }

  try {
    const result = await dramaApi.search({
      q: keyword.value.trim(),
      platform: platform.value,
      page: page.value,
      pageSize
    })

    results.value = reset ? result.items : [...results.value, ...result.items]
    total.value = result.total
    hasMore.value = results.value.length < result.total
  } catch {
    hasMore.value = false
  } finally {
    loading.value = false
  }
}

function handleSearch() {
  router.replace({
    query: {
      ...(keyword.value.trim() ? { q: keyword.value.trim() } : {}),
      ...(platform.value !== 'all' ? { platform: platform.value } : {})
    }
  })
  doSearch(true)
}

function quickSearch(word: string) {
  keyword.value = word
  handleSearch()
}

function loadMore() {
  page.value += 1
  doSearch(false)
}

// 支持从首页/卡片跳转时带参
watch(
  () => route.query,
  (query) => {
    const q = (query.q as string) ?? ''
    const p = (query.platform as string) ?? 'all'

    if (q !== keyword.value || p !== platform.value) {
      keyword.value = q
      platform.value = p
      if (q) doSearch(true)
    }
  },
  { immediate: false }
)

onMounted(() => {
  platformStore.load()
  const q = (route.query.q as string) ?? ''
  const p = (route.query.platform as string) ?? 'all'
  keyword.value = q
  platform.value = p
  if (q) doSearch(true)
})
</script>

<style scoped>
.search-view {
  padding-top: 24px;
}

.search-header {
  max-width: 640px;
  margin: 0 auto 18px;
}

.filters {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 14px;
  flex-wrap: wrap;
  margin-bottom: 22px;
}

.highlight {
  color: var(--sd-primary);
  font-size: 15px;
}

.hint {
  text-align: center;
  padding: 70px 0;
  color: var(--sd-text-secondary);
}

.hint-icon {
  font-size: 46px;
  opacity: 0.35;
  margin-bottom: 14px;
}

.hot-words {
  margin-top: 18px;
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 6px;
  flex-wrap: wrap;
}

.hot-tag {
  cursor: pointer;
  margin: 0;
}

.load-more {
  display: flex;
  justify-content: center;
  margin: 28px 0 10px;
}
</style>
