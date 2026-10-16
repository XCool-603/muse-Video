<template>
  <div class="portal">
    <div class="portal-inner">
      <!-- ---------- HERO ---------- -->
      <section class="hero">
        <p class="mn-tag hero-kicker">OPEN SOURCE · SELF-HOSTED · DOCKER</p>

        <h1 class="hero-title">短剧聚合平台</h1>

        <p class="hero-sub">
          把 25 个平台的短剧聚到一处：统一搜索、按平台浏览、竖屏播放与去广告，一个
          <code class="mn-code hero-inline">docker compose up</code> 就能跑在自己机器上。
        </p>

        <div class="hero-cta">
          <button type="button" class="mn-btn mn-btn--primary" @click="goDeploy">
            {{ deployHint || '一键复制部署命令' }}
          </button>
          <button type="button" class="mn-btn mn-btn--ghost" @click="scrollToEntries">
            查看下载入口
          </button>
        </div>

        <ul class="hero-stats">
          <li v-for="s in heroStats" :key="s.label" class="stat">
            <span class="stat-value">{{ s.value }}</span>
            <span class="stat-label">{{ s.label }}</span>
          </li>
        </ul>
      </section>

      <!-- ---------- 四个下载 / 部署入口 ---------- -->
      <section ref="entriesEl" class="section">
        <header class="section-head">
          <h2 class="section-title">拿到它 · 四条路</h2>
          <p class="section-sub">挑一条走就行，每条右侧都能一键复制</p>
        </header>

        <div class="entries">
          <article
            v-for="(entry, i) in entries"
            :key="entry.key"
            class="mn-panel entry"
          >
            <div class="entry-head">
              <span class="entry-index">{{ String(i + 1).padStart(2, '0') }}</span>
              <div class="entry-head-text">
                <h3 class="entry-title">{{ entry.title }}</h3>
                <p class="entry-desc">{{ entry.desc }}</p>
              </div>
            </div>

            <div class="entry-cmd">
              <code class="mn-code entry-code">{{ entry.command }}</code>
              <button
                type="button"
                class="mn-copy-btn portal-copy"
                :class="{ 'is-copied': copiedKey === entry.key }"
                :aria-label="`复制：${entry.title}`"
                @click="copy(entry)"
              >
                <CheckOutlined v-if="copiedKey === entry.key" />
                <CopyOutlined v-else />
                <span>{{ copiedKey === entry.key ? '已复制' : '复制' }}</span>
              </button>
            </div>

            <p class="entry-note">{{ entry.note }}</p>
          </article>
        </div>

        <p class="entries-hint">
          复制用的是浏览器剪贴板 API；在 http 或旧浏览器上会自动降级为传统复制方式，
          两条路都不通时会弹出输入框让你手动全选复制。
        </p>
      </section>

      <!-- ---------- 功能亮点 ---------- -->
      <section class="section">
        <header class="section-head">
          <h2 class="section-title">它能做什么</h2>
          <p class="section-sub">下面这些能力都已在当前版本里实现</p>
        </header>

        <div class="features">
          <article v-for="f in features" :key="f.title" class="mn-panel feature">
            <span class="feature-icon"><component :is="f.icon" /></span>
            <h3 class="feature-title">{{ f.title }}</h3>
            <p class="feature-desc">{{ f.desc }}</p>
          </article>
        </div>
      </section>

      <!--
        页脚说明：门户页**不再**自绘页脚。
        外壳 App.vue 的 .app-footer 在 /portal 下照常显示（门户页不属于沉浸式布局），
        它已经包含「本系统仅提供技术框架，不包含任何视频内容」的免责声明；
        门户页再画一个自己的页脚，用户就会看到两个页脚、免责声明重复两遍。
        仓库地址在下面四条入口里都能一键复制，导航栏也有「首页」，所以不再单独放页脚链接。
      -->
    </div>

    <!-- 剪贴板被浏览器禁用时的兜底：给出可直接手选的内容 -->
    <a-modal
      v-model:open="manualOpen"
      title="请手动复制"
      :footer="null"
      :width="640"
    >
      <p class="manual-tip">浏览器拒绝了剪贴板写入，请全选下面的内容复制：</p>
      <a-textarea :value="manualText" :rows="4" readonly @focus="selectManual" />
    </a-modal>
  </div>
</template>

<script setup lang="ts">
import { onUnmounted, ref } from 'vue'
import {
  CheckOutlined,
  CloudDownloadOutlined,
  CopyOutlined,
  DatabaseOutlined,
  DeploymentUnitOutlined,
  PlayCircleOutlined,
  SearchOutlined,
  ThunderboltOutlined
} from '@ant-design/icons-vue'

interface PortalEntry {
  key: string
  title: string
  desc: string
  /** 逐字复制到剪贴板的内容 */
  command: string
  note: string
}

/** 四个下载 / 部署入口，命令与仓库 README 的部署章节保持一致 */
const entries: PortalEntry[] = [
  {
    key: 'zip',
    title: '下载源码 zip',
    desc: '不想装 git 就直接下压缩包，解压即得到完整源码。',
    command: 'https://github.com/XCool-603/muse-Video/archive/refs/heads/main.zip',
    note: '地址可粘进浏览器地址栏或下载工具，得到 main 分支快照。'
  },
  {
    key: 'clone',
    title: '克隆仓库',
    desc: '用 git 拿到仓库，之后升级一条 git pull 就行。',
    command: 'git clone https://github.com/XCool-603/muse-Video.git',
    note: '不指定目录名时，默认克隆到当前目录下的 muse-Video 文件夹。'
  },
  {
    key: 'pull',
    title: '拉取预构建镜像',
    desc: '本机不编译，直接拉 GitHub Actions 构建好的镜像。',
    command: 'docker pull ghcr.io/xcool-603/muse-video:latest',
    note: '指向 GHCR 上的预构建镜像；在 .env 里把 SHORTDRAMA_IMAGE 填上这个地址即切到镜像模式，无需本机编译。私有包需先 docker login。'
  },
  {
    key: 'deploy',
    title: '一键部署',
    desc: '克隆 + 生成配置 + 后台构建启动，一条命令把站点跑起来。',
    command:
      'git clone https://github.com/XCool-603/muse-Video.git && cd muse-Video && cp .env.example .env && docker compose up -d --build',
    note: '跑完访问 http://localhost:18080（端口由 .env 的 APP_PORT 决定）；上线前务必改掉 .env 里的 JWT_KEY 与访问口令。'
  }
]

const heroStats = [
  { value: '25', label: '个平台聚合' },
  { value: '1', label: '条命令部署' },
  // 第三项说的是「不托管」：0 是托管量，不是站内视频数量。
  // 只写「视频文件」会被读成「本站有 0 个视频」，所以 label 必须带上「托管」二字。
  { value: '0', label: '视频文件托管' }
]

const features = [
  {
    icon: SearchOutlined,
    title: '25 个平台聚合搜索',
    desc: '跨平台并行检索后按标题相似度去重合并，一屏看完各站结果，还能看到来源标记。'
  },
  {
    icon: PlayCircleOutlined,
    title: '去广告播放',
    desc: '后端代理重写 m3u8 播放列表，边转发边过滤广告片段，前端只管播干净的流。'
  },
  {
    icon: DatabaseOutlined,
    title: '按平台浏览',
    desc: '平台优先：按源站自己的分类翻目录、看榜单，某个源挂了也不影响其它平台。'
  },
  {
    icon: CloudDownloadOutlined,
    title: '种子搜索与边下边播',
    desc: '采集源放不出来的剧可以走种子：聚合公开索引站元数据，校验落盘的分片再边下边播。'
  },
  {
    icon: DeploymentUnitOutlined,
    title: 'Docker 一键部署',
    desc: '默认 SQLite + 单容器，零外部依赖；前端由 .NET 同端口托管，一条 compose 命令起站。'
  },
  {
    icon: ThunderboltOutlined,
    title: '管理后台与收藏历史',
    desc: '数据看板、短剧管理、平台源启停与同步；前台带收藏与观看历史，多平台源一键切换。'
  }
]

const copiedKey = ref('')
let copiedTimer: ReturnType<typeof setTimeout> | undefined

/** 主 CTA 的临时提示：复制成功 / 剪贴板不可用时给个说法 */
const deployHint = ref('')
let hintTimer: ReturnType<typeof setTimeout> | undefined

const manualOpen = ref(false)
const manualText = ref('')

const entriesEl = ref<HTMLElement | null>(null)

/**
 * hero 主 CTA：复制一键部署命令。
 * 现代剪贴板不可用时会自动走 execCommand 降级；两条路都不通时才不装作成功，
 * 而是滚到部署入口并弹出可手选的内容。
 */
async function goDeploy() {
  const deploy = entries.find((e) => e.key === 'deploy')
  if (!deploy) return

  const ok = await writeClipboard(deploy.command)
  flashHint(ok ? '部署命令已复制' : '剪贴板不可用，请手动复制')

  if (!ok) {
    scrollToEntries()
    manualText.value = deploy.command
    manualOpen.value = true
  }
}

function flashHint(text: string) {
  deployHint.value = text
  if (hintTimer) clearTimeout(hintTimer)
  hintTimer = setTimeout(() => {
    deployHint.value = ''
  }, 2400)
}

function scrollToEntries() {
  entriesEl.value?.scrollIntoView({ behavior: 'smooth', block: 'start' })
}

/**
 * 写剪贴板。
 * 优先 navigator.clipboard；它只在安全上下文（https / localhost）可用，
 * 局域网用 IP 访问时是 undefined，所以必须留一条 execCommand 的老路。
 */
async function writeClipboard(text: string): Promise<boolean> {
  const nav = navigator as Navigator & { clipboard?: { writeText?: (t: string) => Promise<void> } }

  if (nav.clipboard?.writeText) {
    try {
      await nav.clipboard.writeText(text)
      return true
    } catch {
      // 继续走下面的降级方案
    }
  }

  return fallbackCopy(text)
}

/** 降级方案：临时 textarea + document.execCommand('copy') */
function fallbackCopy(text: string): boolean {
  if (typeof document === 'undefined' || !document.body) return false

  const area = document.createElement('textarea')
  area.value = text
  area.setAttribute('readonly', 'readonly')
  // 不能 display:none —— 那样选不中，execCommand 会失败
  area.style.position = 'fixed'
  area.style.top = '0'
  area.style.left = '-9999px'
  area.style.opacity = '0'
  document.body.appendChild(area)

  const selection = document.getSelection()
  const savedRange = selection && selection.rangeCount > 0 ? selection.getRangeAt(0) : null

  let ok = false
  try {
    area.select()
    area.setSelectionRange(0, area.value.length)
    ok = document.execCommand('copy')
  } catch {
    ok = false
  } finally {
    document.body.removeChild(area)
    // 还原用户原来的选区，别把人家的选择弄丢
    if (savedRange && selection) {
      selection.removeAllRanges()
      selection.addRange(savedRange)
    }
  }

  return ok
}

async function copy(entry: PortalEntry) {
  const ok = await writeClipboard(entry.command)

  if (!ok) {
    manualText.value = entry.command
    manualOpen.value = true
    return
  }

  copiedKey.value = entry.key
  if (copiedTimer) clearTimeout(copiedTimer)
  copiedTimer = setTimeout(() => {
    copiedKey.value = ''
  }, 2000)
}

function selectManual(e: FocusEvent) {
  const el = e.target as HTMLTextAreaElement | null
  el?.select()
}

onUnmounted(() => {
  if (copiedTimer) clearTimeout(copiedTimer)
  if (hintTimer) clearTimeout(hintTimer)
})
</script>

<style scoped>
/*
 * 配色一律走 --mn- 令牌，不写死任何颜色。
 * 需要半透明的叠加色时用 color-mix() 从令牌现算，
 * 这样主题换色时门户页跟着一起变。
 */
.portal {
  position: relative;
  isolation: isolate;
  min-height: 100%;
  padding-bottom: var(--mn-space-7);
  background: var(--mn-bg);
  color: var(--mn-text);
  font-family: var(--mn-font-ui);
  overflow: hidden;
}

.portal-inner {
  position: relative;
  z-index: 1;
  max-width: 1120px;
  margin: 0 auto;
  padding: 0 20px;
}

/* ---------- HERO ---------- */
.hero {
  padding: 72px 0 40px;
  text-align: center;
}

.hero-kicker {
  display: inline-block;
  letter-spacing: 0.18em;
  margin: 0 0 18px;
}

.hero-title {
  margin: 0 0 18px;
  font-size: clamp(30px, 6vw, 54px);
  line-height: 1.1;
  letter-spacing: -0.02em;
  font-weight: 600;
  color: var(--mn-text);
}

.hero-sub {
  max-width: 720px;
  margin: 0 auto;
  font-size: 15px;
  line-height: 1.85;
  color: var(--mn-text-secondary);
}

.hero-inline {
  /* 主题的 .mn-code 是 display:block；这里要嵌在段落里，改回行内块 */
  display: inline-block;
  padding: 2px 8px;
  font-size: 13px;
  vertical-align: 1px;
}

.hero-cta {
  display: flex;
  flex-wrap: wrap;
  gap: 14px;
  justify-content: center;
  margin: 32px 0 0;
}

.hero-stats {
  display: flex;
  flex-wrap: wrap;
  gap: 40px;
  justify-content: center;
  margin: 44px 0 0;
  padding: 0;
  list-style: none;
}

.stat {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 4px;
  min-width: 96px;
}

.stat-value {
  font-family: var(--mn-font-ui);
  font-size: 30px;
  font-weight: 600;
  line-height: 1;
  color: var(--mn-text);
}

.stat-label {
  font-size: 12px;
  letter-spacing: 0.08em;
  color: var(--mn-text-muted);
}

/* ---------- 区块 ---------- */
.section {
  margin-top: 64px;
}

.section-head {
  margin-bottom: 22px;
}

.section-title {
  margin: 0 0 6px;
  font-size: 20px;
  font-weight: 600;
  color: var(--mn-text);
}

.section-sub {
  margin: 0;
  font-size: 13px;
  color: var(--mn-text-secondary);
}

/* ---------- 四个入口 ---------- */
.entries {
  display: grid;
  /*
   * 第二行是防溢出的关键：minmax(420px, 1fr) 里的 420px 是硬下限，
   * 一旦可用宽度比它窄（极窄屏、或门户被塞进更窄的容器），列会强行撑到 420px，
   * 整个页面就横向溢出、右侧内容被切。
   * min(420px, 100%) 让列宽永远不超过容器；不支持 min() 的老浏览器
   * 会忽略第二行、回落到第一行的写法（现代浏览器两条都支持，后者生效）。
   */
  grid-template-columns: repeat(auto-fit, minmax(420px, 1fr));
  grid-template-columns: repeat(auto-fit, minmax(min(420px, 100%), 1fr));
  gap: 18px;
}

.entry {
  display: flex;
  flex-direction: column;
  gap: 14px;
  padding: 20px;
}

.entry-head {
  display: flex;
  gap: 14px;
  align-items: flex-start;
}

.entry-index {
  flex-shrink: 0;
  font-family: var(--mn-font-ui);
  font-size: 22px;
  font-weight: 600;
  line-height: 1.1;
  color: var(--mn-text-muted);
}

.entry-head-text {
  min-width: 0;
}

.entry-title {
  margin: 0 0 6px;
  font-size: 17px;
  font-weight: 600;
  color: var(--mn-text);
}

.entry-desc {
  margin: 0;
  font-size: 13px;
  line-height: 1.7;
  color: var(--mn-text-secondary);
}

/*
 * 代码块与复制按钮：**竖排**（命令在上、按钮在下）。
 * 横排时按钮要占掉 ~82px，代码块只剩 407px，长命令会被挤成
 * 「...muse-Video.g / it」这种断在半截的折行，看着像命令被截断了；
 * 竖排后代码块拿到整张卡片的宽度，命令能少折行、也绝不可能被按钮压住。
 */
.entry-cmd {
  display: flex;
  flex-direction: column;
  align-items: stretch;
  gap: 10px;
  min-width: 0;
}

.entry-code {
  min-width: 0;
  padding: 12px 14px;
  font-size: 12.5px;
  line-height: 1.6;
  /* 兜底：万一有比整行还长的单词，也允许块内横向滚动，不让它顶破卡片 */
  overflow-x: auto;
  /*
   * 换行策略：只在**空格**处折行，单个超长 URL 才由
   * 主题 .mn-code 的 overflow-wrap: anywhere 断词。
   * 这里给 word-break 用的是 normal —— 千万不能用「按任意字符断行」那个取值，
   * 它会把 git clone https://...muse-Video.git 断成 ...muse-Video.g / it，
   * 肉眼看就是「命令被裁掉了」。
   */
  white-space: pre-wrap;
  word-break: normal;
}

/*
 * 复制按钮的配色/成功态全部交给主题的 .mn-copy-btn 与 .mn-copy-btn.is-copied，
 * 这里只补布局。注意别在这里写 color/background —— 本组件是 scoped，
 * 选择器带 [data-v-*] 权重更高，会把主题的成功态盖掉。
 */
.portal-copy {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 6px;
  /* 竖排时靠右收，别拉成一条通栏长条 */
  align-self: flex-end;
  flex-shrink: 0;
  white-space: nowrap;
  cursor: pointer;
}

.entry-note {
  margin: 0;
  font-size: 12px;
  line-height: 1.7;
  color: var(--mn-text-muted);
  border-top: 1px dashed var(--mn-border);
  padding-top: 10px;
}

.entries-hint {
  margin: 16px 0 0;
  font-size: 12px;
  color: var(--mn-text-muted);
}

/* ---------- 功能亮点 ---------- */
.features {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(280px, 1fr));
  gap: 16px;
}

.feature {
  padding: 20px;
  transition: border-color 0.2s ease, box-shadow 0.2s ease;
}

/* 悬停反馈：边框转主色 + 轻阴影。没有发光，也没有位移（极简风的克制做法） */
.feature:hover {
  border-color: var(--mn-primary-border);
  box-shadow: var(--mn-shadow-md);
}

.feature-icon {
  display: inline-grid;
  place-items: center;
  width: 38px;
  height: 38px;
  margin-bottom: 14px;
  font-size: 19px;
  color: var(--mn-primary);
  border: 1px solid var(--mn-border);
  border-radius: var(--mn-radius-sm);
  background: var(--mn-primary-soft);
}

.feature-title {
  margin: 0 0 8px;
  font-size: 15px;
  font-weight: 600;
  color: var(--mn-text);
}

.feature-desc {
  margin: 0;
  font-size: 13px;
  line-height: 1.75;
  color: var(--mn-text-secondary);
}

/* 弹窗里的说明文字 */
.manual-tip {
  margin: 0 0 10px;
  font-size: 13px;
  color: var(--mn-text-secondary);
}

@media (max-width: 640px) {
  .hero {
    padding: 44px 0 28px;
  }

  .entries {
    grid-template-columns: 1fr;
  }

  /* 窄屏上复制按钮铺满一行，点击目标更大（.entry-cmd 已经是竖排了） */
  .portal-copy {
    align-self: stretch;
    justify-content: center;
  }

  .hero-stats {
    gap: 24px;
  }
}

/*
 * 极窄屏（iPhone SE 的 320px）：.mn-tag 是 white-space: nowrap 的胶囊，
 * 默认 11px + 0.18em 字距时宽 308px，加上 20px 内边距就是 328px ——
 * 在 320px 视口下会被 .portal 的 overflow: hidden 裁掉右端 8px。
 * 这里收字号与字距，让整颗胶囊在 320px 里完整放下。
 */
@media (max-width: 400px) {
  .hero-kicker {
    font-size: 10px;
    letter-spacing: 0.06em;
  }
}
</style>
