<template>
  <div class="category-view sd-container">
    <div class="sd-section-title">
      <h2>分类浏览</h2>
    </div>

    <div class="filter-bar">
      <div class="filter-group">
        <span class="filter-label">分类</span>
        <a-radio-group v-model:value="category" size="small" button-style="solid" @change="reload">
          <a-radio-button v-for="cat in categories" :key="cat" :value="cat">{{ cat }}</a-radio-button>
        </a-radio-group>
      </div>

      <div class="filter-group">
        <span class="filter-label">平台</span>
        <a-radio-group v-model:value="platform" size="small" button-style="solid" @change="reload">
          <a-radio-button value="all">全部</a-radio-button>
          <a-radio-button v-for="p in platformStore.withContent()" :key="p.platformCode" :value="p.platformCode">
            {{ p.platformName }}
          </a-radio-button>
        </a-radio-group>
      </div>

      <div class="filter-group">
        <span class="filter-label">排序</span>
        <a-radio-group v-model:value="sortBy" size="small" button-style="solid" @change="reload">
          <a-radio-button value="hot">最热</a-radio-button>
          <a-radio-button value="new">最新</a-radio-button>
          <a-radio-button value="rating">高分</a-radio-button>
        </a-radio-group>
      </div>
    </div>

    <a-spin :spinning="loading">
      <div v-if="list.length" class="drama-grid">
        <DramaCard v-for="item in list" :key="item.id" :drama="item" />
      </div>
      <a-empty v-else description="该分类下暂无内容" />
    </a-spin>

    <div v-if="hasMore" class="load-more">
      <a-button :loading="loading" @click="loadMore">加载更多</a-button>
    </div>
  </div>
</template>

<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { dramaApi } from '@/api/drama'
import type { Drama } from '@/api/types'
import { usePlatformStore } from '@/stores/platform'
import DramaCard from '@/components/DramaCard.vue'

const platformStore = usePlatformStore()

const categories = ref<string[]>(['全部'])
const category = ref('全部')
const platform = ref('all')
const sortBy = ref('hot')

const list = ref<Drama[]>([])
const loading = ref(false)
const page = ref(1)
const pageSize = 18
const total = ref(0)
const hasMore = ref(false)

async function load(reset = true) {
  loading.value = true
  if (reset) {
    page.value = 1
    list.value = []
  }

  try {
    const result = await dramaApi.list({
      category: category.value,
      platform: platform.value,
      sortBy: sortBy.value,
      page: page.value,
      pageSize
    })

    list.value = reset ? result.items : [...list.value, ...result.items]
    total.value = result.total
    hasMore.value = list.value.length < result.total
  } catch {
    hasMore.value = false
  } finally {
    loading.value = false
  }
}

function reload() {
  load(true)
}

function loadMore() {
  page.value += 1
  load(false)
}

onMounted(async () => {
  platformStore.load()
  try {
    categories.value = await dramaApi.categories()
  } catch {
    categories.value = ['全部']
  }
  load(true)
})
</script>

<style scoped>
.category-view {
  padding-top: 10px;
}

.filter-bar {
  display: flex;
  flex-direction: column;
  gap: 12px;
  margin-bottom: 22px;
  padding: 14px;
  background: var(--sd-bg-elevated);
  border: 1px solid var(--sd-border);
  border-radius: var(--sd-radius);
}

.filter-group {
  display: flex;
  align-items: flex-start;
  gap: 12px;
}

.filter-label {
  font-size: 13px;
  color: var(--sd-text-secondary);
  line-height: 24px;
  flex-shrink: 0;
  width: 32px;
}

.filter-group :deep(.ant-radio-group) {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.filter-group :deep(.ant-radio-button-wrapper) {
  border-radius: 6px;
  border-inline-start-width: 1px;
}

.load-more {
  display: flex;
  justify-content: center;
  margin: 28px 0 10px;
}
</style>
