<template>
  <div class="torrent-player">
    <a-spin :spinning="loading" wrapper-class-name="player-spin">
      <div v-if="task" class="player-layout">
        <div class="stage">
          <div class="stage-inner">
            <VideoPlayer
              :src="file?.playUrl ?? ''"
              stream-type="mp4"
              :autoplay="true"
              @error="onPlayError"
            >
              <template #top>
                <div class="stage-top">
                  <a-button type="text" class="back-btn" @click="goBack">
                    <template #icon><ArrowLeftOutlined /></template>
                  </a-button>
                  <div class="stage-titles">
                    <h3>{{ file?.path || task.name || task.infoHash.slice(0, 12) }}</h3>
                    <span class="sd-muted">
                      {{ statusText(task.status) }} ·
                      {{ formatBytes(task.bytesDone) }} / {{ formatBytes(task.totalBytes) }}
                      <template v-if="task.speed > 0"> · {{ formatBytes(task.speed) }}/s</template>
                      <template v-if="task.peersConnected > 0"> · peer {{ task.peersConnected }}</template>
                    </span>
                  </div>
                </div>
              </template>
            </VideoPlayer>

            <!-- 容器不支持时提前说清楚，而不是让用户对着黑屏猜 -->
            <a-alert v-if="file && !file.browserPlayable" type="warning" show-icon class="stage-alert">
              <template #message>这个文件浏览器可能播不了</template>
              <template #description>
                容器类型是 <b>{{ file.contentType }}</b>。浏览器原生只支持 mp4 / webm，
                mkv、avi、ts 这类通常需要转码才能播。
                数据仍在正常下载，可以换下面其它文件试，或等下载完成后用本地播放器打开下载目录里的文件。
              </template>
            </a-alert>

            <a-alert
              v-if="errorMessage"
              type="error"
              show-icon
              class="stage-alert"
              :message="errorMessage"
            />

            <p class="stream-hint sd-muted">
              边下边播：数据没下到时会等一会儿再返回，这是正常的；若一直失败，多半是 P2P 被网络或代理拦住了。
            </p>
          </div>
        </div>

        <aside class="file-side">
          <h4 class="side-title">
            文件（{{ task.files.length }}）
            <a-tag :color="task.status === 'done' ? 'success' : 'processing'" class="side-tag">
              {{ Math.round((task.progress || 0) * 100) }}%
            </a-tag>
          </h4>

          <div class="side-list">
            <div
              v-for="item in task.files"
              :key="item.index"
              class="side-item"
              :class="{ active: item.index === fileIndex }"
              @click="switchTo(item.index)"
            >
              <span class="side-name" :title="item.path">{{ item.path }}</span>
              <span class="sd-muted side-size">{{ formatBytes(item.length) }}</span>
            </div>
          </div>

          <a-button block class="back-list" @click="goBack">返回种子搜索</a-button>
        </aside>
      </div>
    </a-spin>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ArrowLeftOutlined } from '@ant-design/icons-vue'
import VideoPlayer from '@/components/VideoPlayer.vue'
import { torrentApi, isTaskActive, formatBytes, statusText, type TorrentTask } from '@/api/torrent'

const route = useRoute()
const router = useRouter()

const task = ref<TorrentTask | null>(null)
const loading = ref(true)
const errorMessage = ref('')

const infoHash = computed(() => String(route.params.hash ?? ''))
const fileIndex = computed(() => Number(route.params.index ?? 0))
const file = computed(() => task.value?.files.find((item) => item.index === fileIndex.value) ?? null)

let pollTimer: number | undefined

async function loadTask() {
  try {
    task.value = await torrentApi.task(infoHash.value)
  } catch {
    // 全局拦截器已提示；这里保持空态，避免白屏时没有任何信息
  } finally {
    loading.value = false
  }
}

function startPolling() {
  stopPolling()
  // 下载中时刷新进度（播放本身不依赖这个轮询，只是为了把状态显示准）
  pollTimer = window.setInterval(async () => {
    if (!task.value || !isTaskActive(task.value.status)) {
      stopPolling()
      return
    }
    try {
      task.value = await torrentApi.task(infoHash.value)
    } catch {
      stopPolling()
    }
  }, 3000)
}

function stopPolling() {
  if (pollTimer) {
    window.clearInterval(pollTimer)
    pollTimer = undefined
  }
}

function switchTo(index: number) {
  errorMessage.value = ''
  router.replace(`/torrent/play/${infoHash.value}/${index}`)
}

function onPlayError(message: string) {
  errorMessage.value = message
}

function goBack() {
  router.push('/torrent')
}

onMounted(async () => {
  await loadTask()
  startPolling()
})

onUnmounted(stopPolling)
</script>

<style scoped>
.torrent-player {
  min-height: 100vh;
}

.player-layout {
  display: grid;
  grid-template-columns: 1fr 320px;
  gap: 16px;
  max-width: 1280px;
  margin: 0 auto;
  padding: 16px;
}

@media (max-width: 900px) {
  .player-layout {
    grid-template-columns: 1fr;
  }
}

.stage-inner {
  border-radius: var(--sd-radius);
  overflow: hidden;
  background: #000;
}

.stage-top {
  display: flex;
  align-items: center;
  gap: 8px;
}

.stage-titles h3 {
  margin: 0;
  font-size: 14px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  max-width: 46vw;
}

.stage-titles span {
  font-size: 12px;
}

.stage-alert {
  margin-top: 12px;
}

.stream-hint {
  margin-top: 10px;
  font-size: 12px;
}

.file-side {
  background: var(--sd-surface, #1b1b24);
  border: 1px solid var(--sd-border);
  border-radius: var(--sd-radius);
  padding: 14px;
  height: fit-content;
}

.side-title {
  display: flex;
  align-items: center;
  gap: 8px;
  margin: 0 0 10px;
  font-size: 14px;
}

.side-tag {
  margin-left: auto;
}

.side-list {
  display: flex;
  flex-direction: column;
  gap: 4px;
  max-height: 52vh;
  overflow-y: auto;
}

.side-item {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 7px 8px;
  border-radius: 6px;
  cursor: pointer;
  font-size: 13px;
}

.side-item:hover {
  background: rgba(255, 255, 255, 0.04);
}

.side-item.active {
  background: rgba(255, 77, 109, 0.14);
}

.side-name {
  flex: 1;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.side-size {
  font-size: 12px;
}

.back-list {
  margin-top: 12px;
}
</style>
