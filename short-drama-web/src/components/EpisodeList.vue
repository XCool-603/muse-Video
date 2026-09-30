<template>
  <div class="episode-list">
    <div class="list-header">
      <span class="count">选集 · 共 {{ episodes.length }} 集</span>
      <div class="header-actions">
        <a-radio-group v-model:value="order" size="small" button-style="solid">
          <a-radio-button value="asc">正序</a-radio-button>
          <a-radio-button value="desc">倒序</a-radio-button>
        </a-radio-group>
      </div>
    </div>

    <div class="episode-scroll">
      <p v-if="!episodes.length" class="empty-hint">
        该平台此条目暂无正片剧集，站点只提供了片头或预告
      </p>
      <template v-else>
        <button
          v-for="ep in orderedEpisodes"
          :key="ep.id ?? ep.episodeNumber"
          class="episode-btn"
          :class="{
            active: ep.episodeNumber === currentEpisode,
            locked: ep.isLocked && !ep.isFree
          }"
          @click="handleSelect(ep)"
        >
          <span class="num">{{ ep.episodeNumber }}</span>
          <LockFilled v-if="ep.isLocked && !ep.isFree" class="lock-icon" />
        </button>
      </template>
    </div>

    <!-- 当前剧集信息 -->
    <div v-if="current" class="current-info">
      <span class="current-title">{{ current.title || `第 ${current.episodeNumber} 集` }}</span>
      <span class="sd-muted">
        时长 {{ Math.round((current.durationSeconds || 0) / 60) }} 分钟
        <template v-if="current.isFree"> · 免费</template>
        <template v-else-if="current.isLocked"> · 需解锁</template>
      </span>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'
import { LockFilled } from '@ant-design/icons-vue'
import { message } from 'ant-design-vue'
import type { Episode } from '@/api/types'

const props = defineProps<{
  episodes: Episode[]
  currentEpisode: number
}>()

const emit = defineEmits<{ (e: 'select', episode: number): void }>()

const order = ref<'asc' | 'desc'>('asc')

const orderedEpisodes = computed(() =>
  order.value === 'asc'
    ? [...props.episodes].sort((a, b) => a.episodeNumber - b.episodeNumber)
    : [...props.episodes].sort((a, b) => b.episodeNumber - a.episodeNumber)
)

const current = computed(() => props.episodes.find((e) => e.episodeNumber === props.currentEpisode))

function handleSelect(episode: Episode) {
  if (episode.isLocked && !episode.isFree) {
    message.info('该剧集需解锁后观看（演示环境未开放付费解锁）')
    return
  }
  emit('select', episode.episodeNumber)
}
</script>

<style scoped>
.episode-list {
  background: var(--sd-bg-elevated);
  border: 1px solid var(--sd-border);
  border-radius: var(--sd-radius);
  padding: 14px;
}

.list-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 12px;
}

.count {
  font-size: 14px;
  font-weight: 600;
}

.episode-scroll {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(44px, 1fr));
  gap: 8px;
  max-height: 208px;
  overflow-y: auto;
  padding-right: 4px;
}

.empty-hint {
  grid-column: 1 / -1;
  margin: 4px 0;
  font-size: 12px;
  line-height: 1.7;
  color: var(--sd-text-secondary);
}

.episode-btn {
  position: relative;
  height: 38px;
  border-radius: 8px;
  border: 1px solid var(--sd-border);
  background: #23232b;
  color: var(--sd-text);
  font-size: 13px;
  cursor: pointer;
  transition: all 0.18s;
}

.episode-btn:hover {
  border-color: var(--sd-primary);
  color: var(--sd-primary);
}

.episode-btn.active {
  background: var(--sd-primary);
  border-color: var(--sd-primary);
  color: #fff;
  font-weight: 600;
}

.episode-btn.locked {
  color: #6b6b78;
}

.lock-icon {
  position: absolute;
  top: 3px;
  right: 3px;
  font-size: 9px;
  color: #faad14;
}

.current-info {
  margin-top: 12px;
  padding-top: 12px;
  border-top: 1px solid var(--sd-border);
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
}

.current-title {
  font-size: 13px;
  color: var(--sd-text);
}
</style>
