<template>
  <div class="player-view">
    <a-spin :spinning="loading" wrapper-class-name="player-spin">
      <template v-if="drama">
        <div class="player-layout">
          <!-- 播放器区域（竖屏沉浸式） -->
          <div class="stage">
            <div class="stage-inner">
              <!-- 平台本身不可播（例如红果短剧是 DRM 加密，密钥不下发到网页端）：
                   给出原因和官方入口，而不是让播放器空转、什么都不说 -->
              <div v-if="notPlayable" class="not-playable">
                <ExclamationCircleOutlined class="np-icon" />
                <h3 class="np-title">该平台无法在这里播放</h3>
                <p class="np-reason">{{ notPlayable.reason }}</p>
                <a
                  v-if="notPlayable.officialUrl"
                  :href="notPlayable.officialUrl"
                  target="_blank"
                  rel="noopener noreferrer"
                >
                  <a-button type="primary" size="large">
                    <template #icon><ExportOutlined /></template>
                    去官方观看
                  </a-button>
                </a>
                <p class="np-hint">也可以在右侧切换其他平台源试试</p>
              </div>

              <VideoPlayer
                v-else
                ref="playerRef"
                :src="playUrl"
                :stream-type="streamType"
                :poster="drama.coverUrl"
                :start-position="resumePosition"
                :ad-free="playInfo?.adFree ?? true"
                @ended="onEpisodeEnded"
                @timeupdate="onTimeUpdate"
                @error="onPlayError"
              >
                <template #top>
                  <div class="stage-top-info">
                    <a-button type="text" class="back-btn" @click="goBack">
                      <template #icon><ArrowLeftOutlined /></template>
                    </a-button>
                    <div class="stage-titles">
                      <h3>{{ drama.title }}</h3>
                      <span class="sd-muted">
                        <template v-if="drama.totalEpisodes > 0">
                          第 {{ currentEpisode }} 集 / 共 {{ drama.totalEpisodes }} 集
                        </template>
                        <template v-else>该源暂无正片</template>
                        <template v-if="playInfo"> · {{ playInfo.platformName }}</template>
                      </span>
                    </div>
                  </div>
                </template>
              </VideoPlayer>
            </div>

            <!-- 剧集选择 -->
            <EpisodeList
              class="stage-episodes"
              :episodes="drama.episodes"
              :current-episode="currentEpisode"
              @select="switchEpisode"
            />
          </div>

          <!-- 侧边信息区 -->
          <aside class="sidebar">
            <div class="info-card">
              <div class="info-head">
                <img :src="drama.coverUrl" class="mini-cover" :alt="drama.title" />
                <div class="info-text">
                  <h2 class="drama-title">{{ drama.title }}</h2>
                  <div class="info-meta">
                    <a-rate :value="drama.rating / 2" disabled allow-half :count="5" />
                    <span class="score">{{ drama.rating.toFixed(1) }}</span>
                  </div>
                  <div class="info-tags">
                    <a-tag color="magenta">{{ drama.category }}</a-tag>
                    <a-tag :color="drama.status === 'completed' ? 'green' : 'blue'">
                      {{ drama.status === 'completed' ? '已完结' : '连载中' }}
                    </a-tag>
                  </div>
                </div>
              </div>

              <p class="description">{{ drama.description || '暂无简介' }}</p>

              <div class="action-row">
                <a-button
                  :type="isFavorite ? 'primary' : 'default'"
                  :loading="favLoading"
                  @click="toggleFavorite"
                >
                  <template #icon>
                    <HeartFilled v-if="isFavorite" />
                    <HeartOutlined v-else />
                  </template>
                  {{ isFavorite ? '已收藏' : '收藏' }}
                </a-button>
                <a-button @click="shareDrama">
                  <template #icon><ShareAltOutlined /></template>
                  分享
                </a-button>
              </div>
            </div>

            <!-- 平台切换 -->
            <div class="source-card">
              <PlatformSelector
                v-model="selectedPlatform"
                :sources="drama.sources"
                @update:model-value="onPlatformChange"
              />
              <p class="sd-muted source-tip">
                同一部短剧可能存在于多个平台，切换播放源可获得更稳定的播放体验。
              </p>
            </div>

            <!-- 去广告说明 -->
            <div class="adfree-card">
              <div class="adfree-head">
                <SafetyCertificateOutlined class="adfree-icon" />
                <span>智能去广告已开启</span>
              </div>
              <ul class="adfree-list">
                <li>m3u8 广告分片识别与剔除</li>
                <li>播放地址追踪参数净化</li>
                <li>多平台源自动择优切换</li>
              </ul>
              <p v-if="removedSegments > 0" class="sd-muted">
                本次已跳过 {{ removedSegments }} 个广告分片
              </p>
            </div>

            <!-- 相关推荐 -->
            <div v-if="drama.recommends?.length" class="recommend-card">
              <h4 class="card-title">相关推荐</h4>
              <div class="recommend-list">
                <div
                  v-for="item in drama.recommends"
                  :key="item.id"
                  class="recommend-item"
                  @click="goDrama(item.id)"
                >
                  <img :src="item.coverUrl" class="recommend-cover" :alt="item.title" />
                  <div class="recommend-info">
                    <span class="recommend-title">{{ item.title }}</span>
                    <span class="sd-muted">{{ item.category }} · {{ item.totalEpisodes }}集</span>
                  </div>
                </div>
              </div>
            </div>
          </aside>
        </div>
      </template>

      <a-empty v-else-if="!loading" description="短剧不存在或已下架" class="empty-state" />
    </a-spin>
  </div>
</template>

<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import {
  ArrowLeftOutlined,
  ExclamationCircleOutlined,
  ExportOutlined,
  HeartFilled,
  HeartOutlined,
  SafetyCertificateOutlined,
  ShareAltOutlined
} from '@ant-design/icons-vue'
import { message } from 'ant-design-vue'
import { dramaApi, playApi } from '@/api/drama'
import { userApi } from '@/api/user'
import type { DramaDetail, PlayInfo } from '@/api/types'
import { useUserStore } from '@/stores/user'
import { usePlatformStore } from '@/stores/platform'
import VideoPlayer from '@/components/VideoPlayer.vue'
import EpisodeList from '@/components/EpisodeList.vue'
import PlatformSelector from '@/components/PlatformSelector.vue'

const route = useRoute()
const router = useRouter()
const userStore = useUserStore()
const platformStore = usePlatformStore()

const drama = ref<DramaDetail | null>(null)
const playInfo = ref<PlayInfo | null>(null)
const playUrl = ref('')
const streamType = ref<'hls' | 'mp4'>('hls')
// 直连 CDN 失败后是否已改用后端代理重试过（每次换集重置）
const triedProxyFallback = ref(false)
// 平台本身不可播时的说明（例如红果的 DRM 加密）：非空则用提示卡片替代播放器
const notPlayable = ref<{ reason: string; officialUrl: string | null } | null>(null)
const loading = ref(false)
const currentEpisode = ref(1)
const resumePosition = ref(0)
const removedSegments = ref(0)
const selectedPlatform = ref('')
const isFavorite = ref(false)
const favLoading = ref(false)

const playerRef = ref<InstanceType<typeof VideoPlayer>>()

let progressTimer: number | undefined
let lastReportedSecond = 0

const dramaId = computed(() => Number(route.params.id))

async function loadDrama() {
  if (!dramaId.value) return

  loading.value = true
  try {
    const detail = await dramaApi.detail(dramaId.value)
    drama.value = detail
    selectedPlatform.value = detail.platformCode

    const ep = Number(route.params.episode) || 1
    await switchEpisode(ep, false)

    if (userStore.isLoggedIn) {
      loadFavoriteStatus()
    }
  } catch (error) {
    message.error((error as Error).message || '加载失败')
    drama.value = null
  } finally {
    loading.value = false
  }
}

async function loadFavoriteStatus() {
  try {
    isFavorite.value = await userApi.favoriteStatus(dramaId.value)
  } catch {
    isFavorite.value = false
  }
}

async function switchEpisode(episode: number, updateRoute = true) {
  if (!drama.value) return

  currentEpisode.value = episode
  playUrl.value = ''
  removedSegments.value = 0
  // 换集时先清掉「不可播」提示，否则会一直盖住播放器
  notPlayable.value = null

  if (updateRoute) {
    router.replace(`/play/${dramaId.value}/${episode}`)
  }

  try {
    const info = await playApi.playInfo(dramaId.value, episode)
    playInfo.value = info
    resumePosition.value = info.resumePosition ?? 0
    // 后端告知流类型：hls 走 hls.js，mp4 直接原生播放
    streamType.value = (info.streamType as 'hls' | 'mp4') ?? 'hls'
    // 后端返回相对路径，直接交给播放器
    playUrl.value = info.playUrl
    // 换集时重置代理兜底标记
    triedProxyFallback.value = false
  } catch (error) {
    // 平台本身不可播（后端 4090，例如红果的 DRM 加密）→ 用提示卡片代替播放器，
    // 并把后端附带的官方播放页地址做成可点的按钮
    const np = parseNotPlayable(error)
    if (np) {
      notPlayable.value = np
      playUrl.value = ''
      return
    }
    message.error((error as Error).message || '获取播放地址失败')
  }
}

function onEpisodeEnded() {
  const total = drama.value?.totalEpisodes ?? 0
  if (currentEpisode.value < total) {
    message.success('即将播放下一集')
    switchEpisode(currentEpisode.value + 1)
  } else {
    message.info('已观看完最后一集')
  }
}

function onTimeUpdate({ current }: { current: number; duration: number }) {
  if (!userStore.isLoggedIn) return

  // 每 10 秒上报一次播放进度
  const second = Math.floor(current)
  if (second - lastReportedSecond >= 10) {
    lastReportedSecond = second
    playApi
      .reportProgress({
        dramaId: dramaId.value,
        episode: currentEpisode.value,
        position: second
      })
      .catch(() => undefined)
  }
}

/**
 * 识别「平台本身不可播」的响应（后端 4090，例如红果短剧的 DRM 加密）。
 * 后端把官方播放页地址附在消息里，这里抽出来做成可点的按钮。
 */
function parseNotPlayable(err: unknown): { reason: string; officialUrl: string | null } | null {
  const body = (err as { response?: { data?: { code?: number; message?: string } } })?.response?.data
  if (!body || body.code !== 4090) return null

  const msg = body.message || '该平台无法在服务端播放'
  const url = msg.match(/https?:\/\/\S+/)
  if (!url) return { reason: msg, officialUrl: null }

  // 去掉地址本身，以及「可跳转官方播放页观看：」这类引导语，只留原因
  const reason = msg
    .replace(url[0], '')
    .replace(/[，,]?\s*可跳转[^：:]*[：:]\s*$/, '')
    .trim()

  return { reason: reason || msg, officialUrl: url[0] }
}

function onPlayError(msg: string) {
  // 直连 CDN 失败（多半是该 CDN 不发 CORS 头）→ 自动改用后端代理重试一次。
  // 这样默认「零带宽直连」对绝大多数源都成立，少数需要代理的源也不会播不了。
  if (!triedProxyFallback.value && streamType.value === 'hls' && playUrl.value) {
    triedProxyFallback.value = true
    message.warning('直连播放源失败，改用服务器代理重试…')
    const sep = playUrl.value.includes('?') ? '&' : '?'
    // 必须写 true 而不是 1：.NET 的 bool 查询参数只接受 true/false
    playUrl.value = `${playUrl.value}${sep}proxy=true`
    return
  }

  message.error(msg)
}

function onPlatformChange(code: string) {
  const target = drama.value?.recommends?.find((d) => d.platformCode === code)
  message.info(
    target
      ? `已切换至 ${code} 播放源（对应《${target.title}》）`
      : `已切换至 ${code} 播放源`
  )
}

async function toggleFavorite() {
  if (!userStore.isLoggedIn) {
    message.warning('请先登录')
    router.push({ name: 'login', query: { redirect: route.fullPath } })
    return
  }

  favLoading.value = true
  try {
    if (isFavorite.value) {
      await userApi.removeFavorite(dramaId.value)
      isFavorite.value = false
      message.success('已取消收藏')
    } else {
      await userApi.addFavorite(dramaId.value)
      isFavorite.value = true
      message.success('收藏成功')
    }
  } catch (error) {
    message.error((error as Error).message || '操作失败')
  } finally {
    favLoading.value = false
  }
}

function shareDrama() {
  const url = window.location.href
  if (navigator.clipboard) {
    navigator.clipboard.writeText(url).then(
      () => message.success('播放链接已复制'),
      () => message.info(url)
    )
  } else {
    message.info(url)
  }
}

function goBack() {
  if (window.history.length > 1) router.back()
  else router.push('/')
}

function goDrama(id: number) {
  if (id > 0) router.push(`/play/${id}`)
}

function startProgressTimer() {
  progressTimer = window.setInterval(() => {
    if (!userStore.isLoggedIn || !playerRef.value) return
    const current = playerRef.value.getCurrentTime()
    if (current > 0) {
      playApi
        .reportProgress({
          dramaId: dramaId.value,
          episode: currentEpisode.value,
          position: Math.floor(current)
        })
        .catch(() => undefined)
    }
  }, 15000)
}

watch(
  () => route.params.id,
  (value) => {
    if (value) loadDrama()
  }
)

watch(
  () => route.params.episode,
  (value) => {
    const ep = Number(value)
    if (ep && ep !== currentEpisode.value && drama.value) {
      switchEpisode(ep, false)
    }
  }
)

onMounted(() => {
  platformStore.load()
  loadDrama()
  startProgressTimer()
})

onBeforeUnmount(() => {
  if (progressTimer) window.clearInterval(progressTimer)
})
</script>

<style scoped>
.player-view {
  min-height: 100vh;
  background: #0b0b0e;
  padding: 16px 0 40px;
}

.player-spin {
  display: block;
}

.player-layout {
  max-width: 1280px;
  margin: 0 auto;
  padding: 0 16px;
  display: grid;
  grid-template-columns: minmax(0, 1fr) 360px;
  gap: 18px;
  align-items: start;
}

.stage {
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.stage-inner {
  position: relative;
  width: 100%;
  aspect-ratio: 16 / 9;
  border-radius: var(--sd-radius);
  overflow: hidden;
  background: #000;
  border: 1px solid var(--sd-border);
}

/* 平台本身不可播时的提示卡片（替代播放器） */
.not-playable {
  position: absolute;
  inset: 0;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 12px;
  padding: 24px;
  text-align: center;
  background: var(--sd-bg-elevated);
}

.not-playable .np-icon {
  font-size: 38px;
  color: #faad14;
}

.not-playable .np-title {
  margin: 0;
  font-size: 17px;
  font-weight: 600;
  color: var(--sd-text);
}

.not-playable .np-reason {
  margin: 0;
  max-width: 460px;
  font-size: 13px;
  line-height: 1.7;
  color: var(--sd-text-secondary);
}

.not-playable .np-hint {
  margin: 0;
  font-size: 12px;
  color: var(--sd-text-secondary);
  opacity: 0.75;
}

.stage-top-info {
  display: flex;
  align-items: center;
  gap: 10px;
  min-width: 0;
}

.back-btn {
  color: #fff;
  flex-shrink: 0;
}

.stage-titles {
  min-width: 0;
}

.stage-titles h3 {
  margin: 0;
  font-size: 14px;
  font-weight: 600;
  color: #fff;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.sidebar {
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.info-card,
.source-card,
.adfree-card,
.recommend-card {
  background: var(--sd-bg-elevated);
  border: 1px solid var(--sd-border);
  border-radius: var(--sd-radius);
  padding: 14px;
}

.info-head {
  display: flex;
  gap: 12px;
}

.mini-cover {
  width: 74px;
  height: 98px;
  object-fit: cover;
  border-radius: 8px;
  flex-shrink: 0;
  background: #242430;
}

.info-text {
  min-width: 0;
  flex: 1;
}

.drama-title {
  margin: 0 0 6px;
  font-size: 16px;
  font-weight: 600;
  line-height: 1.35;
  color: var(--sd-text);
}

.info-meta {
  display: flex;
  align-items: center;
  gap: 6px;
  margin-bottom: 6px;
}

.info-meta :deep(.ant-rate) {
  font-size: 13px;
}

.score {
  font-size: 12px;
  color: #ffd666;
  font-weight: 600;
}

.info-tags {
  display: flex;
  gap: 4px;
  flex-wrap: wrap;
}

.info-tags :deep(.ant-tag) {
  margin: 0;
  font-size: 11px;
}

.description {
  margin: 12px 0 0;
  font-size: 12.5px;
  line-height: 1.65;
  color: var(--sd-text-secondary);
  display: -webkit-box;
  -webkit-line-clamp: 4;
  line-clamp: 4;
  -webkit-box-orient: vertical;
  overflow: hidden;
}

.action-row {
  display: flex;
  gap: 8px;
  margin-top: 14px;
}

.source-tip {
  margin: 10px 0 0;
  line-height: 1.6;
}

.adfree-head {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 13px;
  font-weight: 600;
  color: #b7eb8f;
  margin-bottom: 10px;
}

.adfree-icon {
  font-size: 16px;
}

.adfree-list {
  margin: 0;
  padding-left: 18px;
  font-size: 12px;
  line-height: 1.9;
  color: var(--sd-text-secondary);
}

.card-title {
  margin: 0 0 12px;
  font-size: 13px;
  font-weight: 600;
  color: var(--sd-text);
}

.recommend-list {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.recommend-item {
  display: flex;
  gap: 10px;
  cursor: pointer;
  padding: 4px;
  border-radius: 8px;
  transition: background 0.18s;
}

.recommend-item:hover {
  background: rgba(255, 255, 255, 0.05);
}

.recommend-cover {
  width: 46px;
  height: 62px;
  object-fit: cover;
  border-radius: 6px;
  flex-shrink: 0;
  background: #242430;
}

.recommend-info {
  display: flex;
  flex-direction: column;
  gap: 4px;
  min-width: 0;
  justify-content: center;
}

.recommend-title {
  font-size: 12.5px;
  color: var(--sd-text);
  display: -webkit-box;
  -webkit-line-clamp: 2;
  line-clamp: 2;
  -webkit-box-orient: vertical;
  overflow: hidden;
}

.empty-state {
  padding: 100px 0;
}

@media (max-width: 1000px) {
  .player-layout {
    grid-template-columns: minmax(0, 1fr);
  }
}

@media (max-width: 640px) {
  .player-view {
    padding-top: 0;
  }

  .player-layout {
    padding: 0;
    gap: 10px;
  }

  .stage-inner {
    border-radius: 0;
    border-left: none;
    border-right: none;
  }

  .sidebar {
    padding: 0 12px;
  }
}
</style>
