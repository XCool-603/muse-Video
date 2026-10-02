<template>
  <!--
    数据还没到时的占位卡片。
    为什么要它：网格 v-if="list.length" 在请求期间什么都不渲染，整块白掉，
    数据回来又突然弹开 —— 用户感知为「卡」。占位能让高度先稳住，回来的是内容不是跳动。
  -->
  <div class="card-skeleton">
    <div class="sk-cover shimmer" />
    <div class="sk-line shimmer" style="width: 78%" />
    <div class="sk-line shimmer" style="width: 45%" />
  </div>
</template>

<style scoped>
.card-skeleton {
  border-radius: var(--sd-radius);
  overflow: hidden;
  background: var(--sd-bg-elevated);
  border: 1px solid var(--sd-border);
  padding-bottom: 10px;
}

.sk-cover {
  width: 100%;
  aspect-ratio: 2 / 3;
  margin-bottom: 10px;
}

.sk-line {
  height: 12px;
  border-radius: 6px;
  margin: 8px 10px;
}

/* 用 CSS 动画做微光扫过，不引第三方骨架屏库 */
.shimmer {
  background: linear-gradient(
    100deg,
    rgba(255, 255, 255, 0.04) 40%,
    rgba(255, 255, 255, 0.09) 50%,
    rgba(255, 255, 255, 0.04) 60%
  );
  background-size: 200% 100%;
  animation: sk-sweep 1.3s ease-in-out infinite;
}

@keyframes sk-sweep {
  from { background-position: 180% 0; }
  to { background-position: -80% 0; }
}

@media (prefers-reduced-motion: reduce) {
  .shimmer { animation: none; }
}
</style>
