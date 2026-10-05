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
      <a-checkbox v-model:checked="excludeAdult" @change="doSearch">排除疑似成人内容</a-checkbox>
      <span v-if="searched && !searchError" class="sd-muted">
        共 <b class="highlight">{{ results.length }}</b> 条
        <template v-if="filteredIrrelevant + filteredAdult > 0">
          （已挡掉无关 {{ filteredIrrelevant }} / 成人 {{ filteredAdult }}）
        </template>
        · {{ tookMs }} ms<span v-if="cached"> · 命中缓存</span>
      </span>
    </div>

    <!-- 搜索失败要明确区分于「0 条」：原先失败时清空结果、显示成「没搜到」，
         看起来就像「点了没反应」，把真正的原因藏了起来 -->
    <a-alert v-if="searchError" type="error" show-icon class="source-alert">
      <template #message>搜索失败</template>
      <template #description>
        <div>{{ searchError }}</div>
        <div class="sd-muted">这不代表没有资源。可以再点一次搜索重试。</div>
      </template>
    </a-alert>

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

      <div v-else-if="searched && !searchError && !searching" class="hint">
        <p>没有搜到与关键词匹配的结果。</p>
        <p v-if="filteredIrrelevant > 0" class="sd-muted">
          本次挡掉了 <b>{{ filteredIrrelevant }}</b> 条与关键词无关的结果 ——
          部分索引站（如海盗湾）遇到中文查询会返回自己的默认榜单，那些内容看着多，但和你要找的没关系。
        </p>
        <p v-if="filteredAdult > 0" class="sd-muted">
          另外挡掉了 <b>{{ filteredAdult }}</b> 条疑似成人内容。
          想看看被挡掉的内容，可以关掉上面的「排除疑似成人内容」再搜一次。
        </p>
        <!-- 0 条时把各源的实际情况摊开，而不是丢一句「没搜到」 -->
        <p v-if="sources.length" class="sd-muted">
          本次跑了 {{ sources.length }} 个索引源：
          <span v-for="s in sources" :key="s.id" class="source-chip" :class="{ bad: !s.ok }">
            {{ s.id }} {{ s.ok ? s.count : '失败' }}
          </span>
        </p>
        <p class="sd-muted">
          这些索引站以英文影视和动漫为主，中文短剧的收录很少。
          换个更通用的关键词（例如剧名里的两三个字）可能更容易命中。
        </p>
      </div>

      <div v-else-if="!searched" class="hint">
        <p>输入剧名搜索种子资源。找到之后可以边下边播，不必等整部下完。</p>
        <p class="sd-muted">
          提示：做种数为 0 或「未知」的结果通常下不动；优先挑做种数高的。
        </p>
        <p class="sd-muted">
          另外提醒：这些公开索引站以英文影视、动漫和成人内容为主，中文短剧的收录很少。
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
import {
  torrentApi,
  isTaskActive,
  formatBytes,
  statusText,
  type TorrentStatus,
  type TorrentSearchResult,
  type TorrentSourceStatus,
  type TorrentFile,
  type TorrentTask
} from '@/api/torrent'

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
const sources = ref<TorrentSourceStatus[]>([])
const sourceErrors = ref<string[]>([])
/** 搜索失败的原因。必须与「0 条」区分开，否则失败会被误读成「没有资源」 */
const searchError = ref('')
/**
 * 是否挡掉疑似成人内容，默认开。
 * 中文短剧的查询词（霸总/战神/穿越…）在成人标题里极常见，不挡的话第一页基本都是噪声。
 * 判断依据是标题关键词（站点自己的分类标注没用），会漏也会误伤，所以：
 *   · 过滤条数显示在结果上方，不是偷偷吃掉；
 *   · 关掉开关即原样返回。
 */
const excludeAdult = ref(true)
const filteredIrrelevant = ref(0)
const filteredAdult = ref(0)

const task = ref<TorrentTask | null>(null)
const preparingHash = ref('')

let pollTimer: number | undefined
/**
 * 搜索请求序号：防止旧响应覆盖新结果。
 * 搜索要打 6 个索引站、耗时几秒，用户很可能等不及就改了关键词再搜一次；
 * 若先发的慢请求后返回，它会把新词的结果覆盖掉 —— 表现就是「不管搜什么都一样」。
 * 首页/分类页早有这个保护，这里当初漏了。
 */
let searchSeq = 0

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

  const seq = ++searchSeq
  searching.value = true
  searched.value = true
  searchError.value = ''

  try {
    const response = await torrentApi.search({
      q: query,
      limit: 15,
      excludeAdult: excludeAdult.value
    })
    if (seq !== searchSeq) return // 期间又搜了别的词，这次结果作废

    results.value = response.results
    total.value = response.total
    tookMs.value = response.tookMs
    cached.value = response.cached
    sources.value = response.sources ?? []
    sourceErrors.value = response.sourceErrors ?? []
    filteredIrrelevant.value = response.filteredIrrelevant ?? 0
    filteredAdult.value = response.filteredAdult ?? 0
  } catch (error) {
    if (seq !== searchSeq) return

    // 关键：失败不能表现成「0 条」。把原因留下来给用户看，
    // 同时保留上一次的结果，避免页面突然空掉让人以为「点了没反应」。
    results.value = []
    sources.value = []
    sourceErrors.value = []
    searchError.value = extractError(error)
  } finally {
    if (seq === searchSeq) searching.value = false
  }
}

/** 从 axios 错误里取出可读原因（没有响应体时用 message，例如超时/网络错误） */
function extractError(error: unknown): string {
  const err = error as { response?: { status?: number; data?: { message?: string } }; message?: string }
  const status = err?.response?.status
  const message = err?.response?.data?.message || err?.message || '未知错误'
  return status ? `HTTP ${status}：${message}` : message
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

.source-chip {
  display: inline-block;
  margin-right: 8px;
  padding: 1px 6px;
  border-radius: 4px;
  background: rgba(255, 255, 255, 0.06);
  font-size: 12px;
}

.source-chip.bad {
  color: #ff7875;
}

.torrent-list {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

/* 行做紧凑：标题一行 + 元信息一行，尽量在一屏里多放几条 */
.torrent-item {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 8px 10px;
  background: var(--sd-surface, #1b1b24);
  border: 1px solid var(--sd-border);
  border-radius: var(--sd-radius);
}

.torrent-main {
  flex: 1;
  min-width: 0;
}

.torrent-title {
  font-size: 13px;
  line-height: 1.4;
  margin-bottom: 2px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.torrent-meta {
  display: flex;
  gap: 10px;
  flex-wrap: wrap;
  font-size: 12px;
  line-height: 1.3;
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
