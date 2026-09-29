<template>
  <div class="admin-layout">
    <aside class="admin-sider">
      <div class="sider-brand">
        <span class="logo-mark">剧</span>
        <span>管理后台</span>
      </div>

      <a-menu v-model:selectedKeys="selectedKeys" mode="inline" class="admin-menu" @click="onMenuClick">
        <a-menu-item key="dashboard">
          <template #icon><DashboardOutlined /></template>
          数据看板
        </a-menu-item>
        <a-menu-item key="dramas">
          <template #icon><VideoCameraOutlined /></template>
          短剧管理
        </a-menu-item>
        <a-menu-item key="platforms">
          <template #icon><ApiOutlined /></template>
          平台源管理
        </a-menu-item>
      </a-menu>

      <div class="sider-footer">
        <a-button type="text" block @click="router.push('/')">
          <template #icon><HomeOutlined /></template>
          返回前台
        </a-button>
      </div>
    </aside>

    <section class="admin-body">
      <header class="admin-header">
        <h1>{{ pageTitle }}</h1>
        <div class="header-right">
          <span class="sd-muted">{{ userStore.user?.username }}</span>
          <a-avatar :src="userStore.user?.avatarUrl" :size="30">
            {{ userStore.user?.username?.[0]?.toUpperCase() }}
          </a-avatar>
        </div>
      </header>

      <div class="admin-content">
        <RouterView />
      </div>
    </section>
  </div>
</template>

<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { RouterView, useRoute, useRouter } from 'vue-router'
import {
  ApiOutlined,
  DashboardOutlined,
  HomeOutlined,
  VideoCameraOutlined
} from '@ant-design/icons-vue'
import { useUserStore } from '@/stores/user'

const route = useRoute()
const router = useRouter()
const userStore = useUserStore()

const selectedKeys = ref<string[]>(['dashboard'])

const pageTitle = computed(() => (route.meta.title as string) || '管理后台')

const KEY_MAP: Record<string, string> = {
  'admin-dashboard': 'dashboard',
  'admin-dramas': 'dramas',
  'admin-platforms': 'platforms'
}

watch(
  () => route.name,
  (name) => {
    const key = KEY_MAP[name as string]
    if (key) selectedKeys.value = [key]
  },
  { immediate: true }
)

function onMenuClick({ key }: { key: string | number }) {
  const paths: Record<string, string> = {
    dashboard: '/admin',
    dramas: '/admin/dramas',
    platforms: '/admin/platforms'
  }
  router.push(paths[String(key)])
}
</script>

<style scoped>
.admin-layout {
  display: flex;
  min-height: calc(100vh - 56px);
}

.admin-sider {
  width: 210px;
  flex-shrink: 0;
  display: flex;
  flex-direction: column;
  background: var(--sd-bg-elevated);
  border-right: 1px solid var(--sd-border);
}

.sider-brand {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 16px;
  font-size: 15px;
  font-weight: 600;
  border-bottom: 1px solid var(--sd-border);
}

.logo-mark {
  display: grid;
  place-items: center;
  width: 26px;
  height: 26px;
  border-radius: 8px;
  background: linear-gradient(135deg, #ff4d6d, #ff8a5b);
  color: #fff;
  font-weight: 700;
  font-size: 14px;
}

.admin-menu {
  flex: 1;
  background: transparent;
  border-inline-end: none !important;
}

.sider-footer {
  padding: 12px;
  border-top: 1px solid var(--sd-border);
}

.admin-body {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
}

.admin-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 16px 22px;
  border-bottom: 1px solid var(--sd-border);
}

.admin-header h1 {
  margin: 0;
  font-size: 18px;
  font-weight: 600;
}

.header-right {
  display: flex;
  align-items: center;
  gap: 10px;
}

.admin-content {
  flex: 1;
  padding: 22px;
  overflow-x: auto;
}

@media (max-width: 760px) {
  .admin-layout {
    flex-direction: column;
  }

  .admin-sider {
    width: 100%;
    border-right: none;
    border-bottom: 1px solid var(--sd-border);
  }

  .admin-content {
    padding: 14px;
  }
}
</style>
