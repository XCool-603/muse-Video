#!/usr/bin/env node
/**
 * 生成「采集源视频 CDN 域名」的 Clash 规则与说明文档。
 *
 * 用法（仓库根目录）：
 *   node scripts/gen-clash-rules.mjs
 *
 * 产出：
 *   docs/clash-shortdrama-cdn.yaml   rule-provider 规则集（behavior: classical）
 *   docs/CLASH-RULES.md              说明文档（含可直接粘贴的 rules 段）
 *
 * 为什么要脚本生成而不是手写：这类采集站的 CDN 域名更换频繁 ——
 * 实测暴风资源的播放地址里同时出现 10 个不同主机，其中一部分已退役。
 * 域名清单必须从**真实播放地址**里抓，手维护必然过期。
 */

import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const cfgPath = path.join(root, 'ShortDrama.Api', 'apple-cms-sources.json')
const rulesetPath = path.join(root, 'docs', 'clash-shortdrama-cdn.yaml')
const docPath = path.join(root, 'docs', 'CLASH-RULES.md')

const UA = { 'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36' }
const today = new Date().toISOString().slice(0, 10)

// ============ 1) 抓取：接口域 + 播放地址里的 CDN 主机 ============
const cfg = JSON.parse(fs.readFileSync(cfgPath, 'utf8'))
const sources = cfg.AppleCms.sources.filter((s) => s.enabled)

const rows = []
for (const s of sources) {
  const sep = s.api.includes('?') ? '&' : '?'
  const hosts = new Set()
  let apiHost = '?'
  try { apiHost = new URL(s.api).host } catch { /* 配置里地址写坏了也不该中断生成 */ }

  try {
    for (const pg of [1, 2]) {
      const res = await fetch(`${s.api}${sep}ac=detail&pg=${pg}`, { headers: UA, signal: AbortSignal.timeout(25000) })
      const j = await res.json()
      for (const item of (j.list || []).slice(0, 20)) {
        // vod_play_url 格式：第1集$url#第2集$url，多播放源用 $$$ 分隔
        for (const group of String(item.vod_play_url || '').split('$$$')) {
          for (const seg of group.split('#')) {
            const u = seg.split('$')[1]
            if (!u) continue
            try { hosts.add(new URL(u).host) } catch { /* 忽略坏地址 */ }
          }
        }
      }
    }
  } catch (e) {
    console.warn(`  ! ${s.platformName} 抓取失败：${e.name}`)
  }

  rows.push({ name: s.platformName, code: s.platformCode, apiHost, hosts: [...hosts].sort() })
  console.log(`  ${s.platformName.padEnd(10)} 接口 ${apiHost.padEnd(26)} CDN ${hosts.size} 台`)
}

const cdnHosts = [...new Set(rows.flatMap((r) => r.hosts))].sort()
const apiHosts = [...new Set(rows.map((r) => r.apiHost).filter((h) => h && h !== '?'))].sort()

// 归并到主域：v5.ppqrrs.com → ppqrrs.com（一条 DOMAIN-SUFFIX 覆盖整个子域）
const cdnSuffixes = [...new Set(cdnHosts.map((h) => h.split('.').slice(-2).join('.')))].sort()

// ============ 2) 规则集 ============
fs.writeFileSync(rulesetPath, [
  '# 短剧聚合平台：视频 CDN 域名清单（Clash rule-provider, behavior: classical）',
  `# 由 scripts/gen-clash-rules.mjs 生成，抓取日期 ${today}`,
  `# 共 ${cdnSuffixes.length} 个主域，覆盖 ${cdnHosts.length} 台 CDN 主机`,
  'payload:',
  ...cdnSuffixes.map((d) => `  - DOMAIN-SUFFIX,${d}`),
  ''
].join('\n'), 'utf8')

// ============ 3) 文档 ============
const ruleLines = (target) => cdnSuffixes.map((d) => `  - DOMAIN-SUFFIX,${d},${target}`).join('\n')
const bullets = (list) => list.map((x) => `- \`${x}\``).join('\n')

fs.writeFileSync(docPath, `# Clash 规则：让采集源的视频 CDN 走得通

> 抓取日期：${today}　·　视频 CDN 主域 ${cdnSuffixes.length} 个（覆盖 ${cdnHosts.length} 台主机）　·　采集站接口域 ${apiHosts.length} 个
>
> 重新生成：\`node scripts/gen-clash-rules.mjs\`

## 一、为什么需要这份规则

平台里「大部分短剧看不了」不是代码问题，是**出口网络**问题。实测证据：

1. 被拒的 CDN 返回**裸 openresty 403**（无自定义页、无原因），加 \`Referer\`、换 \`User-Agent\` 完全一样 ——
   说明是按**客户端 IP** 做的访问控制，不是防盗链。
2. 同一台机器访问红牛 / 金鹰 / 魔都 / 如意的 CDN 是 **200** —— 不是本机网络坏了。
3. 本机出口是 Clash 代理的**美国机房 IP**（\`38.14.209.58\`，San Jose，Uscloud Inc）。
   服务器与浏览器同一出口，所以 CDN 拒服务器 = 拒浏览器。

结论：**把视频 CDN 域名从代理里摘出去**（或改成走国内节点），这些源立刻能播，代码一行都不用改。

## 二、域名清单

### 视频 CDN（${cdnSuffixes.length} 个主域）

已按主域归并 —— 例如 \`v1.ppqrrs.com\` ~ \`v12.ppqrrs.com\` 只需一条 \`DOMAIN-SUFFIX,ppqrrs.com\`。

${bullets(cdnSuffixes)}

### 采集站接口域（${apiHosts.length} 个，可选）

接口本身目前走代理是通的。若希望整站直连（本机在国内时更稳），一并加上：

${bullets(apiHosts)}

## 三、怎么用

### 方案 A：本机在国内，只是挂了国外代理（推荐）

把这些域名设为 **DIRECT**。粘到 Clash 配置的 \`rules:\` 里，**放在最后那条 \`MATCH\` 之前**：

\`\`\`yaml
rules:
  # ↓↓↓ 短剧聚合：采集源视频 CDN 直连 ↓↓↓
${ruleLines('DIRECT')}
  # ↑↑↑ 短剧聚合 ↑↑↑
  # ……你原有的规则……
  - MATCH,你的默认代理
\`\`\`

### 方案 B：本机在国外（或部署在海外机房）

直连没用（出口还是海外 IP），要让这些域名**走国内节点**。把上面的 \`DIRECT\` 换成你的国内代理组名：

\`\`\`yaml
rules:
${ruleLines('🇨🇳 国内节点')}
\`\`\`

### 方案 C：用 rule-provider 自动更新

本目录下的 \`clash-shortdrama-cdn.yaml\` 就是规则集，可直接引用：

\`\`\`yaml
rule-providers:
  shortdrama-cdn:
    type: http
    behavior: classical
    # 仓库若为私有，改用本地路径或自己托管
    url: "https://raw.githubusercontent.com/XCool-603/muse-Video/main/docs/clash-shortdrama-cdn.yaml"
    path: ./ruleset/shortdrama-cdn.yaml
    interval: 86400

rules:
  - RULE-SET,shortdrama-cdn,DIRECT
\`\`\`

> Clash Verge 也可以在「订阅 → 规则」里直接粘贴方案 A 的规则段。

## 四、验证是否生效

改完规则、重载 Clash 后，任选一种：

**1）直接测一集**（最直观）

点开一部**暴风资源 / 无尽资源 / 最大资源**的剧。改规则前是 502 / 播放失败，改完应当能播。

**2）命令行测 CDN 是否可达**

\`\`\`powershell
# 无尽资源的 CDN 根路径：被拒时是 403，通了是 404/200
curl.exe -s -o NUL -w "%{http_code}\\n" https://v5.ppqrrs.com/
\`\`\`

**3）看平台自己的判定**

平台列表的「可播性」是按**真实播放结果**记录的（\`PlayabilityTracker\`）：
规则生效后随便播一部，被拒的源会自动恢复成可播，平台栏的置灰与排序会自己纠正 ——
**不需要重启后端，也不需要改代码**。

## 五、注意事项

- **规则顺序**：\`DOMAIN-SUFFIX\` 必须排在兜底的 \`MATCH\` / \`GEOIP\` 之前，否则不会命中。
- **fake-ip 不影响**：Clash 的 fake-ip 模式只改 DNS 解析，规则匹配用的是域名，照常生效。
- **域名会变**：采集站 CDN 更换频繁（实测暴风资源的播放地址里同时出现 10 个主机，一部分已退役）。
  本清单从**真实播放地址**抓取，建议定期重跑 \`node scripts/gen-clash-rules.mjs\`。
- **只抓启用的源**：脚本读 \`apple-cms-sources.json\` 里 \`enabled: true\` 的源；
  关掉的源不会进清单（想包含就先把它们启用）。
`, 'utf8')

console.log(`\n已生成：\n  ${path.relative(root, rulesetPath)}\n  ${path.relative(root, docPath)}`)
console.log(`视频 CDN 主域 ${cdnSuffixes.length} 条，接口域 ${apiHosts.length} 条`)
