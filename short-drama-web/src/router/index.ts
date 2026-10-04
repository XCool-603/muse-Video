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

export default router
