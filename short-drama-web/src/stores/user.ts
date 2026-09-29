import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import { userApi, type UserInfo } from '@/api/user'

export const useUserStore = defineStore(
  'user',
  () => {
    const token = ref<string>('')
    const user = ref<UserInfo | null>(null)

    const isLoggedIn = computed(() => !!token.value)
    const isAdmin = computed(() => user.value?.role === 'admin')

    function setToken(value: string) {
      token.value = value
      localStorage.setItem('sd_token', value)
    }

    async function login(username: string, password: string) {
      const result = await userApi.login({ username, password })
      setToken(result.token)
      user.value = result.user
      return result.user
    }

    async function register(payload: { username: string; password: string; email?: string; phone?: string }) {
      const result = await userApi.register(payload)
      setToken(result.token)
      user.value = result.user
      return result.user
    }

    async function fetchProfile() {
      if (!token.value) return null
      try {
        user.value = await userApi.profile()
      } catch {
        logout()
      }
      return user.value
    }

    function logout() {
      token.value = ''
      user.value = null
      localStorage.removeItem('sd_token')
    }

    return { token, user, isLoggedIn, isAdmin, setToken, login, register, fetchProfile, logout }
  },
  {
    persist: {
      key: 'sd_user',
      paths: ['token', 'user']
    }
  }
)
