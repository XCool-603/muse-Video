<template>
  <div class="platform-selector">
    <span class="label">播放源</span>
    <div class="options">
      <button
        v-for="p in options"
        :key="p.code"
        class="platform-btn"
        :class="{ active: p.code === modelValue, disabled: !p.available }"
        :disabled="!p.available"
        :title="p.available ? p.name : `${p.name}（暂无可用播放源）`"
        @click="select(p)"
      >
        <span class="dot" :style="{ background: p.color }" />
        {{ p.name }}
      </button>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { usePlatformStore } from '@/stores/platform'

const props = defineProps<{
  modelValue: string
  /** 当前短剧可用的平台源编码列表 */
  sources: string[]
}>()

const emit = defineEmits<{ (e: 'update:modelValue', value: string): void }>()

const platformStore = usePlatformStore()

// 平台列表来自后端接口，不再硬编码
const options = computed(() =>
  platformStore.platforms.map((p) => ({
    code: p.platformCode,
    name: p.platformName,
    color: p.color,
    playable: p.playable,
    playNote: p.playNote,
    available: props.sources.length === 0 || props.sources.includes(p.platformCode)
  }))
)

function select(platform: { code: string; available: boolean }) {
  if (!platform.available) return
  emit('update:modelValue', platform.code)
}
</script>

<style scoped>
.platform-selector {
  display: flex;
  align-items: center;
  gap: 12px;
  flex-wrap: wrap;
}

.label {
  font-size: 13px;
  color: var(--sd-text-secondary);
  flex-shrink: 0;
}

.options {
  display: flex;
  gap: 8px;
  flex-wrap: wrap;
}

.platform-btn {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 5px 12px;
  border-radius: 999px;
  border: 1px solid var(--sd-border);
  background: #23232b;
  color: var(--sd-text);
  font-size: 13px;
  cursor: pointer;
  transition: all 0.18s;
}

.platform-btn:hover:not(.disabled) {
  border-color: var(--sd-primary);
}

.platform-btn.active {
  border-color: var(--sd-primary);
  background: rgba(255, 77, 109, 0.16);
  color: var(--sd-primary);
}

.platform-btn.disabled {
  opacity: 0.4;
  cursor: not-allowed;
}

.dot {
  width: 7px;
  height: 7px;
  border-radius: 50%;
}
</style>
