import { defineStore } from 'pinia'
import { ref } from 'vue'

export const usePlayerStore = defineStore('player', () => {
  const currentDramaId = ref<number>(0)
  const currentEpisode = ref<number>(1)
  const position = ref<number>(0)
  const duration = ref<number>(0)
  const playing = ref(false)
  const adSegmentsRemoved = ref(0)

  function setCurrent(dramaId: number, episode: number) {
    currentDramaId.value = dramaId
    currentEpisode.value = episode
  }

  function updatePosition(value: number, total: number) {
    position.value = value
    duration.value = total
  }

  function reset() {
    position.value = 0
    duration.value = 0
    playing.value = false
  }

  return {
    currentDramaId,
    currentEpisode,
    position,
    duration,
    playing,
    adSegmentsRemoved,
    setCurrent,
    updatePosition,
    reset
  }
})
