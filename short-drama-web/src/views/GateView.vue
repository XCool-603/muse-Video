<template>
  <div class="gate-view cy-grid-bg">
    <!-- 扫描线铺满视口；pointer-events: none，不影响表单交互 -->
    <div class="cy-scanline cy-scanline--fixed" aria-hidden="true"></div>

    <div class="gate-card cy-panel cy-panel--glow">
      <div class="brand">
        <span class="logo-mark">剧</span>
        <h1 class="cy-title gate-title">短剧聚合</h1>
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

        <!-- 原生 submit 按钮：a-form 在 <form> 上绑了 onSubmit -> validate -> emit('finish')，
             所以原生按钮一样能触发 @finish（见 ant-design-vue/es/form/Form.js handleSubmit）。
             这样就能直接套主题契约里的 .cy-btn / .cy-btn--primary，不必和 antd 按钮样式打架。 -->
        <button
          type="submit"
          class="cy-btn cy-btn--primary gate-submit"
          :disabled="loading || !form.password"
        >
          {{ loading ? '验证中…' : '进入' }}
        </button>
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
/* 口令门页换皮：颜色一律取 cyberpunk.css 的 --cy-* 令牌，本文件不写死颜色。
   卡片 / 标题 / 按钮复用主题契约类 .cy-panel .cy-panel--glow .cy-title .cy-btn--primary。 */
.gate-view {
  position: relative;
  display: grid;
  place-items: center;
  min-height: 100vh;
  padding: 24px;
  background-color: var(--cy-bg);
}

/* 顶部青光 + 底部品红辉光，压在网格之上、卡片之下 */
.gate-view::before {
  content: '';
  position: fixed;
  inset: 0;
  z-index: 0;
  pointer-events: none;
  background-image: radial-gradient(760px 420px at 50% 0%, var(--cy-cyan-dim), transparent 70%),
    radial-gradient(620px 380px at 50% 100%, var(--cy-magenta-dim), transparent 72%);
}

.gate-card {
  position: relative;
  z-index: 1;
  width: 100%;
  max-width: 380px;
  padding: 32px 28px;
}

.brand {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 12px;
  margin-bottom: 16px;
}

.logo-mark {
  display: grid;
  place-items: center;
  width: 48px;
  height: 48px;
  background-image: linear-gradient(135deg, var(--cy-cyan), var(--cy-magenta));
  clip-path: var(--cy-clip);
  color: var(--cy-bg);
  font-family: var(--cy-font-mono);
  font-weight: 700;
  font-size: 23px;
  filter: drop-shadow(0 0 8px var(--cy-cyan-glow-soft));
}

/* .cy-title 默认是页面级大标题（clamp 到 38px），门页卡片里收小 */
.gate-title {
  font-size: 19px;
  letter-spacing: 0.08em;
  text-align: center;
}

.notice {
  margin: 0 0 20px;
  text-align: center;
  font-size: 13px;
  color: var(--cy-text-dim);
}

.gate-submit {
  width: 100%;
}

.pledge {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 6px;
  margin-top: 22px;
  padding-top: 16px;
  border-top: 1px solid var(--cy-border);
  font-size: 12px;
  color: var(--cy-text-dim);
}
</style>
