<template>
  <a-config-provider :locale="zhCN" :theme="themeConfig">
    <div class="app-shell">
      <!-- 赛博朋克背景层：网格 + 扫描线。
           沉浸式播放页不叠加，免得糊在画面上（两者都是 pointer-events: none，不影响点击）。 -->
      <div v-if="showAppBackdrop" class="cy-grid-bg app-grid-layer" aria-hidden="true"></div>
      <div v-if="showAppBackdrop" class="cy-scanline cy-scanline--fixed" aria-hidden="true"></div>

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

// 外壳背景层（网格 + 扫描线）是否叠加。
// 门户页 PortalView 自带背景层（.portal-bg + .cy-scanline），再叠一层会因 56px 头部偏移
// 错位成摩尔纹，所以门户页交给页面自己画。
// 注意：这里只是背景层的开关，**不是**沉浸式布局判断 —— 门户页照常显示导航与页脚。
const showAppBackdrop = computed(() => !isImmersive.value && route.name !== 'portal')

/* --------------------------------------------------------------------------
   Ant Design Vue 主题覆盖
   颜色不写死第二遍：从 cyberpunk.css 的 --cy-* 令牌读出来（读不到才用兜底值），
   保证「设计令牌」是唯一真源，改令牌就能整体换色。
   -------------------------------------------------------------------------- */

const CY_FALLBACK = {
  bg: '#05070e',
  bgSoft: '#0a0f1c',
  panel: '#0d1424',
  panel2: '#121c33',
  border: '#1e2c4a',
  cyan: '#00f0ff',
  magenta: '#ff2bd6',
  yellow: '#ffe14d',
  lime: '#9dff3c',
  text: '#e3f6ff',
  textDim: '#7c93b8',
  radius: '10px',
  fontMono: "'JetBrains Mono', ui-monospace, monospace"
}

function readToken(name: string, fallback: string): string {
  if (typeof document === 'undefined') return fallback
  const value = getComputedStyle(document.documentElement).getPropertyValue(name).trim()
  return value || fallback
}

const cy = {
  bg: readToken('--cy-bg', CY_FALLBACK.bg),
  bgSoft: readToken('--cy-bg-soft', CY_FALLBACK.bgSoft),
  panel: readToken('--cy-panel', CY_FALLBACK.panel),
  panel2: readToken('--cy-panel-2', CY_FALLBACK.panel2),
  border: readToken('--cy-border', CY_FALLBACK.border),
  cyan: readToken('--cy-cyan', CY_FALLBACK.cyan),
  magenta: readToken('--cy-magenta', CY_FALLBACK.magenta),
  yellow: readToken('--cy-yellow', CY_FALLBACK.yellow),
  lime: readToken('--cy-lime', CY_FALLBACK.lime),
  text: readToken('--cy-text', CY_FALLBACK.text),
  textDim: readToken('--cy-text-dim', CY_FALLBACK.textDim),
  fontMono: readToken('--cy-font-mono', CY_FALLBACK.fontMono)
}

const cyRadius = Number.parseInt(readToken('--cy-radius', CY_FALLBACK.radius), 10)

const themeConfig = {
  algorithm: antdTheme.darkAlgorithm,
  token: {
    colorPrimary: cy.cyan,
    colorInfo: cy.cyan,
    colorLink: cy.cyan,
    colorSuccess: cy.lime,
    colorWarning: cy.yellow,
    colorError: cy.magenta,
    colorBgBase: cy.bg,
    colorBgLayout: cy.bg,
    colorBgContainer: cy.panel,
    colorBgElevated: cy.panel2,
    colorBorder: cy.border,
    colorBorderSecondary: cy.border,
    colorText: cy.text,
    colorTextSecondary: cy.textDim,
    colorTextDescription: cy.textDim,
    fontFamily: readToken('--cy-font-ui', CY_FALLBACK.fontMono),
    fontFamilyCode: cy.fontMono,
    borderRadius: Number.isFinite(cyRadius) ? cyRadius : 8,
    wireframe: false
  },
  components: {
    Layout: { bodyBg: 'transparent', headerBg: 'transparent', footerBg: 'transparent' },
    Card: {
      colorBgContainer: cy.panel,
      borderColor: cy.border,
      headerBg: 'transparent',
      actionsBg: 'transparent'
    },
    Button: {
      primaryShadow: 'none',
      defaultBg: 'transparent',
      defaultColor: cy.text,
      defaultBorderColor: cy.border,
      defaultHoverColor: cy.cyan,
      defaultHoverBorderColor: cy.cyan,
      fontWeight: 600
    },
    Menu: {
      itemBg: cy.panel,
      itemColor: cy.text,
      itemHoverBg: cy.panel2,
      itemHoverColor: cy.cyan,
      itemSelectedBg: cy.panel2,
      itemSelectedColor: cy.cyan,
      popupBg: cy.panel2,
      darkItemBg: cy.panel,
      darkItemColor: cy.text
    },
    Dropdown: { colorBgElevated: cy.panel2, controlItemBgHover: cy.panel },
    Input: {
      colorBgContainer: cy.bgSoft,
      activeBorderColor: cy.cyan,
      hoverBorderColor: cy.cyan,
      activeShadow: 'none'
    },
    InputNumber: { colorBgContainer: cy.bgSoft, activeBorderColor: cy.cyan, hoverBorderColor: cy.cyan },
    Select: { colorBgContainer: cy.bgSoft, optionSelectedBg: cy.panel2, optionActiveBg: cy.panel2 },
    Tabs: { itemColor: cy.textDim, itemHoverColor: cy.cyan, itemSelectedColor: cy.cyan, inkBarColor: cy.cyan },
    Table: {
      headerBg: cy.panel2,
      colorBgContainer: cy.panel,
      borderColor: cy.border,
      rowHoverBg: cy.panel2,
      headerColor: cy.text
    },
    Modal: { contentBg: cy.panel, headerBg: cy.panel, titleColor: cy.text },
    Drawer: { colorBgElevated: cy.panel },
    Pagination: { itemActiveBg: cy.panel2, colorPrimary: cy.cyan },
    Empty: { colorTextDescription: cy.textDim },
    Tag: { defaultBg: cy.panel2, defaultColor: cy.text },
    Divider: { colorSplit: cy.border },
    Message: { contentBg: cy.panel2 },
    Tooltip: { colorBgSpotlight: cy.panel2 }
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
/* 外壳换皮：颜色一律取 cyberpunk.css 的 --cy-* 令牌，本文件不写死颜色。 */
.app-shell {
  position: relative;
  display: flex;
  flex-direction: column;
  min-height: 100vh;
}

/* 全屏网格背景层：垫在所有内容下面 */
.app-grid-layer {
  position: fixed;
  inset: 0;
  z-index: 0;
  pointer-events: none;
}

.app-header {
  position: sticky;
  top: 0;
  z-index: 100;
  backdrop-filter: blur(12px);
  background-color: var(--cy-panel-glass);
  border-bottom: 1px solid var(--cy-border);
  box-shadow: 0 10px 30px -22px var(--cy-cyan);
}

/* 头部底边的霓虹渐变线 */
.app-header::after {
  content: '';
  position: absolute;
  left: 0;
  right: 0;
  bottom: -1px;
  height: 1px;
  background-image: linear-gradient(
    90deg,
    transparent,
    var(--cy-cyan),
    var(--cy-magenta),
    transparent
  );
  opacity: 0.75;
  pointer-events: none;
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

.logo-mark {
  display: grid;
  place-items: center;
  width: 28px;
  height: 28px;
  background-image: linear-gradient(135deg, var(--cy-cyan), var(--cy-magenta));
  clip-path: var(--cy-clip);
  color: var(--cy-bg);
  font-family: var(--cy-font-mono);
  font-weight: 700;
  font-size: 15px;
  filter: drop-shadow(0 0 6px var(--cy-cyan-glow-soft));
}

.logo-text {
  font-family: var(--cy-font-mono);
  font-size: 16px;
  font-weight: 700;
  letter-spacing: 1px;
  color: var(--cy-text);
  text-shadow: 0 0 10px var(--cy-cyan-glow-soft);
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
  padding: 6px 14px;
  font-family: var(--cy-font-mono);
  font-size: 13px;
  letter-spacing: 0.06em;
  color: var(--cy-text-dim);
  border: 1px solid transparent;
  clip-path: var(--cy-clip);
  transition: color 0.2s, background-color 0.2s, border-color 0.2s, box-shadow 0.2s;
}

.nav-link:hover {
  color: var(--cy-text);
  background-color: var(--cy-hairline);
  border-color: var(--cy-border);
}

.nav-link.router-link-exact-active {
  color: var(--cy-cyan);
  background-color: var(--cy-cyan-dim);
  border-color: var(--cy-cyan-line);
  box-shadow: inset 0 0 12px var(--cy-cyan-dim);
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
  background-color: var(--cy-magenta);
  color: var(--cy-bg);
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
  margin-top: 40px;
  padding: 20px 0;
  background-color: var(--cy-bg-soft);
  border-top: 1px solid var(--cy-border);
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
