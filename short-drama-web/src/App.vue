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
// antd 只取需要的三个子模块。**不要**写 from 'ant-design-vue' —— 那是桶导入，
// rollup 必须把整个组件库的模块图都读进来解析，实测会把 40+ 个组件模块拖进首屏 entry chunk。
// 这三个深路径与模板里的 <a-config-provider :locale :theme> 一一对应。
import antdTheme from 'ant-design-vue/es/theme'
import ConfigProvider from 'ant-design-vue/es/config-provider'
import zhCN from 'ant-design-vue/es/locale/zh_CN'
import { useUserStore } from '@/stores/user'
const route = useRoute()
const router = useRouter()
const userStore = useUserStore()

const navItems = [
  { path: '/', label: '首页' },
  // 门户页（面向访客的下载 / 部署入口），路由由 src/router/index.ts 提供
  { path: '/portal', label: '门户' },
  { path: '/category', label: '分类' },
  { path: '/search', label: '搜索' },
  { path: '/torrent', label: '种子' },
  { path: '/profile', label: '我的' }
]

// 播放页采用沉浸式布局；入口页不显示导航与页脚。
// 注意：门户页 /portal **不属于**沉浸式布局 —— 它要显示导航与页脚，所以这里不加 'portal'。
const isImmersive = computed(
  () => route.name === 'play' || route.name === 'torrent-play' || route.name === 'gate'
)

/* --------------------------------------------------------------------------
   Ant Design Vue 主题覆盖
   颜色不写死第二遍：从 minimal.css 的 --mn-* 令牌读出来（读不到才用兜底值），
   保证「设计令牌」是唯一真源，改令牌就能整体换色。
   -------------------------------------------------------------------------- */

const MN_FALLBACK = {
  bg: '#ffffff',
  bgSoft: '#f7f7f8',
  surface: '#ffffff',
  surface2: '#f2f2f4',
  border: '#ebebee',
  borderStrong: '#dcdce0',
  primary: '#4f46e5',
  primaryHover: '#4338ca',
  text: '#18181b',
  textSecondary: '#52525b',
  textMuted: '#8a8a93',
  radius: '10',
  fontUi: "-apple-system, BlinkMacSystemFont, 'PingFang SC', sans-serif"
}

function readToken(name: string, fallback: string): string {
  if (typeof document === 'undefined') return fallback
  const value = getComputedStyle(document.documentElement).getPropertyValue(name).trim()
  return value || fallback
}

const mn = {
  bg: readToken('--mn-bg', MN_FALLBACK.bg),
  bgSoft: readToken('--mn-bg-soft', MN_FALLBACK.bgSoft),
  surface: readToken('--mn-surface', MN_FALLBACK.surface),
  surface2: readToken('--mn-surface-2', MN_FALLBACK.surface2),
  border: readToken('--mn-border', MN_FALLBACK.border),
  borderStrong: readToken('--mn-border-strong', MN_FALLBACK.borderStrong),
  primary: readToken('--mn-primary', MN_FALLBACK.primary),
  primaryHover: readToken('--mn-primary-hover', MN_FALLBACK.primaryHover),
  text: readToken('--mn-text', MN_FALLBACK.text),
  textSecondary: readToken('--mn-text-secondary', MN_FALLBACK.textSecondary),
  textMuted: readToken('--mn-text-muted', MN_FALLBACK.textMuted),
  onPrimary: readToken('--mn-on-primary', '#ffffff')
}

const mnRadius = Number.parseInt(readToken('--mn-radius', MN_FALLBACK.radius), 10)

const themeConfig = {
  algorithm: antdTheme.defaultAlgorithm,
  token: {
    colorPrimary: mn.primary,
    colorInfo: mn.primary,
    colorLink: mn.primary,
    colorSuccess: '#15803d',
    colorWarning: '#b45309',
    colorError: '#b42318',
    colorBgBase: '#ffffff',
    colorBgLayout: mn.bg,
    colorBgContainer: mn.surface,
    colorBgElevated: mn.surface,
    colorBorder: mn.border,
    colorBorderSecondary: mn.border,
    colorText: mn.text,
    colorTextSecondary: mn.textSecondary,
    colorTextDescription: mn.textSecondary,
    fontFamily: readToken('--mn-font-ui', MN_FALLBACK.fontUi),
    fontSize: 14,
    borderRadius: Number.isFinite(mnRadius) ? mnRadius : 8,
    borderRadiusSM: 6,
    wireframe: false
  },
  components: {
    Layout: { bodyBg: 'transparent', headerBg: 'transparent', footerBg: 'transparent' },
    Card: {
      colorBgContainer: mn.surface,
      borderColor: mn.border,
      headerBg: 'transparent',
      actionsBg: 'transparent'
    },
    Button: {
      primaryShadow: 'none',
      defaultBg: 'transparent',
      defaultColor: mn.text,
      defaultBorderColor: mn.borderStrong,
      defaultHoverColor: mn.primary,
      defaultHoverBorderColor: mn.borderStrong,
      fontWeight: 500
    },
    Menu: {
      itemBg: 'transparent',
      itemColor: mn.text,
      itemHoverBg: mn.surface2,
      itemHoverColor: mn.text,
      itemSelectedBg: mn.surface2,
      itemSelectedColor: mn.primary,
      popupBg: mn.surface,
      darkItemBg: 'transparent',
      darkItemColor: mn.text
    },
    Dropdown: { colorBgElevated: mn.surface, controlItemBgHover: mn.surface2 },
    Input: {
      colorBgContainer: mn.bg,
      activeBorderColor: mn.primary,
      hoverBorderColor: mn.primary,
      activeShadow: 'none'
    },
    InputNumber: {
      colorBgContainer: mn.bg,
      activeBorderColor: mn.primary,
      hoverBorderColor: mn.primary
    },
    Select: {
      colorBgContainer: mn.bg,
      optionSelectedBg: mn.surface2,
      optionActiveBg: mn.surface2
    },
    Tabs: {
      itemColor: mn.textSecondary,
      itemHoverColor: mn.text,
      itemSelectedColor: mn.primary,
      inkBarColor: mn.primary
    },
    Table: {
      headerBg: mn.surface2,
      colorBgContainer: mn.surface,
      borderColor: mn.border,
      rowHoverBg: mn.surface2,
      headerColor: mn.text
    },
    Modal: { contentBg: mn.surface, headerBg: mn.surface, titleColor: mn.text },
    Drawer: { colorBgElevated: mn.surface },
    Pagination: { itemActiveBg: mn.surface2, colorPrimary: mn.primary },
    Empty: { colorTextDescription: mn.textSecondary },
    Tag: { defaultBg: mn.surface2, defaultColor: mn.textSecondary },
    Divider: { colorSplit: mn.border },
    Message: { contentBg: mn.surface },
    Tooltip: { colorBgSpotlight: mn.surface }
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
/* 外壳：颜色一律取 minimal.css 的 --mn-* 令牌，本文件不写死颜色。
   这里没有任何 fixed 装饰层 —— 层次全部由内容自己建立。 */
.app-shell {
  position: relative;
  display: flex;
  flex-direction: column;
  min-height: 100vh;
}

/* 吸顶头部：不透明底色 + 1px 极淡下边框，不用 backdrop-filter（滚动时会持续重绘） */
.app-header {
  position: sticky;
  top: 0;
  z-index: 100;
  background-color: var(--mn-bg);
  border-bottom: 1px solid var(--mn-border);
}

/* 当前路由：文字加重 + 主色，不加底色块（极简风的「选中态」） */
.app-header::after {
  content: none;
}

.header-inner {
  display: flex;
  align-items: center;
  gap: 24px;
  height: 56px;
  /* 窄屏防溢出：flex 子项的 min-width 默认是 auto，不显式归零的话
     导航的 min-content 会把整行撑宽，把登录按钮顶出视口（390 宽实测溢出 19px）。 */
  min-width: 0;
}

.logo {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-shrink: 0;
}

/* 品牌方块：中性底色 + 主色文字，无渐变、无切角、无发光 */
.logo-mark {
  display: grid;
  place-items: center;
  width: 28px;
  height: 28px;
  background-color: var(--mn-primary);
  color: var(--mn-on-primary);
  font-family: var(--mn-font-ui);
  font-weight: 600;
  font-size: 15px;
  border-radius: var(--mn-radius-sm);
}

.logo-text {
  font-family: var(--mn-font-ui);
  font-size: 16px;
  font-weight: 600;
  letter-spacing: 0;
  color: var(--mn-text);
}

/* 导航条：宽屏铺满中间，窄屏放不下时在**自身内部**横向滚动，
   而不是把整个 header 撑宽（页面级横向滚动条 = 回归问题 N1）。 */
.nav-links {
  display: flex;
  gap: 4px;
  flex: 1 1 auto;
  /* min-width: auto 会让本项拒绝收缩到内容宽度以下；归零后才能真正让出空间 */
  min-width: 0;
  flex-wrap: nowrap;
  overflow-x: auto;
  overflow-y: hidden;
  overscroll-behavior-x: contain;
  -webkit-overflow-scrolling: touch;
  /* Firefox：隐藏滚动条，靠滑动 */
  scrollbar-width: none;
}

/* Chromium / WebKit：同样隐藏滚动条（main.css 里全局给了 8px 滚动条） */
.nav-links::-webkit-scrollbar {
  display: none;
}

.nav-link {
  /* 不参与压缩：文字保持一行，放不下就交给父级滚动 */
  flex: 0 0 auto;
  /* 关键：没有它，窄屏下中文会被逐字竖排 */
  white-space: nowrap;
  padding: 6px 12px;
  font-family: var(--mn-font-ui);
  font-size: 14px;
  color: var(--mn-text-secondary);
  border-radius: var(--mn-radius-sm);
  transition: color 0.16s ease, background-color 0.16s ease;
}

.nav-link:hover {
  color: var(--mn-text);
  background-color: var(--mn-surface-2);
}

/* 选中态：主色文字 + 极淡主色底，不用霓虹内发光 */
.nav-link.router-link-exact-active {
  color: var(--mn-primary);
  background-color: var(--mn-primary-soft);
}

.header-actions {
  display: flex;
  align-items: center;
  gap: 8px;
  /* 不收缩：搜索 / 登录按钮保持原尺寸，压缩全部交给导航条去滚动 */
  flex-shrink: 0;
}

.user-avatar {
  cursor: pointer;
  background-color: var(--mn-surface-2);
  color: var(--mn-text-secondary);
}

.app-main {
  position: relative;
  z-index: 1;
  flex: 1;
  min-height: 0;
}

.app-footer {
  position: relative;
  z-index: 1;
  margin-top: var(--mn-space-7);
  padding: var(--mn-space-5) 0;
  background-color: var(--mn-bg-soft);
  border-top: 1px solid var(--mn-border);
}

.app-footer p {
  margin: 4px 0;
}

@media (max-width: 640px) {
  /* 窄屏：外壳拆成两行 —— 第一行「品牌 + 搜索 + 登录」，第二行整行导航。
     六项导航 + logo + 搜索 + 登录的 min-content 之和本身就超过 390px，
     挤在一行里必然有一方被挤出视口（N1 回归）；换行后四者都完整可见，
     导航也不再需要横向滚动。 */
  .header-inner {
    flex-wrap: wrap;
    height: auto;
    min-height: 56px;
    row-gap: 2px;
    column-gap: 12px;
    padding-top: 6px;
    padding-bottom: 6px;
  }

  .logo {
    order: 0;
  }

  .header-actions {
    order: 1;
    /* 第一行只剩 logo 与按钮组，用 auto 外边距把按钮组顶到右侧 */
    margin-left: auto;
  }

  .nav-links {
    order: 2;
    /* basis 100% 强制换行：导航独占第二行，拿到整行宽度 */
    flex: 1 1 100%;
  }

  .logo-text {
    display: none;
  }

  .nav-link {
    padding: 6px 8px;
    font-size: 12px;
  }
}
</style>
