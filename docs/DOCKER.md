# Docker 一键部署

一条命令跑起来，之后可以自动跟进最新代码。

> ⚠️ **本项目是技术框架，不提供、不存储、不分发任何音视频内容。**
> 默认配置启用了 22 个第三方公开采集源，使用前请自行评估法律风险。
> 完整条款见 **[README 的免责声明](../README.md#免责声明)**。

## 部署（复制即用）

**Linux / macOS**

```bash
git clone https://github.com/XCool-603/muse-Video.git shortdrama && cd shortdrama
cp .env.example .env                 # 生成配置文件
docker compose up -d --build
```

**Windows（PowerShell）**

```powershell
git clone https://github.com/XCool-603/muse-Video.git shortdrama
cd shortdrama
Copy-Item .env.example .env
docker compose up -d --build
```

打开 <http://localhost:18080> 就能用。首次构建要拉基础镜像并编译前后端，约 3-5 分钟；之后启动只要几秒。

> ⚠️ **上线前必须改 `.env` 里的 `JWT_KEY`**（`.env.example` 里是占位值）与 `ACCESS_PASSWORD`。
> 生成随机密钥：
>
> ```bash
> openssl rand -hex 48
> ```
>
> ```powershell
> -join ((1..48) | ForEach-Object { '{0:x}' -f (Get-Random -Max 16) })
> ```

## 升级（复制即用）

**源码模式**（默认，本机构建）：

```bash
git pull --ff-only && docker compose up -d --build
```

**镜像模式**（`.env` 里配了 `SHORTDRAMA_IMAGE`，不在本机编译）：

```bash
docker compose pull app && docker compose up -d
```

两种都是**先构建/拉取成功，再切换容器** —— 失败时旧容器继续跑，站点不中断。
镜像模式详见[第三节第 7 小节](#7-构建太慢改用预构建镜像)（含私有包登录、ARM 机器注意事项）。

**定时自动升级**：直接在宿主 crontab 里写原生命令即可（不需要任何脚本）：

```bash
# 每天 04:00 拉代码并重建；日志写到项目目录下的 logs/
0 4 * * * (cd /path/to/shortdrama && git pull --ff-only && docker compose up -d --build) >> /path/to/shortdrama/logs/auto-update.log 2>&1
```

```powershell
# Windows：用任务计划程序，操作填 docker，参数填 compose up -d --build，起始位置填项目目录
```

**只看有没有新版本**：

```bash
git fetch && git log --oneline HEAD..@{u}
```

---

## 平台速查

| 平台 | 部署 | 升级 | 定时升级 |
|------|------|------|---------|
| Linux / macOS | `docker compose up -d --build` | `git pull --ff-only && docker compose up -d --build` | 宿主 crontab（见上） |
| Windows | `docker compose up -d --build` | 同上 | 任务计划程序（见上） |

默认使用 **SQLite**，数据落在命名卷里，不依赖任何外部服务。想换 PostgreSQL 见[第五节](#五数据库sqlite-与-postgresql)。

> 📌 **默认部署只有 1 个容器**，名字固定为 **`docker-muse-video`**（`docker-compose.yml` 的 `container_name`）。
> **卷名没有被固定**，仍然是 `<项目名>_shortdrama-data`（项目名默认取部署目录名）
> —— 容器名与卷名是两件独立的事，**卷名一个字都不要改**（改了等于换卷、丢数据）。
> 完整说明与「怎么清理历史遗留容器 / 名字被占怎么办」见[第一节第 5 小节](#5-容器名是怎么来的已固定为-docker-muse-video)。

> 📌 **本项目不使用部署包装脚本**（`deploy` + `.sh` / `deploy` + `.ps1` 已移除）。
> 直接用 `docker compose`：报错就是 docker 的原始报错，不会被脚本的行号和退出码藏一层；
> 也不用担心脚本把宿主 UID 之类的值固化进构建参数。需要固定的流程用 docker 自己的机制
> （`depends_on` + `healthcheck`、`build.args`、`env_file`），而不是再包一层。


---

## 一、30 秒上手

### 1. 准备

| 依赖 | 版本 | 说明 |
|------|------|------|
| Docker Engine | 20.10+ | Linux 用 `curl -fsSL https://get.docker.com \| sh` |
| Docker Desktop | 最新版 | Windows / macOS 用 |
| Docker Compose | v2（`docker compose` 子命令） | 随 Docker Desktop 自带 |
| git | 任意 | **只有自动更新需要**；不用自动更新可以不装 |

> Docker Desktop 必须处于运行状态。托盘图标显示「运行中」后再执行下面的命令。

> ⚠️ **目录名必须是 ASCII（纯英文数字）**：Compose 拿**你执行命令时所在的目录名**当项目名，
> 而项目名只允许 `a-z0-9_-`。目录名里如果**一个 ASCII 字符都没有**（例如 `短剧聚合`），
> 项目名会变成空串，`docker compose` 会**直接拒绝执行**并报 `project name must not be empty`
> —— 一个容器都不会起来。所以下面第 2 步的 `clone` 一定要给一个 ASCII 目录名（示例用 `shortdrama`）；
> 已经克隆到中文目录里了，就 `cd` 进去后用 `docker compose -p shortdrama up -d --build`
> 或在 `.env` 里取消 `COMPOSE_PROJECT_NAME=shortdrama` 那行的注释（`.env.example:67`）。规则与四种情况的完整对照见
> 下面「5. 容器名是怎么来的」。
>
> 📌 **这条只管卷名，不管容器名**：容器名已经写死成 `docker-muse-video`，**与目录名无关**；
> 目录名（或 `COMPOSE_PROJECT_NAME` / `-p`）影响的只是**卷名** `<项目名>_shortdrama-data`。
> 两者的区别见下面「5. 容器名是怎么来的」。

### 2. 一条命令

```bash
git clone https://github.com/XCool-603/muse-Video.git shortdrama
cd shortdrama
cp .env.example .env
docker compose up -d --build
```

Windows（PowerShell）：

```powershell
git clone https://github.com/XCool-603/muse-Video.git shortdrama
cd shortdrama
Copy-Item .env.example .env
docker compose up -d --build
```

> 远端已配好 SSH key 的话，也可以把地址换成 `git@github.com:XCool-603/muse-Video.git`。

首次构建需要下载基础镜像并编译前后端，大约 **3-5 分钟**；之后再次启动只需几秒。

### 3. 常用操作

```bash
docker compose logs -f app      # 看日志
docker compose restart app      # 重启
docker compose ps               # 看容器状态
docker compose down             # 停止（保留数据卷）
docker compose down -v          # 停止并清空数据
docker compose down --remove-orphans   # 停止，并清掉已删除服务的遗留容器（旧版本的 redis 等，见 5.2）
```

> 📌 起来之后**只有一个容器，名字就叫 `docker-muse-video`**（**不再**是「项目名 + 服务名 + 序号」
> 拼出来的那种名字 —— 容器名已经写死，与目录名无关）。
> 所有 `docker compose` 子命令仍然用**服务名** `app`（`docker compose logs app`、`docker compose exec app ...`），
> 两种写法都行，服务名与目录名无关、更省心。

### 4. 常用配置

所有可调项都在 `.env`（从 `.env.example` 复制而来，已被 `.gitignore` 忽略）：

| 变量 | 默认值 | 作用 |
|------|--------|------|
| `APP_PORT` | `18080` | 对外端口（容器内仍是 8080） |
| `ACCESS_GATE_ENABLED` | `true` | 是否启用访问口令门 |
| `ACCESS_PASSWORD` | `遵纪守法世界和平` | 访问口令 |
| `JWT_KEY` | 占位值 | JWT 签名密钥，**上线前必须替换** |
| `SHORTDRAMA_IMAGE` | 留空 | 留空 = 源码 / **离线**模式（本地已有 `docker-muse-video:latest` 就直接用，不再构建）；填镜像地址 = 镜像模式 |
| `TORRENT_BASE_URL` | `http://host.docker.internal:8787` | 本地种子服务地址（可选功能） |
| `POSTGRES_PASSWORD` | `shortdrama_pwd` | 仅 PostgreSQL 模式使用 |
| `COMPOSE_PROJECT_NAME` | 未设（用目录名） | **卷名**的前缀（容器名已固定为 `docker-muse-video`，**不受它影响**），见下方「容器名是怎么来的」 |

**临时改一次端口**（不改 `.env`）：`APP_PORT=9000 docker compose up -d`。
**强制不用缓存重建**：`docker compose build --no-cache && docker compose up -d`。
**改用 PostgreSQL**：见[第五节](#五数据库sqlite-与-postgresql)。

### 5. 容器名是怎么来的（已固定为 docker-muse-video）

**容器名是写死的**：`docker-compose.yml` 的 `app` 服务上有

```yaml
container_name: docker-muse-video
```

所以**不管你从哪个目录、用什么项目名部署**，容器名永远是 **`docker-muse-video`**
（完整名字就是它，前面**不带**项目名前缀）。这样定的理由与代价：

| | 说明 |
|---|---|
| ✅ **为什么这个固定名是安全的** | `docker-muse-video` 是**本项目专属**的名字，不会撞上别人的通用名 —— `redis`、`new-api`、`postgres` 这类名字人人都可能用，本项目**一个都不占** |
| ⚠️ **代价** | `container_name` 会**关掉 Compose 的项目名前缀**，于是**同一台机器上不能从两个目录同时部署本项目**：第二个目录敲 `docker compose up -d` 会报 `Conflict. The container name "/docker-muse-video" is already in use`。要跑第二份，必须先把第一份 `docker compose down` 停掉 |
| 💡 操作建议 | 按**服务名**操作永远可用：`docker compose exec app` / `docker compose cp app:/data/...`；想按名字操作时现在可以直接写 `docker-muse-video` |

> ⚠️⚠️ **「容器名固定」与「卷名随项目名」是两件独立的事，千万别混：**
>
> | 名字 | 谁决定 | 当前值 |
> |---|---|---|
> | **容器名** | `docker-compose.yml` 里的 `container_name`（**写死**） | **`docker-muse-video`** —— 与项目名 / 目录名**无关** |
> | **卷名** | Compose 按**项目名**拼出来（`<项目名>_<卷名>`），**没有被写死** | `<项目名>_shortdrama-data`，例如 `shortdrama_shortdrama-data` |
>
> 🚫 **红线：卷名一个字都不要改。**
> 改卷名 = 换卷 = 服务器上**已有的 SQLite 数据、收藏、观看历史全部变成孤儿**
> （Compose 会去挂一个全新的空卷，应用起来就是空的）。
> 本次只固定**容器名**；卷名继续随项目名走，`docker volume ls` 里看到的还是
> `<项目名>_shortdrama-data`。改 `COMPOSE_PROJECT_NAME` / 用 `-p` 同样会换卷，
> 已经有数据时**不要动**（详见本节末尾的警告）。

**卷名（以及 PostgreSQL 模式下 `postgres` 容器的名字）仍然由项目名决定。**
**项目名默认取「你执行 `docker compose` 时所在的目录名」**，并做两步规范化：
**① 全部转小写；② 只保留 `a-z`、`0-9`、`-`、`_` 这四类字符，其余字符一律「删掉」**
（**不是**换成 `-`），最后再砍掉开头的 `-` / `_`。

所以目录名直接决定**卷名**，四种情况的结果**完全不同**：

| 目录名 | 规范化后的项目名 | 卷名 | 结果 |
|---|---|---|---|
| `shortdrama` | `shortdrama` | `shortdrama_shortdrama-data` | 正常 |
| `ShortDrama`（含大写） | `shortdrama` | `shortdrama_shortdrama-data` | 正常 |
| `short drama`（含空格） | `shortdrama` | `shortdrama_shortdrama-data` | 正常 |
| `短剧聚合`（纯中文） | **空串** | —— | **`docker compose` 直接拒绝执行** |

- 前三种「跑得起来」，但项目名可能**不是你以为的那个**（`ShortDrama` → `shortdrama`）；
- 第四种是**纯中文 / 纯非 ASCII 目录名**（`短剧聚合`、`短剧聚合-平台` 之类）：
  中文字符会被全部删掉，规范化结果是**空串**。Compose 不接受空项目名，**所有
  `docker compose` 子命令都会立刻失败**，报：

  ```
  project name must not be empty
  ```

  **一个容器、一个卷都不会被创建** —— 不存在「中文前缀的卷名」这种东西。
  ⚠️ **本仓库当前的工作目录就是 `C:\Work\短剧聚合\短剧聚合`（中文）**，所以在这个目录里
  直接敲 `docker compose up -d` **必然报上面这个错**。必须先做下面两件事之一。
- 混合目录名（中文 + ASCII，例如 `短剧聚合-drama`）不会报错，但项目名是**ASCII 那一半**
  （`drama`）—— 换个目录名就可能与别的项目**撞前缀**，不建议。

> **上面这套规则怎么来的**（写文档的机器上没装 docker，所以是**源码级核对**、不是实跑）：
> 规范化规则逐字来自 Compose 官方实现 `compose-go` 的 `NormalizeProjectName()`
> ——`regexp.MustCompile("[a-z0-9_-]")` + `strings.ToLower` + `TrimLeft(s, "_-")`，
> 也就是「**只保留**这四类字符」而不是「把别的换成 `-`」；空项目名的报错来自同一仓库的
> `loader.go`（`errors.New("project name must not be empty")`），并被两个官方单元测试
> （`cli/options_test.go` 的 "by name must not come from root directory"、
> `loader/loader_test.go` 的 "default" 用例）断言。官方文档
> <https://docs.docker.com/compose/how-tos/project-name/> 也写明项目名
> 「must contain only lowercase letters, decimal digits, dashes, and underscores」。
> 本仓库内可复现的复算脚本：
> `.dsh-team/deploy-polish/work/consistency-auditor/verify-project-name.ps1`
> （不依赖 docker，逐字符复算上表 4 个目录名 + 4 个边界情形）。

想自己定**项目名**（只影响卷名），两种方式（优先级从高到低）：

```bash
# ① 一次性：命令行 -p
docker compose -p mydrama up -d --build          # → 卷名 mydrama_shortdrama-data（容器名仍是 docker-muse-video）

# ② 持久：写进 .env（`.env.example:46-67` 有这一段，把最后一行取消注释即可）
COMPOSE_PROJECT_NAME=mydrama
```

> ⚠️ **改项目名等于换卷**：卷名会跟着变，**旧数据卷不会被复用**（Compose 会建一套新的空卷），
> 已有数据就"看不见"了。已经跑过且有数据时，先 `docker volume ls | grep shortdrama` 记下现名，
> 再决定要不要改。**`-p` / `COMPOSE_PROJECT_NAME` 只改卷名，改不动容器名**（容器名已写死）。

改完 `.env` 后需要重新创建容器才会生效：`docker compose up -d`。

#### 5.1 默认就是 1 个容器；PostgreSQL 是可选的 2 容器路径

| 部署方式 | 容器 | 数量 |
|---|---|---|
| **默认**（`docker compose up -d`，SQLite） | 只有 `app` → **`docker-muse-video`** | **1 个** |
| **可选**（`-f docker-compose.yml -f docker-compose.postgres.yml`） | `docker-muse-video`（app）+ `<项目名>-postgres-1`（postgres） | **2 个** |

- **默认路径不依赖任何外部服务**：没有 Redis、没有 Nginx、没有数据库容器，
  前端静态资源由 .NET 直接托管，全部打进同一个镜像（见[附：文件说明](#附文件说明)）。
- PostgreSQL 覆盖文件是**可选**的，只有在命令里显式带上 `-f docker-compose.postgres.yml`
  时才会多出 `postgres` 容器；`postgres` 服务**刻意不设 `container_name`**，它的名字仍带项目名前缀。
- 你如果在 `docker ps` 里看到**多于上面这些**的容器（`redis`、`new-api` 之类），
  那多半是**历史遗留**或**别的项目**的容器，见下面 5.2。

#### 5.2 清理历史遗留容器（旧版本的 redis 等）

**我们早先的版本里有过一个 `redis` 服务，早已删除** —— 但删除的只是 compose 里的定义，
**旧容器可能还留在机器上**（`docker ps -a` 里能看到），这也是"怎么有这么多容器"的常见来源。

```bash
# ① 先看清有哪些容器、分别属于哪个项目
docker ps -a --format 'table {{.Names}}\t{{.Image}}\t{{.Status}}'
docker compose ls -a

# ② 清掉本项目的容器，连"服务已从 compose 里删掉"的遗留容器（--remove-orphans）一起
cd <项目目录>
docker compose down --remove-orphans

# ③ 复查
docker ps -a
```

> ⚠️ **`--remove-orphans` 只清理"属于本项目"的遗留容器**（按 `com.docker.compose.project`
> 标签判定）。在**别的项目目录**里敲它，清掉的是**那个项目**的容器 —— 所以第 ① 步先看清归属。
> 名字看着可疑、但属于**别人**的容器（`redis`、`new-api` 之类）**不要删**：
> 删了会打断别人的应用，还可能连带删掉匿名卷。
>
> 📌 数据不会因此丢失：`docker compose down` **保留数据卷**，卷名依旧是 `<项目名>_shortdrama-data`
> （只有 `-v` 才会删卷）。

> 📌 **老部署升级后容器名会变**：你之前跑出来的容器名是「项目名 + 服务名 + 序号」那种（带项目名前缀），
> 现在固定成 `docker-muse-video` 了，所以 `docker compose up -d` 会**重建容器**、换成新名字。
> **卷名没变，所以数据不丢**。重建后如果 `docker ps -a` 里还留着旧名字的容器，
> 用上面的 `docker compose down --remove-orphans` 清掉即可。

#### 5.3 名字被占了怎么解封

因为容器名是写死的，这个报错现在**特指** `docker-muse-video` 这个名字被占着
（`Conflict. The container name "/docker-muse-video" is already in use`）：

```bash
# ① 先查它属于谁（working_dir 直接告诉你它在哪个目录起的）
docker inspect docker-muse-video --format '{{index .Config.Labels "com.docker.compose.project"}} ← {{index .Config.Labels "com.docker.compose.project.working_dir"}}'

# ② 非破坏性解封：改个名就够，不删数据、不影响别的服务
docker rename docker-muse-video docker-muse-video-old

# ③ 顺手把旧容器停掉（两个容器同时挂同一个数据卷、同时写 SQLite 会损坏库）
docker stop docker-muse-video-old

# ④ 回到项目目录，正常启动；名字就腾出来了
cd <项目目录>
docker compose up -d
```

> ⚠️ **不要顺手 `docker rm -f docker-muse-video`**：先确认它确实没用了、里面的数据你也不需要。
> 它是本项目旧容器时（换过目录、或早先版本留下的）删掉没问题；
> 但如果 `working_dir` 指向**别的项目**，那是别人的容器，改名解封即可，**不要删**。
> 确认不需要之后再 `docker rm docker-muse-video-old` 收尾。

### 6. 改运行端口

端口由 **`.env` 里的 `APP_PORT`** 决定；compose 里的 `${APP_PORT:-18080}` 只是**没配 `.env` 时的兜底值**。

**三处端口必须同时存在且一致**，缺任何一处都会表现为"打不开"：

| 层 | 写在哪 | 作用 | 漏了会怎样 |
|---|---|---|---|
| 应用监听 | `Dockerfile` 的 `ASPNETCORE_URLS=http://+:8080` | 真正决定进程听哪个端口 | 容器内没人在听，映射再对也没用 |
| 容器内端口 | `Dockerfile` 的 `EXPOSE 8080` | 只是文档与 `-P` 的依据，**不改变实际监听** | 不影响连通，但别人看不出该映哪个端口 |
| 对外映射 | compose 的 `ports: "${APP_PORT:-18080}:8080"` | 把容器内端口发布到宿主 | 宿主上没有 LISTEN，外网必然访问不到 |

> ⚠️ 别把 `expose:` 当 `ports:` 用 —— `expose` 只对**同一网络内的其它容器**可见，
> **不会**发布到宿主。排查顺序（容器在跑吗 → 宿主在监听吗 → 本机 curl 通吗 → 外网）
> 见[第九节](#九故障排查)。

> ⚠️ **升级后端口没变？** 因为 `.env` 优先于 compose 的默认值 ——
> 你现有的 `.env` 里如果还写着 `APP_PORT=8080`，改 compose 的默认值**不会**影响你，必须改 `.env`。
> （仓库把默认端口从 8080 换成 18080 只是给新部署用的。）

**改法**：

```bash
cd <项目目录>
grep '^APP_PORT' .env                          # 先看当前值
sed -i 's/^APP_PORT=.*/APP_PORT=9000/' .env    # 改成你要的端口（或直接编辑 .env）
docker compose up -d                           # 重建容器让新端口生效
```

**确认最终生效的端口** —— 这是唯一可信的检查方式，直接看 compose 解析后的结果：

```bash
docker compose config | grep -A3 'ports:'
# 期望：published: "9000"   target: 8080
```

**别忘了放行新端口**（云安全组 + 宿主防火墙，两处都要，详见[第九节](#九故障排查)）：

```bash
ufw allow 9000/tcp                                        # Ubuntu / Debian
firewall-cmd --permanent --add-port=9000/tcp && firewall-cmd --reload   # CentOS / RHEL
```

> 注意：**只有对外端口要改**。容器内仍然是 8080 —— 容器内的端口是隔离的，
> 跟着改只会多改一堆地方（`EXPOSE`、`ASPNETCORE_URLS`、健康检查）。


---

## 二、跑起来之后

| 项目 | 地址 / 值 |
|------|-----------|
| 站点首页 | <http://localhost:18080> |
| 管理后台 | <http://localhost:18080/admin> |
| 默认管理员 | `admin` / `admin123` |
| 健康检查 | <http://localhost:18080/health> |
| API 文档 | <http://localhost:18080/openapi/v1.json> |

**访问口令门**：默认开启。第一次打开站点会跳到 `/gate`，需要输入口令才能访问整站。

- 默认口令：`遵纪守法世界和平`
- 修改方式：改 `.env` 里的 `ACCESS_PASSWORD`，然后 `docker compose up -d` 重新创建容器
- 完全关闭：`.env` 里设 `ACCESS_GATE_ENABLED=false`
- `/health` 与 `/openapi/*` 不受口令门拦截，方便做健康检查和监控

> 上线前请务必：改掉 `ACCESS_PASSWORD`、改掉 `admin123` 密码、确认 `JWT_KEY` 已换成随机值（生成命令见第一节）。

---

## 三、更新与自动更新

源码模式：**拉取最新代码 → 重建镜像 → 重启容器**。不需要镜像仓库，不依赖任何外部服务。

### 1. 手动更新一次

```bash
cd <项目目录>
git pull --ff-only && docker compose up -d --build
```

**顺序很重要**：compose 会先把新镜像**构建成功**，再重建容器 —— 构建失败时旧容器继续跑，站点不中断。
（这正是以前那个部署脚本里唯一有点价值的行为，而 `docker compose` 本身就自带。）

### 2. 定时自动更新（写进宿主 crontab 即可）

**Linux / macOS**

```bash
crontab -e
```

```cron
# 每天 04:00 拉代码并重建；日志写到项目目录下的 logs/
0 4 * * * (cd /path/to/shortdrama && git pull --ff-only && docker compose up -d --build) >> /path/to/shortdrama/logs/auto-update.log 2>&1
```

```bash
crontab -l | grep shortdrama        # 查看
crontab -e                          # 删掉那一行即可取消
```

> 📌 两个容易踩的点：
> **① 括号不能省。** `a && b && c >> log` 里的重定向**只绑定链尾那条命令** ——
> 写成不带括号的形式时，`cd` 和 `git pull` 的输出（**包括它们失败的原因**）不会进日志，
> 而这个日志正是你排查「定时更新为什么失败」的唯一依据。
> **② 日志目录要先建**：`mkdir -p logs`（目录不存在时重定向会直接失败，任务等于没跑）。
> 路径写成绝对路径，因为重定向是由 cron 的 shell 在外层求值的。

**Windows**：用「任务计划程序」新建任务，操作填 `docker`、参数填 `compose up -d --build`、
「起始于」填项目目录；触发设为每天 04:00。想留日志就把参数写成
`-NoProfile -Command "& { cd 'C:\shortdrama'; git pull --ff-only; docker compose up -d --build } *>> 'C:\shortdrama\logs\auto-update.log'"` ——
`& { ... }` 同样不能省。

> ⚠️ **cron 的 `PATH` 很短**，通常找不到 `docker` / `git`。两种解法：在 crontab 顶部加
> `PATH=/usr/local/bin:/usr/bin:/bin`，或者命令里写绝对路径（`/usr/bin/docker`）。
> 这是"定时任务没跑"最常见的原因。

### 3. 只想检查有没有新版本

```bash
cd <项目目录>
git fetch --quiet && git log --oneline HEAD..@{u}
```

有输出 = 有新版本；无输出 = 已是最新。接监控/通知时用退出码：

```bash
git fetch --quiet
if [ -n "$(git log --oneline HEAD..@{u})" ]; then
    echo "短剧聚合有新版本" | mail -s "更新提醒" you@example.com
fi
```

### 4. 更新过程做了什么

| 步骤 | 说明 |
|------|------|
| 1. 拉取 | `git pull --ff-only` 快进，绝不产生意外的合并提交 |
| 2. 构建 | `docker compose up -d --build` **先构建新镜像**；构建失败时旧容器继续跑 |
| 3. 切换 | 构建成功后重建容器，健康检查不通过时你能从 `docker compose ps` 立刻看出来 |

### 5. 更新失败了怎么回滚

```bash
cd <项目目录>
git log --oneline -5                       # 找到上一个正常的提交
git reset --hard <上一个提交>
docker compose up -d --build
```

> 注意：代码回滚了，但**数据库结构不会自动回退**。如果新版本包含破坏性的数据库变更，
> 请用[第六节](#备份sqlite)的备份恢复。

### 6. 注意事项

**私有仓库的凭据**：远端若是 SSH 地址（`git@github.com:...`），定时任务必须有可用的私钥，
否则报 `Permission denied (publickey)`。改用 HTTPS 地址，或给该用户配置 SSH key。

**git 属主告警**：仓库属主与执行用户不一致时 git 会拒绝操作：

```bash
git config --global --add safe.directory "<仓库绝对路径>"
```

**不要在自动更新的仓库里改代码**：定时任务只跟随远端分支。在部署机上直接改代码会让工作区变「脏」，
`git pull --ff-only` 会失败并保留你的改动（这是好事，但更新就停了）—— 改动请走 `git stash` 或提交到别处。

### 7. 构建太慢？改用预构建镜像

源码模式每次更新都要在你机器上重新编译前端和后端，弱 VPS 上要几分钟且吃满 CPU。彻底的解法是让 GitHub Actions 构建好镜像，你这边只 `docker pull`。

仓库里已经配好了工作流：[.github/workflows/docker-publish.yml](../.github/workflows/docker-publish.yml)。推到 `main` 或打 `v*` 标签时自动构建并推到 GHCR，**同时把镜像导出成 `docker-muse-video.tar` 作为可下载工件**（工件名 `docker-muse-video-tar`）—— 给「拉不到 GHCR」的机器用，见[第三节第 8 小节](#8-离线部署把镜像打成-tar-单文件)。

**启用方式**：在 `.env` 里填一行

```bash
SHORTDRAMA_IMAGE=ghcr.io/xcool-603/muse-video:latest
```



然后照常升级：

```bash
docker compose pull app && docker compose up -d
```

此时**不再拉代码、不再编译**，只做 `docker compose pull app` + `up -d`，几秒完成。
镜像没有变化时 compose 会直接跳过重启。

**三种模式对比**

| | 源码模式（默认） | 镜像模式 | 离线 tar 模式（见第 8 小节） |
|---|---|---|---|
| 触发条件 | `SHORTDRAMA_IMAGE` 留空，且本地**没有**该镜像 | `.env` 里填了 GHCR 地址 | `SHORTDRAMA_IMAGE` 留空，且本地已 `docker load` 了 `docker-muse-video:latest` |
| 更新时做什么 | `git pull` + 编译前后端 + 重启 | 只 `docker pull` + 重启 | 拷 tar + `docker load` + 重启 |
| 耗时 | 3-5 分钟（看 CPU） | 几秒 | 传几百 MB 的 tar（看带宽） |
| 服务器需要 git | 需要 | **不需要** | **不需要**（连源码都不用） |
| 服务器需要编译能力 | 需要（Node 20 + .NET SDK） | **不需要** | **不需要** |
| 服务器要能连 GHCR | 不用 | **要** | **不用** |
| 谁在构建 | 你的服务器 | GitHub Actions | GitHub Actions（或任何一台能构建的机器） |

**拉取私有包要先登录**

GHCR 上的包默认是私有的。如果你没把包改成公开，拉取前需要在部署机上登录一次：

```bash
echo <你的PAT> | docker login ghcr.io -u <你的GitHub用户名> --password-stdin
```

PAT 需要 `read:packages` 权限。登录凭据存在 `~/.docker/config.json`，之后 `docker compose pull` 就不用再登了。

嫌麻烦可以把包设为公开：GitHub → 你的头像 → Your packages → 选中该包 → Package settings → Change visibility → Public。公开后拉取无需任何认证。

**ARM 机器**

工作流默认只构建 `linux/amd64`。如果你的服务器是 ARM（Oracle 免费机、部分 ARM VPS 等），把工作流顶部的

```yaml
PLATFORMS: linux/amd64
```

改成 `linux/amd64,linux/arm64`（⚠️ 多平台时 CI **不再产出 tar 工件** —— `docker save` 的格式装不了多平台清单，见[第 8 小节](#8-离线部署把镜像打成-tar-单文件)）。

> 💡 **编译阶段不会走 QEMU。** Dockerfile 的两个构建阶段都声明了 `--platform=$BUILDPLATFORM`，
> 跑在构建机的**原生架构**上，而它们产出的又是架构无关的东西（前端打出来的 JS/CSS；
> .NET 用 `/p:UseAppHost=false` 发布，得到的是 IL 而不是原生可执行文件）。
> 所以加 arm64 的主要代价是"多拉一份运行时基础镜像 + 多推一份镜像"，
> 而不是"整段构建慢几十倍"。
>
> ⚠️ 反过来说：**`final` 阶段千万不要加 `--platform=$BUILDPLATFORM`** ——
> 它必须是目标架构，否则会构建出与目标平台不符的镜像。

本地给 ARM 机器构建（需要 buildx）：

```bash
docker buildx build --platform linux/arm64 -t docker-muse-video:arm64 --load .
```

> 📌 **给 ARM 服务器做离线 tar 时，标签必须对齐**：上面这条命令打出来的标签是 `docker-muse-video:arm64`，
> 而 compose 的默认值是 `docker-muse-video:latest` —— 直接 `docker save` 这个标签、`load` 到服务器上，
> compose 会因为找不到 `docker-muse-video:latest` 而**转去本地构建**（离线机器上必然失败）。
> 两种做法：① 把构建命令的 `-t` 直接写成 `docker-muse-video:latest`（**在 ARM 机器上构建，最简单**）；
> ② 或者 `load` 之后在服务器上补一句 `docker tag docker-muse-video:arm64 docker-muse-video:latest`。
> 完整流程见[第 8 小节](#8-离线部署把镜像打成-tar-单文件)。

架构不匹配的表现是容器起来就退出，日志里报 `exec format error`。

**基础镜像要不要钉 digest**

Dockerfile 用的是浮动标签（`node:20-alpine`、`mcr.microsoft.com/dotnet/aspnet:10.0`）：
好处是自动吃到上游安全更新，代价是偶发"昨天能构建、今天不能"（上游改了标签指向）。

要极致可复现就把 `FROM` 换成 `FROM <镜像>@sha256:...`，但那样每次安全更新都得手动改 digest，
与本项目"一条命令跟进最新"的取向冲突。**默认选择浮动标签**，并把"构建突然失败"列进
[第九节](#九故障排查)的排查路径。

**改回源码模式**

把 `.env` 里的 `SHORTDRAMA_IMAGE` 清空即可，下一次 `docker compose up -d --build` 就回到本地构建。

### 8. 离线部署：把镜像打成 tar 单文件

**这一节解决两种场景**：

- **部署机没有编译能力** —— 装不了 / 不想装 Node 20 与 .NET SDK，或 CPU、内存太小（源码模式要在这台机器上编译前后端，弱 VPS 上要几分钟且吃满 CPU）；
- **部署机拉不到 GHCR** —— 服务器在受限网络里、镜像仓库不可达，或 GHCR 上的包是私有的而你不方便在上面登录。

`docker save` 出来的 tar 是一个**自包含的单文件**：里面就是完整的镜像层，拷过去 `docker load` 进去就能跑，
**中途不需要网络、也不需要源码**（部署机上只要有 `docker` + `docker-compose.yml` + `.env`）。

**① 在能构建的机器上导出**（仓库根目录）：

```bash
docker build -t docker-muse-video:latest .
docker image inspect docker-muse-video:latest --format '{{.Os}}/{{.Architecture}}'   # 期望 linux/amd64，必须与目标服务器一致
docker save -o docker-muse-video.tar docker-muse-video:latest
ls -lh docker-muse-video.tar        # 通常几百 MB（.NET 运行时基础镜像 + 前端产物），以实测为准
```

> 📌 **镜像名与 compose 的默认值一致**：`docker-compose.yml` 里是
> `image: ${SHORTDRAMA_IMAGE:-docker-muse-video:latest}`，所以 `load` 进去之后
> `docker compose up -d` 直接就能用，**不用改任何配置**。
> ⚠️ **`save` 用的标签必须与 `build` 出来的标签完全一致**，否则报 `No such image`
> —— 先 `docker images docker-muse-video` 确认实际标签（要导出别的标签就把两条命令里的
> `docker-muse-video:latest` 一起换掉）。

**② 拷到服务器**：

```bash
scp docker-muse-video.tar user@server:/tmp/
# 或：rsync -avP docker-muse-video.tar user@server:/tmp/
```

Windows（PowerShell）同一条 `scp` 命令即可；也可以用任何文件传输工具（面板的文件管理器、U 盘都行）。

> 💡 **出发前先自检 tar**：在本机 `docker load -i docker-muse-video.tar` 能读出
> `Loaded image: docker-muse-video:latest`，说明这个文件没损坏（本机已有同名镜像时是幂等的，不会改动什么）。

**③ 在服务器上导入并启动**：

```bash
docker load -i docker-muse-video.tar        # 期望输出：Loaded image: docker-muse-video:latest
# 若 tar 不在当前目录，写路径即可：docker load -i /tmp/docker-muse-video.tar

docker compose up -d                        # ⚠️ 不要加 --build
```

> ⚠️ **`docker compose up -d` 不会重新构建**：本地已经有 `docker-muse-video:latest` 时，
> compose 直接用这个镜像；只有镜像**不存在**、或显式加了 `--build` 时才走 `build:` 那一段。
> 所以离线机器上**不需要仓库源码**，只要 `docker-compose.yml` 和 `.env`。
> ⚠️ **`.env` 里的 `SHORTDRAMA_IMAGE` 必须留空**：填了 GHCR 地址的话，compose 会先去 `pull`，
> 离线机器上就会失败（`SHORTDRAMA_IMAGE` 留空 = 用本地这个 `docker-muse-video:latest`）。
> ⚠️ **导入的镜像架构必须与服务器一致**（amd64 / arm64）：不一致时容器起来就退出，
> 日志里报 `exec format error`。ARM 服务器要在 ARM 机器（或 `docker buildx --platform linux/arm64`）上导出。

**④ 验证**：

```bash
docker images docker-muse-video            # 有 docker-muse-video:latest
docker compose ps                          # 容器在跑（名字是 docker-muse-video）
curl -fsS http://127.0.0.1:18080/health    # 本机能访问（端口以 .env 的 APP_PORT 为准）
```

**⑤ 以后更新**：重新导出 → 重新拷贝 → 重新导入（同名标签会被覆盖），再重建容器：

```bash
docker load -i docker-muse-video.tar       # 覆盖本地的同名标签
docker compose up -d                       # 用新镜像重建容器
```

**⑥ 不用自己构建：CI 已经产出这个 tar**

工作流 [.github/workflows/docker-publish.yml](../.github/workflows/docker-publish.yml) 在构建、推送 GHCR 之后，
会把镜像导出成 `docker-muse-video.tar`，并作为工件 **`docker-muse-video-tar`** 上传
（`actions/upload-artifact`）。在那次 Actions 运行的页面底部 **Artifacts** 区就能下载，
部署机上**连构建机都不需要**。

> ⚠️ **tar 工件只支持单平台**：`docker save` 的格式装不了多平台清单（manifest list）。
> 工作流顶部 `PLATFORMS` 只写一个平台（默认 `linux/amd64`）时才会产出 tar；
> 写成 `linux/amd64,linux/arm64` 时那几步会被**跳过**，日志里有一条 `::warning::`，
> 此时仍可用上面的镜像模式（`docker compose pull app`）或在本机按目标架构重新导出。

---

## 四、配置

所有可调项都在 `.env`（从 `.env.example` 复制而来，已被 `.gitignore` 忽略）。

| 变量 | 默认值 | 说明 |
|------|--------|------|
| `APP_PORT` | `18080` | 对外端口（容器内仍是 8080） |
| `SHORTDRAMA_IMAGE` | 留空 | 留空 = 源码模式（本机构建）或**离线模式**（本地已有 `docker-muse-video:latest` 就直接用，不再构建）；填镜像地址 = 镜像模式（拉预构建镜像，更新只要几秒）。见[第三节第 7 小节](#7-构建太慢改用预构建镜像)与[第 8 小节](#8-离线部署把镜像打成-tar-单文件) |
| `ACCESS_GATE_ENABLED` | `true` | 是否启用访问口令门 |
| `ACCESS_PASSWORD` | `遵纪守法世界和平` | 访问口令 |
| `JWT_KEY` | 占位值 | JWT 签名密钥，**上线前必须自己替换**（`openssl rand -hex 48`） |
| `TORRENT_BASE_URL` | `http://host.docker.internal:8787` | 本地种子服务地址。见下方[「种子搜索」](#种子搜索可选增强)一节 |
| `TORRENT_ENABLED` | `true` | 是否启用种子功能（关掉后「种子」入口显示未启用） |
| `POSTGRES_PASSWORD` | `shortdrama_pwd` | 仅 PostgreSQL 模式使用 |
| `COMPOSE_PROJECT_NAME` | 未设（用目录名） | **卷名**的前缀（容器名已固定为 `docker-muse-video`，**不受它影响**）；目录名含非 ASCII 字符时必须显式设成 ASCII 名。见[第一节第 5 小节](#5-容器名是怎么来的已固定为-docker-muse-video) |

### 种子搜索（可选增强）

「种子」页的搜索与边下边播由另一个项目 **torrent-search** 提供，它是**宿主机上的独立进程**
（默认监听 `127.0.0.1:8787`）。本平台的容器只是转发请求。

> ⚠️ **容器里的 `127.0.0.1` 指的是容器自己，不是宿主机。**
> 所以 Docker 部署下不能沿用 appsettings 里那个 `http://127.0.0.1:8787` ——
> 否则「种子」页会一直显示「连不上本地种子服务」，而种子服务其实好好地在宿主机上跑着。
> compose 已经把 `Torrent__BaseUrl` 传成 `host.docker.internal:8787`，并加了
> `extra_hosts: host.docker.internal:host-gateway`（Linux 上靠它解析，Docker Desktop 原生支持）。

先在宿主机上把种子服务跑起来：

```bash
cd /path/to/torrent-search
node bin/magnet-search.mjs serve      # 默认 127.0.0.1:8787
```

然后照常部署本平台即可，`种子` 入口会自动可用。验证：

```bash
# 容器内能不能连到宿主机的种子服务
docker compose exec app curl -fsS http://host.docker.internal:8787/api/health
```

**种子服务也跑在 Docker 里？** 如果它和本服务在同一个 compose 网络，把地址换成它的服务名：

```bash
TORRENT_BASE_URL=http://torrent-search:8787
```

**不用种子功能？** 设 `TORRENT_ENABLED=false`，或者干脆不管 —— 连不上时页面只会提示
「种子服务没在运行」，不影响其它功能。

改完 `.env` 后需要重新创建容器才会生效：

```bash
docker compose up -d
```

其余环境变量（`AccessGate__ValidDays`、`Demo__Enabled`、日志级别、JWT 签发方等）直接写在 `docker-compose.yml` 的 `environment` 段里，按需修改。

生成随机 JWT 密钥：

```bash
openssl rand -hex 48
```

```powershell
-join ((1..48) | ForEach-Object { '{0:x}' -f (Get-Random -Max 16) })
```

---

## 五、数据库：SQLite 与 PostgreSQL

### SQLite（默认）

零依赖，数据文件在命名卷里：容器内路径 `/data/shortdrama.db`。个人自用、单机部署直接用这个。

### PostgreSQL（可选）

这是**可选**路径：默认部署只有 **1 个**容器（`docker-muse-video`），套上本覆盖文件后变成 **2 个**
（`docker-muse-video` + `<项目名>-postgres-1`）。Windows / PowerShell 同一条命令：

```bash
docker compose -f docker-compose.yml -f docker-compose.postgres.yml up -d
```

- 数据卷：`shortdrama-pgdata`（数据库）—— 卷名仍然带项目名前缀，**没有被固定**，别改
- `postgres` 默认**不对外暴露端口**；需要直连调试时，取消 `docker-compose.postgres.yml` 里 `ports` 的注释
- 切换数据库不会自动迁移已有数据，SQLite 里的旧数据需要另行导出导入
- ⚠️ `postgres` 服务**刻意不设 `container_name`**（名字仍是 `<项目名>-postgres-1`），
  被固定成 `docker-muse-video` 的只有 `app`；原因见[第一节第 5 小节](#5-容器名是怎么来的已固定为-docker-muse-video)

---

## 六、数据卷、备份与恢复

### 数据卷

| 卷 | 内容 | 何时创建 |
|----|------|---------|
| `shortdrama-data` | SQLite 库文件、运行时数据 | 默认模式 |
| `shortdrama-pgdata` | PostgreSQL 数据目录 | 带 `-f docker-compose.postgres.yml` 时 |

> 🚫 **卷名没有被写死，仍然是 `<项目名>_shortdrama-data` / `<项目名>_shortdrama-pgdata`**
> （项目名默认取部署目录名，例如 `shortdrama_shortdrama-data`）。
> **容器名固定为 `docker-muse-video`，卷名照旧随项目名** —— 两者是独立的，见[第一节第 5 小节](#5-容器名是怎么来的已固定为-docker-muse-video)。
>
> ⚠️ **不要为了让名字"统一"去改卷名**（也不要改 `COMPOSE_PROJECT_NAME` / 加 `-p`）：
> 改卷名 = 换卷 = 服务器上已有的 SQLite 数据、收藏、观看历史**全部变成孤儿**，
> 应用起来是空的。数据比名字重要。

### 容器以非 root 运行（镜像内置的 app 用户）

镜像里的业务进程以 `app` 运行，不再是 root。`app` 是 **.NET 官方镜像内置**的非 root 用户
（UID/GID 通常为 `1654`，**以容器内实测为准** —— 见下方「怎么确认业务进程真的以 `app` 运行」），
我们不再自建用户 —— 自建一个同名用户会因组名重复而构建失败（`groupadd` 退出码 9）。`/data` 是唯一需要写的路径。

**升级兼容性已处理**：数据卷只在**首次创建且为空**时继承镜像里的属主；你现在的卷是旧版本（root）
创建的，里面的 `shortdrama.db` 属主是 root —— 直接切成非 root 会写不进去。
所以入口脚本 `docker/entrypoint.sh` 会先以 root 做一次 `chown -R app:app /data`，再 `exec` 降权。
**升级不需要你手动做任何事**，`git pull --ff-only && docker compose up -d --build` 照常用。

设计成失败也不会让站点起不来：拿不到 `gosu`、或 `chown` 失败时，脚本会打印告警并**退回以 root 运行**
（等于改动前的行为），而不是退出。

想完全跳过这一步（例如卷属主已经是对的、或你的编排平台不允许 root 入口）：

```yaml
services:
  app:
    user: "app"
```

此时脚本以 `app` 身份启动，会自动跳过降权分支。前提是 `/data` 已经属于 `app`；
否则容器会因写不了 SQLite 而启动失败，日志里是 `SQLite Error 14: unable to open database file`。

#### 怎么确认业务进程真的以 `app` 运行

> ⚠️ **别用 `docker compose exec app id -u` 判断 —— 它会输出 `0`。**
> 本镜像刻意让入口脚本**以 root 起步**（先修正旧数据卷属主），再 `exec gosu app` 降权；
> 而 `docker exec` 起的是一个**新进程**，用的是容器配置里的用户（root），
> 不是被降权后的业务进程。所以那条命令测的是"我 exec 进去的身份"，不是"应用跑在什么身份下"。

要看真正的业务进程（PID 1）身份：

```bash
docker compose exec app sh -c 'grep ^Uid /proc/1/status'
# 期望：Uid: 后面是**非 0** 的数字（app 的 UID，由基础镜像决定，别写死）
# 出现 0 才说明降权没生效
```

`/proc` 不需要额外装 `ps`/`procps`，在精简镜像里最可靠。也可以从宿主侧看：

```bash
docker top $(docker compose ps -q app) -o pid,user,cmd
# 第一行是 PID 1（dotnet），USER 列不应是 root
```

#### 进阶：只读根文件系统（可选加固）

应用只写两处：`/data`（SQLite 与运行时数据）和 `/home/app`（DataProtection key、缓存）。
所以可以开只读根，挡掉"应用偷偷往别处写文件"：

```yaml
services:
  app:
    read_only: true
    tmpfs:
      - /tmp
    volumes:
      - shortdrama-data:/data
      - shortdrama-home:/home/app

# 顶层声明（少了这段 compose 会报 undefined volume）
volumes:
  shortdrama-data:
  shortdrama-home:      # 只读根模式下必须给 /home/app 一个可写卷
```

> ⚠️ 开了之后若日志出现 `Read-only file system`，**加挂载点或 `tmpfs`，不要退回可写根** ——
> 那等于把加固关掉，而问题（应用在写非预期路径）依然存在。
> 入口脚本的 `chown` 发生在启动早期，不受只读根影响。

手动把已有卷改属主（可选，正常升级用不到 —— 入口脚本启动时会自己修）：

```bash
# 卷的实际名字带项目前缀（取自目录名），先确认它：
docker volume ls | grep shortdrama-data
# 再查镜像里 app 用户的实际 UID（.NET 8+ 内置，通常是 1654 —— 以实测为准）
docker run --rm --entrypoint sh mcr.microsoft.com/dotnet/aspnet:10.0 -c 'id app'
# 用上面两步查到的实际卷名与 UID 执行
docker run --rm -v <卷名>:/data alpine chown -R <UID>:<UID> /data
```

> ⚠️ 别照抄 `-v shortdrama-data:/data`：Compose 给卷名加了项目前缀，写裸名会**新建一个空卷**，
> 你以为在改旧卷，其实什么都没改（备份时更危险——会备份出一个空文件）。
> 日常操作优先用 `docker compose` 子命令，不必关心前缀。

### 备份（SQLite）

先停应用再拷贝，避免拿到写入一半的库文件：

```bash
mkdir -p backup
docker compose stop app
# 用服务名取容器 id（容器名已固定为 docker-muse-video，但按服务名取与名字解耦）
docker cp "$(docker compose ps -aq app):/data/shortdrama.db" ./backup/shortdrama-$(date +%F).db
docker compose start app
```

> 容器名现在**是写死的** `docker-muse-video`（见[第一节第 5 小节](#5-容器名是怎么来的已固定为-docker-muse-video)），
> 所以 `docker cp docker-muse-video:/data/shortdrama.db ./backup/` 也能用。
> 这里仍然用 `docker compose ps -aq app` 取容器 id：它按**服务名**定位，与容器名解耦，
> 将来万一再改名字这段备份命令不用动；服务名 `app` 在 SQLite / PostgreSQL 两种模式下都一样。
>
> ⚠️ 备份和恢复都**只碰容器，不碰卷名** —— 卷名依旧是 `<项目名>_shortdrama-data`，
> 不要为了"名字统一"去改它（改了等于换卷，等于把现有数据丢掉）。

### 恢复（SQLite）

```bash
docker compose stop app
docker cp ./backup/shortdrama-2026-09-30.db "$(docker compose ps -aq app):/data/shortdrama.db"
docker compose start app
```

### 备份 PostgreSQL

```bash
mkdir -p backup
docker compose -f docker-compose.yml -f docker-compose.postgres.yml \
  exec -T postgres pg_dump -U shortdrama -d shortdrama > backup/shortdrama-$(date +%F).sql
```

恢复：

```bash
docker compose -f docker-compose.yml -f docker-compose.postgres.yml \
  exec -T postgres psql -U shortdrama -d shortdrama < backup/shortdrama-2026-09-30.sql
```

---

## 七、反向代理与 HTTPS

容器只监听 8080，前面挂一层反代即可上域名和 HTTPS。**口令门依赖 Cookie，请务必用 HTTPS 对外**（HTTPS 下 Cookie 会自动加 `Secure`）。

Caddy（自动申请证书，最省事）：

```caddyfile
drama.example.com {
    reverse_proxy 127.0.0.1:18080
}
```

Nginx：

```nginx
server {
    listen 443 ssl http2;
    server_name drama.example.com;

    ssl_certificate     /etc/letsencrypt/live/drama.example.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/drama.example.com/privkey.pem;

    location / {
        proxy_pass http://127.0.0.1:18080;
        proxy_http_version 1.1;
        proxy_set_header Host              $host;
        proxy_set_header X-Real-IP         $remote_addr;
        proxy_set_header X-Forwarded-For   $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        # 视频分片是长连接，超时放宽
        proxy_read_timeout 300s;
    }
}
```

用反代时建议只让容器监听本机，别把对外端口直接暴露到公网——把 `docker-compose.yml` 里的 `ports` 改成：

```yaml
    ports:
      - "127.0.0.1:${APP_PORT:-18080}:8080"
```

---

## 八、常用运维命令

```bash
docker compose ps                     # 容器状态
docker compose logs -f app            # 实时日志
docker compose logs --tail 200 app    # 最近 200 行
docker compose restart app            # 重启
docker compose stop app               # 停止
docker compose up -d                  # 应用 .env 改动（重新创建容器）
docker compose down                   # 移除容器（保留数据卷）
docker compose down --remove-orphans  # 移除容器，并清掉已删除服务的遗留容器（旧版 redis 等）
docker compose down -v                # 移除容器并删除数据卷（清空数据）
docker compose exec app sh            # 进容器
docker stats $(docker compose ps -q app)   # 资源占用（容器名已固定，也可直接 docker stats docker-muse-video）
docker rename docker-muse-video docker-muse-video-old   # 名字被占时解封（非破坏性，见 5.3）
```

> 📌 默认部署起来**只有 1 个容器**，名字是 **`docker-muse-video`**（写死的，不带项目名前缀）。
> 上面的命令都按**服务名** `app` 操作 —— 服务名与目录名无关，最省心；
> 想按名字操作时，直接写 `docker-muse-video` 即可。容器名与卷名的区别见
> [第一节第 5 小节](#5-容器名是怎么来的已固定为-docker-muse-video)。

单镜像方式（不用 compose）：

```bash
docker build -t docker-muse-video:latest .
docker run -d --name docker-muse-video-manual \
  -p 18080:8080 \
  -v shortdrama-data:/data \
  -e AccessGate__Password='你的口令' \
  -e Jwt__Key="$(openssl rand -hex 48)" \
  --restart unless-stopped \
  docker-muse-video:latest
```

> 📌 这条是**绕开 compose** 的手动路径，和上面那套**不是一回事**：
> 镜像名与 compose 的默认值一致（都叫 `docker-muse-video:latest`），
> 但**容器名用 `docker-muse-video-manual`**（**故意不叫 `docker-muse-video`** —— 否则会和 compose 创建的那个容器撞名），
> 卷名也是裸的 **`shortdrama-data`**（**没有**项目前缀，因为卷名前缀是 **compose** 的行为）。
> 两种方式的容器与卷**各自独立**，别指望它们共用数据。
> 换成 compose 之后，`docker volume ls` 里看到的会是 `shortdrama_shortdrama-data` 这种形式
> —— **卷名没有被固定，仍然随项目名**（见[第一节第 5 小节](#5-容器名是怎么来的已固定为-docker-muse-video)）。

---

## 九、故障排查

| 现象 | 原因与处理 |
|------|-----------|
| `未找到 docker 命令` | 没装 Docker，或终端没重启。Windows 装完 Docker Desktop 要重开终端 |
| `Docker 守护进程未运行` | 启动 Docker Desktop，等托盘图标变为运行中 |
| `未找到 docker compose（v2）` | 只有 Compose v1（`docker-compose`）。升级 Docker 以获得 `docker compose` |
| 构建失败，提示连不上仓库 | 网络无法访问 Docker Hub / NuGet / npm registry。给 Docker 配置国内镜像加速后 `docker compose build --no-cache` |
| 构建很慢或卡住 | 首次构建要拉基础镜像。确认网络与磁盘空间（约需 3-5 GB）；想知道是哪一层没命中缓存，用 `docker build --progress=plain -t docker-muse-video:latest .` 看每层的 `CACHED` 标记 |
| 健康检查超时 | 首次启动要同步数据源，等 1-2 分钟。仍不行就看 `docker compose logs -f app` |
| 打开站点一直跳 `/gate` | 这是口令门，输入 `ACCESS_PASSWORD` 即可。忘了口令就改 `.env` 再 `docker compose up -d` |
| 改了 `.env` 但没生效 | 环境变量在创建容器时注入，需要 `docker compose up -d` 重建容器 |
| `git pull --ff-only` 报错、更新停住 | 部署机上的代码被改过（工作区脏）。`git stash` 或 `git checkout -- .` 后重试 |
| 定时更新报 `Permission denied (publickey)` | 定时任务环境没有 SSH 私钥，改用 HTTPS 远端或配置 key |
| 定时更新报 `dubious ownership` | 执行 `git config --global --add safe.directory "<仓库绝对路径>"` |
| 定时更新没跑 | 看 `logs/auto-update.log`（按第三节配了 cron 才有日志）；Linux 用 `crontab -l \| grep shortdrama`、Windows 用 `Get-ScheduledTask` 确认任务在。**Windows 还要确认它不是「只在用户登录时运行」**，否则无人登录时静默不执行 |
| 定时更新卡住不返回 | git 网络不通。给 `git` 设超时（如 `timeout 300 git pull`），或改用 HTTPS 远端地址 |
| 定时更新一直失败 | 先看 cron 的日志：`PATH` 里没有 `docker`/`git` 是最常见原因（见第三节第 2 小节） |
| 镜像模式报 `镜像拉取失败` | 包是私有的但没登录：`docker login ghcr.io`；或把包设为公开 |
| 容器起来就退出，日志报 `exec format error` | 镜像架构与本机不符（如 ARM 机器拉了 amd64 镜像）。改工作流的 `PLATFORMS` 重新构建；离线 tar 也要在与服务器同架构的机器上导出 |
| `docker save` 报 `No such image: docker-muse-video:latest` | 本机没有这个标签：先 `docker build -t docker-muse-video:latest .`，或 `docker images` 看实际标签 —— **save 的标签必须与 build 出来的完全一致** |
| `docker load` 报 `open docker-muse-video.tar: no such file or directory` | 当前目录里没有这个 tar：`ls -lh docker-muse-video.tar` 确认路径，或写成绝对路径（`docker load -i /tmp/docker-muse-video.tar`） |
| `docker load` 成功了，但 `docker compose up -d` 又开始编译 | `.env` 里 `SHORTDRAMA_IMAGE` 没留空（填了 GHCR 地址 → compose 先去 pull，离线上失败），或命令里带了 `--build`。离线机器上要**留空**且**不加 `--build`**，见[第三节第 8 小节](#8-离线部署把镜像打成-tar-单文件) |
| Actions 里没有 `docker-muse-video-tar` 工件 | 工作流顶部 `PLATFORMS` 写成了多平台（带逗号）→ 导出 tar 那几步被跳过，日志里有一条 `::warning::`；改成单个平台重跑即可。也可能是那一步自己失败了，看该次运行的日志 |
| Actions 里没有自动构建 | 确认 `.github/workflows/docker-publish.yml` 已推送，且仓库 Settings → Actions 允许运行工作流 |
| 想确认当前是哪种模式 | `grep '^SHORTDRAMA_IMAGE' .env`：有值 = 镜像模式；无输出（只有注释行）= 源码模式 |
| 端口被占用 | `APP_PORT=18081 docker compose up -d` 换个端口，或用 `ss -lntp` 找出占用端口的进程 |
| 磁盘占用越来越大 | 旧镜像堆积：`docker image prune -a`（会删掉所有未被使用的镜像） |
| 面板「更新容器」报 `invalid endpoint settings: user specified IP address is supported only when connecting to networks with user configured subnets` | 面板把容器**当前的动态 IP** 当成"要保留的静态 IP"回填，而 compose 自动创建的网络没有自定义子网，Docker 拒绝。别用面板的容器更新，改用 `docker compose up -d`（见下方说明） |
| `Conflict. The container name "/docker-muse-video" is already in use` | 容器名现在是**写死的**（`docker-compose.yml` 的 `container_name`），所以这个报错特指这个名字被占着 —— 常见于**同一台机器上从两个目录部署本项目**（写死的名字关掉了项目名前缀，第二份必然撞名）。先查归属：`docker inspect docker-muse-video --format '{{index .Config.Labels "com.docker.compose.project"}}'`，解封用 `docker rename docker-muse-video docker-muse-video-old`（**非破坏性**，别急着 `docker rm -f`）。完整步骤见[第一节 5.3](#53-名字被占了怎么解封) |
| `Conflict. The container name "/redis"`、`"/new-api"` 之类**通用名**被占 | 那个名字**不是本项目的**：本项目只固定了 `docker-muse-video` 这一个专属名，`redis` / `new-api` 是别的项目留下的容器。**不要去删**（删了会打断别人，还可能连带删掉匿名卷）；真需要腾名字就 `docker inspect <名字>` 看归属，再 `docker rename <名字> <名字>-old` |
| `docker ps` 里容器比预期多（有 `redis` 等） | 早先版本里有过 `redis` 服务（已从 compose 删除），旧容器可能还留着；也可能混进了别的项目的容器。先 `docker ps -a` 看清归属，再在本项目目录里 `docker compose down --remove-orphans`。见[第一节 5.2](#52-清理历史遗留容器旧版本的-redis-等) |

### 端口打不开？按「从内到外」的顺序查，别一上来就换端口

「打不开」有四种完全不同的原因，按顺序排除：

```bash
# ① 容器在跑吗？（没跑的话端口当然不通）
docker compose ps
docker compose logs --tail=50 app

# ② 宿主机在监听吗？（有 LISTEN 才算通；老系统把 ss 换成 netstat）
ss -lntp | grep 18080

# ③ 本机能访问吗？（通 → 应用没问题，问题在网络层）
curl -fsS http://127.0.0.1:18080/health

# ④ 外网还是访问不了？那就是防火墙 / 安全组
```

| 卡在哪一步 | 原因 | 处理 |
|---|---|---|
| ① 容器没在跑 | 构建失败或启动报错 | `docker compose logs --tail=50 app` |
| ② 没有 LISTEN | 端口映射写错，或容器内应用没起来 | 确认 compose 里是 `"<对外端口>:8080"` —— 容器内应用监听的是 **8080**，别写反 |
| ③ 本机 curl 不通 | 应用启动失败，或还在初始化 | 首次启动要同步数据源，等 1-2 分钟；仍不行看日志 |
| ③ 通、④ 不通 | **云安全组 / 本机防火墙没放行** | 见下（两处都要放） |

**④ 的放行必须做两处，缺一不可**：

1. **云厂商的安全组 / 防火墙规则**（阿里云、腾讯云、甲骨文等控制台里）——放行该 TCP 端口；
2. **宿主机防火墙**：

```bash
# ufw（Ubuntu / Debian）
ufw allow 18080/tcp

# firewalld（CentOS / RHEL / Alma）
firewall-cmd --permanent --add-port=18080/tcp && firewall-cmd --reload

# 宝塔 / 1Panel 等面板：在面板的「防火墙 / 安全」里放行
```

> ⚠️ **8080 是热门端口**（面板、代理、各种测试服务都爱用它），撞车概率高。
> 所以本项目的**对外默认端口是 18080**；容器内部仍然是 8080 ——
> 容器内的端口是隔离的，不存在冲突，改它没有收益（还会多改一堆地方）。
> 想换端口：改 `.env` 里的 `APP_PORT`，或临时用 `APP_PORT=9000 docker compose up -d`。


### 用 `docker compose` 升级，别用面板的「更新容器」

宝塔 / 1Panel 之类的面板提供「更新容器」按钮，但它**不是**在跑 compose，而是把面板记录的容器参数
原样搬到一个新容器上。两边的"期望状态"来源不同，于是出现两类典型故障：

- **动态 IP 被固化成静态 IP**：面板从 `docker inspect` 读到当前 IP，回填成 `ipv4_address`，
  而 compose 创建的网络没有自定义子网 → `user specified IP address is supported only when
  connecting to networks with user configured subnets`，容器起不来（面板会尝试恢复原容器）。
- **配置漂移**：面板不会读你的 `docker-compose.yml`，所以改了 compose（端口、环境变量、
  网络、卷）之后再点「更新容器」，应用的仍是旧参数。

正确做法是让 compose 来管这个容器：

```bash
cd <项目目录>
docker compose down            # 删掉旧容器（数据卷保留）
docker compose up -d           # 按 compose 的期望状态重建
# 或者一条命令搞定（含拉代码/构建）：
git pull --ff-only && docker compose up -d --build
```

如果你必须用面板管理它，就在面板里把该容器的**静态 IP 留空**，或者给网络显式配一个子网
（不推荐：写死的子网可能和宿主已有的网段冲突，导致网络创建失败）。

查看某个容器的详细状态（容器名已固定，可以直接写）：

```bash
docker inspect --format '{{.State.Status}} {{.State.Health.Status}}' docker-muse-video
```

---

## 十、卸载

```bash
docker compose down --remove-orphans   # 停止并移除本项目容器（含遗留容器），保留数据
docker compose down -v          # 停止并移除容器，同时删除数据卷（不可恢复）
docker rmi docker-muse-video:latest     # 删除镜像（离线 tar 导入的也是这个名字）
```

> 📌 `--remove-orphans` 会一并清掉"服务已从 compose 里删掉"的遗留容器（旧版本的 `redis` 等），
> 只影响**本项目**的容器；卷不受影响（只有 `-v` 才删卷）。见[第一节 5.2](#52-清理历史遗留容器旧版本的-redis-等)。

Windows 上这些命令完全一样（在项目目录里用 PowerShell 执行即可）。

彻底清理（含所有相关卷）：

```bash
docker volume ls | grep shortdrama
docker volume rm <上面列出的卷名>
```

> ⚠️ 这里的卷名仍然带**项目前缀**（`<项目名>_shortdrama-data`）—— 容器名固定不影响卷名。
> `docker volume rm` 是**不可恢复**的，删之前确认卷名对不对。

---

## 附：文件说明

| 文件 | 作用 |
|------|------|
| `docker-compose.yml` | 主配置（SQLite，**单容器**，容器名固定为 `docker-muse-video`；卷名仍随项目名） |
| `docker-compose.postgres.yml` | PostgreSQL 覆盖配置（**可选**，套上后共 2 个容器：`docker-muse-video` + `<项目名>-postgres-1`；不含 Redis） |
| `Dockerfile` | 多阶段构建：前端 → 后端 → 运行时，单镜像 |
| `docker/entrypoint.sh` | 容器入口：修正数据卷属主后降权运行 |
| `.env.example` | 环境变量模板 |
| `.dockerignore` | 构建上下文排除清单 |
| `logs/auto-update.log` | 定时更新日志（按第三节配了 cron 后生成） |
