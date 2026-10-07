# 短剧聚合平台

基于 **.NET 10.0 Minimal API + Vue 3 + Ant Design Vue 4** 的短剧聚合平台，通过统一适配器层聚合多个短剧平台（红果、黄豆、剧果、野果、帝果）的内容，提供**统一搜索、分类浏览、竖屏播放、去广告、收藏与观看历史**能力，并配备完整的管理后台。

> ⚠️ **法律合规声明**
> 本系统仅提供技术框架与演示数据，**不包含任何真实视频内容**。生产使用前，使用方必须确保拥有所有内容的合法版权或授权，并具备必要的运营资质（ICP 备案、信息网络传播视听节目许可证等），遵守《著作权法》《网络安全法》等相关法律法规。

---

## 一、快速开始

### 环境要求

| 依赖 | 版本 |
|------|------|
| .NET SDK | 10.0+ |
| Node.js | 20+ |
| 数据库 | SQLite（默认，零配置） / PostgreSQL 16+ / MySQL 8+ |

### 1. 启动后端

```bash
dotnet restore ShortDrama.slnx
dotnet run --project ShortDrama.Api
```

首次启动会自动完成：
1. 创建 SQLite 数据库 `ShortDrama.Api/shortdrama.db`
2. 写入各平台源配置（`platform_sources`）
3. 创建默认管理员 **admin / admin123**
4. 从各平台适配器同步首批短剧数据（15 部 / 1282 集）

后端地址：<http://localhost:5080>
API 文档：<http://localhost:5080/openapi/v1.json>

### 2. 启动前端

```bash
cd short-drama-web
npm install
npm run dev
```

前端地址：<http://127.0.0.1:5173>（Vite 已配置 `/api` 代理到后端 5080 端口）

### 3. 生产模式（单进程部署）

```bash
cd short-drama-web && npm run build
# 把 dist 拷贝到后端 wwwroot，由 .NET 直接托管 SPA
cp -r dist/* ../ShortDrama.Api/wwwroot/
cd .. && dotnet run --project ShortDrama.Api -c Release
```

访问 <http://localhost:5080> 即可同时获得前端页面与 API。

### 4. 容器化部署（推荐）

**一键部署**

```bash
# Linux / macOS
git clone https://github.com/XCool-603/muse-Video.git shortdrama && cd shortdrama
./deploy.sh
```

```powershell
# Windows（PowerShell）
git clone https://github.com/XCool-603/muse-Video.git shortdrama
cd shortdrama
.\deploy.ps1
```

脚本自动完成：检查 Docker 环境 → 生成 `.env`（含**随机 JWT 密钥**）→ 构建镜像 → 启动容器 → 等待健康检查 → 打印访问地址与口令。

**一键升级**

```bash
# Linux / macOS
cd shortdrama && ./deploy.sh --update
```

```powershell
# Windows
cd shortdrama; .\deploy.ps1 -Update
```

默认**源码模式**：`git pull --ff-only` → 构建新镜像 → **构建成功才切换**（构建失败时旧容器继续跑，站点不中断）。

想**几秒升完**，改用预构建镜像（GitHub Actions 已替你编译好）：

```bash
# 一次性：环境变量优先于 .env
SHORTDRAMA_IMAGE=ghcr.io/xcool-603/muse-video:latest ./deploy.sh --update
```

想让之后每次升级（含定时任务）都走镜像，就把 `.env` 里这一行前面的 `#` 去掉：

```bash
SHORTDRAMA_IMAGE=ghcr.io/xcool-603/muse-video:latest
```

此时升级只 `docker pull` + 重启，不再编译。

不想手动升，装一次定时任务即可；也可以只检查有没有新版本（退出码 `10` = 有新版）：

```bash
./deploy.sh --install-cron     # Linux / macOS：每天 04:00 自动升级
./deploy.sh --check            # 只检查，不动容器
```

```powershell
.\deploy.ps1 -InstallTask      # Windows
.\deploy.ps1 -Check
```

**不想用脚本？纯 Docker 命令也行**

部署（三行）：

```bash
git clone https://github.com/XCool-603/muse-Video.git shortdrama && cd shortdrama
cp .env.example .env          # 想改端口/口令就编辑它；不执行这行也能跑，用内置默认值
docker compose up -d --build
```

升级（按你的模式选一条）：

```bash
# 源码模式：拉代码后重建
git pull && docker compose up -d --build

# 镜像模式（.env 里配了 SHORTDRAMA_IMAGE）：只拉镜像再重启，几秒完成
docker compose pull app && docker compose up -d
```

常用运维：

```bash
docker compose logs -f app     # 看日志
docker compose restart app     # 重启
docker compose down            # 停止（保留数据卷）
docker compose down -v         # 停止并清空数据
```

> ⚠️ 纯命令路线**不会自动替换 `JWT_KEY`**：`.env.example` 里放的是占位值，上线前自己改。
> 用 `./deploy.sh` 则会在首次运行时自动生成随机密钥。
>
> 种子搜索是可选增强，它由宿主机上的另一个进程提供。Docker 下要指向宿主机
> （`host.docker.internal`，compose 已配好），详见 [docs/DOCKER.md](docs/DOCKER.md#种子搜索可选增强)。

默认 **SQLite + 单容器**，零外部依赖，前端由 .NET 同端口托管。启动后访问 <http://localhost:18080>，管理后台 `/admin`（默认账号 `admin / admin123`），健康检查 `/health`。

> 完整文档见 **[docs/DOCKER.md](docs/DOCKER.md)**：配置项、自动更新原理与回滚、**预构建镜像（构建慢的根治办法）**、PostgreSQL 切换、备份恢复、反向代理、故障排查。

---

## 二、技术栈

| 层级 | 技术 | 版本 |
|------|------|------|
| 后端框架 | ASP.NET Core Minimal API | .NET 10.0 |
| ORM | Entity Framework Core | 10.0.12 |
| 数据库 | SQLite / PostgreSQL / MySQL | 默认 SQLite |
| 缓存 | IMemoryCache（可换 Redis） | - |
| 前端框架 | Vue 3（组合式 API + TS） | 3.5 |
| UI 组件库 | Ant Design Vue | 4.2 |
| 播放器 | hls.js | 1.5 |
| 状态管理 | Pinia + persistedstate | 2.2 |
| 构建工具 | Vite | 5.4 |
| 测试 | xUnit | 39 个用例 |

---

## 三、项目结构

```
短剧聚合/
├── ShortDrama.slnx
├── deploy.sh / deploy.ps1              # 一键部署 + 自动更新
├── Dockerfile                          # 单镜像多阶段构建（前端 + 后端）
├── docker-compose.yml                  # 默认：SQLite 单容器
├── docker-compose.postgres.yml         # 可选：PostgreSQL + Redis
├── docs/DOCKER.md                      # Docker 部署完整文档
├── ShortDrama.Domain/                  # 领域层：实体
│   └── Entities/  Drama / Episode / User / PlayProgress / Favorite / PlatformSource
├── ShortDrama.Application/             # 应用层：接口、DTO、公共算法
│   ├── Adapters/  IPlatformAdapter / IAdapterFactory
│   ├── Services/  IServices / IAdFilterService
│   ├── DTOs/      Dtos
│   └── Common/    TextSimilarity（跨平台去重）
├── ShortDrama.Infrastructure/          # 基础设施层
│   ├── Data/       AppDbContext / DbSeeder
│   ├── Adapters/   HongGuo / HuangDou / JuGuo / Generic / AdapterFactory
│   └── Services/   Aggregation / Drama / Play / Auth / Favorite / Admin / AdFilter
├── ShortDrama.Api/                     # API 入口层
│   ├── Endpoints/  Drama / Play / User / Admin
│   ├── wwwroot/    （前端构建产物）
│   └── Program.cs
├── ShortDrama.Tests/                   # 单元测试
└── short-drama-web/                    # 前端
    └── src/
        ├── api/         request / drama / user / admin / types
        ├── stores/      user / player
        ├── router/      路由与鉴权守卫
        ├── components/  DramaCard / VideoPlayer / EpisodeList / PlatformSelector
        └── views/       Home / Category / Search / Player / Profile / Login / admin/*
```

依赖方向严格单向：`Api → Infrastructure → Application → Domain`。

---

## 四、核心 API

所有接口统一前缀 `/api/v1`，统一响应格式：

```json
{ "code": 0, "message": "ok", "data": {}, "timestamp": 1790648495443 }
```

`code = 0` 表示成功，非 0 为业务错误码。

| 方法 | 路径 | 说明 | 认证 |
|------|------|------|------|
| GET | `/drama/search?q=&page=&pageSize=&platform=` | 聚合搜索（跨平台并行 + 去重合并） | 公开 |
| GET | `/drama/list` | 本地库列表（关键词/分类/平台/排序/分页） | 公开 |
| GET | `/drama/{id}` | 短剧详情（含剧集、推荐、多平台源） | 公开 |
| GET | `/drama/{id}/episodes` | 剧集列表 | 公开 |
| GET | `/drama/rank?type=hot\|recommend\|new` | 聚合榜单 | 公开 |
| GET | `/drama/latest?category=` | 今日上新 | 公开 |
| GET | `/drama/categories` | 分类列表 | 公开 |
| POST | `/drama/sync?platform=&dramaId=` | 平台内容入库 | 管理员 |
| GET | `/play/{dramaId}/{episode}` | 播放信息（返回代理地址 + 续播位置） | 公开 |
| GET | `/play/stream/{dramaId}/{episode}.m3u8` | **去广告 m3u8 代理** | 公开 |
| GET | `/play/segment?u=` | 分片 / 子播放列表代理 | 公开 |
| POST | `/play/progress` | 上报播放进度 | JWT |
| GET | `/play/progress/{dramaId}` | 查询续播位置 | JWT |
| GET | `/play/history` | 观看历史 | JWT |
| POST | `/user/register` | 注册（首个用户自动成为管理员） | 公开 |
| POST | `/user/login` | 登录（返回 JWT） | 公开 |
| GET | `/user/profile` | 个人信息 | JWT |
| GET/POST/DELETE | `/user/favorites[/{dramaId}]` | 收藏列表 / 添加 / 取消 | JWT |
| GET | `/admin/dashboard` | 数据看板 | 管理员 |
| GET/POST/PUT/DELETE | `/admin/dramas[/{id}]` | 短剧 CRUD | 管理员 |
| PUT | `/admin/dramas/{id}/episodes/{ep}` | 剧集配置（免费/锁定/播放地址） | 管理员 |
| GET | `/admin/platforms` | 平台源列表 | 管理员 |
| PUT | `/admin/platforms/{id}/toggle?enabled=` | 启用/禁用平台源 | 管理员 |
| POST | `/admin/platforms/{code}/sync` | 触发平台数据同步 | 管理员 |
| GET | `/health` | 健康检查 | 公开 |

---

## 五、数据聚合层设计

### 适配器模式

每个平台实现 `IPlatformAdapter`，把异构接口统一为标准模型：

```csharp
public interface IPlatformAdapter
{
    string PlatformName { get; }
    string PlatformCode { get; }

    Task<PlatformSearchResult> SearchAsync(string keyword, int page = 1, int pageSize = 10);
    Task<PlatformDramaDetail?> GetDramaDetailAsync(string dramaId);
    Task<string> GetPlayUrlAsync(string dramaId, int episodeNumber);
    Task<List<PlatformRankItem>> GetRankAsync(string type, int limit = 20);
    Task<List<PlatformNewItem>> GetLatestAsync(string category, int limit = 20);
}
```

`AdapterFactory` 负责注册与解析，启用状态由 `platform_sources` 表驱动，管理后台可实时开关。

### 平台真实情况核实（2026-09，逐个实测）

| 平台 | 真实存在 | 服务端可聚合 | 说明 |
|------|:---:|:---:|------|
| **红果短剧** | ✅ | ⚠️ 目录可，播放不可 | 抖音/番茄系官方 H5 `hongguoduanju.com`，React SSR。目录已实现索引；视频为 MP4 CENC（AES-128 CTR）**DRM 加密**，服务端无法解密 |
| **黄果短剧** | ✅ | ✅ **完整可播** | `huangguodrama.ai`。站点**自己公开**了 catalog API（robots.txt Allow + OpenAPI 规范），视频是**明文 MP4**（无 DRM/鉴权/签名，CORS 全开）。已实现 `HuangGuoAiAdapter`，全量 513 部已入库 |
| **黄豆短剧** | ✅ | ⚠️ 部分 | 网页版 `hddj.tv`。免费集 m3u8 无鉴权；付费集有服务端权益校验（JWT HS384 + DB）。已实现 `HuangDouAdapter`，**待国内网络验证** |
| **野果短剧** | ⚠️ 名字存在 | ❌ | 搜到的站点（`yeguodj.com` / `mmmma.mom` / `ygdj2.com`）经核实为**成人内容聚合站**，且用「最新地址」页轮换域名。**未接入** |
| **剧果短剧** | ❌ | — | GitHub 仓库搜索 0；Bing 精确短语 0；Bing 长尾词 0；Baidu 0；21 个候选域名探测全灭 |
| **帝果短剧** | ❌ | — | 同上，全部 0 |

> **关于「查无实据」的判断**：最初我用「平台名.com」猜域名，方式本身就是错的
> （红果实际在 `hongguoduanju.com`、黄豆在 `hddj.tv`、黄果在 `huangguodrama.ai`），据此下的结论已推翻重查。
> 现结论基于多种独立检索方法，且**对照组有效**：同样方法下「野果短剧」一搜即出多个站点，而「剧果短剧」「帝果短剧」零结果。

### 各适配器现状

| 适配器 | 类型 | 状态 |
|--------|------|------|
| `AppleCmsAdapter` | 真实 | ✅ 12 个采集源，5923 部短剧已入库（可播性受源站 CDN 地区限制影响，见「五之二」） |
| `HuangGuoAiAdapter` | 真实 | ✅ **完整可播**（513 部全量目录，其中 187 部有正片，明文 MP4） |
| `HongGuoWebAdapter` | 真实 | ✅ 目录已实测可用；播放受 DRM 限制 |
| `HuangDouAdapter` | 真实 | ⚠️ 协议已实现，站点在开发环境不可达，**未经端到端验证** |
| `HongGuoDemoAdapter` / `HuangDouDemoAdapter` / `JuGuoAdapter` / `GenericAdapter` | 演示 | 模拟数据，仅 `Demo:Enabled=true` 时启用 |

---

## 五之四、黄果短剧适配器（huangguodrama.ai）

### 接口是站点公开的，不是逆向

站点在 `robots.txt` 里显式 Allow，并发布了 OpenAPI 3.1.0 规范（标题 "Huangguo public catalog"）：

```
Allow: /api/agent/v1/search/
Allow: /api/agent/v1/titles/
Allow: /api/agent/v1/openapi/
Allow: /api/agent/mcp/
```

| 端点 | 说明 |
|------|------|
| `GET /api/agent/v1/openapi/` | 规范本身 |
| `GET /api/agent/v1/search/` | 搜索/列表，参数 `q` `genre` `audio` `subtitle` `completeness` `min_duration` `max_duration` `duration_scope` `locale` `featured` `page` `limit` |
| `GET /api/agent/v1/titles/{id}/` | 剧集详情 |
| `GET /api/agent/v1/titles/{id}/episodes/` | 分集列表 |

**只读，官方限流 60 请求/IP/分钟。** 分集返回的 `attributed_watch_url` 带 `hg_agent` 归因令牌，
说明站点是**主动希望**被 agent 接入的。

### 视频是明文 MP4

播放页 `<source>` 直接给出 CDN 直链，实测响应：

```
URL            https://media.huangguodrama.ai/motion/drama-episodes/{sha256}.mp4
Content-Type   video/mp4
Accept-Ranges  bytes
access-control-allow-origin: *
Server         cloudflare
```

**无 DRM、无加密、无鉴权、无签名、CORS 全开**——与红果的 MP4 CENC（AES-128 CTR）加密性质完全不同。

### 全量目录同步（513 部 / 约 15 秒）

该站标题大量为**中英双语**，用中文关键词播种命中率极低（实测 8 个关键词只捞到 38 个候选、
入库 0 部）。改为逐页遍历全量目录：

```
GET /api/agent/v1/search/?page=1..26&limit=20
```

新增 `IPagedCatalogAdapter` 可选接口，适配器实现后 `SourceBootstrapper` 自动切换为全量遍历模式。

**关键优化——目录入库 + 分集懒加载**：

| 方式 | 请求数 | 耗时 |
|------|-------|------|
| 逐部抓详情 | 513 × 2 ≈ 1026 次 | 约 20 分钟（限流 50/min） |
| **目录入库 + 懒加载** | 26 次 | **约 15 秒** |

目录同步只落元数据（标题/集数/分类/简介），分集由 `DramaService.EnsureEpisodesAsync`
在用户**首次打开详情页**时回源补齐（实测 1.2 秒）。

### 三个已修复的坑

**1. `limit` 上限是 20，不是 50。** 官方超限直接返回 400：

```json
{"error":"INVALID_INPUT","issues":[{"path":["limit"],"message":"Too big: expected number to be <=20"}]}
```

早期 clamp 到 50，导致所有详情请求被拒（表现为「候选 38 部，入库 0 部」）。测试有回归用例。

**2. 前端播放器原先只支持 HLS。** `PlayInfoDto` 新增 `StreamType`（`hls` / `mp4`），
`VideoPlayer` 按类型分流：`mp4` 交给原生 `<video>`，`hls` 走 hls.js + 后端去广告代理。

**3. 目录接口的 `catalog_episode_count` 需要透传。** `PlatformNewItem` 原先没有集数字段，
导致列表页全部显示「0 集」。已补齐 `TotalEpisodes` / `Rating` / `Description` / `Status`。

### 内容提示

该站为**混合平台**：既有常规 AI 短剧，也有成人向作品。
应用内 `tags.json` 的 176 个标签中，`[plot]`（32 个）与 `[style]`（22 个）为成人向标签组，
`other` 组含 `#成人短剧` `#AI成人短剧` 等。

补充实测：公开 catalog API 会剔除明确标记的成人标签
（`genre=chengrenduanju` / `aichengrenduanju` / `cabianduanju` 均返回 0，
而 `genre=dushi`=184、`genre=haomen`=131 正常），
但 catalog 内仍混有成人题材标题。

配置项 `HuangGuoAi:excludeAdult`（默认 `false`）可按标题/简介关键词过滤，
关键词列表见 `HuangGuoAiOptions.AdultKeywords`。

### 关于「黄果」与「黄豆」

两者是**不同平台**：黄果 = `huangguodrama.ai`（本适配器），黄豆 = `hddj.tv`（`HuangDouAdapter`）。



---

## 五之三、红果短剧适配器（hongguoduanju.com）

### 站点性质

抖音/番茄系官方 H5。判定依据：
- 静态资源托管在字节自有 CDN `lf-fe.fqnovelstatic.com/novel-fanqie-fe/.../apps/hongguo/`
  （`fqnovel` = 番茄小说）
- 页脚举报邮箱 `hongguojubao@bytedance.com`
- App 包名 `com.phoenix.read`

> 对比：`hongguoapp.cn` 虽然抄了同样的页脚（同一家公司 + 同一个 ICP 备案号），
> 但它跑的是开源 PHP 的苹果CMS（conch 主题）——**技术栈才是真相**。

### 能做什么

站点是 **React SSR**，目录数据全在 HTML 里，不需要接口：

| 能力 | 路由 | 状态 |
|------|------|------|
| 榜单 | `/rank/hot-drama`、`/rank/hot-real-drama`、`/rank/hot-ai-drama` | ✅ |
| 分类浏览 | `/category/real-drama`、`/category/comic-drama`、`/category/ai-drama`、`/category/comic` | ✅ |
| 详情 | `/detail?series_id={id}` | ✅ 标题/封面/评分/标签/简介/演员/分集 |

实测入库：**24 部 / 1869 集**，字段完整（如《好雨知时节》89 集、评分 9.3、分类「爱情」）。

### 解析策略：不依赖 CSS Modules 的 hash

站点用 CSS Modules，`pc-xxx-{hash}` 和 `m-xxx-{hash}` 的后缀 hash **每次构建都会变**。
因此解析只依赖稳定特征：

| 锚点 | 用途 |
|------|------|
| `m-card` / `m-title` / `m-episode` / `m-tag-text` 前缀 | 卡片解析（`\b` 边界匹配，不受后缀 hash 影响） |
| `href="/detail?series_id={id}"` | 剧集 ID |
| `href="/player/{sid}"` 与 `/player/{sid}/{eid}` | 分集与播放页 |
| `评分` / `热度` / `全N集` 文本特征 | 元数据 |
| `byteimg` / `douyinpic` 图片域名 | 封面 |

单元测试里的 HTML 片段**故意用了与真实页面不同的 hash**，确保改版后测试不会假通过。

### 两个已知限制（如实反映，不编造）

**1. 播放不可用** —— 视频为 MP4 CENC（AES-128 CTR）DRM 加密，密钥不下发到 Web 端。
`GetPlayUrlAsync` 抛 `NotSupportedException` 并说明原因，不返回假装能播的地址。
社区实现（`Erlmo/shortplay`，61★）用纯 C 解密，但作者声明算法不开源（六神、spade_a）。

**2. 搜索不可用** —— 搜索页是纯前端渲染，服务端不返回结果，接口也未在 bundle 中暴露。
`SearchAsync` 返回空，由站内已索引的本地库兜底（`SourceBootstrapper` 会把榜单/分类页播种进库）。

**3. 只有前几集有播放页地址** —— SSR 只渲染前 3 集的 `href`，其余格子是无链接的 `<div>`。
适配器如实留空，不编造地址。实测 24 部 × 3 = 72 集有地址，其余 1797 集只有集号。

### 关于红果的结论

**红果无法在服务端完整聚合**：目录可以索引，播放不行。
要做到「在网页里直接看红果」，只能走客户端路线（Xposed 模块改 App，或自研播放器 + DRM 解密模块），
与本项目的 .NET + Vue 服务端架构不兼容。


协议来自公开逆向文档（`ckldy/surge-modules` 的 hddj-unlock 模块）：

| 项 | 值 | 可信度 |
|----|-----|--------|
| 详情页路由 | `/watch/details/{id}` 或 `/series/details/{id}` | ✅ 文档确认 |
| 免费集流 | `/play/{id}/{ep}.m3u8`（无鉴权，AES-128 HLS） | ✅ 文档确认 |
| 付费集流 | 同端点返回 404（服务端 JWT HS384 + DB 权益） | ✅ 文档确认 |
| 播放页属性 | `data-ep-src`（流地址）、`data-ep-free`（1 免费 / 0 付费） | ✅ 文档确认 |
| 搜索 / 列表接口路径 | 候选路径自动探测 | ⚠️ **推测**，需实测 |

**刻意未实现**：付费集绕过。逆向文档给出的方式是改走第三方盗版线路
（`psfxhhox.top`，与 hddj.tv 并非同一站点），那属于接入另一个未授权源，不在适配器范围内。
付费集会被如实标记为 `IsLocked` 并在取流时抛出明确异常。

**验证方法**：本适配器开发环境无法连通 hddj.tv（DNS 解析到境外停放 IP，TCP 超时），
因此**只对 HTML 解析逻辑做了单元测试**（8 个用例）。真实连通性需要你在国内网络下验证：

```bash
dotnet run --project ShortDrama.Api
# 观察日志：
#   "黄豆短剧：搜索路径探测成功 → /xxx"  → 说明探测命中
#   "黄豆短剧：所有候选路径均不可用"      → 用 F12 抓真实路径，填进 appsettings.json 的 searchPathCandidates
```

### 关于红果短剧

红果无法在服务端聚合，原因是视频流为 DRM 加密：

- CDN 下发的是标准 MP4 CENC（AES-128 CTR）加密内容
- 密钥需要 App 侧配合签名接口获取，Web 端拿不到
- `Erlmo/shortplay` 用纯 C 实现了 `aes.c` / `mp4_cenc.c` 解密核心，但作者明确写了「涉及到的所有算法不开源（六神，spade_a）」

**结论**：红果只能走客户端路线（Xposed / 自研播放器 + DRM 模块），与本项目的 .NET + Vue 服务端架构不兼容。


---

## 五之二、苹果CMS 采集源（真实视频源）

> 📋 **完整清单见 [docs/SOURCES.md](docs/SOURCES.md)**：官方平台与 12 个启用采集源的域名、短剧分类
> `type_id`、实测入库量、平均集数、限流表现；46 个候选源的实测结果（假活 / 失效 / 重复上游）；
> 采集协议字段映射；地区限制与排查方法。

### 为什么是苹果CMS

苹果CMS V10 是国内影视站最常用的开源 CMS，其**采集接口协议是事实标准**：
接口形如 `{api}?ac=detail&wd=关键词`，返回 JSON（或 XML），其中直接包含 m3u8 直链。

这意味着**不需要逆向任何 App 签名**（原方案里的 Frida / X-Argus / X-Gorgon 全部不需要），
填一个接口地址就能接入一个新源。

### 配置方式

所有采集源集中在 **[`ShortDrama.Api/apple-cms-sources.json`](ShortDrama.Api/apple-cms-sources.json)**，
增删源只改这个文件，**不用动任何代码**，重启后端即生效：

```jsonc
{
  "AppleCms": {
    "enabled": true,
    "timeoutSeconds": 12,
    "bootstrapKeywords": ["短剧", "霸总", "重生", "穿越", "战神", "甜宠", "闪婚", "逆袭"],
    "sources": [
      {
        "platformCode": "acms_bfzy",          // 全局唯一编码
        "platformName": "暴风资源",            // 展示名
        "api": "https://bfzyapi.com/api.php/provide/vod/",
        "enabled": true,
        "format": "json",                     // json | xml
        "shortDramaTypeIds": [58, 65, 66],    // 可选，短剧分类 type_id
        "note": "备注，不参与逻辑"
      }
    ]
  }
}
```

新增一个源只需在 `sources` 数组里加一条，聚合搜索 / 榜单 / 上新会自动纳入并行调度。

### 字段映射

`AppleCmsAdapter` 把苹果CMS 字段标准化为站内模型：

| 苹果CMS 字段 | 站内字段 | 说明 |
|-------------|---------|------|
| `vod_id` | `PlatformDramaId` | 平台原始 ID |
| `vod_name` | `Title` | 标题 |
| `vod_pic` | `CoverUrl` | 封面 |
| `vod_blurb` / `vod_content` | `Description` | 简介 |
| `type_name` | `Category` | 分类（含「短」→ 短剧，否则按关键词归一） |
| `vod_remarks` | `Status` | 含「完」→ 已完结 |
| `vod_score` | `Rating` | 评分 |
| `vod_hits` | `PlayCount` | 播放量 |
| `vod_play_url` | `Episodes` | `第1集$url1#第2集$url2`，多源用 `$$$` 分隔 |

### 关键词播种 vs 分类拉取

初始入库**不用**各源的「最新上架」接口，而是用 `bootstrapKeywords` 走搜索接口播种。

原因：实测多数采集站对分类参数支持不一致——`t=27` 返回空、`type_id=27` 被直接忽略、
返回的是全站最新（动漫、综艺居多）。用短剧关键词搜索播种，入库内容才贴合本站定位。

### 默认内置的源（2026-09-30 复测）

实测 **46 个**公开采集接口，结果如下：

| 结果 | 数量 | 说明 |
|------|------|------|
| ✅ 可用、内容干净、且与其他源不重复 | **6** | 无尽 / 最大 / 如意 / 红牛 / 魔都 / 百度 —— 本次新增 |
| ✅ 原默认配置 | **6** | 暴风 / 量子 / 非凡 / 天堂 / 速播 / 金鹰 |
| ⚠️ 返回网页而非 API（假活） | 4 | 旺旺 / 天空 / 卧龙 / 芒果：响应 200 且以 `<` 开头，容易被误判成「XML 格式可用」，实际解析不出任何条目 |
| ⚠️ 内容为成人向 | 9 | 155 / 滴滴 / 嘿嘿 / 乐播 / 鲨鱼 / 森林 / 精品 / 奶香 / 辣椒 —— **已刻意排除**（155 抽样 69 条标题命中 17 条露骨内容） |
| ⚠️ 与其他源完全重复 | 4 | 无尽2 与无尽 100% 相同；极速 / 豪华 / 虎牙 与红牛 97%~99% 相同 |
| ❌ 连接失败 / 非 JSON / 403 | 17 | 见 `apple-cms-sources.json` 末尾的备查清单 |

**当前默认 12 个源**。新增的 6 个相对原有源的内容重合度只有 7%~31%。

### 播种只读第一页（2026-09-30 修复）

这是「平台很多但短剧很少」的真正原因，不是源不够多。

采集接口每页固定返回约 20 条，而 `SourceBootstrapper` 原先**每个关键词只读第 1 页**，
8 个关键词 × 20 条 ≈ 每个源只入库 20~160 部。而各源实际存量：

| 源 | `wd=短剧` 的真实 total | 修复前实际取到 |
|----|----------------------|---------------|
| 无尽 | 648 | ~20 |
| 暴风 | 546 | ~20 |
| 最大 | 492 | ~20 |
| 如意 | 130 | ~20 |

另外 `bootstrapLimitPerKeyword`（每关键词上限）这个配置项**对苹果CMS源完全不生效**：
`AppleCmsAdapter.SearchAsync` 没有把它传给 `FetchListAsync`。已一并修掉。

**修复内容**：

| 改动 | 说明 |
|------|------|
| `bootstrapPagesPerKeyword`（默认 5） | 每个关键词逐页翻。实测关键词翻页各源均有效（pg=1/2/3 零重复） |
| `bootstrapLimitPerKeyword` 真正生效 | `SearchAsync` 现在按 `pageSize` 裁剪，配置项不再是摆设 |
| `bootstrapPageDelayMs`（默认 300） | 页间节流。8 条关键词链并行翻 5 页会在短时间内对同一源打出约 40 个请求，实测会触发限流：暴风返回 429、**无尽直接超时并把该源整轮播种打废（86 秒只收到 80 部）**；加 300ms 间隔后无尽 6.9 秒收到 600 部 |
| `bootstrapTotalTimeoutSeconds` 600 → 1800 | 源数与翻页深度都上去了，600 秒会导致后面的平台被静默跳过 |

**效果（真实重新同步全部 12 个源实测）**：

| | 修复前 | 修复后 |
|---|---|---|
| 采集源合计 | 1235 部 | **5923 部（+380%）** |
| 全库总量 | 1772 部 | **6460 部** |
| 单源最高 | 147 部 | **687 部**（无尽） |

多数源会顶到单源候选上限（`max(bootstrapMaxPerSource, maxCatalogPages × 20)` = 600 部），
想再往上收就调大 `maxCatalogPages`。
暴风资源对翻页较敏感（第 4-5 页仍会 429，但能拿到 562 部）；
若某个源限流严重，把 `bootstrapPagesPerKeyword` 调小或把 `bootstrapPageDelayMs` 调大。

> **排查提示**：怎么找到真正带短剧的源？
> 先抓一个已知有短剧的站点播放页，看 `player_aaaa` 里的 `from` 字段——
> 它标识了该站的内容来自哪个采集源。例如某站 `from` 是 `bfzym3u8`，
> 对应的就是「暴风资源」`bfzyapi.com`。顺藤摸瓜比盲扫高效得多。

> **重要：源站 CDN 按 IP / 地区拒绝，可播性必须在部署机上验证。**
> 从境外机房 IP 实测时，`ppqrrs.com`（无尽）、`zuidazym3u8.com`（最大）、`bdzybf22.com`（百度）、
> `bfeng10.com`（暴风）、`cdnlz12.com`（量子）等 CDN **连根路径都返回 403**；
> 而同一批测试里 `ryplay16.com`（如意）、`hongniuzy2`（红牛）、`mdzyapi`（魔都）正常返回 200。
> 换 User-Agent、加采集站 Referer、加 CDN Referer 四种组合结果完全一致，**不是请求头问题**，是 IP/地区限制。
> 这类 CDN 一般只服务国内，国内服务器通常正常。
> 验证方法：在部署机上 `curl -I '<某一集的 m3u8 地址>'` 看是否 200。


### 关于「全集单文件」

实测发现：**部分采集源把整部短剧打包成一个 m3u8**，而不是按集切分。

12 个源共入库 5923 部后的集数分布：

| 集数 | 数量 | 占比 |
|------|------|------|
| 1 集（全集单文件） | 2951 | 50% |
| 2–30 集 | 830 | 14% |
| 30 集以上 | 2142 | 36% |

各源平均集数差异很大（2026-09-30 实测，翻页修复后）：

| 源 | 部数 | 平均集数 | 全集单文件占比 | 说明 |
|----|------|---------|--------------|------|
| 魔都资源 | 388 | **69.0** | 7% | **分集最完整** |
| 天堂资源 | 379 | 68.4 | 5% | 分集最完整 |
| 非凡资源 | 382 | 65.0 | 4% | 分集最完整 |
| 如意资源 | 576 | 62.5 | 9% | 分集最完整，且量也大 |
| 量子资源 | 606 | 41.2 | 41% | 混合 |
| 暴风资源 | 581 | 38.4 | 43% | 混合 |
| 红牛资源 | 313 | 8.1 | 44% | 多为全集单文件 |
| 百度资源 | 197 | 7.0 | 62% | 多为全集单文件 |
| 金鹰资源 | 605 | 4.8 | 69% | 多为全集单文件 |
| 速播资源 | 603 | 4.8 | 69% | 多为全集单文件 |
| 无尽资源 | 687 | 1.6 | 96% | 几乎全是全集单文件 |
| 最大资源 | 606 | 1.6 | 96% | 几乎全是全集单文件 |

单集剧**播放完全正常**（那一个 m3u8 就是整部剧），只是选集列表只有一项、自动连播不适用。
**想要分集体验，优先选 魔都 / 天堂 / 非凡 / 如意 这几个源**；
无尽与最大虽然入库量最大，但 96% 是全集单文件，分集体验最差。

### 免责声明

`apple-cms-sources.json` 中的源均为**第三方公开采集站**，内容非本系统提供。
这些站点多为未经授权的影视聚合站，使用前请自行评估法律风险。
本系统只提供技术框架与适配能力，不对接入源的内容合法性负责。


### 聚合搜索流程

```
GET /api/v1/drama/search?q=逆袭
        │
        ▼  并行 Task.WhenAll 调度（全部已注册平台）
┌───────────┬───────────┬───────────┬──────────────┐
│ 黄果(官方)  │ 红果(官方)  │ 黄豆(官方)  │ 12 个采集源   │
└───────────┴───────────┴───────────┴──────────────┘
        │  合并 + 本地库补充（跟随 platform 过滤）
        ▼
   标题相似度去重（同剧多源合并为一条，sources 记录全部来源）
        │
        ▼
   相关度 / 热度 / 评分统一排序 + 分页
```

去重算法见 `Application/Common/TextSimilarity.cs`：标题归一化（去噪、去平台后缀）→ 前缀包含判定（较短标题 ≥ 4 字且覆盖率 ≥ 40%）→ Levenshtein 相似度，阈值 0.85。

### 本地只存一部分，其余用接口实时找

这是本项目的核心设计：**不要把「本地入库量」当成「能搜到的量」**。

| 环节 | 行为 |
|------|------|
| 榜单 / 上新 | 并行打各平台接口**实时**返回，不依赖本地库 |
| 关键词搜索 | 实时聚合 + 本地库补充，两条路合并去重后统一排序 |
| 本地库 | 只存一部分：播种入库的 + 用户实际点开过的 |
| 点开未入库的结果 | 前端调 `POST /api/v1/drama/resolve` **按需入库**，拿到本地 Id 再进播放页 |

`resolve` 是**本地优先**的：先查本地库，命中直接返回（实测 26ms）；
不命中才回源拉详情并落库（实测 571ms），下次就命中本地了。
本地库因此只长成「用户真正用到的样子」，而不是把上游全量抄一遍。

> 关键约定：聚合搜索返回的实时结果 **`Id = 0`**（表示尚未落库）。
> 前端 `DramaCard` 靠它判断走哪条路——`Id > 0` 直接进播放页，`Id = 0` 先调 `resolve`。
> `resolve` 匿名可用（搜索本身就是匿名的），并挂 `api` 限流，避免被刷着灌库。

---

## 六、播放与去广告方案

### 播放链路

```
前端请求 /play/{id}/{ep}
   │
   ▼ 鉴权 + 权限校验 + Redis/内存缓存查询
后端返回 /api/v1/play/stream/{id}/{ep}.m3u8（代理地址）+ 续播位置
   │
   ▼ hls.js 拉取代理地址
后端拉取原始 m3u8 → 去广告重写 → 返回干净列表
   │
   ▼ 分片地址全部重写为 /api/v1/play/segment?u=...
后端代理转发 ts / 子播放列表（子列表再次去广告）
```

### 去广告策略（`AdFilterService`）

| 策略 | 实现方式 | 状态 |
|------|---------|------|
| 广告区间剔除 | 识别 `#EXT-X-CUE-OUT` / `#EXT-X-AD` 标记，跳过至 `#EXT-X-CUE-IN` 或 `#EXT-X-DISCONTINUITY` | ✅ 已实现 |
| 广告 URL 过滤 | 匹配 `/ad/`、`preroll`、`midroll`、`trailer`、`guanggao` 等特征 | ✅ 已实现 |
| 异常时长识别 | 时长 ≥ 15s 且为整数的分片判定为广告（正片分片通常 2–10s） | ✅ 已实现 |
| 序列号重写 | 剔除分片后重写 `#EXT-X-MEDIA-SEQUENCE`，避免播放器索引断裂 | ✅ 已实现 |
| 代理转发 | 分片与子播放列表统一走 `/play/segment`，规避跨域与第三方追踪 | ✅ 已实现 |
| 多源切换 | 同剧多平台源，前端 `PlatformSelector` 一键切换 | ✅ 已实现 |

### 播放进度同步

前端每 10 秒 + 每 15 秒定时双通道上报 `/play/progress`，切集/续播时后端返回 `resumePosition`，播放器自动 seek。

---

## 七、前端页面

| 页面 | 路由 | 说明 |
|------|------|------|
| 首页 | `/` | 搜索栏 + 平台筛选、分类快捷入口、榜单（热播/推荐/新剧）、今日上新、精选网格 |
| 分类 | `/category` | 分类 / 平台 / 排序三维筛选，无限加载 |
| 搜索 | `/search` | 跨平台聚合搜索，展示去重后结果与来源标记 |
| 播放 | `/play/:id/:episode?` | 沉浸式播放器 + 剧集选择 + 平台切换 + 收藏 + 去广告说明 + 相关推荐 |
| 我的 | `/profile` | 个人信息、收藏列表、观看历史与续播入口 |
| 登录 | `/login` | 登录 / 注册（含演示账号提示） |
| 管理后台 | `/admin` | 数据看板 / 短剧管理 / 平台源管理 |

播放器特性：HLS 自适应码率、自定义控制条（进度拖拽、±10s、倍速、静音、全屏）、键盘快捷键（`空格`/`k` 播放暂停、`←`/`→` 快退快进、`m` 静音、`f` 全屏）、自动连播下一集、断流自动恢复。

---

## 八、数据库设计

核心表：`dramas`、`episodes`、`users`、`play_progress`、`favorites`、`platform_sources`。

关键约束与索引：
- `dramas(platform_code, platform_drama_id)` 唯一 —— 保证平台内容幂等 upsert
- `episodes(drama_id, episode_number)` 唯一 + 级联删除
- `play_progress(user_id, drama_id)` 唯一 —— 每剧一条续播记录
- `favorites(user_id, drama_id)` 唯一 —— 防重复收藏
- `dramas(category)` / `dramas(platform_code)` 普通索引

### 切换数据库

修改 `ShortDrama.Api/appsettings.json`：

```jsonc
{
  "Database": { "Provider": "PostgreSql" },   // Sqlite | PostgreSql | MySql
  "ConnectionStrings": {
    "Default": "Host=localhost;Database=shortdrama;Username=admin;Password=xxx"
  }
}
```

---

## 九、测试

```bash
dotnet test ShortDrama.slnx
```

覆盖范围（39 个用例，全部通过）：

- **`AdFilterServiceTests`** —— 广告分片按 CUE-OUT 标记 / URL 特征 / 异常时长三种方式识别与剔除；干净列表不被误删；相对地址补全；代理地址重写；master playlist 处理；媒体序列号重写
- **`TextSimilarityTests`** —— 同剧不同命名归一化匹配、包含关系匹配、不同剧不误合并、空输入边界
- **`PlatformAdapterTests`** —— 5 个适配器的搜索/详情/播放地址/榜单契约一致性，未知 ID 返回 null，平台编码唯一

---

## 十、安全与限流

- **认证**：JWT（HS256），默认有效期 7 天，密钥通过 `Jwt:Key` 配置，生产必须替换
- **授权**：`/admin/**` 强制 `admin` 角色；收藏与播放进度需登录
- **密码**：PBKDF2-SHA256，100,000 次迭代 + 16 字节随机盐，`FixedTimeEquals` 恒定时间比较
- **限流**：`api` 策略 300 次/分钟，`stream` 策略 3000 次/分钟（分片代理）
  - ⚠️ 2026-09-30 修复：这两个策略原先**只注册、从未挂到任何端点**（全项目搜不到 `RequireRateLimiting`），
    实际是空转。现在已挂到聚合搜索 / 榜单 / 上新 / 按需入库这几个会打上游的接口上。
    一次聚合搜索会并行打十几个上游，没有限流时单个客户端就能把上游打爆。
- **SSRF 防护**：分片代理仅允许 `http`/`https` 协议，拒绝其他 scheme
- **CORS**：仅放行本地开发端口，生产通过 Nginx 同源代理

---

## 十一、访问口令门

私有实例的入口保护：未通过口令前，整站不可访问。

### 配置

`ShortDrama.Api/appsettings.json`：

```jsonc
{
  "AccessGate": {
    "enabled": true,
    "password": "遵纪守法世界和平",
    "cookieName": "sd_access",
    "validDays": 30,
    "gatePath": "/gate",
    "verifyPath": "/api/v1/access"
  }
}
```

### 行为

| 请求 | 未通过时 | 已通过时 |
|------|---------|---------|
| 页面（`/`、`/play/...`） | `302` → `/gate` | 正常返回 |
| API（`/api/**`） | `401` + `{"code":4010}` | 正常返回 |
| 入口页 `/gate` | 放行 | 放行 |
| 口令接口 `/api/v1/access` | 放行 | 放行 |
| 静态资源 `/assets/*` | 放行 | 放行 |
| `/health`、`/openapi/*` | 放行 | 放行 |

前端拦截器识别 `401 + code 4010`，整页跳转入口页（不用路由跳转——此时后端拦截所有请求，SPA 拿不到数据）。

### 接口

| 方法 | 路径 | 说明 |
|------|------|------|
| POST | `/api/v1/access` | 校验口令，通过后签发 Cookie |
| GET | `/api/v1/access/status` | 查询当前是否已通过 |
| POST | `/api/v1/access/logout` | 清除 Cookie |

### 安全说明

- Cookie 存的是**口令的 SHA256 派生值**，不含明文；改口令即让所有已发放 Cookie 失效
- Cookie 为 `HttpOnly` + `SameSite=Lax`，HTTPS 下自动加 `Secure`
- 口令比较用 `CryptographicOperations.FixedTimeEquals`，避免时序侧信道
- 中间件挂在管道最前面（静态文件之前），避免资源被绕过

> 这是**单口令准入**，不是身份认证系统。适合个人自用实例防止误入；
> 若要多用户/权限体系，请用 `UserEndpoints` 那套 JWT 登录。

---

## 十二、平台列表接口

前端不再硬编码平台名，改为从后端拉取：

```
GET /api/v1/drama/platforms
```

```json
{"code":0,"data":[
  {"platformCode":"huangguo","platformName":"黄果短剧","dramaCount":513,
   "playable":true,"playNote":null,"color":"#fa8c16"},
  {"platformCode":"hongguo","platformName":"红果短剧","dramaCount":24,
   "playable":false,"playNote":"视频为 DRM 加密，仅支持浏览目录与跳转官方观看",
   "color":"#ff4d4f"}
]}
```

字段说明：

| 字段 | 说明 |
|------|------|
| `platformCode` | 平台编码，用作筛选值 |
| `platformName` | 展示名 |
| `dramaCount` | 库内短剧数（前端用 `withContent()` 只显示有内容的） |
| `playable` | 是否支持站内直接播放（红果为 `false`，DRM 限制） |
| `playNote` | 不可播放时的原因说明 |
| `color` | 前端来源圆点/标签配色 |

前端通过 `usePlatformStore` 统一管理，`HomeView` / `SearchView` / `CategoryView` /
`PlatformSelector` / `DramaCard` 全部改为读接口数据。新增数据源后前端自动出现，无需改前端代码。

---

## 十三、开发路线图

| 阶段 | 周期 | 核心任务 | 状态 |
|------|------|---------|------|
| Phase 1 基础框架 | 1-2 周 | .NET 10 分层项目、Vue3+AntdV 工程、数据库设计 | ✅ 完成 |
| Phase 2 数据聚合 | 2-3 周 | 适配器模式、数据标准化、聚合搜索与去重 | ✅ 完成 |
| Phase 3 播放功能 | 1-2 周 | hls.js 集成、m3u8 去广告代理、进度同步 | ✅ 完成 |
| Phase 4 用户系统 | 1-2 周 | 注册登录、JWT、收藏、观看历史 | ✅ 完成 |
| Phase 5 管理后台 | 2 周 | 数据看板、短剧管理、平台源管理 | ✅ 完成 |
| Phase 6 优化上线 | 1-2 周 | Redis 缓存、CDN 接入、真实平台对接 | 🔜 待接入 |

---

## 十四、接入真实平台数据源

1. 新建适配器类实现 `IPlatformAdapter`（参考 `HongGuoAdapter`）
2. 在 `Infrastructure/DependencyInjection.cs` 注册：
   ```csharp
   services.AddSingleton<IPlatformAdapter, YourPlatformAdapter>();
   ```
3. 在 `DbSeeder` 的 `platforms` 列表补充平台源配置
4. 重启后端，管理后台即可看到新平台并触发同步

聚合搜索、榜单、上新、去广告代理会自动纳入新平台，无需修改上层代码。
