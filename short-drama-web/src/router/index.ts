import { createRouter, createWebHistory } from 'vue-router'
import { useUserStore } from '@/stores/user'

const router = createRouter({
  history: createWebHistory(),
  scrollBehavior: () => ({ top: 0 }),
  routes: [
    {
      path: '/gate',
      name: 'gate',
      component: () => import('@/views/GateView.vue'),
      meta: { title: '访问验证', layout: 'blank' }
    },
    {
      path: '/',
      name: 'home',
      component: () => import('@/views/HomeView.vue'),
      meta: { title: '首页' }
    },
    {
      path: '/category',
      name: 'category',
      component: () => import('@/views/CategoryView.vue'),
      meta: { title: '分类' }
    },
    {
      path: '/search',
      name: 'search',
      component: () => import('@/views/SearchView.vue'),
      meta: { title: '搜索' }
    },
    {
      path: '/torrent',
      name: 'torrent',
      component: () => import('@/views/TorrentView.vue'),
      meta: { title: '种子' }
    },
    {
      // 种子播放：独立路由，复用播放页的沉浸式布局（App.vue 按 name 隐藏导航）
      path: '/torrent/play/:hash/:index',
      name: 'torrent-play',
      component: () => import('@/views/TorrentPlayerView.vue'),
      meta: { title: '种子播放' }
    },
    {
      path: '/play/:id/:episode?',
      name: 'play',
      component: () => import('@/views/PlayerView.vue'),
      meta: { title: '播放' }
    },
    {
      path: '/profile',
      name: 'profile',
      component: () => import('@/views/ProfileView.vue'),
      meta: { title: '我的', requiresAuth: true }
    },
    {
      path: '/login',
      name: 'login',
      component: () => import('@/views/LoginView.vue'),
      meta: { title: '登录' }
    },
    {
      path: '/admin',
      component: () => import('@/views/admin/AdminLayout.vue'),
      meta: { requiresAuth: true, requiresAdmin: true },
      children: [
        {
          path: '',
          name: 'admin-dashboard',
          component: () => import('@/views/admin/DashboardView.vue'),
          meta: { title: '数据看板' }
        },
        {
          path: 'dramas',
          name: 'admin-dramas',
          component: () => import('@/views/admin/DramaManageView.vue'),
          meta: { title: '短剧管理' }
        },
        {
          path: 'platforms',
          name: 'admin-platforms',
          component: () => import('@/views/admin/PlatformManageView.vue'),
          meta: { title: '平台源管理' }
        }
      ]
    },
    {
      path: '/:pathMatch(.*)*',
      name: 'not-found',
      component: () => import('@/views/NotFoundView.vue'),
      meta: { title: '页面不存在' }
    }
  ]
})

router.beforeEach((to) => {
  const userStore = useUserStore()

  if (to.meta.requiresAuth && !userStore.isLoggedIn) {
    return { name: 'login', query: { redirect: to.fullPath } }
  }

  if (to.meta.requiresAdmin && !userStore.isAdmin) {
    return { name: 'home' }
  }

  return true
})

router.afterEach((to) => {
  const title = (to.meta.title as string) || ''
  document.title = title ? `${title} · 短剧聚合` : '短剧聚合 · 一站式短剧搜索与播放'
})

/**
 * 懒加载 chunk 取不到时自动刷新一次。
 *
 * 为什么需要：路由组件是动态 import 的，而构建产物带内容 hash ——
 * 每次重新部署，旧的 chunk 文件名就不存在了。如果用户的标签页是**部署前打开的**，
 * 里面记着的还是旧文件名，这时点导航（例如「种子」）会去请求一个 404 的 chunk，
 * 表现是**点了完全没反应**（Vue Router 默认不提示，控制台外看不到任何反馈）。
 *
 * 刷新一次就能拿到新的 index.html 与新的 chunk 名。用 sessionStorage 加时间窗
 * 兜底，避免万一持续失败时无限刷新。
 */
router.onError((error) => {
  const message = String((error as Error)?.message ?? '')
  const isChunkLoadFailure =
    /Failed to fetch dynamically imported module|Importing a module script failed|error loading dynamically imported module|Loading chunk \d+ failed/i.test(
      message
    )

  if (!isChunkLoadFailure) return

  const KEY = 'sd-chunk-reload-at'
  const last = Number(sessionStorage.getItem(KEY) ?? 0)
  const now = Date.now()

  // 10 秒内已经刷过一次就不再刷，避免死循环
  if (now - last < 10_000) {
    console.error('[路由] 懒加载 chunk 仍然取不到，已放弃自动刷新：', message)
    return
  }

  sessionStorage.setItem(KEY, String(now))
  window.location.reload()
})

export default router
