<template>
  <div class="platform-manage">
    <a-alert
      type="info"
      show-icon
      class="tip-alert"
      message="平台适配器采用适配器模式，新增平台只需实现 IPlatformAdapter 并在依赖注入中注册"
      description="启用/禁用会实时生效于聚合搜索、榜单与上新接口；同步操作会把平台内容拉取并标准化入库。"
    />

    <a-spin :spinning="loading">
      <a-row :gutter="[16, 16]">
        <a-col v-for="item in platforms" :key="item.platformCode" :xs="24" :md="12" :xl="8">
          <div class="platform-card">
            <div class="card-head">
              <div class="platform-name">
                <span class="dot" :style="{ background: colorOf(item.platformCode) }" />
                <span>{{ item.platformName }}</span>
              </div>
              <a-switch
                :checked="item.isEnabled"
                :loading="togglingCode === item.platformCode"
                @change="(checked: any) => handleToggle(item, !!checked)"
              />
            </div>

            <div class="card-body">
              <div class="row">
                <span class="label">平台编码</span>
                <span class="value">{{ item.platformCode }}</span>
              </div>
              <div class="row">
                <span class="label">适配器</span>
                <span class="value">{{ item.adapterType }}</span>
              </div>
              <div class="row">
                <span class="label">接口地址</span>
                <span class="value ellipsis">{{ item.baseUrl || '—' }}</span>
              </div>
              <div class="row">
                <span class="label">上次同步</span>
                <span class="value">{{ item.lastSyncAt ? formatTime(item.lastSyncAt) : '未同步' }}</span>
              </div>
            </div>

            <div class="card-foot">
              <a-badge
                :status="item.isEnabled ? 'success' : 'default'"
                :text="item.isEnabled ? '已启用' : '已禁用'"
              />
              <a-button
                type="primary"
                size="small"
                :loading="syncingCode === item.platformCode"
                :disabled="!item.isEnabled"
                @click="handleSync(item)"
              >
                <template #icon><SyncOutlined /></template>
                立即同步
              </a-button>
            </div>
          </div>
        </a-col>
      </a-row>
    </a-spin>

    <a-empty v-if="!loading && !platforms.length" description="暂无平台配置" />
  </div>
</template>

<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { SyncOutlined } from '@ant-design/icons-vue'
import { message } from 'ant-design-vue'
import { adminApi } from '@/api/admin'
import type { PlatformSource } from '@/api/types'

const platforms = ref<PlatformSource[]>([])
const loading = ref(false)
const togglingCode = ref('')
const syncingCode = ref('')

const COLORS: Record<string, string> = {
  hongguo: '#ff4d4f',
  huangdou: '#faad14',
  juguo: '#52c41a',
  yeguo: '#13c2c2',
  diguo: '#722ed1'
}

function colorOf(code: string) {
  return COLORS[code] ?? '#888'
}

function formatTime(value: string) {
  return new Date(value).toLocaleString('zh-CN', { hour12: false })
}

async function load() {
  loading.value = true
  try {
    platforms.value = await adminApi.platforms()
  } finally {
    loading.value = false
  }
}

async function handleToggle(item: PlatformSource, enabled: boolean) {
  if (!item.id) {
    message.warning('该平台尚未落库，请先执行一次同步以写入配置')
    return
  }

  togglingCode.value = item.platformCode
  try {
    await adminApi.togglePlatform(item.id, enabled)
    item.isEnabled = enabled
    message.success(enabled ? `${item.platformName} 已启用` : `${item.platformName} 已禁用`)
  } catch (error) {
    message.error((error as Error).message || '操作失败')
  } finally {
    togglingCode.value = ''
  }
}

async function handleSync(item: PlatformSource) {
  syncingCode.value = item.platformCode
  try {
    const count = await adminApi.syncPlatform(item.platformCode)
    message.success(`${item.platformName} 同步完成，共处理 ${count} 部短剧`)
    load()
  } catch (error) {
    message.error((error as Error).message || '同步失败')
  } finally {
    syncingCode.value = ''
  }
}

onMounted(load)
</script>

<style scoped>
.tip-alert {
  margin-bottom: 16px;
}

.platform-card {
  background: var(--sd-bg-elevated);
  border: 1px solid var(--sd-border);
  border-radius: var(--sd-radius);
  padding: 16px;
  height: 100%;
  display: flex;
  flex-direction: column;
}

.card-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 14px;
}

.platform-name {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 15px;
  font-weight: 600;
}

.dot {
  width: 10px;
  height: 10px;
  border-radius: 50%;
}

.card-body {
  flex: 1;
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  font-size: 12.5px;
}

.label {
  color: var(--sd-text-secondary);
  flex-shrink: 0;
}

.value {
  color: var(--sd-text);
  font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
  font-size: 12px;
  min-width: 0;
}

.ellipsis {
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.card-foot {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-top: 14px;
  padding-top: 12px;
  border-top: 1px solid var(--sd-border);
}
</style>
