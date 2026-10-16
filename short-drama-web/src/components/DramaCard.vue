<template>
  <div class="drama-card" @click="handleClick">
    <div class="cover-wrap">
      <img
        :src="cover"
        :alt="drama.title"
        loading="lazy"
        decoding="async"
        class="cover"
        @error="onCoverError"
      />

      <div class="badges">
        <span v-if="drama.totalEpisodes" class="badge badge-episodes">
          {{ drama.totalEpisodes }}集
        </span>
        <span v-if="drama.status === 'completed'" class="badge badge-done">完结</span>
      </div>

      <div class="rating" v-if="drama.rating > 0">{{ drama.rating.toFixed(1) }}</div>

      <!-- 多平台来源标识 -->
      <div v-if="platforms.length > 1" class="multi-source">
        <span v-for="p in platforms" :key="p.code" class="source-dot" :style="{ background: p.color }" :title="p.name" />
      </div>

      <div class="play-overlay">
        <LoadingOutlined v-if="resolving" class="play-icon" spin />
        <PlayCircleFilled v-else class="play-icon" />
      </div>
    </div>

    <div class="info">
      <h3 class="title" :title="drama.title">{{ drama.title }}</h3>
      <div class="meta">
        <a-tag v-if="drama.category" :color="categoryColor" class="category-tag">
          {{ drama.category }}
        </a-tag>
        <span v-if="drama.platformName" class="platform">{{ drama.platformName }}</span>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'
import { LoadingOutlined, PlayCircleFilled } from '@ant-design/icons-vue'
// 按需引入：message 的传递闭包有 351KB 源码（含 antd 全量 locale 表 192.8KB），
// 见 src/utils/toast.ts。首屏静态依赖里一条 message 都不弹，这份体积不该跟着走。
import { toast } from '@/utils/toast'
import { dramaApi } from '@/api/drama'
import type { Drama } from '@/api/types'
import { usePlatformStore } from '@/stores/platform'

const props = defineProps<{ drama: Drama }>()

const router = useRouter()
const platformStore = usePlatformStore()

// 实时聚合结果点开时要先回源落库，这期间给个加载态，避免重复点击
const resolving = ref(false)

const FALLBACK_COVER =
  'data:image/svg+xml;charset=utf-8,' +
  encodeURIComponent(
    `<svg xmlns="http://www.w3.org/2000/svg" width="300" height="400">
      <rect width="300" height="400" fill="#f2f2f4"/>
      <text x="150" y="205" font-size="18" fill="#8a8a93" text-anchor="middle">暂无封面</text>
    </svg>`
  )

const cover = computed(() => props.drama.coverUrl || FALLBACK_COVER)

const platforms = computed(() =>
  (props.drama.sources ?? []).map((code) => ({
    code,
    name: platformStore.nameOf(code),
    color: platformStore.colorOf(code)
  }))
)

const CATEGORY_COLORS: Record<string, string> = {
  霸总: 'magenta',
  穿越: 'purple',
  重生: 'volcano',
  种田: 'green',
  剧情: 'blue'
}

const categoryColor = computed(() => CATEGORY_COLORS[props.drama.category] ?? 'default')

function onCoverError(e: Event) {
  const img = e.target as HTMLImageElement
  if (img.src !== FALLBACK_COVER) img.src = FALLBACK_COVER
}

function handleClick() {
  // 已在本地库的直接进播放页
  if (props.drama.id && props.drama.id > 0) {
    router.push(`/play/${props.drama.id}`)
    return
  }

  // 实时聚合结果（id=0）还没落库：先按需入库拿到本地 Id，再进播放页。
  // 后端会先查本地，命中就直接返回，不会再打上游。
  void resolveAndPlay()
}

async function resolveAndPlay() {
  const { platformCode, platformDramaId } = props.drama
  if (!platformCode || !platformDramaId) return
  if (resolving.value) return

  resolving.value = true
  try {
    const id = await dramaApi.resolve(platformCode, platformDramaId)
    if (id > 0) {
      router.push(`/play/${id}`)
    } else {
      toast('error', '该剧暂时无法入库')
    }
  } catch (e) {
    toast('error', (e as Error).message || '该平台暂时无法播放这部剧')
  } finally {
    resolving.value = false
  }
}
</script>

<style scoped>
.drama-card {
  cursor: pointer;
  /*
    长列表渲染优化：「加载更多」堆到上百张卡片后，屏幕外的卡片不必参与布局与绘制。
    contain-intrinsic-size 的 auto 关键字很关键 —— 首次渲染后浏览器会记住真实尺寸，
    滚动条不会因为占位高度估算不准而跳动（这里 300px 只是首次的估算值）。
    卡片高度 = 栅格宽度 × 4/3（封面 3/4）+ 约 66px 信息区，150~200px 栅格下约 266~333px。

    注意：这里**不写 transform transition**。整卡位移（translateY(-4px)）在
    长列表下会让每张 hover 的卡片都新建合成层并重绘，是「卡卡的」来源之一；
    反馈全部交给封面（见 .drama-card:hover .cover-wrap）。
  */
  content-visibility: auto;
  contain-intrinsic-size: auto 300px;
}

.cover-wrap {
  position: relative;
  width: 100%;
  aspect-ratio: 3 / 4;
  border-radius: var(--mn-radius);
  overflow: hidden;
  background: var(--mn-surface-2);
  border: 1px solid var(--mn-border);
  transition: border-color 0.18s ease, box-shadow 0.18s ease;
}

.cover {
  width: 100%;
  height: 100%;
  object-fit: cover;
  display: block;
  transition: transform 0.3s ease;
}

/*
 * hover 反馈只落在封面上：边框转主色 + 轻阴影 + 图极轻微 scale。
 * 刻意**不位移整卡** —— 长列表几百张卡片一起 translateY 会触发合成层重排，
 * 正是用户说的「卡卡的」来源之一；极简风的层次也靠克制而非位移。
 */
.drama-card:hover .cover-wrap {
  border-color: var(--mn-primary-border);
  box-shadow: var(--mn-shadow-md);
}

.drama-card:hover .cover {
  transform: scale(1.03);
}

.badges {
  position: absolute;
  top: 6px;
  left: 6px;
  display: flex;
  gap: 4px;
}

.badge {
  padding: 1px 6px;
  border-radius: 4px;
  font-size: 11px;
  line-height: 16px;
  color: var(--mn-on-primary);
  background: var(--mn-on-image-bg);
  border: 1px solid rgba(255, 255, 255, 0.14);
}

.badge-done {
  background: rgba(21, 128, 61, 0.85);
}

.rating {
  position: absolute;
  right: 6px;
  top: 6px;
  padding: 1px 6px;
  border-radius: 4px;
  font-size: 11px;
  font-weight: 600;
  color: #ffffff;
  background: var(--mn-on-image-bg);
}

.multi-source {
  position: absolute;
  left: 6px;
  bottom: 6px;
  display: flex;
  gap: 4px;
}

.source-dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  border: 1px solid rgba(255, 255, 255, 0.6);
}

.play-overlay {
  position: absolute;
  inset: 0;
  display: grid;
  place-items: center;
  background: rgba(0, 0, 0, 0.35);
  opacity: 0;
  transition: opacity 0.22s ease;
}

.drama-card:hover .play-overlay {
  opacity: 1;
}

.play-icon {
  font-size: 40px;
  color: rgba(255, 255, 255, 0.92);
}

.info {
  padding: 8px 2px 0;
}

.title {
  margin: 0;
  font-size: 13px;
  font-weight: 500;
  line-height: 1.4;
  color: var(--mn-text);
  display: -webkit-box;
  -webkit-line-clamp: 2;
  line-clamp: 2;
  -webkit-box-orient: vertical;
  overflow: hidden;
}

.meta {
  margin-top: 6px;
  display: flex;
  align-items: center;
  gap: 6px;
  min-height: 22px;
}

.category-tag {
  margin: 0;
  font-size: 11px;
  line-height: 18px;
  padding: 0 6px;
}

.platform {
  font-size: 11px;
  color: var(--mn-text-secondary);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
</style>
