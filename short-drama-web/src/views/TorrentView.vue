<template>
  <div class="torrent-view sd-container">
    <!-- 种子服务没跑起来时说清楚怎么跑，而不是让用户对着报错猜 -->
    <a-alert v-if="status && !status.reachable" type="warning" show-icon class="status-alert">
      <template #message>本地种子服务没在运行</template>
      <template #description>
        <p>{{ status.message }}</p>
        <p class="sd-muted">
          种子搜索是可选增强：采集源都放不出来的剧可以试试这条路。启动命令（在种子搜索项目目录里）：
        </p>
        <code class="cmd">node bin/magnet-search.mjs serve</code>
      </template>
    </a-alert>

    <div class="search-header">
      <a-input-search
        v-model:value="keyword"
        size="large"
        placeholder="搜索种子（剧名 / 片名）"
        enter-button="搜索"
        allow-clear
        :loading="searching"
        @search="doSearch"
      />
    </div>

    <div class="filters">
      <span v-if="searched" class="sd-muted">
        共 <b class="highlight">{{ total }}</b> 条 · 用时 {{ tookMs }} ms
        <span v-if="cached">（命中缓存）</span>
      </span>
      <span class="sd-muted">按做种数排序 —— 做种为 0 的通常下不动</span>
    </div>

    <a-alert v-if="sourceErrors.length" type="info" class="source-alert">
      <template #message>部分索引源没返回结果</template>
      <template #description>
        <div v-for="(err, index) in sourceErrors" :key="index" class="source-error">{{ err }}</div>
      </template>
    </a-alert>

    <a-spin :spinning="searching">
      <div v-if="results.length" class="torrent-list">
        <div v-for="item in results" :key="item.infoHash" class="torrent-item">
          <div class="torrent-main">
            <div class="torrent-title" :title="item.title">{{ item.title }}</div>
            <div class="torrent-meta">
              <span>{{ item.sizeText || '体积未知' }}</span>
              <span :class="seedClass(item.seeders)">
                做种 {{ item.seeders ?? '未知' }}
              </span>
              <span v-if="item.leechers != null">下载 {{ item.leechers }}</span>
              <span v-if="item.sources?.length" class="sd-muted">{{ item.sources.join(' / ') }}</span>
            </div>
          </div>
          <a-button
            type="primary"
            size="small"
            :loading="preparingHash === item.infoHash"
            @click="prepare(item)"
          >
            下载并播放
          </a-button>
        </div>
      </div>

      <a-empty v-else-if="searched" description="没搜到相关种子，换个关键词试试" />
      <div v-else class="hint">
        <p>输入剧名搜索种子资源。找到之后可以边下边播，不必等整部下完。</p>
        <p class="sd-muted">
          提示：做种数为 0 或「未知」的结果通常下不动；优先挑做种数高的。
        </p>
      </div>
    </a-spin>

    <!-- 选中任务：进度 + 文件清单 -->
    <div v-if="task" class="task-panel">
      <div class="task-head">
        <b class="task-name">{{ task.name || task.infoHash.slice(0, 12) }}</b>
        <a-tag :color="statusColor(task.status)">{{ statusText(task.status) }}</a-tag>
        <span class="sd-muted">
          {{ formatBytes(task.bytesDone) }} / {{ formatBytes(task.totalBytes) }}
        </span>
        <span v-if="task.speed > 0" class="sd-muted">{{ formatBytes(task.speed) }}/s</span>
        <span class="sd-muted">peer {{ task.peersConnected }}</span>
      </div>

      <a-progress
        :percent="Math.round((task.progress || 0) * 100)"
        size="small"
        :status="task.status === 'failed' ? 'exception' : 'active'"
      />

      <p v-if="task.error" class="error-text">{{ task.error }}</p>

      <div v-if="task.files.length" class="file-list">
        <div v-for="file in task.files" :key="file.index" class="file-item">
          <span class="file-name" :title="file.path">{{ file.path }}</span>
          <span class="sd-muted file-size">{{ formatBytes(file.length) }}</span>
          <a-tag v-if="!file.browserPlayable" color="warning">
            浏览器可能播不了（{{ file.contentType }}）
          </a-tag>
          <a-button type="link" size="small" @click="play(file)">播放</a-button>
        </div>
      </div>

      <div v-else class="file-waiting">
        <a-spin size="small" />
        <span class="sd-muted">
          正在从 peer 获取元数据…（这一步需要能连上 peer；若一直停在这里，多半是 P2P 被网络或代理拦住了）
        </span>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { onMounted, onUnmounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { torrentApi, isTaskActive, formatBytes, statusText, type TorrentStatus, type TorrentSearchResult, type TorrentFile, type TorrentTask } from '@/api/torrent'

const route = useRoute()
const router = useRouter()

const status = ref<TorrentStatus | null>(null)
const keyword = ref('')
const results = ref<TorrentSearchResult[]>([])
const searching = ref(false)
const searched = ref(false)
const total = ref(0)
const tookMs = ref(0)
const cached = ref(false)
const sourceErrors = ref<string[]>([])

const task = ref<TorrentTask | null>(null)
const preparingHash = ref('')

let pollTimer: number | undefined

/** 做种数为 0 的结果基本下不动，标红提示；为 null 表示站点没提供，不做判断 */
function seedClass(seeders?: number | null) {
  if (seeders == null) return 'sd-muted'
  return seeders > 0 ? 'seed-good' : 'seed-bad'
}

function statusColor(value: string) {
  if (value === 'done') return 'success'
  if (value === 'failed' || value === 'cancelled') return 'error'
  if (value === 'downloading') return 'processing'
  return 'default'
}

async function loadStatus() {
  try {
    status.value = await torrentApi.status()
  } catch {
    // 状态接口失败不阻塞页面：搜索时会再报一次具体错误
  }
}

async function doSearch() {
  const query = keyword.value.trim()
  if (!query) return

  searching.value = true
  searched.value = true
  try {
    const response = await torrentApi.search({ q: query, limit: 15 })
    results.value = response.results
    total.value = response.total
    tookMs.value = response.tookMs
    cached.value = response.cached
    sourceErrors.value = response.sourceErrors ?? []
  } catch {
    results.value = []
    sourceErrors.value = []
  } finally {
    searching.value = false
  }
}

async function prepare(item: TorrentSearchResult) {
  preparingHash.value = item.infoHash
  try {
    task.value = await torrentApi.prepare(item.magnet)
    startPolling()
  } catch {
    // 全局拦截器已经提示过原因（例如种子服务没启动）
  } finally {
    preparingHash.value = ''
  }
}

function startPolling() {
  stopPolling()
  // 元数据没到、或还在下载，就持续刷新进度
  pollTimer = window.setInterval(async () => {
    const hash = task.value?.infoHash
    if (!hash) return

    try {
      const next = await torrentApi.task(hash)
      task.value = next
      if (!isTaskActive(next.status) && next.metadataReady) stopPolling()
      if (['failed', 'cancelled'].includes(next.status)) stopPolling()
    } catch {
      stopPolling()
    }
  }, 1500)
}

function stopPolling() {
  if (pollTimer) {
    window.clearInterval(pollTimer)
    pollTimer = undefined
  }
}

function play(file: TorrentFile) {
  if (!task.value) return
  router.push(`/torrent/play/${task.value.infoHash}/${file.index}`)
}

onMounted(async () => {
  await loadStatus()
  const initial = typeof route.query.q === 'string' ? route.query.q : ''
  if (initial) {
    keyword.value = initial
    await doSearch()
  }
})

onUnmounted(stopPolling)
</script>

<style scoped>
.status-alert,
.source-alert {
  margin-bottom: 16px;
}

.cmd {
  display: inline-block;
  margin-top: 6px;
  padding: 4px 8px;
  background: #1b1b24;
  border: 1px solid var(--sd-border);
  border-radius: 6px;
  font-size: 12px;
}

.search-header {
  margin-bottom: 12px;
}

.filters {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-bottom: 16px;
  flex-wrap: wrap;
}

.source-error {
  font-size: 12px;
  color: var(--sd-text-muted, #9a9aa8);
}

.torrent-list {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.torrent-item {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 12px 14px;
  background: var(--sd-surface, #1b1b24);
  border: 1px solid var(--sd-border);
  border-radius: var(--sd-radius);
}

.torrent-main {
  flex: 1;
  min-width: 0;
}

.torrent-title {
  font-size: 14px;
  margin-bottom: 6px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.torrent-meta {
  display: flex;
  gap: 12px;
  flex-wrap: wrap;
  font-size: 12px;
  color: var(--sd-text-muted, #9a9aa8);
}

.seed-good {
  color: #52c41a;
}

.seed-bad {
  color: #ff4d4f;
}

.hint {
  padding: 32px 0;
  text-align: center;
  color: var(--sd-text-muted, #9a9aa8);
  font-size: 13px;
}

.task-panel {
  margin-top: 24px;
  padding: 16px;
  background: var(--sd-surface, #1b1b24);
  border: 1px solid var(--sd-border);
  border-radius: var(--sd-radius);
}

.task-head {
  display: flex;
  align-items: center;
  gap: 10px;
  flex-wrap: wrap;
  margin-bottom: 10px;
}

.task-name {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  max-width: 60%;
}

.error-text {
  margin-top: 8px;
  color: #ff7875;
  font-size: 13px;
}

.file-list {
  margin-top: 12px;
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.file-item {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 6px 8px;
  border-radius: 6px;
  background: rgba(255, 255, 255, 0.02);
}

.file-name {
  flex: 1;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  font-size: 13px;
}

.file-size {
  font-size: 12px;
}

.file-waiting {
  display: flex;
  align-items: center;
  gap: 10px;
  margin-top: 12px;
  font-size: 13px;
}
</style>
