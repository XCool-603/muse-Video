# Docker 一键部署

一条命令跑起来，之后可以自动跟进最新代码。

## 三行部署

```bash
git clone https://github.com/XCool-603/muse-Video.git shortdrama && cd shortdrama
cp .env.example .env
docker compose up -d --build
```

打开 <http://localhost:8080> 就能用。首次构建要拉基础镜像并编译前后端，约 3-5 分钟；之后启动只要几秒。

> 第 2 行的作用是给你一个可编辑的 `.env`。不执行它，compose 会用内置的同名默认值，跑起来效果一样。
>
> ⚠️ 但这两条路**都不会自动更换 `JWT_KEY`** —— `.env.example` 里放的也是同一个占位值，上线前必须自己改。
> 想让密钥自动随机生成，用下面的 `./deploy.sh`，它在首次运行时会把占位值替换成随机密钥。

想再少一行、并且顺带拿到**随机 JWT 密钥**和**定时自动更新**，用项目自带脚本（推荐）：

```bash
git clone https://github.com/XCool-603/muse-Video.git shortdrama && cd shortdrama
./deploy.sh
```

---

## 平台速查

| 平台 | 一键运行 | 一键开启自动更新 |
|------|---------|-----------------|
| Linux / macOS | `./deploy.sh` | `./deploy.sh --install-cron` |
| Windows | `.\deploy.ps1` | `.\deploy.ps1 -InstallTask` |

默认使用 **SQLite**，数据落在命名卷里，不依赖任何外部服务。想换 PostgreSQL 见[第五节](#五数据库sqlite--postgresql)。

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
./deploy.sh
```

Windows（PowerShell）：

```powershell
git clone https://github.com/XCool-603/muse-Video.git shortdrama
cd shortdrama
.\deploy.ps1
```

> 远端已配好 SSH key 的话，也可以把地址换成 `git@github.com:XCool-603/muse-Video.git`。

脚本会自动完成：检查 Docker 环境 → 生成 `.env`（含随机 JWT 密钥）→ 构建镜像 → 启动容器 → 等待健康检查 → 打印访问信息。

首次构建需要下载基础镜像并编译前后端，大约 **3-5 分钟**；之后再次启动只需几秒。

> 如果提示 `Permission denied`（用 ZIP 下载的源码，或文件系统丢了可执行位），先 `chmod +x deploy.sh`，或者直接用 `bash deploy.sh` 代替 `./deploy.sh`。

### 3. 不想用脚本

脚本只是把下面的步骤串起来，等价的手工命令是：

```bash
cp .env.example .env
docker compose up -d --build
```

### 4. 常用参数

| Linux / macOS | Windows | 作用 |
|---------------|---------|------|
| `-p 80` / `--port 80` | `-Port 80` | 对外端口，默认 8080 |
| `--postgres` | `-Postgres` | 改用 PostgreSQL |
| `--rebuild` | `-Rebuild` | 强制重建（不用缓存） |
| `--down` | `-Down` | 停止（**保留**数据） |
| `--purge` | `-Purge` | 停止并删除数据卷（**清空**数据） |
| `-h` / `--help` | `Get-Help .\deploy.ps1` | 帮助 |

---

## 二、跑起来之后

| 项目 | 地址 / 值 |
|------|-----------|
| 站点首页 | <http://localhost:8080> |
| 管理后台 | <http://localhost:8080/admin> |
| 默认管理员 | `admin` / `admin123` |
| 健康检查 | <http://localhost:8080/health> |
| API 文档 | <http://localhost:8080/openapi/v1.json> |

**访问口令门**：默认开启。第一次打开站点会跳到 `/gate`，需要输入口令才能访问整站。

- 默认口令：`遵纪守法世界和平`
- 修改方式：改 `.env` 里的 `ACCESS_PASSWORD`，然后 `docker compose up -d` 重新创建容器
- 完全关闭：`.env` 里设 `ACCESS_GATE_ENABLED=false`
- `/health` 与 `/openapi/*` 不受口令门拦截，方便做健康检查和监控

> 上线前请务必：改掉 `ACCESS_PASSWORD`、改掉 `admin123` 密码、确认 `JWT_KEY` 已是随机值（脚本首次运行会自动生成）。

---

## 三、自动更新

源码模式：**拉取最新代码 → 重建镜像 → 重启容器**。不需要镜像仓库，不依赖任何外部服务。

### 1. 手动更新一次

```bash
./deploy.sh --update
```

```powershell
.\deploy.ps1 -Update
```

### 2. 定时自动更新（一条命令装好）

**Linux / macOS**

```bash
./deploy.sh --install-cron          # 每天 04:00 自动更新
./deploy.sh --install-cron 03:30    # 自定义时间
./deploy.sh --uninstall-cron        # 移除
crontab -l | grep shortdrama        # 查看
```

**Windows**

```powershell
.\deploy.ps1 -InstallTask                    # 每天 04:00 自动更新
.\deploy.ps1 -InstallTask -TaskTime 03:30    # 自定义时间
.\deploy.ps1 -UninstallTask                  # 移除
Get-ScheduledTask -TaskName ShortDramaAutoUpdate   # 查看
```

装好之后就不需要再管了。更新日志写在项目目录下的 `logs/auto-update.log`。

- Linux/macOS 用 `crontab`，重复执行只会替换那一条，不会叠加
- Windows 用「任务计划程序」，任务名固定为 `ShortDramaAutoUpdate`，并发策略为 `IgnoreNew`（上一次没跑完时新的一次直接跳过）

### 3. 只想检查有没有新版本

```bash
./deploy.sh --check
```

```powershell
.\deploy.ps1 -Check
```

退出码（方便接监控 / 通知）：

| 退出码 | 含义 |
|--------|------|
| `0` | 已是最新版本 |
| `10` | 有新版本，可更新 |
| `1` | 检查失败（不是 git 仓库、连不上远端等） |

例：有新版本时发一条通知

```bash
./deploy.sh --check
case $? in
    0)  echo "已是最新版本" ;;
    10) echo "短剧聚合有新版本" | mail -s "更新提醒" you@example.com ;;
    *)  echo "检查失败" ;;
esac
```

### 4. 更新过程做了什么

| 步骤 | 说明 |
|------|------|
| 1. 检查 | `git fetch` 后比较本地与远端，**只读**，不动工作区 |
| 2. 拉取 | 只用 `git pull --ff-only` 快进，绝不产生意外的合并提交 |
| 3. 构建 | **先构建新镜像，构建成功才切换**；构建失败时旧容器继续跑，站点不中断 |
| 4. 重启 | `docker compose up -d` 用新镜像重建容器 |
| 5. 收尾 | 清理被替换下来的旧镜像层（只删 dangling），等待健康检查 |

为安全起见，以下情况会**拒绝更新并说明原因**，而不是硬来：

- **工作区有未提交的修改** —— 不覆盖你的改动，也不会把半成品代码构建进镜像
- **本地与远端已分叉** —— 不覆盖本地提交，提示你手动 `git pull --rebase`
- **另一次更新正在跑** —— 直接跳过（避免定时任务和手动更新撞车）

另外，两处超时保护是给无人值守准备的：

- Linux/macOS：`git fetch` / `git pull` 超过 **300 秒**会被 `timeout` 掐掉
- Windows：计划任务设有 **1 小时**运行上限

没有这两条的话，一次卡死的网络请求会一直占着更新锁，之后每一次定时更新都只会「跳过」，自动更新就静默停摆了。

### 5. 更新失败了怎么回滚

每次成功更新后，脚本会打印这一行：

```
版本变更   a1b2c3d → e4f5g6h
回滚       git reset --hard a1b2c3d && ./deploy.sh --rebuild
```

直接执行那条回滚命令即可退回上一个版本并重新构建。

> 注意：代码回滚了，但**数据库结构不会自动回退**。如果新版本包含破坏性的数据库变更，请用第六节的备份恢复。

### 6. 注意事项

**cron / 计划任务里的环境**

- Linux：安装脚本会把当前的 `PATH` 一起写进 crontab。cron 默认 `PATH` 很短，通常找不到 `docker` 和 `git`，不写进去会直接失败。
- Windows：计划任务默认「只在用户登录时运行」，因为 Docker Desktop 需要用户会话。任务里调用的是 `pwsh`（装了就用它），否则回退到 `powershell.exe`。

**私有仓库的凭据**

如果远端是 SSH 地址（`git@github.com:...`），定时任务必须有可用的私钥：

- 改用 HTTPS 地址，或给该用户配置 SSH key
- 报错 `Permission denied (publickey)` 就是这个原因

**git 属主告警**

仓库属主与执行用户不一致时 git 会拒绝操作：

```bash
git config --global --add safe.directory "<仓库绝对路径>"
```

**不要在自动更新的仓库里改代码**

自动更新只跟随远端分支。如果你在部署机上直接改代码，工作区变「脏」后自动更新会一直拒绝执行——这是刻意的保护。

**停止自动更新**

```bash
./deploy.sh --uninstall-cron     # Linux / macOS
```

```powershell
.\deploy.ps1 -UninstallTask      # Windows
```

---

## 四、配置

所有可调项都在 `.env`（从 `.env.example` 复制而来，已被 `.gitignore` 忽略）。

| 变量 | 默认值 | 说明 |
|------|--------|------|
| `APP_PORT` | `8080` | 对外端口 |
| `ACCESS_GATE_ENABLED` | `true` | 是否启用访问口令门 |
| `ACCESS_PASSWORD` | `遵纪守法世界和平` | 访问口令 |
| `JWT_KEY` | 占位值 | JWT 签名密钥，**首次运行脚本会自动替换为随机值** |
| `POSTGRES_PASSWORD` | `shortdrama_pwd` | 仅 PostgreSQL 模式使用 |

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
./deploy.sh --postgres
```

```powershell
.\deploy.ps1 -Postgres
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

> compose 会给卷名加上项目前缀（取自目录名）。用 `docker volume ls` 查看实际名称；日常操作用 `docker compose` 命令即可，不必关心前缀。

### 备份（SQLite）

先停应用再拷贝，避免拿到写入一半的库文件：

```bash
mkdir -p backup
docker compose stop app
docker cp shortdrama:/data/shortdrama.db ./backup/shortdrama-$(date +%F).db
docker compose start app
```

`shortdrama` 是 `docker-compose.yml` 里固定写死的容器名（`container_name: shortdrama`），所以可以直接用 `docker cp`。

### 恢复（SQLite）

```bash
docker compose stop app
docker cp ./backup/shortdrama-2026-09-30.db shortdrama:/data/shortdrama.db
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
    reverse_proxy 127.0.0.1:8080
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
        proxy_pass http://127.0.0.1:8080;
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

用反代时建议只让容器监听本机，别把 8080 直接暴露到公网——把 `docker-compose.yml` 里的 `ports` 改成：

```yaml
    ports:
      - "127.0.0.1:${APP_PORT:-8080}:8080"
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
docker stats shortdrama               # 资源占用
```

单镜像方式（不用 compose）：

```bash
docker build -t shortdrama .
docker run -d --name shortdrama \
  -p 8080:8080 \
  -v shortdrama-data:/data \
  -e AccessGate__Password='你的口令' \
  -e Jwt__Key="$(openssl rand -hex 48)" \
  --restart unless-stopped \
  shortdrama
```

---

## 九、故障排查

| 现象 | 原因与处理 |
|------|-----------|
| `未找到 docker 命令` | 没装 Docker，或终端没重启。Windows 装完 Docker Desktop 要重开终端 |
| `Docker 守护进程未运行` | 启动 Docker Desktop，等托盘图标变为运行中 |
| `未找到 docker compose（v2）` | 只有 Compose v1（`docker-compose`）。升级 Docker 以获得 `docker compose` |
| 构建失败，提示连不上仓库 | 网络无法访问 Docker Hub / NuGet / npm registry。给 Docker 配置国内镜像加速后 `--rebuild` |
| 构建很慢或卡住 | 首次构建要拉基础镜像。确认网络与磁盘空间（约需 3-5 GB） |
| 健康检查超时 | 首次启动要同步数据源，等 1-2 分钟。仍不行就看 `docker compose logs -f app` |
| 打开站点一直跳 `/gate` | 这是口令门，输入 `ACCESS_PASSWORD` 即可。忘了口令就改 `.env` 再 `docker compose up -d` |
| 改了 `.env` 但没生效 | 环境变量在创建容器时注入，需要 `docker compose up -d` 重建容器 |
| 自动更新提示「工作区有未提交的修改」 | 部署机上的代码被改过。`git stash` 或 `git checkout -- .` 后重试 |
| 自动更新报 `Permission denied (publickey)` | 定时任务环境没有 SSH 私钥，改用 HTTPS 远端或配置 key |
| 自动更新报 `dubious ownership` | 执行 `git config --global --add safe.directory "<仓库绝对路径>"` |
| 自动更新没跑 | 看 `logs/auto-update.log`；`crontab -l \| grep shortdrama`（Linux）或 `Get-ScheduledTask -TaskName ShortDramaAutoUpdate`（Windows）确认任务在 |
| 自动更新报 `无法访问远端`（超过 300 秒无响应） | 网络不通或 SSH 的 22 端口被挡。改用 HTTPS 远端地址 |
| 自动更新一直「跳过」 | 上一次更新卡死占着锁。Linux 上 `pkill -f 'deploy.sh --update'`；Windows 上结束对应进程。锁释放后即可恢复 |
| 端口被占用 | `./deploy.sh -p 8081` 换个端口，或找出占用 8080 的进程 |
| 磁盘占用越来越大 | 旧镜像堆积：`docker image prune -a`（会删掉所有未被使用的镜像） |

查看某个容器的详细状态：

```bash
docker inspect --format '{{.State.Status}} {{.State.Health.Status}}' shortdrama
```

---

## 十、卸载

```bash
./deploy.sh --down     # 停止并移除容器，保留数据
./deploy.sh --purge    # 停止并移除容器，同时删除数据卷（不可恢复）
docker rmi shortdrama:latest        # 删除镜像
```

Windows 把 `./deploy.sh` 换成 `.\deploy.ps1`。

彻底清理（含所有相关卷）：

```bash
docker volume ls | grep shortdrama
docker volume rm <上面列出的卷名>
```

---

## 附：文件说明

| 文件 | 作用 |
|------|------|
| `deploy.sh` / `deploy.ps1` | 一键部署 + 自动更新脚本 |
| `docker-compose.yml` | 主配置（SQLite，单容器） |
| `docker-compose.postgres.yml` | PostgreSQL + Redis 覆盖配置 |
| `Dockerfile` | 多阶段构建：前端 → 后端 → 运行时，单镜像 |
| `.env.example` | 环境变量模板 |
| `.dockerignore` | 构建上下文排除清单 |
| `logs/auto-update.log` | 自动更新日志（运行后生成） |
