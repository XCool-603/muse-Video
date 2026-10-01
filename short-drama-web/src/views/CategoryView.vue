<template>
  <div class="category-view sd-container">
    <div class="sd-section-title">
      <h2>分类浏览</h2>
      <span class="sd-muted">内容实时取自该平台接口</span>
    </div>

    <div class="filter-bar">
      <div class="filter-group">
        <span class="filter-label">平台</span>
        <a-radio-group v-model:value="platform" size="small" button-style="solid" @change="onPlatformChange">
          <a-radio-button v-for="p in platformStore.all()" :key="p.platformCode" :value="p.platformCode">
            {{ p.platformName }}
          </a-radio-button>
        </a-radio-group>
      </div>

      <!--
        分类来自源站自己的分类表（ac=list）。本地库统计出来的那套只覆盖「已入库的部分」，
        新开的源本地是空的，统计出来只剩「全部」一条。
      -->
      <div v-if="categories.length" class="filter-group">
        <span class="filter-label">分类</span>
        <a-radio-group v-model:value="typeId" size="small" button-style="solid" @change="load(true)">
          <a-radio-button value="">全部</a-radio-button>
          <a-radio-button v-for="c in categories" :key="c.typeId" :value="c.typeId">
            {{ c.typeName }}
          </a-radio-button>
        </a-radio-group>
      </div>
    </div>

    <a-spin :spinning="loading">
      <div v-if="list.length" class="drama-grid">
        <DramaCard
          v-for="item in list"
          :key="`${item.platformCode}-${item.platformDramaId}`"
          :drama="item"
        />
      </div>
      <a-empty v-else description="暂时取不到内容：可能是源站这个分类翻页不全，也可能是内容被过滤规则剔除，或该源本身没有内容" />
    </a-spin>

    <div v-if="hasMore" class="load-more">
      <a-button :loading="loading" @click="loadMore">加载更多</a-button>
    </div>
  </div>
</template>

<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { dramaApi, type PlatformCategory } from '@/api/drama'
import type { Drama } from '@/api/types'
import { usePlatformStore } from '@/stores/platform'
import DramaCard from '@/components/DramaCard.vue'

const platformStore = usePlatformStore()

/** 当前平台。没有「全部」，始终是一个具体平台 */
const platform = ref('')
/** 源站分类；空 = 该源全站目录 */
const typeId = ref('')
const categories = ref<PlatformCategory[]>([])

const list = ref<Drama[]>([])
const loading = ref(false)
const page = ref(1)
const pageSize = 18
const total = ref(0)
const hasMore = ref(false)

async function loadCategories() {
  try {
    categories.value = await dramaApi.liveCategories(platform.value)
  } catch {
    categories.value = []
  }
}

async function load(reset = true) {
  loading.value = true
  if (reset) {
    page.value = 1
    list.value = []
  }

  try {
    const result = await dramaApi.liveCatalog({
      platform: platform.value,
      typeId: typeId.value || undefined,
      page: page.value,
      pageSize
    })

    list.value = reset ? result.items : [...list.value, ...result.items]
    total.value = result.total
    // 有的源翻到第 2 页就是空的，所以「还有更多」要看这一页实际拿到了什么
    hasMore.value = result.items.length > 0 && list.value.length < result.total
  } catch {
    hasMore.value = false
  } finally {
    loading.value = false
  }
}

function loadMore() {
  page.value += 1
  load(false)
}

async function onPlatformChange() {
  // 切平台同时记住，回首页也停在这个平台
  platformStore.select(platform.value)
  typeId.value = ''
  await loadCategories()
  load(true)
}

onMounted(async () => {
  await platformStore.load()
  platform.value = platformStore.ensureSelected()

  await loadCategories()
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
