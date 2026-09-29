<template>
  <div class="gate-view">
    <div class="gate-card">
      <div class="brand">
        <span class="logo-mark">剧</span>
        <h1>短剧聚合</h1>
      </div>

      <p class="notice">本站为个人自用实例，请先输入访问口令</p>

      <a-form :model="form" @finish="handleSubmit">
        <a-form-item :validate-status="error ? 'error' : ''" :help="error">
          <a-input-password
            v-model:value="form.password"
            size="large"
            placeholder="请输入访问口令"
            autofocus
            :disabled="loading"
          >
            <template #prefix><LockOutlined /></template>
          </a-input-password>
        </a-form-item>

        <a-button
          type="primary"
          size="large"
          block
          html-type="submit"
          :loading="loading"
          :disabled="!form.password"
        >
          进入
        </a-button>
      </a-form>

      <div class="pledge">
        <SafetyCertificateOutlined />
        <span>遵纪守法，世界和平</span>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { reactive, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { LockOutlined, SafetyCertificateOutlined } from '@ant-design/icons-vue'
import { message } from 'ant-design-vue'
import { accessApi } from '@/api/access'

const route = useRoute()
const router = useRouter()

// a-form 必须绑定 :model，否则 ant-design-vue 的 handleSubmit 会直接跳过，
// 既不校验也不 emit('finish')，点提交按钮就成了「没反应」（连请求都不会发）
const form = reactive({ password: '' })
const loading = ref(false)
const error = ref('')

async function handleSubmit() {
  if (!form.password) return

  loading.value = true
  error.value = ''

  try {
    await accessApi.verify(form.password)
    message.success('验证通过')

    const redirect = (route.query.redirect as string) || '/'
    // 用整页跳转，确保后端签发的 Cookie 立即生效
    window.location.replace(redirect)
  } catch (e) {
    error.value = (e as Error).message || '口令不正确'
    form.password = ''
  } finally {
    loading.value = false
  }
}
</script>

<style scoped>
.gate-view {
  display: grid;
  place-items: center;
  min-height: 100vh;
  padding: 24px;
  background:
    radial-gradient(900px 400px at 50% -10%, rgba(255, 77, 109, 0.18), transparent),
    var(--sd-bg);
}

.gate-card {
  width: 100%;
  max-width: 380px;
  padding: 32px 28px;
  background: var(--sd-bg-elevated);
  border: 1px solid var(--sd-border);
  border-radius: 16px;
}

.brand {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 10px;
  margin-bottom: 14px;
}

.logo-mark {
  display: grid;
  place-items: center;
  width: 48px;
  height: 48px;
  border-radius: 13px;
  background: linear-gradient(135deg, #ff4d6d, #ff8a5b);
  color: #fff;
  font-weight: 700;
  font-size: 23px;
}

.brand h1 {
  margin: 0;
  font-size: 19px;
  font-weight: 600;
  color: var(--sd-text);
}

.notice {
  margin: 0 0 20px;
  text-align: center;
  font-size: 13px;
  color: var(--sd-text-secondary);
}

.pledge {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 6px;
  margin-top: 22px;
  padding-top: 16px;
  border-top: 1px solid var(--sd-border);
  font-size: 12px;
  color: var(--sd-text-secondary);
}
</style>
