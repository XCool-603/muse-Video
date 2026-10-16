<template>
  <!-- 无背景装饰：页面底就是 body 的 --mn-bg，层次全部由卡片自己建立 -->
  <div class="gate-view">
    <div class="gate-card mn-panel">
      <div class="brand">
        <span class="logo-mark">剧</span>
        <h1 class="gate-title">短剧聚合</h1>
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
             这样就能直接套主题契约里的 .mn-btn / .mn-btn--primary，不必和 antd 按钮样式打架。 -->
        <button
          type="submit"
          class="mn-btn mn-btn--primary gate-submit"
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
// 按需引入：message 的传递闭包有 351KB 源码（含 antd 全量 locale 表），
// 见 src/utils/toast.ts。首屏静态依赖里没有它。
import { toast } from '@/utils/toast'
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
    toast('success', '验证通过')

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
/* 口令门页换皮：颜色一律取 minimal.css 的 --mn-* 令牌，本文件不写死颜色。
   卡片 / 按钮复用主题契约类 .mn-panel / .mn-btn--primary；
   没有网格背景、没有扫描线、没有 ::before 辉光 —— 页面底就是 body 的白底。 */
.gate-view {
  position: relative;
  display: grid;
  place-items: center;
  min-height: 100vh;
  padding: 24px;
  background-color: var(--mn-bg);
  font-family: var(--mn-font-ui);
  color: var(--mn-text);
}

.gate-card {
  position: relative;
  z-index: 1;
  width: 100%;
  max-width: 380px;
  padding: var(--mn-space-6) var(--mn-space-5);
  box-shadow: var(--mn-shadow-md);
}

.brand {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: var(--mn-space-3);
  margin-bottom: var(--mn-space-4);
}

/* logo：纯主色方块，无渐变、无切角、无发光 */
.logo-mark {
  display: grid;
  place-items: center;
  width: 48px;
  height: 48px;
  background-color: var(--mn-primary);
  border-radius: var(--mn-radius);
  color: var(--mn-on-primary);
  font-family: var(--mn-font-ui);
  font-weight: 600;
  font-size: 23px;
}

.gate-title {
  margin: 0;
  font-size: 19px;
  font-weight: 600;
  letter-spacing: 0;
  text-align: center;
  color: var(--mn-text);
}

.notice {
  margin: 0 0 var(--mn-space-5);
  text-align: center;
  font-size: 13px;
  color: var(--mn-text-secondary);
}

.gate-submit {
  width: 100%;
}

.pledge {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: var(--mn-space-2);
  margin-top: var(--mn-space-5);
  padding-top: var(--mn-space-4);
  border-top: 1px solid var(--mn-border);
  font-size: 12px;
  color: var(--mn-text-muted);
}
</style>
