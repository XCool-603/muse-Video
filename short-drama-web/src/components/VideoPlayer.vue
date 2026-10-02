<template>
  <div ref="containerRef" class="video-player" :class="{ 'is-fullscreen': isFullscreen }">
    <video
      ref="videoRef"
      class="video-el"
      playsinline
      webkit-playsinline
      :poster="poster"
      referrerpolicy="no-referrer"
      @timeupdate="onTimeUpdate"
      @loadedmetadata="onLoadedMetadata"
      @ended="onEnded"
      @play="onPlay"
      @pause="onPause"
      @waiting="loading = true"
      @playing="loading = false"
      @canplay="loading = false"
      @error="onError"
      @click="togglePlay"
    />

    <!-- 加载中 -->
    <div v-if="loading" class="center-mask">
      <a-spin size="large" />
    </div>

    <!-- 错误提示 -->
    <div v-if="errorMessage" class="center-mask error-mask">
      <WarningOutlined class="error-icon" />
      <p>{{ errorMessage }}</p>
      <a-button type="primary" size="small" @click="retry">重新加载</a-button>
    </div>

    <!-- 中央播放按钮 -->
    <button v-if="!playing && !loading && !errorMessage" class="center-play" @click.stop="togglePlay">
      <PlayCircleFilled />
    </button>

    <!-- 顶部信息条 -->
    <div class="top-bar" :class="{ hidden: !controlsVisible }">
      <slot name="top" />
      <div class="top-right">
        <span v-if="adFree" class="ad-free-badge">
          <SafetyCertificateOutlined /> 已去广告
        </span>
        <span v-if="rate !== 1" class="rate-badge">{{ rate }}x</span>
      </div>
    </div>

    <!-- 底部控制条 -->
    <div class="bottom-bar" :class="{ hidden: !controlsVisible }">
      <div class="progress-row">
        <span class="time">{{ formatTime(currentTime) }}</span>
        <div ref="barRef" class="progress-track" @click="seekByClick">
          <div class="progress-buffer" :style="{ width: bufferedPercent + '%' }" />
          <div class="progress-played" :style="{ width: playedPercent + '%' }">
            <span class="progress-thumb" />
          </div>
        </div>
        <span class="time">{{ formatTime(duration) }}</span>
      </div>

      <div class="control-row">
        <button class="ctrl-btn" @click.stop="togglePlay">
          <PauseOutlined v-if="playing" />
          <CaretRightOutlined v-else />
        </button>

        <button class="ctrl-btn" @click.stop="skip(-10)">
          <UndoOutlined />
          <span class="skip-label">10</span>
        </button>

        <button class="ctrl-btn" @click.stop="skip(10)">
          <RedoOutlined />
          <span class="skip-label">10</span>
        </button>

        <div class="spacer" />

        <a-dropdown placement="topRight">
          <button class="ctrl-btn rate-btn" @click.stop>{{ rate }}x</button>
          <template #overlay>
            <a-menu :selected-keys="[String(rate)]" @click="onRateSelect">
              <a-menu-item v-for="r in rateOptions" :key="String(r)">{{ r }}x</a-menu-item>
            </a-menu>
          </template>
        </a-dropdown>

        <button class="ctrl-btn" @click.stop="toggleMute">
          <SoundOutlined v-if="!muted" />
          <AudioMutedOutlined v-else />
        </button>

        <button class="ctrl-btn" @click.stop="toggleFullscreen">
          <FullscreenExitOutlined v-if="isFullscreen" />
          <FullscreenOutlined v-else />
        </button>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import Hls from 'hls.js'
import {
  AudioMutedOutlined,
  CaretRightOutlined,
  FullscreenExitOutlined,
  FullscreenOutlined,
  PauseOutlined,
  PlayCircleFilled,
  RedoOutlined,
  SafetyCertificateOutlined,
  SoundOutlined,
  UndoOutlined,
  WarningOutlined
} from '@ant-design/icons-vue'

const props = withDefaults(
  defineProps<{
    src: string
    /** 流类型：hls 走 hls.js，mp4 直接原生播放 */
    streamType?: 'hls' | 'mp4'
    poster?: string
    autoplay?: boolean
    startPosition?: number
    adFree?: boolean
    /** 是否显示顶部插槽内容 */
    showTop?: boolean
  }>(),
  {
    streamType: 'hls',
    poster: '',
    autoplay: true,
    startPosition: 0,
    adFree: true,
    showTop: true
  }
)

const emit = defineEmits<{
  (e: 'ended'): void
  (e: 'timeupdate', payload: { current: number; duration: number }): void
  (e: 'error', message: string): void
}>()

const containerRef = ref<HTMLElement>()
const videoRef = ref<HTMLVideoElement>()
const barRef = ref<HTMLElement>()

const playing = ref(false)
const loading = ref(true)
const muted = ref(false)
const currentTime = ref(0)
const duration = ref(0)
const buffered = ref(0)
const rate = ref(1)
const isFullscreen = ref(false)
const errorMessage = ref('')
const controlsVisible = ref(true)

const rateOptions = [0.75, 1, 1.25, 1.5, 2]

let hls: Hls | null = null
let hideTimer: number | undefined
let resumeApplied = false
// fatal 网络错误的重试次数（有上限，见 ERROR 处理）
let networkRetries = 0

const playedPercent = computed(() =>
  duration.value > 0 ? Math.min(100, (currentTime.value / duration.value) * 100) : 0
)
const bufferedPercent = computed(() =>
  duration.value > 0 ? Math.min(100, (buffered.value / duration.value) * 100) : 0
)

function formatTime(seconds: number): string {
  if (!Number.isFinite(seconds) || seconds < 0) return '00:00'
  const total = Math.floor(seconds)
  const m = Math.floor(total / 60)
  const s = total % 60
  return `${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}`
}

/** 挂载播放源：m3u8 走 hls.js，明文 mp4 直接交给原生 <video> */
function attachSource(url: string, streamType: 'hls' | 'mp4' = 'hls') {
  const video = videoRef.value
  if (!video || !url) return

  destroyHls()
  errorMessage.value = ''
  loading.value = true
  resumeApplied = false
  // 换源时重置网络重试计数
  networkRetries = 0

  // 明文 MP4：原生播放，不需要 hls.js
  if (streamType === 'mp4') {
    video.src = url
    video.addEventListener('loadedmetadata', applyResumeAndPlay, { once: true })
    return
  }

  if (Hls.isSupported()) {
    hls = new Hls({
      // 低延迟与稳定性折中；短剧多为点播，开启自适应码率
      enableWorker: true,
      lowLatencyMode: false,
      backBufferLength: 30,
      maxBufferLength: 30,
      manifestLoadingMaxRetry: 3,
      levelLoadingMaxRetry: 3,
      fragLoadingMaxRetry: 4
    })

    hls.loadSource(url)
    hls.attachMedia(video)

    hls.on(Hls.Events.MANIFEST_PARSED, () => {
      loading.value = false
      applyResumeAndPlay()
    })

    hls.on(Hls.Events.ERROR, (_event, data) => {
      if (!data.fatal) return

      switch (data.type) {
        case Hls.ErrorTypes.NETWORK_ERROR:
          // 有上限地重试。源站被地区拒绝 / CORS / 404 这类错误重试多少次都不会好，
          // 原先无上限 startLoad() 会让页面一直转圈、永远不给提示。
          if (networkRetries < 2) {
            networkRetries++
            hls?.startLoad()
          } else {
            errorMessage.value = '播放失败：拉取不到源站播放列表（该源可能在服务器所在地不可达，或受地区限制）'
            loading.value = false
            destroyHls()
            emit('error', errorMessage.value)
          }
          break
        case Hls.ErrorTypes.MEDIA_ERROR:
          hls?.recoverMediaError()
          break
        default:
          errorMessage.value = '视频加载失败，请切换其他平台源或稍后重试'
          loading.value = false
          destroyHls()
          emit('error', errorMessage.value)
          break
      }
    })
  } else if (video.canPlayType('application/vnd.apple.mpegurl')) {
    // Safari / iOS 原生 HLS
    video.src = url
    video.addEventListener('loadedmetadata', applyResumeAndPlay, { once: true })
  } else {
    errorMessage.value = '当前浏览器不支持 HLS 播放'
    loading.value = false
  }
}

function applyResumeAndPlay() {
  const video = videoRef.value
  if (!video || resumeApplied) return
  resumeApplied = true

  if (props.startPosition > 3) {
    video.currentTime = props.startPosition
  }

  if (props.autoplay) {
    video.play().catch(() => {
      // 浏览器自动播放策略拦截，等待用户交互
      playing.value = false
    })
  }
}

function destroyHls() {
  if (hls) {
    hls.destroy()
    hls = null
  }
}

function togglePlay() {
  const video = videoRef.value
  if (!video) return

  if (video.paused) {
    video.play().catch(() => undefined)
  } else {
    video.pause()
  }
}

function skip(seconds: number) {
  const video = videoRef.value
  if (!video) return
  video.currentTime = Math.max(0, Math.min(video.duration || 0, video.currentTime + seconds))
}

function toggleMute() {
  const video = videoRef.value
  if (!video) return
  video.muted = !video.muted
  muted.value = video.muted
}

function onRateSelect({ key }: { key: string | number }) {
  const value = Number(key)
  rate.value = value
  if (videoRef.value) videoRef.value.playbackRate = value
}

function seekByClick(event: MouseEvent) {
  const video = videoRef.value
  const bar = barRef.value
  if (!video || !bar || !duration.value) return

  const rect = bar.getBoundingClientRect()
  const ratio = Math.max(0, Math.min(1, (event.clientX - rect.left) / rect.width))
  video.currentTime = ratio * duration.value
}

async function toggleFullscreen() {
  const container = containerRef.value
  if (!container) return

  if (!document.fullscreenElement) {
    await container.requestFullscreen?.().catch(() => undefined)
  } else {
    await document.exitFullscreen?.().catch(() => undefined)
  }
}

function onFullscreenChange() {
  isFullscreen.value = !!document.fullscreenElement
}

function onTimeUpdate() {
  const video = videoRef.value
  if (!video) return

  currentTime.value = video.currentTime
  if (video.buffered.length > 0) {
    buffered.value = video.buffered.end(video.buffered.length - 1)
  }

  emit('timeupdate', { current: video.currentTime, duration: video.duration || 0 })
  showControlsTemporarily()
}

function onLoadedMetadata() {
  const video = videoRef.value
  if (!video) return
  duration.value = video.duration || 0
  loading.value = false
}

function onPlay() {
  playing.value = true
  scheduleHideControls()
}

function onPause() {
  playing.value = false
  controlsVisible.value = true
  if (hideTimer) window.clearTimeout(hideTimer)
}

function onEnded() {
  playing.value = false
  controlsVisible.value = true
  emit('ended')
}

function onError() {
  if (hls) return // hls.js 已处理
  errorMessage.value = '视频播放出错，请重试'
  loading.value = false
  emit('error', errorMessage.value)
}

function retry() {
  errorMessage.value = ''
  attachSource(props.src, props.streamType)
}

function showControlsTemporarily() {
  controlsVisible.value = true
  scheduleHideControls()
}

function scheduleHideControls() {
  if (hideTimer) window.clearTimeout(hideTimer)
  hideTimer = window.setTimeout(() => {
    if (playing.value) controlsVisible.value = false
  }, 3200)
}

function onKeydown(event: KeyboardEvent) {
  const target = event.target as HTMLElement
  if (target && ['INPUT', 'TEXTAREA'].includes(target.tagName)) return

  switch (event.key) {
    case ' ':
    case 'k':
      event.preventDefault()
      togglePlay()
      break
    case 'ArrowLeft':
      skip(-10)
      break
    case 'ArrowRight':
      skip(10)
      break
    case 'm':
      toggleMute()
      break
    case 'f':
      toggleFullscreen()
      break
  }
}

watch(
  () => [props.src, props.streamType] as const,
  ([value, type]) => {
    if (value) attachSource(value, type)
  }
)

onMounted(() => {
  if (props.src) attachSource(props.src, props.streamType)
  document.addEventListener('fullscreenchange', onFullscreenChange)
  window.addEventListener('keydown', onKeydown)
  containerRef.value?.addEventListener('mousemove', showControlsTemporarily)
})

onBeforeUnmount(() => {
  destroyHls()
  if (hideTimer) window.clearTimeout(hideTimer)
  document.removeEventListener('fullscreenchange', onFullscreenChange)
  window.removeEventListener('keydown', onKeydown)
  containerRef.value?.removeEventListener('mousemove', showControlsTemporarily)
})

defineExpose({
  play: () => videoRef.value?.play(),
  pause: () => videoRef.value?.pause(),
  seek: (time: number) => {
    if (videoRef.value) videoRef.value.currentTime = time
  },
  getCurrentTime: () => videoRef.value?.currentTime ?? 0,
  getDuration: () => videoRef.value?.duration ?? 0
})
</script>

<style scoped>
.video-player {
  position: relative;
  width: 100%;
  height: 100%;
  background: #000;
  overflow: hidden;
  user-select: none;
}

.video-el {
  width: 100%;
  height: 100%;
  object-fit: contain;
  display: block;
  background: #000;
}

.center-mask {
  position: absolute;
  inset: 0;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 12px;
  background: rgba(0, 0, 0, 0.55);
  z-index: 5;
}

.error-mask p {
  margin: 0;
  color: #d9d9d9;
  font-size: 14px;
}

.error-icon {
  font-size: 40px;
  color: #faad14;
}

.center-play {
  position: absolute;
  inset: 0;
  margin: auto;
  width: 72px;
  height: 72px;
  border: none;
  border-radius: 50%;
  background: rgba(0, 0, 0, 0.4);
  color: #fff;
  font-size: 64px;
  display: grid;
  place-items: center;
  cursor: pointer;
  z-index: 4;
  transition: transform 0.2s;
}

.center-play:hover {
  transform: scale(1.08);
}

.top-bar {
  position: absolute;
  top: 0;
  left: 0;
  right: 0;
  padding: 12px 14px;
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 12px;
  background: linear-gradient(to bottom, rgba(0, 0, 0, 0.7), transparent);
  transition: opacity 0.25s;
  z-index: 6;
}

.top-right {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-shrink: 0;
}

.ad-free-badge,
.rate-badge {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  padding: 2px 8px;
  border-radius: 999px;
  font-size: 11px;
  color: #b7eb8f;
  background: rgba(82, 196, 26, 0.18);
  border: 1px solid rgba(82, 196, 26, 0.35);
}

.rate-badge {
  color: #ffd666;
  background: rgba(250, 173, 20, 0.16);
  border-color: rgba(250, 173, 20, 0.35);
}

.bottom-bar {
  position: absolute;
  left: 0;
  right: 0;
  bottom: 0;
  padding: 10px 14px 14px;
  background: linear-gradient(to top, rgba(0, 0, 0, 0.78), transparent);
  transition: opacity 0.25s;
  z-index: 6;
}

.hidden {
  opacity: 0;
  pointer-events: none;
}

.progress-row {
  display: flex;
  align-items: center;
  gap: 10px;
}

.time {
  font-size: 12px;
  color: #d9d9d9;
  font-variant-numeric: tabular-nums;
  flex-shrink: 0;
}

.progress-track {
  position: relative;
  flex: 1;
  height: 4px;
  border-radius: 2px;
  background: rgba(255, 255, 255, 0.22);
  cursor: pointer;
}

.progress-track:hover {
  height: 6px;
  margin: -1px 0;
}

.progress-buffer {
  position: absolute;
  left: 0;
  top: 0;
  bottom: 0;
  background: rgba(255, 255, 255, 0.35);
  border-radius: inherit;
}

.progress-played {
  position: absolute;
  left: 0;
  top: 0;
  bottom: 0;
  background: var(--sd-primary);
  border-radius: inherit;
}

.progress-thumb {
  position: absolute;
  right: -6px;
  top: 50%;
  transform: translateY(-50%);
  width: 12px;
  height: 12px;
  border-radius: 50%;
  background: #fff;
  box-shadow: 0 0 4px rgba(0, 0, 0, 0.5);
  opacity: 0;
  transition: opacity 0.2s;
}

.progress-track:hover .progress-thumb {
  opacity: 1;
}

.control-row {
  display: flex;
  align-items: center;
  gap: 4px;
  margin-top: 8px;
}

.spacer {
  flex: 1;
}

.ctrl-btn {
  position: relative;
  display: inline-grid;
  place-items: center;
  min-width: 34px;
  height: 34px;
  padding: 0 6px;
  border: none;
  border-radius: 8px;
  background: transparent;
  color: #f0f0f0;
  font-size: 18px;
  cursor: pointer;
  transition: background 0.18s;
}

.ctrl-btn:hover {
  background: rgba(255, 255, 255, 0.14);
}

.rate-btn {
  font-size: 13px;
  font-weight: 600;
}

.skip-label {
  position: absolute;
  font-size: 8px;
  font-weight: 700;
  bottom: 5px;
  left: 50%;
  transform: translateX(-50%);
}
</style>
