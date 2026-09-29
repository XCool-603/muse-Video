<template>
  <div class="login-view">
    <div class="login-card">
      <div class="brand">
        <span class="logo-mark">剧</span>
        <h1>短剧聚合</h1>
        <p class="sd-muted">登录后可使用收藏、观看历史与续播功能</p>
      </div>

      <a-tabs v-model:activeKey="mode" centered>
        <a-tab-pane key="login" tab="登录" />
        <a-tab-pane key="register" tab="注册" />
      </a-tabs>

      <a-form :model="form" layout="vertical" @finish="handleSubmit">
        <a-form-item
          label="用户名"
          name="username"
          :rules="[{ required: true, message: '请输入用户名' }]"
        >
          <a-input v-model:value="form.username" size="large" placeholder="请输入用户名" allow-clear>
            <template #prefix><UserOutlined /></template>
          </a-input>
        </a-form-item>

        <a-form-item
          label="密码"
          name="password"
          :rules="[{ required: true, message: '请输入密码' }, { min: 6, message: '密码至少 6 位' }]"
        >
          <a-input-password v-model:value="form.password" size="large" placeholder="请输入密码">
            <template #prefix><LockOutlined /></template>
          </a-input-password>
        </a-form-item>

        <template v-if="mode === 'register'">
          <a-form-item label="邮箱" name="email">
            <a-input v-model:value="form.email" size="large" placeholder="选填">
              <template #prefix><MailOutlined /></template>
            </a-input>
          </a-form-item>

          <a-form-item label="手机号" name="phone">
            <a-input v-model:value="form.phone" size="large" placeholder="选填">
              <template #prefix><PhoneOutlined /></template>
            </a-input>
          </a-form-item>
        </template>

        <a-button type="primary" size="large" block html-type="submit" :loading="loading">
          {{ mode === 'login' ? '登录' : '注册并登录' }}
        </a-button>
      </a-form>

      <div class="tip">
        <a-alert
          v-if="mode === 'login'"
          type="info"
          show-icon
          message="演示账号：admin / admin123"
          description="首次部署时会自动创建该管理员账号。"
        />
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { reactive, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { LockOutlined, MailOutlined, PhoneOutlined, UserOutlined } from '@ant-design/icons-vue'
import { message } from 'ant-design-vue'
import { useUserStore } from '@/stores/user'

const route = useRoute()
const router = useRouter()
const userStore = useUserStore()

const mode = ref<'login' | 'register'>('login')
const loading = ref(false)

const form = reactive({
  username: '',
  password: '',
  email: '',
  phone: ''
})

async function handleSubmit() {
  loading.value = true
  try {
    if (mode.value === 'login') {
      await userStore.login(form.username, form.password)
      message.success('登录成功')
    } else {
      await userStore.register({
        username: form.username,
        password: form.password,
        email: form.email,
        phone: form.phone
      })
      message.success('注册成功，已自动登录')
    }

    const redirect = (route.query.redirect as string) || '/'
    router.push(redirect)
  } catch (error) {
    message.error((error as Error).message || '操作失败，请检查输入')
  } finally {
    loading.value = false
  }
}
</script>

<style scoped>
.login-view {
  display: grid;
  place-items: center;
  min-height: calc(100vh - 180px);
  padding: 30px 16px;
}

.login-card {
  width: 100%;
  max-width: 420px;
  padding: 28px;
  background: var(--sd-bg-elevated);
  border: 1px solid var(--sd-border);
  border-radius: 14px;
}

.brand {
  text-align: center;
  margin-bottom: 16px;
}

.brand h1 {
  margin: 10px 0 4px;
  font-size: 20px;
  color: var(--sd-text);
}

.brand p {
  margin: 0;
}

.logo-mark {
  display: inline-grid;
  place-items: center;
  width: 46px;
  height: 46px;
  border-radius: 12px;
  background: linear-gradient(135deg, #ff4d6d, #ff8a5b);
  color: #fff;
  font-weight: 700;
  font-size: 22px;
}

.tip {
  margin-top: 18px;
}
</style>
