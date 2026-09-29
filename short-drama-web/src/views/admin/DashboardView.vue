<template>
  <div class="dashboard-view">
    <a-spin :spinning="loading">
      <a-row :gutter="[16, 16]">
        <a-col v-for="card in statCards" :key="card.title" :xs="12" :sm="12" :md="8" :lg="4">
          <div class="stat-card">
            <div class="stat-icon" :style="{ background: card.bg, color: card.color }">
              <component :is="card.icon" />
            </div>
            <div class="stat-body">
              <span class="stat-value">{{ card.value }}</span>
              <span class="stat-title">{{ card.title }}</span>
            </div>
          </div>
        </a-col>
      </a-row>

      <a-row :gutter="[16, 16]" class="chart-row">
        <a-col :xs="24" :lg="12">
          <a-card title="分类分布" size="small" :bordered="true">
            <div v-if="data?.categoryStats?.length" class="bar-list">
              <div v-for="item in data.categoryStats" :key="item.category" class="bar-item">
                <span class="bar-label">{{ item.category }}</span>
                <div class="bar-track">
                  <div
                    class="bar-fill"
                    :style="{ width: barWidth(item.count, maxCategoryCount) }"
                  />
                </div>
                <span class="bar-value">{{ item.count }}</span>
              </div>
            </div>
            <a-empty v-else description="暂无数据" />
          </a-card>
        </a-col>

        <a-col :xs="24" :lg="12">
          <a-card title="平台内容分布" size="small">
            <div v-if="data?.platformStats?.length" class="bar-list">
              <div v-for="item in data.platformStats" :key="item.platformCode" class="bar-item">
                <span class="bar-label">{{ item.platformName }}</span>
                <div class="bar-track">
                  <div
                    class="bar-fill bar-fill-alt"
                    :style="{ width: barWidth(item.count, maxPlatformCount) }"
                  />
                </div>
                <span class="bar-value">{{ item.count }}</span>
              </div>
            </div>
            <a-empty v-else description="暂无数据" />
          </a-card>
        </a-col>
      </a-row>

      <a-card title="快捷操作" size="small" class="quick-card">
        <a-space wrap>
          <a-button type="primary" @click="router.push('/admin/dramas')">
            <template #icon><PlusOutlined /></template>
            录入短剧
          </a-button>
          <a-button @click="router.push('/admin/platforms')">
            <template #icon><SyncOutlined /></template>
            同步平台数据
          </a-button>
          <a-button @click="load">
            <template #icon><ReloadOutlined /></template>
            刷新看板
          </a-button>
        </a-space>
      </a-card>
    </a-spin>
  </div>
</template>

<script setup lang="ts">
import { computed, h, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import {
  ApiOutlined,
  EyeOutlined,
  PlusOutlined,
  ReloadOutlined,
  SyncOutlined,
  TeamOutlined,
  VideoCameraOutlined,
  PlaySquareOutlined
} from '@ant-design/icons-vue'
import { adminApi } from '@/api/admin'
import type { Dashboard } from '@/api/types'

const router = useRouter()

const data = ref<Dashboard | null>(null)
const loading = ref(false)

const maxCategoryCount = computed(() =>
  Math.max(1, ...(data.value?.categoryStats ?? []).map((c) => c.count))
)

const maxPlatformCount = computed(() =>
  Math.max(1, ...(data.value?.platformStats ?? []).map((p) => p.count))
)

const statCards = computed(() => [
  {
    title: '短剧总数',
    value: data.value?.dramaCount ?? 0,
    icon: h(VideoCameraOutlined),
    bg: 'rgba(255,77,109,0.16)',
    color: '#ff4d6d'
  },
  {
    title: '剧集总数',
    value: data.value?.episodeCount ?? 0,
    icon: h(PlaySquareOutlined),
    bg: 'rgba(250,173,20,0.16)',
    color: '#faad14'
  },
  {
    title: '用户数',
    value: data.value?.userCount ?? 0,
    icon: h(TeamOutlined),
    bg: 'rgba(19,194,194,0.16)',
    color: '#13c2c2'
  },
  {
    title: '总播放量',
    value: data.value?.totalPlayCount ?? 0,
    icon: h(EyeOutlined),
    bg: 'rgba(82,196,26,0.16)',
    color: '#52c41a'
  },
  {
    title: '接入平台',
    value: data.value?.platformCount ?? 0,
    icon: h(ApiOutlined),
    bg: 'rgba(114,46,209,0.16)',
    color: '#722ed1'
  }
])

function barWidth(value: number, max: number) {
  return `${Math.max(4, (value / max) * 100)}%`
}

async function load() {
  loading.value = true
  try {
    data.value = await adminApi.dashboard()
  } finally {
    loading.value = false
  }
}

onMounted(load)
</script>

<style scoped>
.stat-card {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 16px;
  background: var(--sd-bg-elevated);
  border: 1px solid var(--sd-border);
  border-radius: var(--sd-radius);
}

.stat-icon {
  display: grid;
  place-items: center;
  width: 40px;
  height: 40px;
  border-radius: 10px;
  font-size: 18px;
  flex-shrink: 0;
}

.stat-body {
  display: flex;
  flex-direction: column;
  min-width: 0;
}

.stat-value {
  font-size: 20px;
  font-weight: 700;
  line-height: 1.2;
  color: var(--sd-text);
}

.stat-title {
  font-size: 12px;
  color: var(--sd-text-secondary);
}

.chart-row {
  margin-top: 16px;
}

.bar-list {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.bar-item {
  display: flex;
  align-items: center;
  gap: 10px;
}

.bar-label {
  width: 76px;
  font-size: 12.5px;
  color: var(--sd-text-secondary);
  flex-shrink: 0;
  text-align: right;
}

.bar-track {
  flex: 1;
  height: 10px;
  border-radius: 5px;
  background: #26262e;
  overflow: hidden;
}

.bar-fill {
  height: 100%;
  border-radius: 5px;
  background: linear-gradient(90deg, #ff4d6d, #ff8a5b);
  transition: width 0.4s ease;
}

.bar-fill-alt {
  background: linear-gradient(90deg, #722ed1, #13c2c2);
}

.bar-value {
  width: 40px;
  font-size: 12.5px;
  color: var(--sd-text);
  text-align: right;
  flex-shrink: 0;
}

.quick-card {
  margin-top: 16px;
}
</style>
