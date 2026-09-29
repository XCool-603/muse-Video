<template>
  <a-config-provider :locale="zhCN" :theme="themeConfig">
    <div class="app-shell">
      <header v-if="!isImmersive" class="app-header">
        <div class="sd-container header-inner">
          <RouterLink to="/" class="logo">
            <span class="logo-mark">剧</span>
            <span class="logo-text">短剧聚合</span>
          </RouterLink>

          <nav class="nav-links">
            <RouterLink v-for="item in navItems" :key="item.path" :to="item.path" class="nav-link">
              {{ item.label }}
            </RouterLink>
          </nav>

          <div class="header-actions">
            <a-button type="text" @click="goSearch">
              <template #icon><SearchOutlined /></template>
            </a-button>

            <template v-if="userStore.isLoggedIn">
              <a-dropdown>
                <a-avatar :src="userStore.user?.avatarUrl" :size="32" class="user-avatar">
                  {{ userStore.user?.username?.[0]?.toUpperCase() }}
                </a-avatar>
                <template #overlay>
                  <a-menu>
                    <a-menu-item key="profile" @click="router.push('/profile')">个人中心</a-menu-item>
                    <a-menu-item v-if="userStore.isAdmin" key="admin" @click="router.push('/admin')">
                      管理后台
                    </a-menu-item>
                    <a-menu-divider />
                    <a-menu-item key="logout" @click="handleLogout">退出登录</a-menu-item>
                  </a-menu>
                </template>
              </a-dropdown>
            </template>
            <a-button v-else type="primary" size="small" @click="router.push('/login')">登录</a-button>
          </div>
        </div>
      </header>

      <main class="app-main">
        <RouterView v-slot="{ Component }">
          <component :is="Component" />
        </RouterView>
      </main>

      <footer v-if="!isImmersive" class="app-footer">
        <div class="sd-container">
          <p class="sd-muted">
            短剧聚合平台 · 聚合多平台短剧资源，提供统一搜索与播放体验
          </p>
          <p class="sd-muted">
            ⚠️ 本系统仅提供技术框架，不包含任何视频内容。使用方须确保拥有合法版权与运营资质。
          </p>
        </div>
      </footer>
    </div>
  </a-config-provider>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { useRoute, useRouter, RouterLink, RouterView } from 'vue-router'
import { SearchOutlined } from '@ant-design/icons-vue'
import zhCN from 'ant-design-vue/es/locale/zh_CN'
import { theme as antdTheme } from 'ant-design-vue'
import { useUserStore } from '@/stores/user'

const route = useRoute()
const router = useRouter()
const userStore = useUserStore()

const navItems = [
  { path: '/', label: '首页' },
  { path: '/category', label: '分类' },
  { path: '/search', label: '搜索' },
  { path: '/profile', label: '我的' }
]

// 播放页采用沉浸式布局；入口页不显示导航与页脚
const isImmersive = computed(() => route.name === 'play' || route.name === 'gate')

const themeConfig = {
  algorithm: antdTheme.darkAlgorithm,
  token: {
    colorPrimary: '#ff4d6d',
    borderRadius: 8
  }
}

function goSearch() {
  router.push('/search')
}

function handleLogout() {
  userStore.logout()
  router.push('/')
}
</script>

<style scoped>
.app-shell {
  display: flex;
  flex-direction: column;
  min-height: 100vh;
}

.app-header {
  position: sticky;
  top: 0;
  z-index: 100;
  backdrop-filter: blur(12px);
  background: rgba(15, 15, 18, 0.85);
  border-bottom: 1px solid var(--sd-border);
}

.header-inner {
  display: flex;
  align-items: center;
  gap: 24px;
  height: 56px;
}

.logo {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-shrink: 0;
}

.logo-mark {
  display: grid;
  place-items: center;
  width: 28px;
  height: 28px;
  border-radius: 8px;
  background: linear-gradient(135deg, #ff4d6d, #ff8a5b);
  color: #fff;
  font-weight: 700;
  font-size: 15px;
}

.logo-text {
  font-size: 16px;
  font-weight: 600;
  letter-spacing: 0.5px;
}

.nav-links {
  display: flex;
  gap: 4px;
  flex: 1;
}

.nav-link {
  padding: 6px 14px;
  border-radius: 8px;
  font-size: 14px;
  color: var(--sd-text-secondary);
  transition: all 0.2s;
}

.nav-link:hover {
  color: var(--sd-text);
  background: rgba(255, 255, 255, 0.06);
}

.nav-link.router-link-exact-active {
  color: #fff;
  background: rgba(255, 77, 109, 0.16);
}

.header-actions {
  display: flex;
  align-items: center;
  gap: 8px;
}

.user-avatar {
  cursor: pointer;
  background: var(--sd-primary);
}

.app-main {
  flex: 1;
  min-height: 0;
}

.app-footer {
  border-top: 1px solid var(--sd-border);
  padding: 20px 0;
  margin-top: 40px;
}

.app-footer p {
  margin: 4px 0;
}

@media (max-width: 640px) {
  .logo-text {
    display: none;
  }

  .nav-link {
    padding: 6px 10px;
    font-size: 13px;
  }
}
</style>
