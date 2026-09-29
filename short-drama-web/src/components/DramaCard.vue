<template>
  <div class="drama-card" @click="handleClick">
    <div class="cover-wrap">
      <img :src="cover" :alt="drama.title" loading="lazy" class="cover" @error="onCoverError" />

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
        <PlayCircleFilled class="play-icon" />
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
import { computed } from 'vue'
import { useRouter } from 'vue-router'
import { PlayCircleFilled } from '@ant-design/icons-vue'
import type { Drama } from '@/api/types'
import { usePlatformStore } from '@/stores/platform'

const props = defineProps<{ drama: Drama }>()

const router = useRouter()
const platformStore = usePlatformStore()

const FALLBACK_COVER =
  'data:image/svg+xml;charset=utf-8,' +
  encodeURIComponent(
    `<svg xmlns="http://www.w3.org/2000/svg" width="300" height="400">
      <rect width="300" height="400" fill="#242430"/>
      <text x="150" y="205" font-size="18" fill="#6b6b78" text-anchor="middle">暂无封面</text>
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
  // 未入库的聚合结果先落库再跳转播放页
  if (props.drama.id && props.drama.id > 0) {
    router.push(`/play/${props.drama.id}`)
    return
  }

  router.push({
    path: '/search',
    query: { q: props.drama.title, platform: props.drama.platformCode }
  })
}
</script>

<style scoped>
.drama-card {
  cursor: pointer;
  transition: transform 0.22s ease;
}

.drama-card:hover {
  transform: translateY(-4px);
}

.cover-wrap {
  position: relative;
  width: 100%;
  aspect-ratio: 3 / 4;
  border-radius: var(--sd-radius);
  overflow: hidden;
  background: #242430;
  border: 1px solid var(--sd-border);
}

.cover {
  width: 100%;
  height: 100%;
  object-fit: cover;
  display: block;
  transition: transform 0.3s ease;
}

.drama-card:hover .cover {
  transform: scale(1.05);
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
  color: #fff;
  background: rgba(0, 0, 0, 0.6);
  backdrop-filter: blur(4px);
}

.badge-done {
  background: rgba(82, 196, 26, 0.85);
}

.rating {
  position: absolute;
  right: 6px;
  top: 6px;
  padding: 1px 6px;
  border-radius: 4px;
  font-size: 11px;
  font-weight: 600;
  color: #ffd666;
  background: rgba(0, 0, 0, 0.6);
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
  color: var(--sd-text);
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
  color: var(--sd-text-secondary);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
</style>
