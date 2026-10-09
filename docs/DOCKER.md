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
镜像模式详见[第七节](#7-构建太慢改用预构建镜像)（含私有包登录、ARM 机器注意事项）。

**定时自动升级**：直接在宿主 crontab 里写原生命令即可（不需要任何脚本）：

```bash
# 每天 04:00 拉代码并重建；日志写到项目目录下的 logs/
0 4 * * * cd /path/to/shortdrama && git pull --ff-only && docker compose up -d --build >> logs/auto-update.log 2>&1
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

默认使用 **SQLite**，数据落在命名卷里，不依赖任何外部服务。想换 PostgreSQL 见[第五节](#五数据库sqlite--postgresql)。

> 📌 **本项目不使用部署包装脚本**（`deploy.sh` / `deploy.ps1` 已移除）。
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
```

### 4. 常用配置

所有可调项都在 `.env`（从 `.env.example` 复制而来，已被 `.gitignore` 忽略）：

| 变量 | 默认值 | 作用 |
|------|--------|------|
| `APP_PORT` | `18080` | 对外端口（容器内仍是 8080） |
| `ACCESS_GATE_ENABLED` | `true` | 是否启用访问口令门 |
| `ACCESS_PASSWORD` | `遵纪守法世界和平` | 访问口令 |
| `JWT_KEY` | 占位值 | JWT 签名密钥，**上线前必须替换** |
| `SHORTDRAMA_IMAGE` | 留空 | 留空 = 源码模式；填镜像地址 = 镜像模式 |
| `TORRENT_BASE_URL` | `http://host.docker.internal:8787` | 本地种子服务地址（可选功能） |
| `POSTGRES_PASSWORD` | `shortdrama_pwd` | 仅 PostgreSQL 模式使用 |

**临时改一次端口**（不改 `.env`）：`APP_PORT=9000 docker compose up -d`。
**强制不用缓存重建**：`docker compose build --no-cache && docker compose up -d`。
**改用 PostgreSQL**：见[第五节](#五数据库sqlite--postgresql)。

改完 `.env` 后需要重新创建容器才会生效：`docker compose up -d`。

### 5. 改运行端口

端口由 **`.env` 里的 `APP_PORT`** 决定；compose 里的 `${APP_PORT:-18080}` 只是**没配 `.env` 时的兜底值**。

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

> 上线前请务必：改掉 `ACCESS_PASSWORD`、改掉 `admin123` 密码、确认 `JWT_KEY` 已是随机值（脚本首次运行会自动生成）。

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
0 4 * * * cd /path/to/shortdrama && git pull --ff-only && docker compose up -d --build >> logs/auto-update.log 2>&1
```

```bash
crontab -l | grep shortdrama        # 查看
crontab -e                          # 删掉那一行即可取消
```

**Windows**：用「任务计划程序」新建任务，操作填 `docker`、参数填 `compose up -d --build`、
「起始于」填项目目录；触发设为每天 04:00。

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
> 请用第六节的备份恢复。

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

仓库里已经配好了工作流：[.github/workflows/docker-publish.yml](.github/workflows/docker-publish.yml)。推到 `main` 或打 `v*` 标签时自动构建并推到 GHCR。

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

**两种模式对比**

| | 源码模式（默认） | 镜像模式 |
|---|---|---|
| 触发条件 | `SHORTDRAMA_IMAGE` 留空 | `.env` 里填了镜像地址 |
| 更新时做什么 | `git pull` + 编译前后端 + 重启 | 只 `docker pull` + 重启 |
| 耗时 | 3-5 分钟（看 CPU） | 几秒 |
| 服务器需要 git | 需要 | **不需要** |
| 谁在构建 | 你的服务器 | GitHub Actions |

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

改成 `linux/amd64,linux/arm64`。注意加 arm64 后 CI 构建时间会明显变长（QEMU 模拟）。架构不匹配的表现是容器起来就退出，日志里报 `exec format error`。

**改回源码模式**

把 `.env` 里的 `SHORTDRAMA_IMAGE` 清空即可，脚本会自动回到本地构建。

---

## 四、配置

所有可调项都在 `.env`（从 `.env.example` 复制而来，已被 `.gitignore` 忽略）。

| 变量 | 默认值 | 说明 |
|------|--------|------|
| `APP_PORT` | `18080` | 对外端口（容器内仍是 8080） |
| `SHORTDRAMA_IMAGE` | 留空 | 留空 = 源码模式（本机构建）；填镜像地址 = 镜像模式（拉预构建镜像，更新只要几秒）。见[第三节第 7 小节](#7-构建太慢改用预构建镜像) |
| `ACCESS_GATE_ENABLED` | `true` | 是否启用访问口令门 |
| `ACCESS_PASSWORD` | `遵纪守法世界和平` | 访问口令 |
| `JWT_KEY` | 占位值 | JWT 签名密钥，**首次运行脚本会自动替换为随机值** |
| `TORRENT_BASE_URL` | `http://host.docker.internal:8787` | 本地种子服务地址。见下方「种子搜索」一节 |
| `TORRENT_ENABLED` | `true` | 是否启用种子功能（关掉后「种子」入口显示未启用） |
| `POSTGRES_PASSWORD` | `shortdrama_pwd` | 仅 PostgreSQL 模式使用 |

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

## 五、数据库：SQLite / PostgreSQL

### SQLite（默认）

零依赖，数据文件在命名卷里：容器内路径 `/data/shortdrama.db`。个人自用、单机部署直接用这个。

### PostgreSQL（可选）

会额外拉起 `postgres` 和 `redis` 两个容器：

```bash
docker compose -f docker-compose.yml -f docker-compose.postgres.yml up -d
```

```powershell
（Windows 同一条命令）
```

等价的 compose 命令：

```bash
docker compose -f docker-compose.yml -f docker-compose.postgres.yml up -d
```

- 数据卷：`shortdrama-pgdata`（数据库）、`shortdrama-redisdata`（Redis AOF）
- `postgres` 默认**不对外暴露端口**；需要直连调试时，取消 `docker-compose.postgres.yml` 里 `ports` 的注释
- 切换数据库不会自动迁移已有数据，SQLite 里的旧数据需要另行导出导入

---

## 六、数据卷、备份与恢复

### 数据卷

| 卷 | 内容 | 何时创建 |
|----|------|---------|
| `shortdrama-data` | SQLite 库文件、运行时数据 | 默认模式 |
| `shortdrama-pgdata` | PostgreSQL 数据目录 | `--postgres` |
| `shortdrama-redisdata` | Redis AOF | `--postgres` |

### 容器以非 root 运行（镜像内置的 app 用户）

镜像里的业务进程以 `app` 运行，不再是 root。`app` 是 **.NET 官方镜像内置**的非 root 用户（UID/GID `1654`），我们不再自建用户 —— 自建一个同名用户会因组名重复而构建失败（`groupadd` 退出码 9）。`/data` 是唯一需要写的路径。

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
# 用容器 id 而不是容器名：compose 的容器名带项目前缀，写死名字以后很容易失效
docker cp "$(docker compose ps -aq app):/data/shortdrama.db" ./backup/shortdrama-$(date +%F).db
docker compose start app
```

> 这里刻意用 `docker compose ps -aq app` 取容器 id，而不是 `docker cp shortdrama:...`：
> compose 里**没有**设 `container_name`，实际容器名形如 `shortdrama-app-1`（带项目名前缀），
> 写死名字在换目录、改项目名之后就会失效。

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
docker compose down -v                # 移除容器并删除数据卷（清空数据）
docker compose exec app sh            # 进容器
docker stats $(docker compose ps -q app)   # 资源占用（用容器 id，别写死名字）
```

单镜像方式（不用 compose）：

```bash
docker build -t shortdrama .
docker run -d --name shortdrama \
  -p 18080:8080 \
  -v shortdrama-data:/data \
  -e AccessGate__Password='你的口令' \
  -e Jwt__Key="$(openssl rand -hex 48)" \
  --restart unless-stopped \
  shortdrama
```

> 注意：这条手动命令里的卷名就是 **`shortdrama-data`**（没有前缀）——
> 卷名带项目前缀是 **compose** 的行为。两种方式的卷是各自独立的，别指望它们共用数据。
> 换成 compose 之后，`docker volume ls` 里看到的会是 `shortdrama_shortdrama-data` 这种形式。

---

## 九、故障排查

| 现象 | 原因与处理 |
|------|-----------|
| `未找到 docker 命令` | 没装 Docker，或终端没重启。Windows 装完 Docker Desktop 要重开终端 |
| `Docker 守护进程未运行` | 启动 Docker Desktop，等托盘图标变为运行中 |
| `未找到 docker compose（v2）` | 只有 Compose v1（`docker-compose`）。升级 Docker 以获得 `docker compose` |
| 构建失败，提示连不上仓库 | 网络无法访问 Docker Hub / NuGet / npm registry。给 Docker 配置国内镜像加速后 `docker compose build --no-cache` |
| 构建很慢或卡住 | 首次构建要拉基础镜像。确认网络与磁盘空间（约需 3-5 GB） |
| 健康检查超时 | 首次启动要同步数据源，等 1-2 分钟。仍不行就看 `docker compose logs -f app` |
| 打开站点一直跳 `/gate` | 这是口令门，输入 `ACCESS_PASSWORD` 即可。忘了口令就改 `.env` 再 `docker compose up -d` |
| 改了 `.env` 但没生效 | 环境变量在创建容器时注入，需要 `docker compose up -d` 重建容器 |
| 自动更新提示「工作区有未提交的修改」 | 部署机上的代码被改过。`git stash` 或 `git checkout -- .` 后重试 |
| 自动更新报 `Permission denied (publickey)` | 定时任务环境没有 SSH 私钥，改用 HTTPS 远端或配置 key |
| 自动更新报 `dubious ownership` | 执行 `git config --global --add safe.directory "<仓库绝对路径>"` |
| 自动更新没跑 | 看 `logs/auto-update.log`；`crontab -l \| grep shortdrama`（Linux）或 `Get-ScheduledTask -TaskName ShortDramaAutoUpdate`（Windows）确认任务在 |
| 自动更新报 `无法访问远端`（超过 300 秒无响应） | 网络不通或 SSH 的 22 端口被挡。改用 HTTPS 远端地址 |
| 定时更新一直失败 | 先看 cron 的日志：`PATH` 里没有 `docker`/`git` 是最常见原因（见第三节第 2 小节） |
| 镜像模式报 `镜像拉取失败` | 包是私有的但没登录：`docker login ghcr.io`；或把包设为公开 |
| 容器起来就退出，日志报 `exec format error` | 镜像架构与本机不符（如 ARM 机器拉了 amd64 镜像）。改工作流的 `PLATFORMS` 重新构建 |
| Actions 里没有自动构建 | 确认 `.github/workflows/docker-publish.yml` 已推送，且仓库 Settings → Actions 允许运行工作流 |
| 想确认当前是哪种模式 | 看部署完成后打印的「部署方式」一行；或 `grep SHORTDRAMA_IMAGE .env` |
| 端口被占用 | `APP_PORT=18081 docker compose up -d` 换个端口，或用 `ss -lntp` 找出占用端口的进程 |
| 磁盘占用越来越大 | 旧镜像堆积：`docker image prune -a`（会删掉所有未被使用的镜像） |
| 面板「更新容器」报 `invalid endpoint settings: user specified IP address is supported only when connecting to networks with user configured subnets` | 面板把容器**当前的动态 IP** 当成"要保留的静态 IP"回填，而 compose 自动创建的 `shortdrama_default` 网络没有自定义子网，Docker 拒绝 | 别用面板的容器更新，改用 `docker compose up -d`（见下方说明） |
| `Conflict. The container name "/xxx" is already in use` | 那个名字**不一定属于你**：报错里的容器可能是别的项目的（先 `docker inspect xxx` 看 `com.docker.compose.project` 标签） | `docker rename xxx xxx-old` 解封（非破坏性）；本仓库的 compose 已不设 `container_name`，不会自己撞名 |

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

查看某个容器的详细状态：

```bash
docker inspect --format '{{.State.Status}} {{.State.Health.Status}}' shortdrama
```

---

## 十、卸载

```bash
docker compose down             # 停止并移除容器，保留数据
docker compose down -v          # 停止并移除容器，同时删除数据卷（不可恢复）
docker rmi shortdrama:latest        # 删除镜像
```

Windows 上这些命令完全一样（在项目目录里用 PowerShell 执行即可）。

彻底清理（含所有相关卷）：

```bash
docker volume ls | grep shortdrama
docker volume rm <上面列出的卷名>
```

---

## 附：文件说明

| 文件 | 作用 |
|------|------|
| `docker-compose.yml` | 主配置（SQLite，单容器） |
| `docker-compose.postgres.yml` | PostgreSQL + Redis 覆盖配置 |
| `Dockerfile` | 多阶段构建：前端 → 后端 → 运行时，单镜像 |
| `docker/entrypoint.sh` | 容器入口：修正数据卷属主后降权运行 |
| `.env.example` | 环境变量模板 |
| `.dockerignore` | 构建上下文排除清单 |
| `logs/auto-update.log` | 定时更新日志（按第三节配了 cron 后生成） |
