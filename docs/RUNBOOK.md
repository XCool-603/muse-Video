# 短剧聚合平台 · 部署与运维手册（RUNBOOK）

> **本手册写给谁**：手里有一台装了 Docker 的机器（Linux VPS / Windows + Docker Desktop / macOS），
> 要自己把「短剧聚合平台」跑起来、并且长期维护它的人。
>
> **怎么用**：按章节顺序做即可。每一章都是「照着敲 → 看到预期结果 → 继续下一步」。
> 出问题时看该步骤的「不对时怎么办」，或直接跳第七章的故障排查表。
>
> **事实依据**：本手册所有值都来自仓库提交 `2e8f0ff`（2026-10-09）的实际文件，每条关键值都标了 `文件:行`，
> 可用另一份产物 `DEPLOY-FACTS.md` 机械核对。**手册与仓库文件冲突时，以仓库文件为准。**
>
> **本手册不含任何部署包装脚本**：仓库里的部署包装脚本已经删除，所有部署 / 升级 / 定时更新
> 一律使用原生 `docker compose` 与 `git` 命令（原因见 1.6）。

**目录（7 个一级章节）**

| 章节 | 内容 | 什么时候看 |
|---|---|---|
| 一、前置要求 | 环境、关键事实速查、禁令、命令标注约定 | 动手之前 |
| 二、首次部署 | 从零到能打开站点，含验证清单 | 第一次部署 |
| 三、升级与回滚 | 源码模式 / 镜像模式升级、回滚、升级后已知坑 | 每次更新 |
| 四、端口配置 | 端口的三层结构、改端口、只绑本机 | 换端口 / 配反代 |
| 五、定时更新与迁移 | cron / 计划任务的原生写法，**从旧脚本时代迁移** | 想自动更新 / 老部署机 |
| 六、种子服务（可选） | 宿主上的 torrent-search 接入与排查 | 要用种子功能 |
| 七、备份恢复与故障排查 | 卷、备份、恢复、卸载、故障速查 | 出事时 / 定期维护 |

---

## 一、前置要求

### 1.1 部署形态（先知道你要部署的是什么）

- **单容器**：前端（Vue3 + Vite）与后端（.NET 10）打进同一个镜像，静态资源由 .NET 直接托管，
  **不需要额外的 Nginx 容器**（`Dockerfile:1-9`）。
- **compose 只有一个服务，服务名是 `app`**（`docker-compose.yml:17`）。
  所有 compose 子命令都用服务名操作：`docker compose logs app`、`docker compose exec app ...`。
- **compose 刻意不设 `container_name`**（`docker-compose.yml:23-26`），所以容器名形如
  `<项目名>-app-1`。**不要**在命令里写死容器名，用服务名或 `docker compose ps -q app` 取 id。
- **默认数据库是 SQLite**，数据落在命名卷里，零外部依赖（`docker-compose.yml:6-7`、`:35-37`）。
  想换 PostgreSQL 见 7.4。

### 1.2 关键事实速查（写死这些值，照着用就行）

| 事项 | 值 | 证据（文件:行） |
|---|---|---|
| compose 服务名 | `app` | `docker-compose.yml:17` |
| 对外端口 | `${APP_PORT:-18080}` → **默认 18080** | `docker-compose.yml:30` |
| 容器内端口 | **8080**（固定，不要改） | `Dockerfile:121`、`Dockerfile:128` |
| 数据卷逻辑名 | `shortdrama-data` | `docker-compose.yml:72`、`:87-89` |
| 数据卷实际名 | `<项目名>_shortdrama-data`，例如 `shortdrama_shortdrama-data` | `docs/DOCKER.md:629` |
| 容器内数据路径 | `/data`，SQLite 文件 `/data/shortdrama.db` | `docker-compose.yml:37`、`Dockerfile:118` |
| `.env` 生效变量 | **7 个**：`APP_PORT`、`ACCESS_GATE_ENABLED`、`ACCESS_PASSWORD`、`JWT_KEY`、`TORRENT_BASE_URL`、`TORRENT_ENABLED`、`POSTGRES_PASSWORD` | `.env.example:11-44` |
| `.env` 里被注释掉的变量 | `SHORTDRAMA_IMAGE`（默认注释；填了就是「镜像模式」） | `.env.example:20` |
| 默认访问口令 | `遵纪守法世界和平` | `.env.example:25`、`docker-compose.yml:42` |
| 健康检查 | `GET /health`，返回 `{"status":"healthy",...}` | `docker-compose.yml:75`、`Dockerfile:130-131`、`ShortDrama.Api/Program.cs:162` |
| 管理后台 | `/admin`，默认账号 `admin` / `admin123` | `README.md:155`、`ShortDrama.Infrastructure/Data/DbSeeder.cs:147-151` |
| 站点首页 | `http://<宿主IP>:18080` | `docs/DOCKER.md:200` |
| 运行身份 | 镜像内置的非 root 用户 `app`：**容器仍以 root 启动**（要先给数据卷 `chown`），入口脚本再用 `gosu` 把业务进程降权到 `app`。UID/GID 以容器内实测为准（`docker compose exec app id`），**不要写死数字** | `Dockerfile:84-98`、`Dockerfile:111-114`、`docker/entrypoint.sh:17-31` |
| 容器日志上限 | json-file，10MB × 3 个文件 | `docker-compose.yml:81-85` |
| 时区 | `Asia/Shanghai` | `Dockerfile:81` |

> `/health` 与 `/openapi/*` **不受访问口令门拦截**（`ShortDrama.Api/Access/AccessGateMiddleware.cs:74`、
> `README.md:904`），所以健康检查和监控可以直接打这两个路径。
>
> ⚠️ **「容器以非 root 运行」这句话不准确，别照着理解**：`Dockerfile` 里**没有 `USER` 指令**
> （已用 `^USER` 精确核对，无匹配），所以 `docker inspect <容器> --format '{{.Config.User}}'`
> 会返回空（即 root）。真正发生的是：容器以 **root 启动** → 入口脚本先 `chown -R app:app /data`
> （兼容旧版本 root 属主的卷）→ 再用 `gosu app` 降权 → `exec` 交棒给 dotnet。
> 所以 **业务进程**是非 root 的 `app`，而**容器配置层**是 root —— 两者不矛盾，验证方式见 2.9 第 8 条。
> `app` 的 UID/GID 由基础镜像决定（`Dockerfile:87` 注释里写的 1654 只是注释，仓库内**无实测证据**），
> 需要数字时**实测**：`docker compose exec app id`（`Dockerfile:110-114` 的构建期自检也是按
> **用户名**比较，而不是数字 UID）。

### 1.3 上线前必须改的 3 项

| # | 改什么 | 在哪改 | 不改的后果 |
|---|---|---|---|
| 1 | `JWT_KEY`（占位值 `please-change-this-secret-key-in-production-2025`） | `.env` | 任何人都能用公开的占位密钥伪造 JWT |
| 2 | `ACCESS_PASSWORD`（默认 `遵纪守法世界和平`） | `.env` | 全网都知道你的口令 |
| 3 | 后台管理员密码（默认 `admin` / `admin123`） | 站点 `/admin` 里登录后改 | 后台被默认口令进入 |

生成随机密钥（`JWT_KEY` 用，两个平台的结果**长度相同**）：

**执行位置**：宿主 shell（任意目录）

```bash
openssl rand -hex 48
```

**成功判据**：输出**一行**、**96 个十六进制字符**（只含 `0-9` 和 `a-f`），即 **48 字节**的随机值。
（换算关系：1 字节 = 2 个十六进制字符，所以 96 个字符 = 48 字节。）

Windows（PowerShell，**执行位置**同上）：

```powershell
-join ((1..96) | ForEach-Object { '{0:x}' -f (Get-Random -Max 16) })
```

**成功判据**：与上一条相同 —— 输出 **96 个十六进制字符**（只含 `0-9` 和 `a-f`）。
把整串填进 `.env` 的 `JWT_KEY=` 后面（**不要带引号、不要带空格**）。

**自己验一遍长度**（不信上面的描述就跑这条，**执行位置**：宿主 shell（项目目录））：

```bash
sed -n 's/^JWT_KEY=//p' .env | tr -d '\r\n' | wc -c
```

**成功判据**：输出 `96`。

Windows（PowerShell，**执行位置**同上）：

```powershell
((Get-Content .env -Encoding UTF8 | Where-Object { $_ -match '^JWT_KEY=' }) -replace '^JWT_KEY=', '').Trim().Length
```

**成功判据**：与上一条相同 —— 输出 `96`。

> ⚠️ Windows PowerShell 下**必须带 `-Encoding UTF8`**。实测（PowerShell 5.1，本仓库的 `.env.example` 副本）：
> `Get-Content .env` 不带编码参数只读出 **38 行**（正确是 **44 行**），
> 且会把 `ACCESS_PASSWORD=遵纪守法世界和平` 里的中文读成乱码 —— 因为 5.1 的默认编码是系统 ANSI 而不是 UTF-8。
> 上面的自检命令与 4.3 的改端口命令都已加上这个参数。

**不对时怎么办**：

- PowerShell 下**只数到 48 个字符** → 你用的是旧写法（`1..48`）。按上面这条 `1..96` 重新生成即可，
  两个平台都是 96 个字符；
- **超过 96** → 把两行输出粘在一起了，或粘贴时混进了换行，重新生成一次；
- 只有 1 个字符或 `0` → 上面这条 `sed` 没取到值：确认 `.env` 里是 `JWT_KEY=` 开头、
  **行首没有空格**、且这一行没有被 `#` 注释掉。

> ⚠️ **别照抄 `.env.example:29` 的 Windows 命令**：那里写的是
> `-join ((1..48) | ForEach-Object { '{0:x}' -f (Get-Random -Max 16) })`，
> 循环 48 次只会得到 **48 个**十六进制字符，而同文件下一行 `:30` 的 bash 命令 `openssl rand -hex 48`
> 得到 **96 个**字符，两条命令**长度不一致** —— 容易让人误以为生成失败而反复重试。
> 生成 JWT 密钥时以本节为准（`1..96`）。**该长度不一致属仓库侧既存事实**（`.env.example:29` 的 pwsh 命令
> 与同文件 `:30` 的 bash 命令，48 vs 96 个字符）。`DEPLOY-FACTS.md` **目前未收录**这一条 ——
> 该文件 §2-1 讲的是另一个问题（「JWT_KEY 首次运行脚本会自动替换为随机值」的那个脚本已不存在），
> 建议提给仓库维护者。

### 1.4 环境要求

| 依赖 | 版本 | 说明 |
|---|---|---|
| Docker Engine | 20.10+ | Linux 安装：`curl -fsSL https://get.docker.com \| sh` |
| Docker Desktop | 最新版 | Windows / macOS 用；**必须处于运行状态**（托盘图标显示「运行中」） |
| Docker Compose | v2（`docker compose` 子命令） | 随 Docker Desktop 自带。只有 `docker-compose`（v1）不行 |
| git | 任意 | **只有「定时自动更新」需要**；纯手工升级也可以不装 |
| 磁盘 | 首次构建约需 3-5 GB | `docs/DOCKER.md:641` |
| 端口 | 默认对外 `18080/tcp`（可改） | 云服务器要在安全组 + 宿主防火墙两处放行 |

### 1.5 部署目录必须是 ASCII 名（很重要，且很容易踩）

Compose 的**项目名默认取自当前目录名**，而项目名又决定了卷名前缀与容器名前缀。
Compose 只保留 `a-z0-9_-`，其余字符会被清洗掉：

- 目录叫 `shortdrama` → 项目名 `shortdrama` → 卷名 `shortdrama_shortdrama-data`、容器名 `shortdrama-app-1`；
- 目录叫**中文名**（例如 `短剧聚合`）→ 清洗后可能变成空串 → `docker compose` 会因为项目名无效而直接拒绝执行。

**结论：克隆到一个 ASCII 目录名**，本手册统一用 `shortdrama`：

**执行位置**：宿主 shell（任意目录）

```bash
cd /opt                                    # 换成你的部署父目录
git clone https://github.com/XCool-603/muse-Video.git shortdrama
cd shortdrama
```

**成功判据**：`pwd` 结尾是 `/shortdrama`；`ls docker-compose.yml .env.example` 两个文件都在。

**不对时怎么办**：如果已经用中文目录部署过、并且容器正在跑，**不要**随便改目录名或项目名 ——
项目名一变，卷名前缀跟着变，`docker compose up -d` 会去挂一个**全新的空卷**，表现就是「数据没了」。
必须改的话先按第七章备份，改完再把数据恢复到新卷。确实要用中文目录时，在 `.env` 里显式写死项目名
（`COMPOSE_PROJECT_NAME=shortdrama`），并且**从一开始就这么做**。

### 1.6 四条禁令（照做能避开本手册里 80% 的坑）

| 禁止 | 为什么 |
|---|---|
| **不要使用任何部署包装脚本**（仓库里两个以 `deploy` 开头的脚本已经删除，提交 `09cdd7b`） | 报错会被脚本的行号和退出码藏一层；脚本会把宿主 UID 之类的值固化进构建参数。需要固定流程就用 docker 自己的机制：`depends_on` + `healthcheck`、`build.args`、`env_file`。**定时更新也直接写 `docker compose` 命令**（见第五章） |
| **不要用宝塔 / 1Panel 等面板的「更新容器」按钮升级** | 它不是在跑 compose，而是把当前容器的动态 IP 回填成静态 IP，报 `user specified IP address is supported only when connecting to networks with user configured subnets`；而且它不读你的 compose 文件，会出现「改了配置却不生效」。用 `docker compose up -d` |
| **不要把种子服务地址写成 `127.0.0.1`** | 容器里的 `127.0.0.1` 指容器自己，不是宿主机。必须用 `host.docker.internal`（见第六章） |
| **不要直接编辑被 git 跟踪的 `docker-compose.yml`** | 工作区一变脏，`git pull --ff-only` 会失败 → 定时更新静默停摆。要改就写 `docker-compose.override.yml`（见 4.5） |

### 1.7 命令标注约定（怎么读下面的命令块）

本手册**每一条命令**都标注了两件事，照着核对就不会「敲了但不知道对不对」：

- **执行位置**，只有三种取值：
  - `宿主 shell（任意目录）`：在服务器的普通终端里执行；
  - `宿主 shell（项目目录 <项目目录>）`：先 `cd` 到 clone 出来的 `shortdrama` 目录再执行（compose 命令必须在这里跑）；
  - `容器内（app）`：命令前面必须带 `docker compose exec app`，在容器里执行。
- **成功判据**：预期输出、退出码，或一条**独立的验证命令**。看不到判据描述的现象，就走该步骤的
  「不对时怎么办」。

> 全文命令均为 Linux/macOS 语法；Windows 只在语法不同的地方单独给出 PowerShell 版本。
> 所有 compose 命令都假设你已经 `cd` 到项目目录。
>
> 以**表格**形式给出的命令，其「执行位置」与「成功判据」写在表格的列里，或写在表格正上方的一句话说明里；
> 以**独立代码块**形式给出的命令，标注都紧贴在代码块的上一行和下一行。

---

## 二、首次部署

> 目标：从一台只有 Docker 的机器，做到浏览器能打开站点、健康检查通过、后台能登录。
> 全流程约 10 分钟（首次构建 3-5 分钟）。

### 2.1 步骤 1：确认 Docker 可用

**执行位置**：宿主 shell（任意目录）

```bash
docker version --format '{{.Server.Version}}'
docker compose version
```

**成功判据**：

- 第一条打印出版本号（如 `27.3.1`），**没有** `Cannot connect to the Docker daemon`；
- 第二条打印 `Docker Compose version v2.x.y`（**必须是 v2**）。

**不对时怎么办**：

- `docker: command not found` → 没装 Docker，或 Windows 上装完没重开终端；
- `Cannot connect to the Docker daemon` → Docker Desktop 没启动，等托盘图标变「运行中」；
- 只有 `docker-compose`（带横杠）→ 那是 Compose v1，升级 Docker 以获得 `docker compose`。

### 2.2 步骤 2：取代码

见 1.5 的命令块（克隆到 ASCII 目录）。

**不对时怎么办**：`Permission denied (publickey)` → 远端地址换成 HTTPS；
克隆慢 → 用镜像源或先下载 zip 再解压（解压目录同样要 ASCII 名）。

### 2.3 步骤 3：生成配置文件 `.env`

**执行位置**：宿主 shell（项目目录）

```bash
cp .env.example .env
grep -vE '^\s*#|^\s*$' .env | wc -l
```

**成功判据**：最后一条命令输出 **`7`**（即 `.env` 里有 7 个生效变量，与 1.2 一致）。

Windows（PowerShell，**执行位置**同上）：

```powershell
Copy-Item .env.example .env
(Get-Content .env -Encoding UTF8 | Where-Object { $_ -notmatch '^\s*#' -and $_ -notmatch '^\s*$' }).Count
```

**成功判据**：与上一条相同 —— Windows 下输出同一个数 **`7`**。

**不对时怎么办**：

- 输出不是 7 → 说明你改过 `.env` 或没复制成功，重新 `cp .env.example .env` 覆盖；
- 提示 `.env` 已存在 → 说明之前部署过，**先备份**：`cp .env .env.bak`，再决定是否覆盖
  （老 `.env` 里可能有你改过的端口和口令，**不要无脑覆盖**）。

> `.env` 已被 `.gitignore` 忽略（`.gitignore:25`），不会进仓库，也不会进构建上下文
> （`.dockerignore:37-39`）。

### 2.4 步骤 4：改掉必改项

编辑 `.env`，至少改这两行（生成方式见 1.3）。下面两行是**文件内容**、不是命令
（**执行位置**：宿主 shell（项目目录），用编辑器打开 `.env` 改）：

```
JWT_KEY=<粘贴 96 位随机十六进制串>
ACCESS_PASSWORD=<你自己的口令>
```

**执行位置**：宿主 shell（项目目录）

```bash
grep -E '^(JWT_KEY|ACCESS_PASSWORD|APP_PORT)=' .env
```

**成功判据**：`JWT_KEY` 不是 `please-change-this-secret-key-in-production-2025`；`ACCESS_PASSWORD` 不是默认口令；
`APP_PORT` 显示 `18080`（或你想要的端口）。

**不对时怎么办**：`.env` 的改动**必须重新创建容器才生效**，改完执行 2.5 的 `docker compose up -d`
（已经跑着就 `docker compose up -d`，不需要 `--build`）。

### 2.5 步骤 5：构建并启动

**执行位置**：宿主 shell（项目目录）

```bash
docker compose up -d --build
```

**成功判据**：结尾出现类似

```
✔ Container shortdrama-app-1  Started
```

且退出码为 0（`echo $?` 输出 0）。首次构建会拉基础镜像并编译前后端，**约 3-5 分钟**属正常。

**不对时怎么办**：

- 构建失败 → 看报错最后 20 行；若是拉包失败（Docker Hub / NuGet / npm），给 Docker 配国内镜像加速后
  `docker compose build --no-cache && docker compose up -d`；
- `project name must not be empty` / 项目名无效 → 目录名不是 ASCII，见 1.5；
- `port is already allocated` → 18080 被占用，见 4.3 换端口；
- `Conflict. The container name ... is already in use` → **那个名字不一定属于你**：
  `docker inspect <名字> --format '{{index .Config.Labels "com.docker.compose.project"}}'` 看它属于哪个项目；
  是别人的就别删，用 `docker rename <名字> <名字>-old` 解封（非破坏性）。

### 2.6 步骤 6：看状态与日志

**执行位置**：宿主 shell（项目目录）

```bash
docker compose ps
docker compose logs --tail=50 app
```

**成功判据**：

- `docker compose ps` 的 `STATUS` 列出现 `Up ... (healthy)`；
- 日志里没有 `EACCES`、没有 `SQLite Error 14: unable to open database file`、没有反复重启的堆栈。

**不对时怎么办**：健康检查的 `start_period` 是 40 秒（`docker-compose.yml:78`），首次启动还要同步数据源，
**等 1-2 分钟**再判断。仍不 healthy 就 `docker compose logs -f app` 跟日志；权限类报错见 7.6。

### 2.7 步骤 7：放行端口（外网访问必做，两处都要）

**执行位置**：宿主 shell（任意目录），以及云厂商控制台

```bash
# Ubuntu / Debian
sudo ufw allow 18080/tcp

# CentOS / RHEL / Alma
sudo firewall-cmd --permanent --add-port=18080/tcp && sudo firewall-cmd --reload
```

**成功判据**：

```bash
ss -lntp | grep 18080
```

输出里的绑定地址是 `0.0.0.0:18080` 或 `*:18080`（**不是** `127.0.0.1:18080`），说明宿主在监听且对所有网卡开放。

**不对时怎么办**：

- 显示 `127.0.0.1:18080` → 端口映射里带了本机前缀（见 4.5），外网永远访问不到；
- 本机 `curl` 通、外网不通 → **云安全组没放行**（阿里云 / 腾讯云 / 甲骨文控制台里再放行一次该 TCP 端口）；
- 完全没有 LISTEN → 端口映射写反了或应用没起来，回到 2.6。

### 2.8 步骤 8：浏览器验证并改后台密码

**执行位置**：你的浏览器

1. 打开 `http://<宿主IP>:18080`；
2. 第一次会跳到 `/gate`，输入 `.env` 里的 `ACCESS_PASSWORD`（默认 `遵纪守法世界和平`）；
3. 进站后打开 `http://<宿主IP>:18080/admin`，用 `admin` / `admin123` 登录，**立刻改掉这个密码**。

**成功判据**：首页能正常出内容；`/admin` 能登录；`http://<宿主IP>:18080/health` 返回
`{"status":"healthy","time":"..."}`。

**不对时怎么办**：一直跳 `/gate` → 口令输错，或 `.env` 改完没重建容器（`docker compose up -d`）；
`/health` 打不开但首页能开 → 检查是否被反向代理拦截了 `/health`。

### 2.9 首次部署后的验证清单（≥5 条，逐条复制执行）

> 这是本手册的**验收清单**：8 条全过，才算「部署成功」。
> 全部命令的**执行位置**：宿主 shell（项目目录），第 7、8 条除外。

| # | 验证命令 | 成功判据 | 不对时怎么办 |
|---|---|---|---|
| 1 | `docker compose config \| grep -A3 'ports:'` | 输出 `published: "18080"` 与 `target: 8080`（变量插值已生效） | 端口不对 → 第四章 |
| 2 | `docker compose ps` | `STATUS` 列是 `Up ... (healthy)` | 见 2.6 |
| 3 | `curl -fsS http://127.0.0.1:18080/health` | 输出 `{"status":"healthy","time":"..."}` | 见 2.6 / 7.7 |
| 4 | `curl -s -o /dev/null -w '%{http_code}\n' http://127.0.0.1:18080/admin` | 输出 `302`（口令门生效）或 `200`（已关口令门） | 输出 `000` → 应用没起来 |
| 5 | `docker compose logs --tail=50 app` | 无 `EACCES`、无 `SQLite Error 14` | 见 7.6 |
| 6 | `docker volume ls \| grep shortdrama-data` | 看到 `<项目名>_shortdrama-data`（默认 `shortdrama_shortdrama-data`） | 没看到 → 卷没建起来，看 `docker compose ps -a` |
| 7 | `docker compose exec app curl -fsS http://localhost:8080/health` | 输出同上（**执行位置：容器内 app**，同时验证镜像里 `curl` 可用） | 报 `curl: not found` → 镜像不对，重新 `--build` |
| 8 | `docker compose exec app id` | 输出 `uid=<非0>(app) gid=<非0>(app)` —— 也就是**用户名是 `app`、UID 不是 0**（UID 具体数字由基础镜像决定，用这条命令实测，**不要照抄任何写死的数字**）（**执行位置：容器内 app**，证明业务进程不是 root 在跑） | 显示 `uid=0(root)` → 入口脚本降权失败，看容器日志里的 gosu 告警 |

---

## 三、升级与回滚

### 3.1 先确认你现在是哪种模式

`.env` 里的 `SHORTDRAMA_IMAGE` 决定升级方式：**留空 = 源码模式**（本机构建）；**填了地址 = 镜像模式**（拉预构建镜像）。

**执行位置**：宿主 shell（项目目录）

```bash
grep -E '^SHORTDRAMA_IMAGE=' .env || echo "SHORTDRAMA_IMAGE 未设置 → 源码模式"
```

**成功判据**：输出 `SHORTDRAMA_IMAGE=ghcr.io/...` → 镜像模式（走 3.4）；
输出「未设置」或什么都没有 → 源码模式（走 3.3）。

> `.env.example` 里这一行是**注释掉的**（`.env.example:20`），所以从模板复制出来的 `.env` 默认就是源码模式。

### 3.2 升级前：先看有没有新版本（可选，推荐）

**执行位置**：宿主 shell（项目目录）

```bash
git fetch --quiet && git log --oneline HEAD..@{u}
```

**成功判据**：**有输出** = 远端有新提交（可升级）；**无输出** = 已是最新，不用动。

**不对时怎么办**：

- `fatal: detected dubious ownership` → `git config --global --add safe.directory "<仓库绝对路径>"`；
- `Permission denied (publickey)` → 该用户没有远端私钥，改用 HTTPS 远端地址；
- 卡住超过 300 秒 → 网络不通或 22 端口被挡，改用 HTTPS。

### 3.3 源码模式升级（默认）

**执行位置**：宿主 shell（项目目录）

```bash
git pull --ff-only && docker compose up -d --build
```

**成功判据**：

- `git` 输出 `Fast-forward`（或 `Already up to date.`）；
- compose 结尾出现 `Started` / `Recreated`；
- 随后 `docker compose ps` 的 `STATUS` 回到 `Up ... (healthy)`。

**为什么安全**：compose 会**先把新镜像构建成功，再重建容器**。构建失败时旧容器继续跑，站点不中断
（`docs/DOCKER.md:228`）。

**不对时怎么办**：

- `git pull` 报 `Your local changes would be overwritten` → 部署机上有被跟踪文件的改动。**不要在部署机上改被 git 跟踪的文件**（尤其 `docker-compose.yml`）；用 `git stash` 暂存，或把定制写进 `docker-compose.override.yml`（见 4.5）；
- 构建失败 → 旧容器仍在跑，先 `docker compose logs --tail=50 app` 确认站点没挂，再修构建问题。

### 3.4 镜像模式升级（`.env` 里填了镜像地址）

**执行位置**：宿主 shell（项目目录）

```bash
docker compose pull app && docker compose up -d
```

**成功判据**：`pull` 输出 `Pulled` 或 `Image ... is up to date`；`up -d` 输出 `Started` / `Recreated`
或 `Container shortdrama-app-1  Running`（镜像没变时 compose 会跳过重启）。

**不对时怎么办**：

- 拉取报 `unauthorized` / `denied` → GHCR 包是私有的，先登录一次：

  **执行位置**：宿主 shell（任意目录）

  ```bash
  echo <你的PAT> | docker login ghcr.io -u <你的GitHub用户名> --password-stdin
  ```

  **成功判据**：输出 `Login Succeeded`（PAT 需要 `read:packages` 权限）。

- 容器起来就退出、日志报 `exec format error` → 镜像架构与本机不符（ARM 机器拉了 amd64 镜像）。
  去改 `.github/workflows/docker-publish.yml` 顶部的 `PLATFORMS`，加上 `linux/arm64` 重新构建。

### 3.5 升级过程做了什么

| 步骤 | 说明 |
|---|---|
| 1. 拉取 | `git pull --ff-only` 快进合并，绝不产生意外的合并提交 |
| 2. 构建/拉取 | 先拿到新镜像；**失败则不动容器** |
| 3. 切换 | 重建 `app` 容器；健康检查不通过时 `docker compose ps` 立刻能看出来 |

### 3.6 回滚

**执行位置**：宿主 shell（项目目录）

```bash
git log --oneline -5                 # 找到上一个正常的提交
git reset --hard <上一个正常的提交>    # 例如 git reset --hard 09cdd7b
docker compose up -d --build
```

**成功判据**：`docker compose ps` 回到 `Up ... (healthy)`，站点可访问。

**不对时怎么办**：

- **镜像模式无法这样回滚**：`latest` 标签指向最新镜像，回滚不了。要么改用固定 tag，
  要么记录当前可用的 digest：`docker inspect --format '{{index .RepoDigests 0}}' ghcr.io/<你>/<仓库>:latest`，
  出问题时把 `.env` 里的 `SHORTDRAMA_IMAGE` 改成 `<镜像>@<digest>` 再 `docker compose up -d`；
- **数据库结构不会自动回退**：代码回滚了，但新版本可能已经改过库结构。涉及破坏性变更时，
  用第七章的备份恢复，不要只回滚代码。

### 3.7 升级后的三个已知坑（老部署机必看）

1. **端口不会跟着仓库默认值变**：`.env` 里的 `APP_PORT` 优先于 compose 的默认值。
   仓库把默认端口从 8080 改成 18080 只影响**新部署**，老部署必须自己改 `.env`（见第四章）。
2. **老数据卷的属主会被自动修正**：旧版本容器以 root 写过 `/data`，而当前镜像的入口脚本会把
   **业务进程降权到非 root 的 `app` 用户**（容器本身仍以 root 启动，正是为了先做这次 `chown`）。
   入口脚本 `docker/entrypoint.sh:20-31` 会先 `chown -R app:app /data` 再降权，
   **你不需要做任何事**。如果日志出现 `SQLite Error 14: unable to open database file`，见 7.6。
3. **旧的定时任务还指向已删除的脚本**：部署机上如果装过 cron / 计划任务，它调用的脚本文件已经不在了，
   会一直失败而更新早就停了 —— **必须按 5.5 清理并换成原生命令**。

---

## 四、端口配置

### 4.1 先分清三个端口（这是「端口打不开」的头号原因）

| 层 | 写在哪 | 当前值 | 作用 |
|---|---|---|---|
| 应用监听端口 | `Dockerfile:121` 的 `ASPNETCORE_URLS=http://+:8080` | 容器内 `8080` | 真正决定进程听哪个端口 |
| 容器内端口 | `Dockerfile:128` 的 `EXPOSE 8080` | `8080` | 只是文档/`-P` 的依据，**不改变实际监听** |
| 对外映射 | `docker-compose.yml:30` 的 `"${APP_PORT:-18080}:8080"` | 宿主 `18080` → 容器 `8080` | 把容器端口发布到宿主 |

**只改对外端口**。容器内保持 8080 —— 容器内的端口是隔离的，改它只会多改一堆地方
（`EXPOSE`、`ASPNETCORE_URLS`、健康检查）。

### 4.2 查当前真正生效的端口

**执行位置**：宿主 shell（项目目录）

```bash
docker compose config | grep -A3 'ports:'
```

**成功判据**：输出里 `published: "18080"`、`target: 8080`（这是 compose 解析 `.env` 之后的**最终值**，
比看 `.env` 更可信）。

### 4.3 改端口（推荐：改 `.env`）

**执行位置**：宿主 shell（项目目录）

```bash
grep '^APP_PORT' .env                          # 先看当前值
sed -i 's/^APP_PORT=.*/APP_PORT=9000/' .env    # 改成你要的端口（也可以直接编辑 .env）
docker compose up -d                           # 重建容器，新端口才生效
docker compose config | grep -A3 'ports:'      # 确认生效
```

**成功判据**：`docker compose config` 显示 `published: "9000"`。

Windows（PowerShell，**执行位置**同上）：

```powershell
$utf8 = New-Object System.Text.UTF8Encoding($false)   # $false = 不写 BOM
$lines = (Get-Content .env -Encoding UTF8) -replace '^APP_PORT=.*', 'APP_PORT=9000'
[System.IO.File]::WriteAllText((Resolve-Path .env), (($lines -join "`n") + "`n"), $utf8)
docker compose up -d
docker compose config | Select-String -Pattern 'ports:' -Context 0,3
```

> ⚠️ **不要用 `(Get-Content .env) -replace ... | Set-Content .env`**（这是最容易顺手写出的写法，**实测会损坏配置**）。
> 在 PowerShell 5.1 上实测本仓库的 `.env`：不带 `-Encoding UTF8` 的 `Get-Content` 只读出 **38 行**（正确 44 行）、
> 中文口令读成乱码；`Set-Content` 默认编码还会把行尾从 **LF 改成 CRLF**。结果是
> 「口令没错却一直跳 `/gate`」或「变量凭空少了几个」。
> 上面那段用 `-Encoding UTF8` 读、用 `WriteAllText` 以**无 BOM + LF** 写，
> 实测改完仍是 44 行、中文完好、行尾仍是 LF。
> **更省事的做法**：直接用记事本 / VS Code / Notepad++ 编辑 `.env`（存成 **UTF-8 无 BOM**、行尾 **LF**）。

**成功判据**：

- `docker compose config` 显示 `published: "9000"`；
- `ss -lntp | grep 9000` 有 LISTEN；
- `curl -fsS http://127.0.0.1:9000/health` 返回 `{"status":"healthy",...}`。

**不对时怎么办**：

- `docker compose config` 里还是旧端口 → `.env` 没改对（注意行首不能有空格、不能是注释行）；
- 本机通、外网不通 → **新端口要重新放行**：云安全组 + 宿主防火墙两处都放（命令见 2.7，把 18080 换成 9000）。

### 4.4 临时换一次端口（不改 `.env`）

**执行位置**：宿主 shell（项目目录）

```bash
APP_PORT=9000 docker compose up -d
```

**成功判据**：**把同一个前缀再加到验证命令上**（前缀不写进 `.env`，所以验证时也要带上）：

**执行位置**：宿主 shell（项目目录）

```bash
APP_PORT=9000 docker compose config | grep -A3 'ports:'
```

显示 `published: "9000"`、`target: 8080`，才算这次覆盖生效了。

> ⚠️ **不带前缀的 `docker compose config` 仍会显示 `.env` 里的端口（例如 `published: "18080"`），
> 这是预期行为，不是失败。** 原因：`APP_PORT=9000 docker compose up -d` 只是给**那一条命令**
> 临时加了个环境变量，既不写进 `.env`、也不导出到当前 shell；compose 的插值优先级是
> **shell 环境变量 > `.env` 文件**（`DEPLOY-FACTS.md` §F2），前缀一去掉就只剩 `.env` 的值。
> **别因为看到 18080 就去改 `.env`** —— 那会把一次性覆盖变成永久改动，正是本节要避免的。

**注意**：这是一次性的。下次不带这个环境变量执行 `docker compose up -d` 会**退回 `.env` 里的值**
（容器会被重建回原端口）。要长期生效请走 4.3。

**另外注意**：容器重建时如果新端口已被占用，`up -d` 会报 `port is already allocated` ——
这次覆盖也就没生效，先用 `ss -lntp | grep 9000` 确认端口空闲。

### 4.5 升级不会改你的端口（`.env` 优先于 compose 默认值）

`docker-compose.yml:30` 里的 `${APP_PORT:-18080}`，`:-` 后面的 `18080` 只是**没配 `.env` 时的兜底值**。

> ⚠️ 老部署机常见误区：看到仓库把默认端口从 8080 改成 18080，就以为升级后端口会变。
> **不会** —— 你的 `.env` 里如果还写着 `APP_PORT=8080`，升级后仍然是 8080。要改就改 `.env`。

### 4.6 想只绑本机（前面挂反向代理）—— 用 override 文件，别改被跟踪的 compose

反向代理方案（Caddy / Nginx 配置样例见 `docs/DOCKER.md:555-595`）通常希望容器只监听 `127.0.0.1`。
**不要直接改 `docker-compose.yml`**：它被 git 跟踪，改脏了 `git pull --ff-only` 会失败，定时更新就停了。

正确做法是新建一个 **`docker-compose.override.yml`**（Compose 会自动加载它，且它不在仓库里，不会弄脏工作区）。
**执行位置**：宿主 shell（项目目录），把下面这段内容原样存成 `docker-compose.override.yml`（这是文件内容，不是命令）：

```yaml
services:
  app:
    ports: !override
      - "127.0.0.1:${APP_PORT:-18080}:8080"
```

> ⚠️ `!override` 标签是必须的（需要 Compose ≥ 2.24）。Compose 对 `ports` 是**合并**而不是替换，
> 少了这个标签你会同时得到两条映射（`18080:8080` 和 `127.0.0.1:18080:8080`），
> 容器启动时报 `port is already allocated`。
>
> 老版本 Compose 不支持 `!override` 时，退回「独立完整文件」方案：把 `docker-compose.yml` 复制成
> `docker-compose.local.yml`，改掉 `ports`，然后用 `docker compose -f docker-compose.local.yml up -d` 启动
> （完整文件不参与合并，没有合并语义问题）。代价是以后升级要手工同步这个文件。

**执行位置**：宿主 shell（项目目录）

```bash
docker compose up -d
docker compose config | grep -A3 'ports:'
ss -lntp | grep 18080
```

**成功判据**：`docker compose config` 里**只有一条**端口映射，且带 `host_ip: 127.0.0.1`；
`ss -lntp` 显示 `127.0.0.1:18080`（而不是 `0.0.0.0:18080`）。

**不对时怎么办**：报 `port is already allocated` → override 里的 `!override` 没生效（Compose 版本太低）。

> 📌 用了 `-f` 显式指定 compose 文件时（例如 PostgreSQL 模式），`docker-compose.override.yml`
> **不会**被自动加载，必须再 `-f docker-compose.override.yml` 一次。

---

## 五、定时更新与迁移

> 定时更新的目标是：每天固定时间自动 `git pull` + 重建 + 切换，失败时不影响正在跑的站点。
> **原生命令即可，不需要任何脚本**。

### 5.1 动手前必须做的两件准备

**准备 1：建 `logs/` 目录。** 定时命令要把输出重定向到 `logs/auto-update.log`，
而**仓库里没有 `logs/` 目录**（`.gitignore:19` 把它忽略了）。目录不存在时，shell 的重定向会失败，
**整条命令根本不会执行** —— 表现为「定时任务静默不跑」，很难查。

**执行位置**：宿主 shell（项目目录）

```bash
mkdir -p logs && ls -d logs
```

**成功判据**：输出 `logs`。

**准备 2：确认 `git` 与 `docker` 的绝对路径**（cron 的 `PATH` 很短，找不到它们）：

**执行位置**：宿主 shell（任意目录）

```bash
command -v git docker
```

**成功判据**：打印两个绝对路径，例如 `/usr/bin/git`、`/usr/bin/docker`。把它们记下来备用。

### 5.2 Linux / macOS：写进宿主 crontab

**执行位置**：宿主 shell（任意目录）

```bash
crontab -e
```

**成功判据**：crontab 编辑器正常打开（保存退出时无报错）。

在文件末尾加入下面两行（**把 `/opt/shortdrama` 换成你的项目绝对路径**；
**执行位置**：宿主 shell（任意目录），就在上一步打开的编辑器里；下面这段是**要粘贴的 crontab 内容**，不是命令）：

```cron
PATH=/usr/local/bin:/usr/bin:/bin
0 4 * * * cd /opt/shortdrama && git pull --ff-only && docker compose up -d --build >> logs/auto-update.log 2>&1 # shortdrama-auto-update
```

- 第 1 行补上 `PATH`，否则 cron 找不到 `docker` / `git`（这是「定时任务没跑」最常见的原因）；
- 行尾的 `# shortdrama-auto-update` 是**标记注释**，卸载/迁移时按它整行删除；
- `cd` 是必须的：compose 命令必须在项目目录里执行，且日志路径是相对目录的。

**成功判据**：下面这段内容会出现在 `crontab -l` 的输出里。

**执行位置**：宿主 shell（任意目录）

```bash
crontab -l | grep shortdrama
```

**成功判据**：输出里能看到上面那条完整命令（含 `cd ... && git pull --ff-only && docker compose up -d --build`）。

**不对时怎么办**：

- 没看到 → 保存时出错，重新 `crontab -e`；
- 到了 04:00 没动静 → 先**手动跑一遍那条命令**（`cd /opt/shortdrama && git pull --ff-only && docker compose up -d --build`），
  手动能跑通说明是 cron 环境问题（`PATH`、`HOME`、`safe.directory`）；再 `tail -n 50 logs/auto-update.log` 看日志。

### 5.3 Windows：用任务计划程序（原生 `docker compose`）

**推荐用 PowerShell 注册**（等价于图形界面操作，参数不易填错）：

**执行位置**：宿主 shell（任意目录，PowerShell）

```powershell
$proj = 'C:\shortdrama'      # 换成你的项目绝对路径（ASCII 名）
$action = New-ScheduledTaskAction -Execute 'pwsh.exe' `
    -Argument "-NoProfile -Command `"cd '$proj'; git pull --ff-only; docker compose up -d --build *>> logs\auto-update.log`"" `
    -WorkingDirectory $proj
$trigger  = New-ScheduledTaskTrigger -Daily -At 04:00
$settings = New-ScheduledTaskSettingsSet -MultipleInstances IgnoreNew `
    -StartWhenAvailable -ExecutionTimeLimit (New-TimeSpan -Hours 1)
$principal = New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" `
    -LogonType S4U -RunLevel Highest
Register-ScheduledTask -TaskName 'ShortDramaAutoUpdate' -Action $action -Trigger $trigger `
    -Settings $settings -Principal $principal `
    -Description '短剧聚合平台：定时拉取最新代码并重建重启' -Force
```

- 没装 PowerShell 7 就把 `pwsh.exe` 换成 `powershell.exe`；
- `-MultipleInstances IgnoreNew`：上一次还没跑完时，这一次直接跳过，不叠加；
- `-WorkingDirectory` 必须设对，否则 compose 找不到 `docker-compose.yml`；
- **必须让任务在未登录时也能运行**（`-LogonType S4U`，或图形界面选「不管用户是否登录都要运行」），
  否则宿主上没人登录时任务不会执行，而且不会有任何报错。`S4U` 不存密码，但要求该账户具备
  「作为批处理作业登录」权限；目标机不支持 `S4U` 时改用 `-LogonType Password` 并另外提供该账户的密码。

**执行位置**：宿主 shell（任意目录，PowerShell）

```powershell
Get-ScheduledTask -TaskName ShortDramaAutoUpdate | Select-Object TaskName,State
Get-ScheduledTaskInfo -TaskName ShortDramaAutoUpdate | Select-Object LastRunTime,LastTaskResult
```

**成功判据**：第一条能看到任务、`State` 为 `Ready`；第二条 `LastTaskResult` 为 `0`（表示上次运行成功）。
刚注册、还没到过触发点时，`LastRunTime` 会显示 `1999/11/30`、`LastTaskResult` 为 `267011`
（`0x41303`，任务从未运行）——**这是正常的**，等首次触发之后再回来看是否为 `0`。

**图形界面等价操作**（不想用命令行时）：任务计划程序 → 创建任务 → 常规里填名称
`ShortDramaAutoUpdate`，并选**「不管用户是否登录都要运行」**（默认是「只在用户登录时运行」，
选错就会在无人登录时静默不执行；选好后点确定时会要求输入该账户密码）→ 触发器「每天 04:00」→
操作「启动程序」：程序 `pwsh.exe`，
参数 `-NoProfile -Command "cd 'C:\shortdrama'; git pull --ff-only; docker compose up -d --build *>> logs\auto-update.log"`，
**「起始于」填项目目录** → 设置里勾「如果错过计划开始时间，请尽快启动任务」，
并选「如果任务已在运行，则以下规则适用：不启动新实例」。

**不对时怎么办**：`LastTaskResult` 非 0 → `Get-Content "$proj\logs\auto-update.log" -Tail 50` 看输出；
常见结果码：`0x1` 是**子进程自己的退出码**（要看日志才知道为什么失败）；`0x80070002` 是**找不到程序**（例如 `-Execute` 写的 `pwsh.exe` 本机没装，改成 `powershell.exe`）；`0x8007010B` 是**工作目录无效**（`-WorkingDirectory` 指向的目录不存在，确认 `logs` 目录已存在，见 5.1）。
`LastTaskResult` 为 `0x41303`（267011）、`LastRunTime` 显示 `1999/11/30` = 任务从未运行；
若已过触发点（比如次日 04:00 之后）仍是这个组合，多半是「只在用户登录时运行」而当时无人登录，
按上面第 1/2 条改成未登录也能运行。

### 5.4 只想检查有没有新版本（不升级）

**执行位置**：宿主 shell（项目目录）

```bash
git fetch --quiet && git log --oneline HEAD..@{u}
```

**成功判据**：**有输出** = 有新版本；**无输出** = 已是最新。接监控时可以用它判断：

**执行位置**：宿主 shell（项目目录）

```bash
git fetch --quiet
if [ -n "$(git log --oneline HEAD..@{u})" ]; then echo "短剧聚合有新版本"; fi
```

**成功判据**：有新版本时打印一行提示，无新版本时无输出（退出码 0）。

### 5.5 从旧脚本时代迁移（老部署机必做）

**背景**：仓库已经删除了两个以 `deploy` 开头的部署包装脚本（提交 `09cdd7b`）。
如果你的部署机上装过定时更新任务，那个任务调用的是**已经不存在的脚本文件**：

- 每次触发都会失败（`No such file or directory`），**自动更新其实早就停了**；
- 失败是静默的 —— 站点照常在跑，你不会收到任何提示。

所以升级到当前版本后，**必须**按下面的 M1 → M8 清理并换成原生命令。

#### 步骤 M1：确认有没有旧任务

**执行位置**：宿主 shell（任意目录，Linux/macOS）

```bash
crontab -l
sudo crontab -l
grep -rn -e deploy -e shortdrama /etc/cron.d/ /etc/crontab /etc/cron.daily/ 2>/dev/null
systemctl list-timers --all | grep -i -e shortdrama -e deploy
tail -n 50 /opt/shortdrama/logs/auto-update.log
```

**成功判据**：输出里能看到调用旧脚本的行（含 `deploy` 字样），或日志最后几次运行报脚本文件不存在。

**执行位置**：宿主 shell（任意目录，Windows PowerShell）

```powershell
Get-ScheduledTask -TaskName ShortDramaAutoUpdate -ErrorAction SilentlyContinue | Select-Object TaskName,State
Get-ScheduledTask | Where-Object { $_.Actions.Arguments -match 'deploy' -or $_.Actions.Execute -match 'deploy' } |
    Select-Object TaskName,TaskPath,State
```

**成功判据**：与上一条相同 —— 能看出**是否存在**调用旧脚本的定时项：

- Linux：`crontab -l` 里出现含 `deploy` 字样的行（通常还带 `shortdrama-auto-update` 标记）；
  或日志最后几次运行报脚本文件不存在；
- Windows：第二条命令列出任务名（`ShortDramaAutoUpdate`，或你当年手建时用的别的名字）。
- **无输出 = 没有旧任务**，本节的 M3 可以跳过，直接做 M4。

#### 步骤 M2：先备份，保证可回滚

**执行位置**：宿主 shell（任意目录，Linux/macOS）

```bash
crontab -l > "$HOME/crontab.bak.$(date +%F-%H%M)" && ls -l "$HOME"/crontab.bak.*
```

**成功判据**：打印出刚生成的备份文件路径（`crontab.bak.<时间戳>`）。

**执行位置**：宿主 shell（任意目录，Windows PowerShell）

```powershell
Export-ScheduledTask -TaskName ShortDramaAutoUpdate | Out-File "$HOME\ShortDramaAutoUpdate.xml" -Encoding utf8
Test-Path "$HOME\ShortDramaAutoUpdate.xml"
```

**成功判据**：与上一条相同 —— Windows 下 `Test-Path` 输出 `True`（导出的 xml 文件已生成）。

#### 步骤 M3：删除旧任务（只删调用旧脚本的行，别误删别的）

**执行位置**：宿主 shell（任意目录，Linux/macOS）

```bash
crontab -l | grep -v -e 'shortdrama-auto-update' -e 'deploy' | crontab -
crontab -l
```

**成功判据**：第二条命令的输出里**不再有**含 `deploy` 或 `shortdrama-auto-update` 的行，
而你自己其它的 cron 行**都还在**。

**不对时怎么办**：删多了或删错了，从 M2 的备份恢复：

**执行位置**：宿主 shell（任意目录，Linux/macOS）

```bash
crontab "$HOME/crontab.bak.<你备份时的时间戳>"
```

**成功判据**：`crontab -l` 又能看到迁移前的全部内容（含原来那条旧调用行）。

**执行位置**：宿主 shell（任意目录，Windows PowerShell）

```powershell
Unregister-ScheduledTask -TaskName ShortDramaAutoUpdate -Confirm:$false
Get-ScheduledTask -TaskName ShortDramaAutoUpdate -ErrorAction SilentlyContinue
```

**成功判据**：第二条命令**没有任何输出**（任务已不存在）。
若当年是手建的、名字不同，就 `Unregister-ScheduledTask -TaskName '<M1 里查到的名字>' -Confirm:$false`。

#### 步骤 M4：把原生命令写回去

按 **5.2**（Linux/macOS）或 **5.3**（Windows）重新安装定时更新 —— 这次任务里跑的是
`git pull --ff-only && docker compose up -d --build`，不再调用任何脚本。

#### 步骤 M5：验证新任务真的会跑（别等到明天）

**执行位置**：宿主 shell（任意目录，Windows PowerShell）—— 立刻触发一次

```powershell
Start-ScheduledTask -TaskName ShortDramaAutoUpdate
Start-Sleep -Seconds 60
Get-ScheduledTaskInfo -TaskName ShortDramaAutoUpdate | Select-Object LastRunTime,LastTaskResult
Get-Content 'C:\shortdrama\logs\auto-update.log' -Tail 20
```

**成功判据**：`LastTaskResult` 为 `0`，日志末尾出现本次 `docker compose` 的输出。

**Linux 的等价做法**：先手动跑一遍那条 cron 命令（确认命令本身没问题），
再把时间临时改成每分钟（`* * * * *`）验证 cron 环境，**验证完务必改回 `0 4 * * *`**：

**执行位置**：宿主 shell（任意目录，Linux/macOS）

```bash
cd /opt/shortdrama && git pull --ff-only && docker compose up -d --build >> logs/auto-update.log 2>&1; tail -n 20 logs/auto-update.log
```

**成功判据**：命令退出码 0（`echo $?`），日志有本次输出，`docker compose ps` 仍是 `Up ... (healthy)`。

#### 步骤 M6：清理残留

| 残留 | 怎么处理 |
|---|---|
| 旧脚本文件本身 | `git pull --ff-only` 会随仓库的删除操作自动移除它们。如果你当年手工复制过一份到别处（未被 git 跟踪），自己删掉 |
| `logs/auto-update.log` | **保留**。它是历史记录，新任务继续往同一个文件追加 |
| `.env` | **保留**。旧脚本首次运行会生成 `.env` 并写入随机 `JWT_KEY`，这份配置继续有效；升级不会覆盖它。但 `ACCESS_PASSWORD` 与后台 `admin/admin123` 仍建议按 1.3 检查一遍 |
| 指向旧脚本的其它引用 | 见下方代码块，再确认一次没有漏网的 |

**执行位置**：宿主 shell（任意目录）

```bash
grep -rn -e deploy -e shortdrama /etc/cron.d/ /etc/crontab /etc/cron.daily/ /etc/systemd/system/ 2>/dev/null
```

**成功判据**：**没有输出**（没有任何文件还在引用旧脚本）。有输出就按 M3 的同样方式处理。

#### 步骤 M7：旧脚本能力 → 原生命令对照表

> 迁移时按这张表核对：旧脚本提供的每一项能力，都能用原生命令做到。

| 旧脚本能力 | 原生命令 | 执行位置 | 成功判据 |
|---|---|---|---|
| 首次生成配置 + 随机 JWT 密钥 | `cp .env.example .env`，再用 1.3 的命令生成密钥填进 `JWT_KEY` | 宿主 shell（项目目录） | `grep -vE '^\s*#\|^\s*$' .env \| wc -l` 输出 7 |
| 更新（拉码 → 构建 → 切换） | `git pull --ff-only && docker compose up -d --build` | 宿主 shell（项目目录） | `docker compose ps` 为 `Up ... (healthy)` |
| 检查有没有新版本 | `git fetch --quiet && git log --oneline HEAD..@{u}` | 宿主 shell（项目目录） | 有输出=有新版本，无输出=最新 |
| 安装定时自动更新 | 5.2 的 crontab 行 / 5.3 的计划任务 | 宿主 shell（任意目录） | `crontab -l \| grep shortdrama` 有输出 / `LastTaskResult` 为 0 |
| 移除定时自动更新 | `crontab -l \| grep -v -e 'shortdrama-auto-update' -e 'deploy' \| crontab -` / `Unregister-ScheduledTask -TaskName ShortDramaAutoUpdate -Confirm:$false` | 宿主 shell（任意目录） | `crontab -l` 不再有该行 / 任务查询无输出 |
| 改对外端口 | 编辑 `.env` 的 `APP_PORT` 后 `docker compose up -d` | 宿主 shell（项目目录） | `docker compose config \| grep -A3 'ports:'` 显示新端口 |
| 强制不用缓存重建 | `docker compose build --no-cache && docker compose up -d` | 宿主 shell（项目目录） | 构建日志里没有 `CACHED`，容器起来后 healthy |
| 切到 PostgreSQL | `docker compose -f docker-compose.yml -f docker-compose.postgres.yml up -d` | 宿主 shell（项目目录） | `docker compose ps` 里 `app`、`postgres`、`redis` 都在（见 7.4） |
| 停止（保留数据） | `docker compose down` | 宿主 shell（项目目录） | `docker compose ps` 无容器；`docker volume ls \| grep shortdrama` 卷还在 |
| 停止并清空数据 | `docker compose down -v` | 宿主 shell（项目目录） | 容器与 `shortdrama-data` 卷都没了（**不可恢复**） |

#### 步骤 M8：迁移完成自检

> 全部命令的**执行位置**：宿主 shell（项目目录）；Windows 用 `Get-ScheduledTask` / `Get-Content` 的等价命令。
> **成功判据**：6 条全部打勾。

- [ ] `crontab -l`（或 `Get-ScheduledTask -TaskName ShortDramaAutoUpdate`）里**不再有**任何含 `deploy` 的调用；
- [ ] 新的定时项里跑的是 `git pull --ff-only` 与 `docker compose up -d --build` 原生命令；
- [ ] 已经**手动触发过一次**，`LastTaskResult` 为 0 / 日志有输出；
- [ ] `logs/` 目录存在，日志文件在追加而不是空的；
- [ ] `docker compose ps` 仍是 `Up ... (healthy)`；
- [ ] 仓库目录里没有未跟踪的旧脚本副本：`git status --short` 输出里没有以 `deploy` 开头的文件。

### 5.6 定时更新的其它注意事项

> 下表里出现的命令，**执行位置**均为 `宿主 shell（项目目录）`；
> **成功判据**统一是：定时更新下一次触发后 `logs/auto-update.log` 有新输出、`docker compose ps` 为 `Up ... (healthy)`。

| 事项 | 处理 |
|---|---|
| 私有仓库没有 SSH key，报 `Permission denied (publickey)` | 改用 HTTPS 远端地址，或给执行用户配置 SSH key |
| 报 `dubious ownership` | `git config --global --add safe.directory "<仓库绝对路径>"` |
| 部署机上改过代码，`git pull` 失败 | 定时任务只跟随远端。改动请 `git stash`，或写进 `docker-compose.override.yml` |
| 想用面板的「更新容器」代替 | **不要**。用 `docker compose up -d`（见 1.6） |
| 磁盘越来越大 | 旧镜像堆积：`docker image prune -a`（会删掉所有未被使用的镜像，正在用的不受影响） |
| 日志文件无限增长 | 容器日志已限制为 10MB × 3（`docker-compose.yml:81-85`）；`logs/auto-update.log` 需要自己轮转，例如在 crontab 里加一行每月清空 |

---

## 六、种子服务（可选）

> 「种子」页的搜索与边下边播由**另一个项目 `torrent-search`** 提供，它是**宿主机上的独立进程**
> （默认监听 `127.0.0.1:8787`）。本平台的容器只做转发（`docs/DOCKER.md:382-391`）。
> 不用这个功能可以完全跳过本章，不影响其它任何功能。

### 6.1 核心坑：容器里的 `127.0.0.1` 不是宿主机

容器里的 `127.0.0.1` 指**容器自己**。种子服务跑在宿主机上，所以必须用 `host.docker.internal`。
compose 已经处理好（`docker-compose.yml:58` 传 `Torrent__BaseUrl`，`:67-68` 加 `extra_hosts: host.docker.internal:host-gateway`
让 Linux 也能解析；Docker Desktop 原生支持）。**不要**把它改成 `127.0.0.1`，否则「种子」页会一直显示
「连不上本地种子服务」，而服务其实好好地在宿主机上跑着。

### 6.2 在宿主机上把种子服务跑起来

**执行位置**：宿主 shell（任意目录）

```bash
cd /path/to/torrent-search
node bin/magnet-search.mjs serve        # 默认监听 127.0.0.1:8787
```

**成功判据**：终端打印监听地址（`127.0.0.1:8787`）且不退出。

### 6.3 确认 `.env` 里的两个变量

**执行位置**：宿主 shell（项目目录），用编辑器打开 `.env`（下面两行是**文件内容**，不是命令）：
```
TORRENT_ENABLED=true
TORRENT_BASE_URL=http://host.docker.internal:8787
```

改完必须重建容器才生效：

**执行位置**：宿主 shell（项目目录）

```bash
docker compose up -d
```

**成功判据**：`docker compose ps` 显示 `app` 被 `Recreated`/`Started`，且随后 healthy。

### 6.4 验证容器能不能连到宿主机的种子服务

**执行位置**：宿主 shell（项目目录；命令在容器内执行）

```bash
docker compose exec app curl -fsS http://host.docker.internal:8787/api/health
```

**成功判据**：返回种子服务的健康检查响应（JSON，HTTP 200）。返回非 0 退出码或连接被拒 = 没通。

**不对时怎么办**：

- `Could not resolve host: host.docker.internal` → compose 的 `extra_hosts` 没生效，
  确认 `docker-compose.yml:67-68` 没被改动，并 `docker compose up -d` 重建；
- `Connection refused` → 宿主上的种子服务没在跑，或它监听的不是 8787；
- 容器内通、网页里仍提示「种子服务没在运行」→ 打开浏览器开发者工具看 `/api/...` 的实际请求地址，
  确认 `.env` 的改动已通过 `docker compose up -d` 生效。

### 6.5 种子服务也跑在 Docker 里？

如果它与本服务在**同一个 compose 网络**，把地址换成它的服务名即可
（**执行位置**：宿主 shell（项目目录），编辑 `.env`；下面一行是**文件内容**，不是命令）：

```
TORRENT_BASE_URL=http://torrent-search:8787
```

**执行位置**：宿主 shell（项目目录）

```bash
docker compose up -d && docker compose exec app curl -fsS http://torrent-search:8787/api/health
```

**成功判据**：返回健康检查响应。

### 6.6 不用种子功能

**执行位置**：宿主 shell（项目目录）

```bash
sed -i 's/^TORRENT_ENABLED=.*/TORRENT_ENABLED=false/' .env && docker compose up -d
```

**成功判据**：`grep '^TORRENT_ENABLED' .env` 输出 `TORRENT_ENABLED=false`；站点导航里的「种子」入口显示未启用。

> 也可以什么都不做 —— 连不上时页面只会提示「种子服务没在运行」，不影响其它功能。

---

## 七、备份恢复与故障排查

### 7.1 数据卷与「卷名前缀」（备份前必须看懂）

| 卷逻辑名 | 内容 | 何时存在 | 证据 |
|---|---|---|---|
| `shortdrama-data` | SQLite 库文件、运行时数据 | 默认模式 | `docker-compose.yml:72`、`:87-89` |
| `shortdrama-pgdata` | PostgreSQL 数据目录 | 用了 PostgreSQL 覆盖配置 | `docker-compose.postgres.yml:30`、`:54` |
| `shortdrama-redisdata` | Redis AOF | 用了 PostgreSQL 覆盖配置 | `docker-compose.postgres.yml:46`、`:56` |

> ⚠️ **Compose 会给卷名加项目前缀**：实际名字形如 `shortdrama_shortdrama-data`（`docs/DOCKER.md:629`）。
> 写裸名 `-v shortdrama-data:/data` 会**新建一个空卷** —— 你以为在改旧卷，其实什么都没改
> （备份时更危险：会备份出一个空文件）。先查实际名字：

**执行位置**：宿主 shell（任意目录）

```bash
docker volume ls | grep shortdrama
```

**成功判据**：列出 `<项目名>_shortdrama-data` 这类带前缀的名字（默认 `shortdrama_shortdrama-data`）。

### 7.2 备份 SQLite（默认模式）

先停应用再拷贝，避免拿到写入一半的库文件：

**执行位置**：宿主 shell（项目目录）

```bash
mkdir -p backup
docker compose stop app
docker cp "$(docker compose ps -aq app):/data/shortdrama.db" "./backup/shortdrama-$(date +%F).db"
docker compose start app
ls -l backup/
```

**成功判据**：`backup/` 下生成了 `shortdrama-<日期>.db`，**文件大小明显大于 0**；`docker compose ps` 回到 `Up ... (healthy)`。

**不对时怎么办**：报 `No such container` → 应用没在跑，先 `docker compose up -d`；
报找不到 `/data/shortdrama.db` → 你的库不在默认路径，用
`docker compose exec app ls -l /data` 确认实际文件名。

> 这里刻意用 `docker compose ps -aq app` 取容器 id，而不是写死容器名 —— compose 没设 `container_name`，
> 写死名字在换目录、改项目名之后就会失效。

### 7.3 恢复 SQLite

**执行位置**：宿主 shell（项目目录）

```bash
docker compose stop app
docker cp ./backup/shortdrama-2026-10-09.db "$(docker compose ps -aq app):/data/shortdrama.db"
docker compose start app
docker compose logs --tail=30 app
```

**成功判据**：`docker compose ps` 为 `Up ... (healthy)`；站点能打开；日志里没有
`SQLite Error 14: unable to open database file`。

**不对时怎么办**：报权限错误 → 入口脚本会在启动时修正 `/data` 属主，先看日志里有没有
`警告：/data 属主修正失败`；仍不行按 7.6 的「升级后写不进数据卷」处理。

### 7.4 PostgreSQL 模式（可选）

启用（会额外拉起 `postgres` 与 `redis` 两个容器）：

**执行位置**：宿主 shell（项目目录）

```bash
docker compose -f docker-compose.yml -f docker-compose.postgres.yml up -d
docker compose -f docker-compose.yml -f docker-compose.postgres.yml ps
```

**成功判据**：`ps` 里 `app`、`postgres`、`redis` 三个服务都是 `Up`（`postgres`/`redis` 应为 healthy）。

> ⚠️ **用了 `-f` 之后，以后所有 compose 命令都必须带上同样两个 `-f`**，否则 compose 会按默认的
> SQLite 配置重建 `app` 容器（配置漂移，表现是「数据库突然空了」）。建议在 `.env` 里固化：
> `COMPOSE_FILE=docker-compose.yml:docker-compose.postgres.yml`（Windows 用 `;` 分隔），
> 之后直接 `docker compose ps` 即可。改完用 `docker compose config | grep -A2 'Database__Provider'`
> 确认生效值是 `PostgreSql`。

备份：

**执行位置**：宿主 shell（项目目录）

```bash
mkdir -p backup
docker compose -f docker-compose.yml -f docker-compose.postgres.yml \
  exec -T postgres pg_dump -U shortdrama -d shortdrama > "backup/shortdrama-$(date +%F).sql"
ls -l backup/
```

**成功判据**：`backup/shortdrama-<日期>.sql` 生成且大小明显大于 0。

恢复：

**执行位置**：宿主 shell（项目目录）

```bash
docker compose -f docker-compose.yml -f docker-compose.postgres.yml \
  exec -T postgres psql -U shortdrama -d shortdrama < backup/shortdrama-2026-10-09.sql
```

**成功判据**：命令无报错，退出码 0；`docker compose ... ps` 仍全部 `Up`。

> **切换数据库不会自动迁移数据**：SQLite 里的旧数据需要另行导出导入（`docs/DOCKER.md:462`）。
> PostgreSQL 默认**不对外暴露端口**（`docker-compose.postgres.yml:37-39`），需要直连调试时取消 `ports` 的注释。

### 7.5 卸载

**执行位置**：宿主 shell（项目目录）

```bash
docker compose down             # 停止并移除容器，保留数据卷
docker compose down -v          # 同时删除数据卷（不可恢复！）
docker rmi shortdrama:latest    # 删除本地构建的镜像
```

**成功判据**：`docker compose ps` 无容器；`docker volume ls | grep shortdrama` 在 `down` 后仍有卷，
在 `down -v` 后为空。

彻底清理残留卷：

**执行位置**：宿主 shell（任意目录）

```bash
docker volume ls | grep shortdrama
docker volume rm <上面列出的卷名>
```

**成功判据**：`docker volume ls | grep shortdrama` 无输出。

**不对时怎么办**：卷删不掉、报 `volume is in use` → 还有容器在用它，
先 `docker compose -f docker-compose.yml -f docker-compose.postgres.yml down`（PostgreSQL 模式）
或 `docker compose down`，再删。

### 7.6 故障速查表

> 下表里出现的命令，**执行位置**均为 `宿主 shell（项目目录）`（除非行内明确写了「容器内」或「面板里」）；
> **成功判据**统一是：该现象消失，且 `docker compose ps` 显示 `Up ... (healthy)`。

| 现象 | 原因与处理 |
|---|---|
| 打开站点一直跳 `/gate` | 这是口令门，输入 `.env` 里的 `ACCESS_PASSWORD`。忘了口令就改 `.env` 再 `docker compose up -d` |
| 改了 `.env` 但没生效 | 环境变量在**创建容器时**注入，必须 `docker compose up -d` 重建容器 |
| 健康检查一直 `starting` | 首次启动要同步数据源，等 1-2 分钟；仍不行看 `docker compose logs -f app` |
| 日志报 `SQLite Error 14: unable to open database file` | 数据卷属主不对（老卷是 root 属主）。入口脚本本应自动修好；仍出现就手动修（见下方「升级后写不进数据卷」） |
| 日志报 `EACCES` / `permission denied` | 同上（数据卷属主不对）。**特例**：如果你按 `Dockerfile:133-135` 的说明在 compose 里设了 `user: "app"`（容器直接以 `app` 身份启动，入口脚本的降权分支被跳过），那么启动时**不会**再有那次 `chown`，必须先把卷属主改成 `app` —— 见下方「升级后写不进数据卷」 |
| 容器起来就退出，日志报 `exec format error` | 镜像架构与本机不符（ARM 机器拉了 amd64）。改工作流 `PLATFORMS` 后重新构建（见 3.4） |
| `Conflict. The container name "/xxx" is already in use` | 那个名字**不一定属于你**：`docker inspect xxx --format '{{index .Config.Labels "com.docker.compose.project"}}'` 看归属；是别人的就 `docker rename xxx xxx-old` 解封，**别删** |
| 面板「更新容器」报 `user specified IP address is supported only when connecting to networks with user configured subnets` | 面板把动态 IP 固化成静态 IP。改用 `docker compose down && docker compose up -d`；或在面板里把静态 IP 留空 |
| 定时更新没跑 | 先确认 `logs/` 目录存在（5.1）；再看 `logs/auto-update.log`；`crontab -l \| grep shortdrama`（Linux）或 `Get-ScheduledTask -TaskName ShortDramaAutoUpdate`（Windows）确认任务在 |
| 定时更新一直失败 | 多半是 cron 的 `PATH` 里没有 `docker`/`git`（5.2），或工作区有未提交改动 |
| 定时任务里跑的脚本找不到了 | 老部署机的历史遗留，按 5.5 迁移 |
| `镜像拉取失败` / `unauthorized`（镜像模式） | 包是私有的但没登录：`docker login ghcr.io`；或把包设为公开 |
| 端口被占用 | `APP_PORT=18081 docker compose up -d` 换个端口；或用 `ss -lntp \| grep 18080` 找出占用者 |
| 磁盘占用越来越大 | `docker image prune -a`（删掉未被使用的镜像）；容器日志已限制为 10MB × 3 |
| 后端接口 401 且 `code=4010` | 访问口令门拦截。正常行为：先在页面输入口令 |

**升级后写不进数据卷（老卷属主是 root）** —— 手动修一次：

**执行位置**：宿主 shell（任意目录）

```bash
docker volume ls | grep shortdrama-data
docker run --rm --entrypoint sh mcr.microsoft.com/dotnet/aspnet:10.0 -c 'id app'
docker run --rm -v <上一步查到的实际卷名>:/data alpine chown -R <上一步查到的UID>:<上一步查到的UID> /data
```

**成功判据**：最后一条命令无输出且退出码 0；随后 `docker compose up -d` 后站点能正常读写数据。

> 正常升级**用不到**这一步 —— 入口脚本启动时会自己修。这里只是兜底。

### 7.7 「端口打不开」：按从内到外的顺序查，别一上来就换端口

**执行位置**：宿主 shell（项目目录）

```bash
docker compose ps                                    # ① 容器在跑吗
ss -lntp | grep 18080                                # ② 宿主在监听吗（注意绑定地址）
curl -fsS http://127.0.0.1:18080/health              # ③ 本机通吗
```

**成功判据**：三条命令依次给出 `Up ... (healthy)`、`0.0.0.0:18080` 的 LISTEN、`{"status":"healthy",...}`。

| 卡在哪一步 | 原因 | 处理 |
|---|---|---|
| ① 容器没在跑 | 构建失败或启动报错 | `docker compose logs --tail=50 app` |
| ② 没有 LISTEN | 端口映射写错，或容器内应用没起来 | 确认 compose 里是 `"<对外端口>:8080"`（容器内是 8080，别写反），见第四章 |
| ② 显示 `127.0.0.1:18080` 而不是 `0.0.0.0:18080` | 映射里加了本机前缀 | 见 4.6（配反代时才这么做；否则外网访问不到） |
| ③ 本机 curl 不通 | 应用启动失败或还在初始化 | 等 1-2 分钟；仍不行看日志 |
| ③ 通、④ 外网不通 | **云安全组 / 宿主防火墙没放行** | 两处都要放：控制台安全组 + `ufw` / `firewall-cmd`，见 2.7 |

### 7.8 日常运维命令速查

| 目的 | 命令 | 执行位置 | 成功判据 |
|---|---|---|---|
| 看容器状态 | `docker compose ps` | 宿主 shell（项目目录） | 列出 `app`，`STATUS` 为 `Up ... (healthy)` |
| 跟实时日志 | `docker compose logs -f app` | 宿主 shell（项目目录） | 持续输出日志行 |
| 看最近 200 行 | `docker compose logs --tail 200 app` | 宿主 shell（项目目录） | 输出日志，无 `EACCES` / `SQLite Error` |
| 重启应用 | `docker compose restart app` | 宿主 shell（项目目录） | 随后 `ps` 回到 healthy |
| 停止应用 | `docker compose stop app` | 宿主 shell（项目目录） | `docker compose ps` 不再列出该容器（卷保留） |
| 应用 `.env` 改动 | `docker compose up -d` | 宿主 shell（项目目录） | 输出 `Recreated`/`Started` |
| 进容器 | `docker compose exec app sh` | 宿主 shell（项目目录） | 进入容器 shell，提示符变化 |
| 看资源占用 | `docker stats $(docker compose ps -q app)` | 宿主 shell（项目目录） | 实时打印 CPU / 内存 |
| 看健康状态字段 | `docker inspect --format '{{.State.Health.Status}}' "$(docker compose ps -q app)"` | 宿主 shell（项目目录） | 输出 `healthy` |

---

### 7.9 本手册覆盖不到的地方（去哪看）

- **反向代理与 HTTPS 的完整配置**（Caddy / Nginx 样例、证书申请）：见 `docs/DOCKER.md` 第七节。
  一句话提醒：**口令门依赖 Cookie，对外务必用 HTTPS**（HTTPS 下 Cookie 会自动加 `Secure`）。
- **预构建镜像模式的完整说明**（私有包、ARM 构建、工作流）：见 `docs/DOCKER.md` 第三节第 7 小节与
  `.github/workflows/docker-publish.yml`。
- **采集源、播放、去广告等业务功能**：见 `README.md`。
- 本手册的每一条事实都可在 `DEPLOY-FACTS.md` 中按 `文件:行` 逐条核对；
  **唯一例外**是 1.3 末尾指出的「`.env.example:29` 与 `:30` 两条生成命令长度不一致（48 vs 96 个字符）」这一点，
  该文件目前尚未收录（原因与建议见 1.3 末尾），其余条目均可对上。
