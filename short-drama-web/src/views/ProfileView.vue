<template>
  <div class="profile-view sd-container">
    <div class="profile-head">
      <a-avatar :src="userStore.user?.avatarUrl" :size="64" class="avatar">
        {{ userStore.user?.username?.[0]?.toUpperCase() }}
      </a-avatar>
      <div class="head-text">
        <h2>{{ userStore.user?.username }}</h2>
        <div class="head-meta">
          <a-tag :color="userStore.isAdmin ? 'gold' : 'blue'">
            {{ userStore.isAdmin ? '管理员' : '普通用户' }}
          </a-tag>
          <span class="sd-muted">{{ userStore.user?.email || '未绑定邮箱' }}</span>
        </div>
      </div>
      <div class="head-actions">
        <a-button v-if="userStore.isAdmin" @click="router.push('/admin')">管理后台</a-button>
        <a-button danger @click="handleLogout">退出登录</a-button>
      </div>
    </div>

    <a-tabs v-model:activeKey="activeTab">
      <a-tab-pane key="favorites" tab="我的收藏">
        <a-spin :spinning="favLoading">
          <div v-if="favorites.length" class="drama-grid">
            <DramaCard v-for="item in favorites" :key="item.id" :drama="item" />
          </div>
          <a-empty v-else description="还没有收藏任何短剧" />
        </a-spin>
      </a-tab-pane>

      <a-tab-pane key="history" tab="观看历史">
        <a-spin :spinning="historyLoading">
          <div v-if="history.length" class="history-list">
            <div v-for="item in history" :key="`${item.dramaId}`" class="history-item">
              <div class="history-main">
                <span class="history-title">短剧 #{{ item.dramaId }}</span>
                <span class="sd-muted">
                  看到第 {{ item.episodeNumber }} 集 ·
                  {{ formatDuration(item.positionSeconds) }}
                </span>
              </div>
              <div class="history-actions">
                <span class="sd-muted">{{ formatTime(item.updatedAt) }}</span>
                <a-button type="link" size="small" @click="router.push(`/play/${item.dramaId}/${item.episodeNumber}`)">
                  继续观看
                </a-button>
              </div>
            </div>
          </div>
          <a-empty v-else description="暂无观看记录" />
        </a-spin>
      </a-tab-pane>
    </a-tabs>
  </div>
</template>

<script setup lang="ts">
import { onMounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { message } from 'ant-design-vue'
import { playApi } from '@/api/drama'
import { userApi } from '@/api/user'
import type { Drama, PlayProgress } from '@/api/types'
import { useUserStore } from '@/stores/user'
import DramaCard from '@/components/DramaCard.vue'

const router = useRouter()
const userStore = useUserStore()

const activeTab = ref('favorites')
const favorites = ref<Drama[]>([])
const history = ref<PlayProgress[]>([])
const favLoading = ref(false)
const historyLoading = ref(false)

function formatDuration(seconds: number) {
  const m = Math.floor(seconds / 60)
  const s = Math.floor(seconds % 60)
  return `${m}分${String(s).padStart(2, '0')}秒`
}

function formatTime(value: string) {
  const date = new Date(value)
  return date.toLocaleString('zh-CN', { hour12: false })
}

async function loadFavorites() {
  favLoading.value = true
  try {
    favorites.value = await userApi.favorites()
  } catch {
    favorites.value = []
  } finally {
    favLoading.value = false
  }
}

async function loadHistory() {
  historyLoading.value = true
  try {
    history.value = await playApi.history(20)
  } catch {
    history.value = []
  } finally {
    historyLoading.value = false
  }
}

function handleLogout() {
  userStore.logout()
  message.success('已退出登录')
  router.push('/')
}

watch(activeTab, (tab) => {
  if (tab === 'favorites') loadFavorites()
  if (tab === 'history') loadHistory()
})

onMounted(() => {
  loadFavorites()
  loadHistory()
})
</script>

<style scoped>
.profile-view {
  padding-top: 24px;
}

.profile-head {
  display: flex;
  align-items: center;
  gap: 16px;
  padding: 18px;
  background: var(--sd-bg-elevated);
  border: 1px solid var(--sd-border);
  border-radius: var(--sd-radius);
  margin-bottom: 18px;
}

.avatar {
  background: var(--sd-primary);
  flex-shrink: 0;
}

.head-text {
  flex: 1;
  min-width: 0;
}

.head-text h2 {
  margin: 0 0 6px;
  font-size: 18px;
  color: var(--sd-text);
}

.head-meta {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
}

.head-actions {
  display: flex;
  gap: 8px;
  flex-shrink: 0;
}

.history-list {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.history-item {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  padding: 12px 14px;
  background: var(--sd-bg-elevated);
  border: 1px solid var(--sd-border);
  border-radius: var(--sd-radius);
}

.history-main {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.history-title {
  font-size: 14px;
  color: var(--sd-text);
}

.history-actions {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-shrink: 0;
}

@media (max-width: 640px) {
  .profile-head {
    flex-wrap: wrap;
  }

  .head-actions {
    width: 100%;
  }
}
</style>
