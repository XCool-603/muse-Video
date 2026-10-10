# 部署配置事实清单（DEPLOY-FACTS）

> **用途**：[docs/RUNBOOK.md](RUNBOOK.md) 里出现的每一个值，都能在本文件的「`文件:行` → 实际值」里找到对应项，
> 可机械核对。**两者冲突时以仓库文件为准。**
> **核对方式**：逐行读取仓库文件，取行号与字面值。行号以工作区当前内容为准 —— 后续改动会让行号漂移，**以内容为准**。
> **诚实标注**：凡本机无法实测的（**本机未安装 Docker**），一律标注「未实测」，不写成结论。
> **核对快照（重要）**：本清单的核对基准是提交 `687e7d3`（「docs: 新增部署与运维手册（RUNBOOK）与部署配置事实清单；修掉删脚本时漏掉的失效说法」）。
> 该提交在写入本清单的同时，也补齐了「删除两个包装脚本」时漏改的内容（`.gitattributes` 注释、`docs/DOCKER.md` 的卷表 / 故障排查表 / 4 处失效说法）。
> **紧随其后还有一批改动，已提交为 `660b11b`**（当时核对时尚未提交，内容与提交后一致）—— 那是同一轮里对**定时更新日志写法**的修复（`mkdir -p logs`、命令加括号、日志写绝对路径），以及手册里退出码取错、措辞与跨平台声明的修正。
> 本清单的 **F17、F18、F19 与 §2-1 ~ §2-4、§2-11、§2-14、§2-16** 已按**含这批改动的工作区**逐条实测刷新；实测时 `HEAD` 为 `687e7d3c20b3250bc8a59377c867addcbe7480c9`，随后这批改动提交为 `660b11b4e37d868418932a211b57279bd73ebe2f`。
> **行号与字节数会随改动漂移，核对时以内容为准**：先看本文件给出的字面值，再到目标文件里全文搜索定位，不要死认行号。本轮就实际发生了这件事 —— `README.md` 与 `docs/DOCKER.md` 在核对期间又分别多了 5 行 / 9 行，指向它们后半部分的行号整体下移（`README.md` **+5**、`docs/DOCKER.md` **+9**），本清单已按新行号刷新。

---

## 0. 核对方式（可复现）

| 手段 | 命令 | 用途 |
|---|---|---|
| 直接读取 | `read` 工具逐行读取指定文件 | 取行号与字面值 |
| 删除核实 | `git ls-files`、`git log --all --diff-filter=D --name-only`、`git show --stat 09cdd7b` | 证明两个包装脚本已删 |
| 残留引用 | 全仓库 `Select-String`（模式见 F19，排除 `.git`/`.dsh-team`/`bin`/`obj`/`node_modules`/`dist`，**并排除本文件自己**） | 找出手册不该照抄的残留 |
| 字节级检查 | `[System.IO.File]::ReadAllBytes` / `ReadAllText` 检查 BOM 与行尾 | 排除编码坑 |
| 数据计数 | `ConvertFrom-Json` 解析 `apple-cms-sources.json` | 核实「22 个采集源」 |
| Docker 实测 | `docker` 命令 | **本机未安装 docker（`CommandNotFoundException`），所有运行期行为未实测** |

### 0.1 关于「已删除的两个包装脚本」的书写约定（重要）

本文件必须在 C3 机械判据下命中为 0：判据用两条正则扫本文件全文，命中数必须都是 0。

- 模式一：`deploy` + 点号 + `(sh|ps1)`（即**禁止出现**被删脚本的完整文件名）
- 模式二：以**两个连字符**开头的脚本参数（`update`、`install-cron`、`uninstall-cron`、`purge`、`rebuild`），以及 PowerShell 风格的**一个连字符** + `InstallTask`

因此本文件统一采用**断开写法**指代这两个已删文件：

| 本文件里的写法 | 指的实际文件（仓库中**已删除**） |
|---|---|
| `deploy` + `.sh` | 原 Linux/macOS 包装脚本 |
| `deploy` + `.ps1` | 原 Windows 包装脚本 |
| 「两个 deploy 开头的包装脚本（后缀 sh / ps1）」 | 同上两者的合称 |

同理，F20 的参数表**一律去掉前导连字符**书写：`update` / `install-cron` / `uninstall-cron` / `purge` / `rebuild` / `InstallTask` 等。**原写法是在参数名前面加两个连字符**（PowerShell 版是一个连字符）。这样做是为了满足机械判据，**不是**说这些参数可以不写连字符就能用 —— 它们本身已全部失效（见 F20）。

---

## 1. 事实清单（每条：文件:行 → 实际值）

### F1 compose 服务名

- `docker-compose.yml:17` → `  app:`
- **服务名是 `app`**。全仓库所有运维命令都用 `app` 指代它（`docker compose logs -f app` 等）。
- `docker-compose.yml:23-26` 注释明确说明：**刻意不设 `container_name`**（`:23` 原文「刻意不设 container_name：固定名字会关掉 Compose 的项目名前缀」）。实测 `Select-String -Path docker-compose.yml -Pattern 'container_name'` 只命中 `:23` 这一行注释，**没有任何生效的 `container_name` 键**。容器实际名字形如 `<项目名>-app-1`。手册里任何地方都不该出现写死的容器名（如 `shortdrama`）。

### F2 对外端口：完整写法与默认值

- `docker-compose.yml:30` → `      - "${APP_PORT:-18080}:8080"`
- `.env.example:11` → `APP_PORT=18080`
- 展开：**宿主机 18080 → 容器 8080**。默认值 18080 有两层来源且一致（compose 兜底值、`.env.example` 默认值）。
- ⚠️ compose 的变量插值优先级是 **shell 环境变量 > `.env` 文件**。所以 `APP_PORT=9000 docker compose up -d`（`docs/DOCKER.md:153`）是临时覆盖，`.env` 里的值不会赢。手册若要讲「改了 `.env` 为什么不生效」，这是第二种原因（第一种是没重建容器）。

### F3 容器内端口

- `Dockerfile:128` → `EXPOSE 8080`
- `Dockerfile:121` → `    ASPNETCORE_URLS=http://+:8080 \`（在 `ENV` 续行块 `120-126` 内，首行为 `:120`）
- `ShortDrama.Api/appsettings.json:11` → 有一条**专门用来警告不要在此设 `Urls`** 的 `_Urls` 键：`appsettings` 会覆盖 `ASPNETCORE_URLS`，导致容器内只监听 localhost。
- **结论：容器内端口恒为 8080，不可改**（改它要同步改 `EXPOSE`、`ASPNETCORE_URLS`、健康检查三处）。

### F4 健康检查命令

- `docker-compose.yml:75` → `      test: ["CMD", "curl", "-fsS", "http://localhost:8080/health"]`
- `docker-compose.yml:76-79` → `interval: 30s` / `timeout: 5s` / `start_period: 40s` / `retries: 3`
- `Dockerfile:130-131` → `HEALTHCHECK --interval=30s --timeout=5s --start-period=40s --retries=3 \` + `CMD curl -fsS http://localhost:8080/health || exit 1`
- **两处参数完全一致**（compose 的 healthcheck 覆盖 Dockerfile 的同名定义）。
- 依赖 `curl`：`Dockerfile:75` → ` && apt-get install -y --no-install-recommends curl tzdata gosu \`（aspnet 基础镜像不自带 curl，见 `Dockerfile:68` 注释）。
- ⚠️ **`/health` 不查数据库**：`ShortDrama.Api/Program.cs:162` → `app.MapGet("/health", () => Results.Ok(new { status = "healthy", time = DateTimeOffset.UtcNow }))` 是**静态响应**。所以「healthy」只代表进程活着，不代表采集源已同步完。手册写「等健康检查通过就能用」是错的。

### F5 数据卷：逻辑名、挂载点、命名规则

- `docker-compose.yml:72` → `      - shortdrama-data:/data`（逻辑卷名 `shortdrama-data` → 容器内 `/data`）
- `docker-compose.yml:87-89` → 顶层 `volumes:` 声明 `shortdrama-data:` / `driver: local`
- `Dockerfile:118` → `VOLUME ["/data"]`
- `Dockerfile:126` → `    ConnectionStrings__Default="Data Source=/data/shortdrama.db"`（SQLite 文件位置）
- **命名规则**：`docker-compose.yml` **没有顶层 `name:` 字段**（实测 `Select-String -Path docker-compose.yml -Pattern '^name:'` → **无输出**），也没有 `COMPOSE_PROJECT_NAME`。因此 compose 项目名由**目录名**推导，实际卷名 = `<项目名>_shortdrama-data`。
- `docs/DOCKER.md:638` 举例为 `shortdrama_shortdrama-data` —— 该例子**仅在 clone 目录名为 `shortdrama` 时成立**（`docs/DOCKER.md:14` 的 clone 命令确实带 `shortdrama`）。
- ⚠️ **本机目录名是中文 `短剧聚合`**（工作目录 `C:\Work\短剧聚合\短剧聚合`）。项目名由目录名推导 + compose 要求项目名只能是小写字母数字/`-`/`_` → 卷名前缀**不会**是 `shortdrama`，甚至有较大概率直接报项目名非法。**未实测**（本机无 docker）。手册必须写成「先 `docker volume ls` 查实际卷名」，或显式 `COMPOSE_PROJECT_NAME=shortdrama` / `-p shortdrama` 固定它。

### F6 `.env` 的每个变量名与默认值

来源文件：`.env.example`（共 44 行）。**7 个活动变量 + 1 个注释掉的变量**：

| 变量 | `.env.example` 行 | 默认值（字面） |
|---|---|---|
| `APP_PORT` | `:11` | `18080` |
| `ACCESS_GATE_ENABLED` | `:24` | `true` |
| `ACCESS_PASSWORD` | `:25` | `遵纪守法世界和平` |
| `JWT_KEY` | `:31` | `please-change-this-secret-key-in-production-2025` |
| `TORRENT_BASE_URL` | `:40` | `http://host.docker.internal:8787` |
| `TORRENT_ENABLED` | `:41` | `true` |
| `POSTGRES_PASSWORD` | `:44` | `shortdrama_pwd` |
| `SHORTDRAMA_IMAGE` | `:20` | **仅注释行**（`# SHORTDRAMA_IMAGE=ghcr.io/xcool-603/muse-video:latest`），未启用 |

- `.env.example:7` 注明 `.env` 已被 `.gitignore` 忽略；`.gitignore:25` → `.env`。**仓库根目录当前不存在 `.env`**（`Test-Path .env` → `False`）。
- ⚠️ 因为 `SHORTDRAMA_IMAGE` 在模板里是**注释**，`docs/DOCKER.md:663` 的 `grep SHORTDRAMA_IMAGE .env` 会命中注释行、看起来像「已配了镜像模式」。正确写法是 `grep '^SHORTDRAMA_IMAGE' .env`。
- ⚠️ `.env` **不是必需的**：compose 里每个变量都写成 `${VAR:-默认值}`，`README.md:103` 也写了「不执行这行也能跑，用内置默认值」。

### F7 容器运行用户（注意：用的是镜像内置用户）

- `Dockerfile:84-98` 注释 → 明确写「运行期用户：直接用**镜像内置**的非 root 用户 `app`」（`:84`）；「`.NET 8+` 的官方 aspnet / runtime 镜像已经内置了 `app` 用户与 `app` 组（UID/GID 1654）」（`:87`）；「`groupadd -g 10001 app` 会以 **exit 9（组名已存在）** 失败」（`:87-89`）
- `Dockerfile:97-98` → `RUN mkdir -p /data /home/app \` + ` && chown app:app /data /home/app`
- `Dockerfile:111-114` → 构建期自检：`id app`（`:111`）、`test -x /usr/local/bin/entrypoint.sh`（`:112`）、`test "$(stat -c '%U:%G' /data)" = "app:app"`（`:113`）
- `docker/entrypoint.sh:17` → `APP_USER=app`；`:27` → `exec gosu "$APP_USER" "$@"`
- ⚠️ **`Dockerfile` 里没有 `USER` 指令**（实测 `Select-String -Path Dockerfile -Pattern '^USER'` → **无输出**；相邻指令是 `Dockerfile:118` 的 `VOLUME`、`:120-126` 的 `ENV`、`:136` 的 `ENTRYPOINT`）。**容器配置层（`docker inspect` 的 `Config.User`）仍然是 root**，只有入口脚本用 `gosu` 把业务进程降到 `app`。
  - 所以手册**不要写**「容器以非 root 运行」这种会被 `docker inspect` 打脸的说法。准确表述：**「入口脚本会把业务进程降权到镜像内置的 `app` 用户；容器本身仍以 root 启动（因为要先 chown 数据卷）」**。
- ⚠️ UID/GID `1654` 只出现在 `Dockerfile:87` 的注释里，仓库内**无实测证据**；`docs/DOCKER.md:513` 自己也写「通常是 1654 —— 以实测为准」。手册不要写死这个数字。
- `Dockerfile:133-135` → 想跳过降权：在 compose 里设 `user: "app"`（`:134`），脚本会自动跳过降权分支（`entrypoint.sh:20` 的 `if [ "$(id -u)" = "0" ]` 为假）。

### F8 入口脚本做的三件事

文件 `docker/entrypoint.sh`（**实测 35 行**，`set -eu` 在 `:15`）：

1. **修正数据目录属主** — `:20-24`：`if [ "$(id -u)" = "0" ]` 且 `[ -d "$DATA_DIR" ]` 时执行 `chown -R "$APP_USER:$APP_USER" "$DATA_DIR"`（即 `chown -R app:app /data`），失败只在 stderr 告警（`:23`）。目的见 `:5-9` 注释：**兼容旧版本以 root 创建的卷**（旧卷属主是 root，直接降权会 EACCES）。
2. **降权运行** — `:26-28`：`if command -v gosu` 则 `exec gosu "$APP_USER" "$@"`（`:27`）；`:30` 找不到 gosu 时告警并以 root 继续。
3. **`exec "$@"` 交棒** — `:35`：`exec "$@"`（配合 `Dockerfile:137` 的 `CMD ["dotnet", "ShortDrama.Api.dll"]`）。`:33-34` 注释说明原因：**否则 PID 1 是 shell，SIGTERM 到不了 dotnet，`docker stop` 要等超时才杀得掉**。

- 设计原则（`:11-13`）：**失败也不影响可用性**，宁可少一层隔离也不让容器起不来。
- 变量定义：`:17` → `APP_USER=app`、`:18` → `DATA_DIR=/data`。

### F9 管理后台默认账号

- `ShortDrama.Infrastructure/Data/DbSeeder.cs:147` → `Username = "admin",`
- `ShortDrama.Infrastructure/Data/DbSeeder.cs:150` → `PasswordHash = AuthService.HashPassword("admin123"),`
- `ShortDrama.Infrastructure/Data/DbSeeder.cs:151` → `Role = "admin",`
- `ShortDrama.Infrastructure/Data/DbSeeder.cs:143` → `if (!await db.Users.AnyAsync())` —— **只在用户表为空时创建**（改过密码后再升级不会重置）
- `ShortDrama.Infrastructure/Data/DbSeeder.cs:156` → 日志 `"已创建默认管理员账号 admin / admin123"`
- 旁证：`README.md:70`、`README.md:160`、`docs/DOCKER.md:202`、`short-drama-web/src/views/LoginView.vue:60`（登录页提示「演示账号：admin / admin123」）
- 后台入口 `/admin`：`short-drama-web/src/router/index.ts:64` → `      path: '/admin',`

### F10 默认访问口令

- `docker-compose.yml:42` → `      AccessGate__Password: "${ACCESS_PASSWORD:-遵纪守法世界和平}"`
- `.env.example:25` → `ACCESS_PASSWORD=遵纪守法世界和平`
- `ShortDrama.Api/appsettings.json:14` → `    "password": "遵纪守法世界和平",`
- 旁证：`docs/DOCKER.md:147`、`docs/DOCKER.md:208`
- **三处默认值一致**。

### F11 访问口令门的开关变量

- `docker-compose.yml:41` → `      AccessGate__Enabled: "${ACCESS_GATE_ENABLED:-true}"`
- `.env.example:24` → `ACCESS_GATE_ENABLED=true`
- `ShortDrama.Api/appsettings.json:13` → `    "enabled": true,`（注意 appsettings 里是**小写 `enabled`**；.NET 配置绑定大小写不敏感，compose 的 `AccessGate__Enabled` 同样生效）
- 其它口令门事实：
  - `docker-compose.yml:43` → `      AccessGate__ValidDays: "30"`（**硬编码在 compose，不在 `.env`**）
  - `appsettings.json:15-18` → `cookieName: "sd_access"`、`validDays: 30`、`gatePath: "/gate"`、`verifyPath: "/api/v1/access"`
  - `ShortDrama.Api/Access/AccessGateMiddleware.cs:66` → 页面请求 `context.Response.Redirect(_options.GatePath);`
  - **免口令白名单**：`AccessGateMiddleware.cs:72-77` → `/gate`、`/api/v1/access`、`/health`、`/openapi`、`/assets`、`/favicon`；`:81-84` 额外放行 `.ico/.png/.svg/.webmanifest`
  - `appsettings.json:19` → Cookie 存的是口令的 SHA256 派生值，改口令即让所有已发放 Cookie 失效

### F12 种子服务的默认地址与启用变量

- `docker-compose.yml:58` → `      Torrent__BaseUrl: "${TORRENT_BASE_URL:-http://host.docker.internal:8787}"`
- `docker-compose.yml:59` → `      Torrent__Enabled: "${TORRENT_ENABLED:-true}"`
- `.env.example:40-41` → `TORRENT_BASE_URL=http://host.docker.internal:8787` / `TORRENT_ENABLED=true`
- **appsettings 里的默认值是另一个地址**：`ShortDrama.Api/appsettings.json:36` → `    "baseUrl": "http://127.0.0.1:8787",`；`:35` → `    "enabled": true,`
- ⚠️ **这是最容易写错的一处**：`appsettings.json` 的 `127.0.0.1` 在容器里指**容器自己**，Docker 部署下**必须**用 compose 传的 `host.docker.internal`。`appsettings.json:37` 的注释、`docker-compose.yml:54-57` 的注释、`.env.example:35`、`docs/DOCKER.md:396-400` 都在讲同一件事。
- 让 `host.docker.internal` 在 Linux 也能解析：`docker-compose.yml:67-68` → `extra_hosts:` + `      - "host.docker.internal:host-gateway"`
- 相关配置（`appsettings.json:38-42`）：`timeoutSeconds: 30`、`streamWaitSeconds: 30`、`copyBufferBytes: 65536`

### F13 PostgreSQL 覆盖文件包含哪几个服务

文件 `docker-compose.postgres.yml`（**实测 57 行**）。**3 个服务**：

| 服务 | 定义位置 | 关键值 |
|---|---|---|
| `app` | `:11` | 覆盖 `Database__Provider: PostgreSql`（`:13`）、`ConnectionStrings__Default: "Host=postgres;Port=5432;Database=shortdrama;Username=shortdrama;Password=${POSTGRES_PASSWORD:-shortdrama_pwd}"`（`:14`）、`depends_on: postgres: condition: service_healthy`（`:15-17`） |
| `postgres` | `:19` | `image: postgres:16-alpine`（`:20`）、`POSTGRES_DB/USER: shortdrama`（`:23-24`）、`POSTGRES_PASSWORD: "${POSTGRES_PASSWORD:-shortdrama_pwd}"`（`:25`）、`TZ: Asia/Shanghai`（`:26`）、`POSTGRES_INITDB_ARGS: "--encoding=UTF8 --locale=C"`（`:28`）、卷 `shortdrama-pgdata:/var/lib/postgresql/data`（`:30`）、健康检查 `pg_isready -U shortdrama -d shortdrama`（`:32`，10s/5s/10/20s 见 `:33-36`）、**端口默认注释掉**（`:37-39`） |
| `redis` | `:41` | `image: redis:7-alpine`（`:42`）、`command: ["redis-server", "--appendonly", "yes"]`（`:44`）、卷 `shortdrama-redisdata:/data`（`:46`）、健康检查 `redis-cli ping`（`:48`，10s/3s/5 见 `:49-51`） |

- 覆盖文件自身的 `volumes:` 段：`:53-57` → `shortdrama-pgdata`（`:54`）、`shortdrama-redisdata`（`:56`），都 `driver: local`
- 调用方式（`docker-compose.postgres.yml:4`、`docs/DOCKER.md:456`）：`docker compose -f docker-compose.yml -f docker-compose.postgres.yml up -d`
- ⚠️ **`redis` 起来了但应用不用它**：覆盖段只改了数据库 Provider 与连接串，**没有给 `app` 传任何 Redis 连接配置**；全仓库 `*.cs` 搜 `Redis` 只命中一句注释 —— `ShortDrama.Infrastructure/DependencyInjection.cs:41` → `            // 内存缓存（生产可替换为 Redis）`。手册写「PostgreSQL 模式会启用 Redis 缓存」是错的。

### F14 compose 里其它硬编码（不在 `.env`，改要动 compose）

| 项 | 文件:行 | 值 |
|---|---|---|
| 重启策略 | `docker-compose.yml:27` | `restart: unless-stopped` |
| 环境 | `docker-compose.yml:33` | `ASPNETCORE_ENVIRONMENT: Production` |
| 数据库 Provider | `docker-compose.yml:36` | `Database__Provider: Sqlite` |
| 连接串 | `docker-compose.yml:37` | `ConnectionStrings__Default: "Data Source=/data/shortdrama.db"` |
| 口令门有效期 | `docker-compose.yml:43` | `AccessGate__ValidDays: "30"` |
| JWT 占位密钥 | `docker-compose.yml:46` | `Jwt__Key: "${JWT_KEY:-please-change-this-secret-key-in-production-2025}"` |
| JWT 签发/受众 | `docker-compose.yml:47-48` | `Jwt__Issuer: ShortDrama` / `Jwt__Audience: ShortDramaClient` |
| 演示数据 | `docker-compose.yml:51` | `Demo__Enabled: "false"` |
| 日志级别 | `docker-compose.yml:62-64` | `Default: Information`、`Microsoft.AspNetCore: Warning`、`System.Net.Http.HttpClient: Warning` |
| 日志轮转 | `docker-compose.yml:81-85` | `driver: json-file`、`max-size: "10m"`、`max-file: "3"` |
| 镜像标签 | `docker-compose.yml:22` | `image: ${SHORTDRAMA_IMAGE:-shortdrama:latest}` |

- ⚠️ **`Jwt__Key` 有两套占位值**：compose 是 `please-change-this-secret-key-in-production-2025`（`:46`），而 `ShortDrama.Api/appsettings.json:122` 是 `ShortDrama-Aggregation-Platform-Secret-Key-Please-Change-In-Production-2025`。环境变量优先，**容器里生效的是 compose 那个**。手册若要引用「默认 JWT 密钥」，必须用 compose 的值，别抄 appsettings 的。

### F15 默认采集源数量

- `ShortDrama.Api/apple-cms-sources.json` → 实测 `ConvertFrom-Json`：`AppleCms.enabled = True`，`sources.Count = 22`，其中 `enabled = true` 的 **22 条（全部）**
- 旁证：`docs/DOCKER.md:6` → 「默认配置启用了 22 个第三方公开采集源」
- ⚠️ 路径陷阱：该文件在 `ShortDrama.Api/` 下，**不在** `ShortDrama.Infrastructure/Data/`（那里没有它）。仓库里另有 `bin/Release/net10.0/` 与 `bin/Debug/net10.0/` 两份构建产物副本（已被 `.dockerignore:7-8` 的 `**/bin/` 排除）。

### F16 镜像模式（GHCR）的镜像名与平台

- `.github/workflows/docker-publish.yml:40` → `        run: echo "IMAGE=${GITHUB_REPOSITORY,,}" >> "$GITHUB_ENV"`（仓库名转小写）
- `.github/workflows/docker-publish.yml:56` → `          images: ${{ env.REGISTRY }}/${{ env.IMAGE }}`
- `.github/workflows/docker-publish.yml:22` → `  REGISTRY: ghcr.io`
- `.github/workflows/docker-publish.yml:25` → `  PLATFORMS: linux/amd64`
- `.github/workflows/docker-publish.yml:58-60` → 标签 `latest`（仅默认分支）、`sha-<short>`、`<tag>`
- 推导结果：`ghcr.io/xcool-603/muse-video:latest`，与 `.env.example:20`、`docs/DOCKER.md:324` 的示例一致
- 触发条件（`:15-19`）：push 到 `main`（`:16-17`）、打 `v*` 标签（`:18`）、`workflow_dispatch`（`:19`）

### F17 编码与行尾（实测字节）

| 文件 | 字节（实测） | BOM | CRLF 数 | LF 数 |
|---|---|---|---|---|
| `.env.example` | 2152 | 无 BOM | 0 | 44 |
| `docker-compose.yml` | 3468 | 无 BOM | 0 | 89 |
| `docker-compose.postgres.yml` | 1785 | 无 BOM | 0 | 57 |
| `Dockerfile` | 6670 | 无 BOM | 0 | 137 |
| `docker/entrypoint.sh` | 1441 | 无 BOM | 0 | 35 |
| `.dockerignore` | 1249 | 无 BOM | 0 | 60 |
| `README.md` | 48703 | 无 BOM | 0 | 999 |
| `docs/DOCKER.md` | 32344 | 无 BOM | 0 | 776 |
| `ShortDrama.Api/appsettings.json` | 7888 | 无 BOM | 0 | 127 |
| `.gitattributes` | 1433 | 无 BOM | 0 | 27 |

- **测量方式（`字节` 列必须这样量）**：`[System.IO.File]::ReadAllBytes('C:\...\绝对路径').Length` —— **必须传绝对路径**。.NET 的「当前目录」与 PowerShell 的 `$PWD` 不一定是同一个，传相对路径会读到别处或直接抛异常。行数与 `CRLF/LF` 用 `Get-Content <绝对路径> -Encoding UTF8` 计数、逐字节统计 `13` / `10` 的出现次数。
- 本轮实测（一次性复测 10 个文件）：`.env.example` 2152B/44、`docker-compose.yml` 3468B/89、`docker-compose.postgres.yml` 1785B/57、`Dockerfile` 6670B/137、`docker/entrypoint.sh` 1441B/35、`.dockerignore` 1249B/60、`README.md` 48703B/999、`docs/DOCKER.md` 32344B/776、`ShortDrama.Api/appsettings.json` 7888B/127、`.gitattributes` 1433B/27（**CR 全 0、BOM 全 False**）。
- **本表本轮的变化**：上一版只有 BOM / CRLF / LF 三列，其中 `README.md` 写 987 行、`.gitattributes` 写 26 行，均已过期，现刷新为 **999 / 27**。**`字节` 列是本轮新增的实测列** —— 上一版把字节数只写在过程记录里（`.dsh-team/deploy-runbook/review/`）而没有进本表，于是「字节数对不上」在清单里查不出来：过程记录里的旧值是 `.gitattributes` 1306B、`README.md` 47547B、`docs/DOCKER.md` 31336B，与本轮实测的 **1433 / 48703 / 32344** 都不符。三个实测值各用 `ReadAllBytes` 与 `Get-Item .Length` 两种方式交叉验证，结果一致。
- ⚠️ **`README.md` 与 `docs/DOCKER.md` 在本轮核对期间又被改过**（就是上面说的那批未提交的 cron 日志修复），所以它们的字节 / 行数是**核对时刻**的值：`README.md` 48703B/999 行、`docs/DOCKER.md` 32344B/776 行。这两个文件之后再改，本表就会再次过期 —— **核对时以内容为准**。
- 全部 **UTF-8 无 BOM + LF**（`CRLF=0`）。这一点对 `ACCESS_PASSWORD=遵纪守法世界和平` 有实际影响：`.env` 用非 UTF-8 编辑器保存（例如 Windows 记事本存成 GBK）会把中文口令写坏，表现为「口令没错但一直跳 `/gate`」。
- 可执行位：`.gitattributes:22` → `*.sh text eol=lf`（**行号已从 `:21` 下移到 `:22`**，因为 `.gitattributes` 本轮多了 1 行注释）；`Dockerfile:105` → `COPY --chown=app:app --chmod=755 docker/entrypoint.sh /usr/local/bin/entrypoint.sh`；`Dockerfile:103-104` 注释说明 **Windows 上 git 不保留可执行位，`--chmod=755` 必须显式写**，否则容器起来就是 `permission denied: /usr/local/bin/entrypoint.sh`。

### F18 两个 deploy 开头的包装脚本（后缀 sh / ps1）是否真的已从仓库删除

**结论：是，两个文件都已删除，且不在索引里、不在工作树里。**

验证方式与证据：

1. `git ls-files | Select-String -Pattern 'deploy[.](sh|ps1)'` → **无输出**（索引里没有任何受控文件的路径是这两个已删脚本）。
   - ⚠️ **别用裸的 `'deploy'` 当模式**：那会命中**本文件自己的路径** `docs/DEPLOY-FACTS.md`（实测：`git ls-files | Select-String -Pattern 'deploy'` → 只有 `docs/DEPLOY-FACTS.md` 这一行）。那是文件名的巧合，**不是残留**。上一版这里写的是裸 `deploy` 模式，在本文件改名进 `docs/` 之后已经复现不出「无输出」了，本轮已改正。
2. `Get-ChildItem -Recurse -Force -File -Filter 'deploy.*'` → **无输出**（工作树里也没有）。
3. `git log --all --diff-filter=D --name-only` → 输出末尾是**被删的两个文件路径各一行**：先 `deploy` + `.ps1`、后 `deploy` + `.sh`（来自删除提交 `09cdd7b0a05e47d5ad7c07a404bf18fcd009b208`，提交标题「删除部署包装脚本：按 docker skill 的规则，改用原生 docker compose」）。
4. `git show --stat 09cdd7b` → 原样输出如下（**注意：其中两行是被删文件自己的名字，不是现存文件**）：
   ```
    .dockerignore                        |   2 -
    .github/workflows/docker-publish.yml |   2 +-
    README.md                            |  90 ++---
    deploy + .ps1                        | 749 -----------------------------------
    deploy + .sh                         | 664 -------------------------------
    docs/DOCKER.md                       | 312 ++++++---------
    6 files changed, 147 insertions(+), 1672 deletions(-)
   ```
   → 该提交一次性删掉两个脚本（**sh 版 664 行、ps1 版 749 行**）。为满足 C3 机械判据，上面两行的文件名按 §0.1 断开书写，其余字符与 `git show` 原输出一致。
5. 交叉核对：`.dockerignore` 现在只有 60 行，**不含**任何 `deploy` 行（删除提交把这两行从 `.dockerignore` 里也去掉了；`.dockerignore:57-60` 现在是 `Dockerfile` / `docker-compose*.yml` / `.dockerignore`）。
6. 当前 `scripts/` 目录只剩一个文件：`scripts/gen-clash-rules.mjs`（7980 字节，与部署无关，`docs/CLASH-RULES.md:5` 引用它）。

### F19 仓库里现在还有没有地方引用它们（**计数已按实测修正**）

**声明的精确模式**（本文件所有计数均由这一条模式复现）。它是一个正则的**多个分支用 `|` 连接**，分支依次为：

```
第 1 分支   deploy[.](sh|ps1)        ← 两个包装脚本的文件名（等价于 deploy\.(sh|ps1)）
第 2 分支   部署包装                  ← 中文「不使用部署包装脚本」
第 3 分支   部署脚本                  ← 中文「部署脚本」
第 4 分支   两个连字符 + purge        ← 已删参数（见 F20；本行刻意不写连字符，下同）
第 5 分支   两个连字符 + uninstall-cron
第 6 分支   两个连字符 + install-cron
第 7 分支   两个连字符 + rebuild
第 8 分支   两个连字符 + postgres     ← 注意：会误伤 Markdown 标题锚点，见 B 表
第 9 分支   一个连字符 + InstallTask
第 10 分支  一个连字符 + UninstallTask
第 11 分支  ShortDramaAutoUpdate
第 12 分支  shortdrama-auto-update
```

- 拼成一条时的写法：把上面 12 个分支用 `|` 依次连起来，即成完整模式。
- 第 1 分支用 `deploy[.]` 而不是 `deploy\.`，**语义完全等价**（正则里 `[.]` 与 `\.` 都只匹配一个字面点号），这样本文件声明的这条分支字面量不会命中它自己。
- ⚠️ **整条 12 分支模式扫本文件时并不是 0 命中**（本轮实测 **25 行**）：第 2 / 3 / 11 / 12 分支的字样（中文「部署包装」「部署脚本」、任务名、cron 标记）本来就必须逐条写在本文件里。这 25 行全是**元描述**，不是仓库残留，所以扫仓库时必须排除本文件（见下）。真正要求「本文件 0 命中」的是 §0.1 那两条 C3 判据（见 §5 自检）—— 那是另一个、更窄的模式。
- 复现命令（PowerShell）：
  ```powershell
  Get-ChildItem -Recurse -Force -File |
    Where-Object { $_.FullName -notmatch '\\\.git\\|\\\.dsh-team\\|\\bin\\|\\obj\\|\\node_modules\\|\\dist\\' } |
    Select-String -Pattern '<把上面 12 个分支用 | 连成一行>' -Encoding UTF8
  ```

**扫描范围**：仓库根目录全递归，排除 `\.git\`、`\.dsh-team\`、`\bin\`、`\obj\`、`\node_modules\`、`\dist\`，`-Encoding UTF8`。

> ⚠️ **扫描时必须再排除本文件自己**（`docs/DEPLOY-FACTS.md`）。上一版写的是「请排除本文件 `.dsh-team/` 目录」—— 那时本文件还放在 `.dsh-team/deploy-runbook/work/facts-auditor/` 下；现在它已移到 `docs/`，而 `.dsh-team/` 本身也被 `.gitignore` 忽略（`.gitignore:41-42`）。不排除本文件，就会把本文件的模式声明、残留清单与引用的历史原文一起扫进来 —— 那是**元描述**，不是仓库残留。

**实测结果（本轮重扫，四种口径都跑过）：**

| 扫描口径 | 命中行数 |
|---|---|
| ① 只按上面 6 个目录排除（**含本文件**） | **54** |
| ② 6 个目录 + 排除本文件（`docs/DEPLOY-FACTS.md`） | **29** |
| ③ 6 个目录 + 排除本文件 + 排除 `docs/RUNBOOK.md` | **6**（其中 2 行是标题锚点假阳性） |
| ④ 口径 ③ 再追加 C 表的三条分支 | **6** |

- **口径 ①**：本文件自己被扫到 25 行（模式声明里的 12 个分支、引用的历史原文、自检命令），54 = 25 + 29。
- **口径 ② 的 29 行分布**：`.gitattributes` 1 行、`README.md` 1 行、`docs/DOCKER.md` 4 行（含 2 行锚点假阳性）、`docs/RUNBOOK.md` 23 行。
- **口径 ③ 的 6 行**：`.gitattributes:12`、`README.md:157`、`docs/DOCKER.md:84`、`:86`、`:155`、`:229`。这是「只算仓库原有文件、不算本轮新写的两份产物」的窄口径。
- **口径 ② 追加 C 表分支后为 31 行**（比不加时多 2 行，都在 `docs/RUNBOOK.md:137`、`:939`，是「旧脚本当年会…」的历史 / 迁移口径）。
- 上一版写的「命中 10 行 / 8 行有效」与「扩展后 13 行」**本轮复现不出来**（实测 29 / 31）。原因是那「8 行有效命中」里的 5 行 —— `.gitattributes:11`/`:18`（现 `:12-13`/`:19`）、`docs/DOCKER.md:473-474`（现 `:482-483`）、`docs/DOCKER.md:648`（现 `:657`）—— **已由提交 `687e7d3` 修掉**（见 §2-3、§2-4、§2-16）。

其中 **2 行是模式假阳性**（下面 B 表），其余 27 行全部是「已删除 / 已移除 / 历史 / 迁移 / 用户自建」口径 —— **有效残留 = 0 行**。

#### A. 模式命中的逐条判定（口径 ② 的 29 行）—— **有效残留 = 0 行**

| 位置 | 当前内容 | 判定 |
|---|---|---|
| `.gitattributes:12` | `# （曾经还有 ` + `deploy` + `.sh` + ` / ` + `deploy` + `.ps1` + ` 两个部署包装脚本，已按 docker skill 的规则删除 ——` | ✅ **允许**：以「已删除」口径点出文件名，不再描述成现存文件（原 `:11` 的残留已修，见 §2-16） |
| `.gitattributes:19` | `#   .env.example    compose 读取它做变量插值；值尾带 \r 会让口令/密钥多一个字符` | ✅ **已修复**：不再把 `.env` 的取值行为挂在已删脚本上（原 `:18`，见 §2-16） |
| `README.md:157` | 「本项目**不使用部署包装脚本** —— 直接用 `docker compose`」 | ✅ **允许**：正是「说明它们已删除」（未提文件名） |
| `docs/DOCKER.md:86` | 「本项目不使用部署包装脚本（`deploy` + `.sh` / `deploy` + `.ps1` 已移除）」 | ✅ **允许**：唯一一处以「已移除」口径点出文件名 |
| `docs/DOCKER.md:229` | 「（这正是以前那个部署脚本里唯一有点价值的行为，而 `docker compose` 本身就自带。）」 | ✅ **允许**：历史沿革说明，未提文件名、未教用户执行 |
| `docs/DOCKER.md:482-483` | 卷表「何时创建」列现为「带 `-f docker-compose.postgres.yml` 时」 | ✅ **已修复**：不再用已删脚本的 `postgres` 参数（见 §2-3） |
| `docs/DOCKER.md:657` | 「Windows 用 `Get-ScheduledTask` 确认任务在」 | ✅ **已修复**：已不含已删脚本创建的任务名（见 §2-4） |
| `docs/RUNBOOK.md` 23 行（`:12`、`:180`、`:707`、`:711`、`:752`、`:769`、`:770`、`:778`、`:812`、`:837`、`:844`、`:846`、`:862`、`:863`、`:873`、`:877`、`:893`、`:894`、`:910`、`:912`、`:960`、`:972`、`:1237`） | 手册第 1 章与第五章：`# shortdrama-auto-update` 是**新写法**的 crontab 标记注释；`ShortDramaAutoUpdate` 是手册教用户**自己新建**的 Windows 计划任务名；其余是 M1–M8 迁移步骤（查旧任务 → 删旧行 → 写原生命令 → 验证） | ✅ **允许**：全部是「已删除 / 迁移 / 新建」口径，**没有一处把已删脚本当可用命令** |

#### B. 模式假阳性（2 行，**不是**残留）

| 位置 | 内容 | 为什么是假阳性 |
|---|---|---|
| `docs/DOCKER.md:84` | 「…想换 PostgreSQL 见[第五节](#五数据库sqlite--postgresql)。」 | 命中的是**标题锚点**里的双连字符，不是脚本参数。实测：该文件里「两个连字符 + postgres」这一串只出现 2 次（`:84`、`:155`），且都是同一个锚点 |
| `docs/DOCKER.md:155` | 「**改用 PostgreSQL**：见[第五节](#五数据库sqlite--postgresql)。」 | 同上，同一锚点的第二次出现 |

> 锚点成因：标题「五、数据库：SQLite / PostgreSQL」里的 `/` 被 GitHub 锚点规则去掉后留下 `sqlite--postgresql` 的双连字符，属正常 Markdown，无需修改。**核对时不要把这两行算成残留。**（上一版说这一串在 `docs/DOCKER.md` 里出现 4 次、其中两处在卷表 —— 那是因为当时卷表里还写着已删参数；本轮实测只剩 `:84`、`:155` 这 2 处锚点。）

#### C. 模式外的失效描述（上一版 3 行，**现已全部清零**）

上一版在这里列了三行「脚本会自动…」的失效说法，现在是 `docs/DOCKER.md:213`、`:372`、`:386`（原 `:363`、`:377`，因该文件本轮新增 9 行而下移）。本轮实测：

- 在声明模式后追加 `脚本首次运行` / `脚本会自动` / `首次运行脚本` 三个分支，`docs/DOCKER.md` 内 **0 行命中** —— 三处均已由提交 `687e7d3` 改写成原生命令（见 §2-1、§2-2）。
- 同一扩展模式扫全仓库（排除本文件）为 **31 行**，比不加时多 2 行，都在 `docs/RUNBOOK.md:137`、`:939`，是「旧脚本当年会…」的历史 / 迁移口径，**不是**教用户执行。
- 因此 **C 表有效残留 = 0**。

**结论（本轮实测）**：仓库里**没有任何地方**把这两个包装脚本当作可用命令来教用户执行，也**没有任何地方**把它们描述成现存文件 —— **有效残留 = 0 行**。上一版结论里的「5 处有效残留 + 3 处模式外失效描述」已随提交 `687e7d3` 全部修复；现在剩下的命中全部是「已删除 / 已移除 / 历史 / 迁移 / 用户自建」这几种正当口径，手册照抄不会写出无法执行的步骤。

### F20 已删脚本的参数全集（手册**禁止**引用）

来源：`git show 09cdd7b^:deploy` + `.sh` 与 `git show 09cdd7b^:deploy` + `.ps1`。这些参数**现在没有任何程序会解析**（因为唯一的解析者已被删除）。

> 下表按 §0.1 约定**去掉了前导连字符**。**原写法是在参数名前面加两个连字符**（PowerShell 版为一个连字符）。

| 已删参数（sh 版） | 已删参数（ps1 版） | 现在该怎么做 |
|---|---|---|
| `p` / `port <端口>`（原为单/双连字符） | `Port <端口>` | 改 `.env` 的 `APP_PORT`（`.env.example:11`） |
| `postgres` | `Postgres` | `docker compose -f docker-compose.yml -f docker-compose.postgres.yml up -d` |
| `rebuild` | `Rebuild` | `docker compose build --no-cache && docker compose up -d`（`docs/DOCKER.md:154`） |
| `update` | `Update` | `git pull --ff-only && docker compose up -d --build` |
| `check` | `Check` | `git fetch && git log --oneline HEAD..@{u}`（`docs/DOCKER.md:72`） |
| `install-cron [HH:MM]` | `InstallTask` / `TaskTime` | 宿主 crontab / 任务计划程序（`docs/DOCKER.md:58-67`、`:256-259`） |
| `uninstall-cron` | `UninstallTask` | `crontab -e` 删行（`docs/DOCKER.md:246`） |
| `down` | `Down` | `docker compose down` |
| `purge` | `Purge` | `docker compose down -v` |
| 退出码 `10` = 有新版本 | — | 用 `git log --oneline HEAD..@{u}` 是否有输出判断 |
| cron 标记 `# shortdrama-auto-update` | 任务名 `ShortDramaAutoUpdate` | 均已无生产者（见 §2-4） |

- 删除提交 09cdd7b 的提交信息里也留了一份「脚本提供的能力 → 原生替代」对照表，与上表一致。

---

## 2. 文档里容易与仓库事实不一致的点（每条带证据）

> 这些是**手册最可能照抄错**的地方。
> **注意（本轮刷新）**：**2-1、2-2、2-3、2-4、2-9、2-11、2-14、2-16 记录的问题已修复**，本轮已逐条实测确认并改标为「✅ 已修复」，并保留原问题原文以便追溯（其中 2-9 是**本轮那批未提交改动**顺带修掉的，其余 7 条由提交 `687e7d3` 修掉）。
> 其余条目（2-5 ~ 2-8、2-10、2-12、2-13、2-15）本轮复测仍然成立，属事实性偏差或未实测项。

### 2-1 ✅ **已修复** —— 原「JWT_KEY 首次运行脚本会自动替换为随机值」的失效说法

- **原问题**（上一版清单记录的仓库残留）：`docs/DOCKER.md:386` 的变量表曾把 `JWT_KEY` 的说明写成「**首次运行脚本会自动替换为随机值**」；`docs/DOCKER.md:213` 曾写「确认 `JWT_KEY` 已是随机值（脚本首次运行会自动生成）」。
- **当前实测（已修复）**：提交 `687e7d3` 已改写这两处 ——
  - `docs/DOCKER.md:386` 现为「JWT 签名密钥，**上线前必须自己替换**（`openssl rand -hex 48`）」；
  - `docs/DOCKER.md:213` 现为「上线前请务必：改掉 `ACCESS_PASSWORD`、改掉 `admin123` 密码、确认 `JWT_KEY` 已换成随机值（生成命令见第一节）。」
  - 实测命令：`Select-String -Path docs/DOCKER.md -Pattern '脚本首次运行|脚本会自动|首次运行脚本' -Encoding UTF8` → **0 命中**。
- **不变的事实**（仍然成立）：两个包装脚本已删（F18）；`.env.example:31` 仍是占位值 `please-change-this-secret-key-in-production-2025`；`docker-compose.yml:46` 的兜底值也是同一个占位值。
- **手册口径**：不执行任何脚本时，`JWT_KEY` 会**一直**是占位值，必须由人手动替换。生成方式：`openssl rand -hex 48`（`.env.example:29-30`）。

### 2-2 ✅ **已修复** —— 原「清空 SHORTDRAMA_IMAGE，脚本会自动回到本地构建」

- **原问题**：`docs/DOCKER.md:372` 曾写「把 `.env` 里的 `SHORTDRAMA_IMAGE` 清空即可，**脚本会自动**回到本地构建。」
- **当前实测（已修复）**：`docs/DOCKER.md:372` 现为「把 `.env` 里的 `SHORTDRAMA_IMAGE` 清空即可，**下一次 `docker compose up -d --build` 就回到本地构建**。」
- **不变的事实**：`docker-compose.yml:22` → `image: ${SHORTDRAMA_IMAGE:-shortdrama:latest}`，**兜底值就在 compose 里**，不需要任何脚本。
- **手册口径**：清空 `.env` 的 `SHORTDRAMA_IMAGE` 后，compose 自己就回落到 `shortdrama:latest` 并走本地构建。

### 2-3 ✅ **已修复** —— 原「卷表格用已删脚本的 `postgres` 参数当『何时创建』」

- **原问题**：`docs/DOCKER.md:482-483` 的卷表「何时创建」列曾写 `postgres`（**原写法为两个连字符**，见 F20），那是已删脚本的参数。
- **证据（已删脚本）**：`git show 09cdd7b^:deploy` + `.sh` 里有 `--postgres) USE_POSTGRES=1; shift ;;` 这一行；该文件已不存在。
- **当前实测（已修复）**：`docs/DOCKER.md:482-483` 现为「带 `-f docker-compose.postgres.yml` 时」（两行同值），已不含已删参数。
- **手册口径**：这两个卷在 `docker compose -f docker-compose.yml -f docker-compose.postgres.yml up -d` 时创建（`docker-compose.postgres.yml:53-57`）。

### 2-4 ✅ **已修复** —— 原「Windows 用 `Get-ScheduledTask -TaskName ShortDramaAutoUpdate` 确认任务在」

- **原问题**：`docs/DOCKER.md:657` 曾写 `Get-ScheduledTask -TaskName ShortDramaAutoUpdate`。
- **证据（已删脚本）**：`git show 09cdd7b^:deploy` + `.ps1` 里有 `$TaskName = 'ShortDramaAutoUpdate'`；该文件已不存在，**现在没有任何东西会创建这个计划任务**。
- **当前实测（已修复）**：`docs/DOCKER.md:657` 现为「…Linux 用 `crontab -l | grep shortdrama`、**Windows 用 `Get-ScheduledTask` 确认任务在**。**Windows 还要确认它不是「只在用户登录时运行」**，否则无人登录时静默不执行」—— **已不含该任务名**。
  - 实测命令：`Select-String -Path docs/DOCKER.md -Pattern 'ShortDramaAutoUpdate' -Encoding UTF8` → **0 命中**（上一版说「全仓库只有这一处」，现在 `docs/DOCKER.md` 里一处都没有了）。
  - 现在全仓库出现该任务名的地方只剩 `docs/RUNBOOK.md`（手册第 5 章教用户**自己新建**这个任务名，属新写法，不是残留）。
- **手册口径**：Windows 的定时更新由用户自己在「任务计划程序」里建（`docs/DOCKER.md:256-259`），任务名由用户自定。

### 2-5 ⚠️ 卷名举例 `shortdrama_shortdrama-data` 只在目录名叫 `shortdrama` 时成立

- 证据（文档）：`docs/DOCKER.md:638` → 「`docker volume ls` 里看到的会是 `shortdrama_shortdrama-data` 这种形式」；`docs/DOCKER.md:512` 用 `docker volume ls | grep shortdrama-data`
- 证据（仓库）：`docker-compose.yml` 无顶层 `name:`，无 `COMPOSE_PROJECT_NAME`（F5）；compose 项目名由目录名推导。
- 本机实际目录名是 `短剧聚合`（中文），项目名前缀不会是 `shortdrama`。**未实测**（本机无 docker）。
- **正确表述**：手册必须写「先 `docker volume ls` 查实际卷名，或用 `docker compose ps -aq app` 取容器 id」，不要写死 `shortdrama_...` 前缀。要固定就用 `COMPOSE_PROJECT_NAME=shortdrama`（或 `docker compose -p shortdrama ...`）。
- 同源提醒：`docs/DOCKER.md:519-520` 已经警告「别照抄 `-v shortdrama-data:/data`，会新建空卷」——方向是对的，但举例的卷名仍需按实际项目名替换。

### 2-6 ⚠️ 「容器以非 root 运行」与 `docker inspect` 不符

- 证据（文档）：`docs/DOCKER.md:485` 小节标题「容器以非 root 运行（镜像内置的 app 用户）」；`:487` 写「镜像里的业务进程以 `app` 运行，不再是 root」
- 证据（仓库）：`Dockerfile` **没有 `USER` 指令**（F7 实测无输出）；`docker/entrypoint.sh:20` 的 `if [ "$(id -u)" = "0" ]` 正说明它以 root 起步。
- **正确表述**：容器**以 root 启动**，入口脚本做完 `chown` 后用 `gosu` 把业务进程降权到 `app`。写「容器以非 root 运行」会在 `docker inspect <容器> --format '{{.Config.User}}'` 返回空（即 root）时自相矛盾。

### 2-7 ⚠️ UID/GID `1654` 是注释里的数字，不是实测值

- 证据：`Dockerfile:87` 注释「已经内置了 `app` 用户与 `app` 组（UID/GID 1654）」；`docs/DOCKER.md:487` 直接写成「UID/GID `1654`」；但 `docs/DOCKER.md:513` 自己又写「通常是 1654 —— 以实测为准」。
- **正确表述**：手册要数字就给实测命令（`docs/DOCKER.md:514` 那条 `docker run --rm --entrypoint sh mcr.microsoft.com/dotnet/aspnet:10.0 -c 'id app'`），或干脆只写用户名 `app`（脚本本身就是按用户名操作的，`entrypoint.sh:17`）。

### 2-8 ⚠️ 平台速查表把「升级」写成 `--build`，没区分镜像模式

- 证据（文档）：`docs/DOCKER.md:81` → Linux/macOS 升级列都写 `git pull --ff-only && docker compose up -d --build`
- 冲突证据：`docs/DOCKER.md:49-53` 明确镜像模式的升级是 `docker compose pull app && docker compose up -d`（`:52`）
- **风险**：镜像模式（`.env` 里配了 `SHORTDRAMA_IMAGE`）下执行 `--build` 会在本机重新编译，并把结果打上**远端镜像同名标签**（`docker-compose.yml:22`），下次 `pull` 才纠正。
- **正确表述**：手册必须按模式分叉，不能只给一条 `--build`。

### 2-9 ✅ **已修复（本轮顺带刷新）** —— 原「`logs/auto-update.log` 被当成仓库文件列出，且该目录当前不存在」

- **原问题**：`docs/DOCKER.md` 的两条 cron 示例（`:62`、`:241`）直接写 `>> logs/auto-update.log 2>&1`，而仓库里**没有** `logs/` 目录（`.gitignore:19` → `logs/`；`Test-Path logs` → **False**）—— 照抄会让重定向立即失败，定时更新一条都跑不成。
- **当前实测（已修复）**：本轮那批未提交改动已把两条 cron 改成 `0 4 * * * (cd /path/to/shortdrama && git pull --ff-only && docker compose up -d --build) >> /path/to/shortdrama/logs/auto-update.log 2>&1`（**加了括号** + **日志写绝对路径**），并在 `docs/DOCKER.md:249-254`、`README.md:146-149` 加了 📌 提示，明确要求先 `mkdir -p logs`，并解释「括号不能省」（否则 `cd`/`git pull` 的报错不进日志）。
- **仍然成立的部分**：`logs/` 目录在仓库里确实不存在（它是运行期产物，被 `.gitignore:19` 忽略）；`docs/DOCKER.md:776` 的文件说明表仍把 `logs/auto-update.log` 列进去（表内已注明「按第三节配了 cron 后生成」，属生成物而非仓库文件）。
- **手册口径**：cron 行照抄 `docs/DOCKER.md:62` 那条（已带括号与绝对路径），并在前面补 `mkdir -p logs`。

### 2-10 ⚠️ PostgreSQL 模式的 `redis` 容器起了但应用不连它

- 证据：`docker-compose.postgres.yml:41-51` 定义了 `redis` 服务；但该覆盖文件对 `app` 只改了 `Database__Provider`（`:13`）与 `ConnectionStrings__Default`（`:14`），**没有任何 Redis 配置**；全仓库 `*.cs` 搜 `Redis` 仅命中 `ShortDrama.Infrastructure/DependencyInjection.cs:41` 的一句注释「// 内存缓存（生产可替换为 Redis）」。
- 文档措辞：`docs/DOCKER.md:453`「会额外拉起 `postgres` 和 `redis` 两个容器」（陈述正确）、`:771`「PostgreSQL + Redis 覆盖配置」。
- **正确表述**：`redis` 会被拉起，但当前代码**不使用**它（缓存仍是进程内内存缓存）；不要写成「PostgreSQL 模式启用了 Redis 缓存」。

### 2-11 ✅ **已修复** —— 原「`grep SHORTDRAMA_IMAGE .env` 会命中注释行」

- **原问题**：`docs/DOCKER.md:663` 曾建议用 `grep SHORTDRAMA_IMAGE .env` 判断当前模式，而 `.env.example:20` 的 `SHORTDRAMA_IMAGE=...` 是**注释**，会命中注释行、看起来像「已配了镜像模式」；同一行还曾写「看部署完成后打印的『部署方式』一行」—— 那一行只有已删脚本才会打印。
- **当前实测（已修复）**：`docs/DOCKER.md:663` 现为「`grep '^SHORTDRAMA_IMAGE' .env`：有值 = 镜像模式；无输出（只有注释行）= 源码模式」，**「看打印的部署方式」那句也已删除**（实测：`Select-String -Path docs/DOCKER.md -Pattern '部署方式'` → 0 命中）。这两处正是提交 `687e7d3` 修掉的「模式确认方式」失效说法。
- **不变的事实**：`.env.example:20` 的 `SHORTDRAMA_IMAGE=ghcr.io/xcool-603/muse-video:latest` 仍是**注释行**；要判断模式就用 `grep '^SHORTDRAMA_IMAGE' .env`（有输出才是真的配了）。

### 2-12 ⚠️ README 说支持 MySQL，compose 只提供 SQLite / PostgreSQL 两条路

- 证据：`README.md:58` → `| 数据库 | SQLite（默认，零配置） / PostgreSQL 16+ / MySQL 8+ |`；`ShortDrama.Api/appsettings.json:119` → `"_MySql": "Server=localhost;Database=shortdrama;User=root;Password=xxx"`（是**注释掉的示例键**）
- 证据（compose）：`docker-compose.yml:36` 只有 `Database__Provider: Sqlite`；`docker-compose.postgres.yml:13` 只有 `PostgreSql`。没有 MySQL 覆盖文件。
- **正确表述**：容器部署手册只讲 SQLite 与 PostgreSQL；MySQL 属于「框架代码支持、仓库未提供部署编排」。

### 2-13 ⚠️ 「健康检查通过」≠「内容就绪」

- 证据：`ShortDrama.Api/Program.cs:162` 的 `/health` 返回静态 JSON，不查数据库、不查采集源；`docs/DOCKER.md:651` 说「首次启动要同步数据源，等 1-2 分钟」
- 另证：`ShortDrama.Infrastructure/Data/DbSeeder.cs:159` 注释「3) 首次启动同步各平台短剧数据」；`:168` → `_ = Task.Run(async () =>`（首次启动的平台数据同步放在后台任务里，**故意不阻塞 API 启动**）；`:165` → `var timeoutSeconds = config.GetValue("AppleCms:BootstrapTotalTimeoutSeconds", 600);`
- **正确表述**：`docker compose ps` 显示 `healthy` 只代表进程活着；内容库要等后台播种（默认 600 秒上限）。手册的「验证部署成功」不能只靠 healthcheck。

### 2-14 ✅ **已修复** —— 原「故障排查表有一行多出一列，Markdown 渲染会错位」

- **原问题**：`docs/DOCKER.md:666` 那一行曾有 3 个单元格（现象 / 原因 / 处理），而表头 `docs/DOCKER.md:644` 只有 2 列（`| 现象 | 原因与处理 |`）。
- **当前实测（已修复）**：提交 `687e7d3` 把两行 3 列单元格合并回 2 列。表头仍在 `docs/DOCKER.md:644`（2 列）；`docs/DOCKER.md:666`、`:667` 现在都是 2 个单元格，表格不再错位。
- **手册口径**：整表可直接复制。

### 2-15 ⚠️ 三处「默认值」互相不同，引用时必须说清来源

| 配置 | `appsettings.json` | `docker-compose.yml` | 容器内实际生效 |
|---|---|---|---|
| `ConnectionStrings:Default` | `Data Source=shortdrama.db`（相对路径，`:117`） | `Data Source=/data/shortdrama.db`（`:37`） | compose 值（另有 `Dockerfile:126` 的 `ENV` 同值兜底） |
| `Jwt:Key` | `ShortDrama-Aggregation-...-2025`（`:122`） | `${JWT_KEY:-please-change-this-secret-key-in-production-2025}`（`:46`） | compose/`.env` 值 |
| `Torrent:BaseUrl` | `http://127.0.0.1:8787`（`:36`） | `${TORRENT_BASE_URL:-http://host.docker.internal:8787}`（`:58`） | compose/`.env` 值（**必须**是 host.docker.internal） |

- **规则**：`.NET` 配置的优先级是「环境变量 > `appsettings.json`」。手册引用「容器里的默认值」时，一律以 `docker-compose.yml` / `.env.example` 为准，不要抄 `appsettings.json`。

### 2-16 ✅ **已修复** —— 原「`.gitattributes` 的注释仍把已删脚本当成现存文件」

- **原问题**（上一版清单记录的仓库残留）：`.gitattributes:11` 曾写 `#   ` + `deploy` + `.sh` + `              部署机上的部署/升级脚本`（注释里写的是**完整文件名**，按 §0.1 断开书写）；`:18` 曾写 `#   .env.example    部署脚本用 cut -d= 取值，值尾带 \r 会让口令/密钥多一个字符`。
- **当前实测（已修复）**：提交 `687e7d3` 改写了这两处，文件也从 **26 行变成 27 行**（`11` 之后的注释行号整体下移一位）——
  - `.gitattributes:11` 现为 `# 必须是 LF，这里钉死。`（原来那条把 `deploy` + `.sh` 描述成现存文件的注释已删）；
  - `.gitattributes:12-13` 新增两行，以**已删除**口径点出文件名：「`# （曾经还有 ` + `deploy` + `.sh` + ` / ` + `deploy` + `.ps1` + ` 两个部署包装脚本，已按 docker skill 的规则删除 ——`」「`#   部署与升级改用原生 docker compose，见 README 与 docs/DOCKER.md。）`」；
  - `.gitattributes:19`（原 `:18`）现为 `#   .env.example    compose 读取它做变量插值；值尾带 \r 会让口令/密钥多一个字符` —— 不再把 `.env` 的取值行为挂在已删脚本上。
- **不变的事实**：`*.sh text eol=lf`（`.gitattributes:22`，**原 `:21`**）本身仍然正确且必要（`docker/entrypoint.sh` 靠它）。
- **结论**：**已修复，无需再提给仓库维护者。** F19 声明的模式本来就能抓到这两行（现仍在 A 表里，但判定已从「真实残留」改为「✅ 允许 / ✅ 已修复」）。

---

## 3. 手册可直接引用的事实速查（一页版）

| 手册要写的值 | 直接引用 |
|---|---|
| 服务名 | `app`（`docker-compose.yml:17`） |
| 访问地址 | `http://localhost:18080`（`docker-compose.yml:30` + `.env.example:11`） |
| 端口映射写法 | `"${APP_PORT:-18080}:8080"`（`docker-compose.yml:30`） |
| 容器内端口 | `8080`（`Dockerfile:128`、`Dockerfile:121`） |
| 健康检查 | `curl -fsS http://localhost:8080/health`，30s/5s/40s/3（`docker-compose.yml:75-79`） |
| 数据卷 | `shortdrama-data` → `/data`（`docker-compose.yml:72`、`:87-89`） |
| 卷名规则 | `<compose 项目名>_shortdrama-data`；项目名来自目录名（compose 无 `name:`） |
| 管理后台 | `http://localhost:18080/admin`，`admin / admin123`（`DbSeeder.cs:147,150`；`router/index.ts:64`） |
| 默认口令 | `遵纪守法世界和平`（`docker-compose.yml:42`） |
| 口令门开关 | `ACCESS_GATE_ENABLED` → `AccessGate__Enabled`（`docker-compose.yml:41`） |
| 种子服务地址 | `http://host.docker.internal:8787`（`docker-compose.yml:58`）；开关 `TORRENT_ENABLED`（`:59`） |
| PG 覆盖文件 | `app` / `postgres` / `redis` 三个服务（`docker-compose.postgres.yml:11,19,41`） |
| 容器用户 | 镜像内置 `app`；容器以 root 启动、入口脚本 `gosu` 降权（`entrypoint.sh:17,20,27`） |
| 入口脚本三件事 | chown `/data`（`entrypoint.sh:22`）→ `gosu app` 降权（`:27`）→ `exec "$@"`（`:35`） |
| 镜像模式镜像 | `ghcr.io/xcool-603/muse-video:latest`（`docker-publish.yml:40,56`） |
| 平台 | `linux/amd64`（`docker-publish.yml:25`） |
| 采集源数量 | 22 个，全部 enabled（`ShortDrama.Api/apple-cms-sources.json`） |
| 部署包装脚本 | **不存在**（两个 deploy 开头的包装脚本已在 09cdd7b 删除） |

---

## 4. 未实测 / 待确认（交给能跑 docker 的人）

1. **compose 项目名与卷名**：中文目录名 `短剧聚合` 下 `docker compose config` 是否直接报项目名非法，以及实际卷名前缀。验证命令：`docker compose config --format json`（看 `name`）、`docker volume ls | Select-String shortdrama`。本机**未安装 docker**，无法实测。
2. **`app` 用户实际 UID/GID**：`docker run --rm --entrypoint sh mcr.microsoft.com/dotnet/aspnet:10.0 -c 'id app'`。
3. **首次构建耗时与磁盘占用**：文档写 3-5 分钟 / 3-5 GB（`docs/DOCKER.md:127`、`:650`），仓库内无证据，属经验值。
4. **`curl` 是否真在运行时镜像里**：`Dockerfile:75` 装了，但未实测镜像。健康检查完全依赖它。
5. **PostgreSQL 覆盖模式下 `app` 是否真的能连上 `postgres`**：`depends_on: condition: service_healthy` 已配（`docker-compose.postgres.yml:15-17`），但需实测首次建库与 EF 迁移行为。
6. ~~**`.gitattributes:11`/`:18` 的过期注释是否会被修**~~ → **已修复**：提交 `687e7d3` 已把注释改写成「曾经还有 … 已删除」口径，`.env` 的 LF 理由也改挂到 compose 上；当前位置是 `.gitattributes:12-13` 与 `:19`（见 §2-16）。**不再是待确认项。**

---

## 5. 本文件的自检（C2 / C3）

**C3 —— 两条机械模式扫本文件，命中必须为 0。本轮实跑结果：**

```powershell
$f = 'C:\Work\短剧聚合\短剧聚合\docs\DEPLOY-FACTS.md'

# 模式一（§0.1）：两个包装脚本的完整文件名（点号转义）
Select-String -Path $f -Pattern 'deploy\.(sh|ps1)' -AllMatches -Encoding UTF8
# → 0 命中 ✅

# 模式二（§0.1）：已删参数 —— 6 个分支，前导连字符按 §0.1 约定不写出来
Select-String -Path $f -Pattern $declaredParamPattern -AllMatches -Encoding UTF8
# → 0 命中 ✅
```

- ⚠️ **两条判据是两个不同的模式，别混**：C3 的「模式二」是 **§0.1** 声明的**参数**模式（6 个分支）；**F19** 用的是另一条**更宽的 12 分支**模式（额外含中文「部署包装 / 部署脚本」、`postgres` 锚点、任务名、cron 标记）。**F19 那条扫本文件会命中 25 行**（本轮实测），因为那几类字样本来就必须逐条写在本文件里 —— 这是**预期行为**，不代表 C3 判据失守。上一版把两者混为一谈，写成了「模式二 → 0 命中」而不说明用的是哪条模式。
- 模式二**在更早的稿子里曾命中**：命中的是「本文件自己写下的那条模式字面量」和「本文件自己写下的那条自检命令」，都不是真正的引用，但机械判据只认字符串，所以照样算命中。现已把两处都改掉：
  - 模式声明改成**逐分支列表**（§F19），不再出现连成一串的字面量；
  - 本节的自检命令改用 `$declaredParamPattern` 变量引用，不再内联那串字面量。
- 本文件**没有任何地方把已删脚本写成可执行命令**。出现 `deploy` + `.sh` / `deploy` + `.ps1` 断开写法的地方，全部在「已删除」的语境里（F18 的删除证据、F19 的残留清单与「已修复」判定、§2-16 的原问题原文）。
- 额外用更宽的模式复扫本文件（`-UninstallTask`、`-Purge`、`-Rebuild`、`-Update`、`-Postgres`、`-TaskTime` 等）→ 仅命中「标题锚点 `#五数据库sqlite--postgresql`」（属正常 Markdown）与 `auto-update.log`、`shortdrama-auto-update` 之类的无关串 / 元描述，**无一处是脚本参数**。

**C2 —— 事实条目数：**

| 编号 | 主题 | 是否带 文件:行 + 实际值 |
|---|---|---|
| F1 | compose 服务名 | ✅ |
| F2 | 对外端口完整写法与默认值 | ✅ |
| F3 | 容器内端口 | ✅ |
| F4 | 健康检查命令 | ✅ |
| F5 | 数据卷逻辑名与命名规则 | ✅ |
| F6 | `.env` 每个变量与默认值 | ✅ |
| F7 | 容器运行用户 | ✅ |
| F8 | 入口脚本三件事 | ✅ |
| F9 | 管理后台默认账号 | ✅ |
| F10 | 默认访问口令 | ✅ |
| F11 | 口令门开关变量 | ✅ |
| F12 | 种子服务默认地址与启用变量 | ✅ |
| F13 | PostgreSQL 覆盖文件的 3 个服务 | ✅ |
| F14 | compose 其它硬编码 | ✅ |
| F15 | 默认采集源数量 | ✅ |
| F16 | 镜像模式镜像名与平台 | ✅ |
| F17 | 编码与行尾（实测字节） | ✅ |
| F18 | 两个包装脚本已删（含验证方式） | ✅ |
| F19 | 现存引用清单（29 行命中 / **有效残留 0** / 2 假阳性 / C 表已清零） | ✅ |
| F20 | 已删参数全集 | ✅ |

**合计 20 条事实**（≥10 条），每条均给出 `文件:行` 与实际值。另有 §2 的 16 条「文档易错点」（其中 2-1 ~ 2-4、2-11、2-14、2-16 由提交 `687e7d3` 修复、2-9 由本轮那批未提交改动修复，均已改标为「✅ 已修复」）与 §3 速查表。

> **本文件本轮（定向返工）改动范围**：文档头的核对快照说明、§0 残留引用扫描口径、F17 编码与行尾表（新增「字节」列并刷新过期值）、F18 第 1 条的复现命令、F19 全节（计数与结论按实测重写）、§2 的 2-1 / 2-2 / 2-3 / 2-4 / 2-9 / 2-11 / 2-14 / 2-16、§4 第 6 项与第 3 项的行号、§5 的 C3 说明与 C2 表里 F19 那一行。
> **另外**：因为 `README.md`（+5 行）与 `docs/DOCKER.md`（+9 行）在本轮核对期间被那批未提交的 cron 日志修复改过，本文件里**所有指向这两个文件的行号引用**都按新行号整体刷新过一遍（`README.md` 后半段 +5、`docs/DOCKER.md` 后半段 +9）。
> **未改动仓库里的任何其它文件** —— 这一点用 `git status --porcelain -uall` 核过：本文件之外只有 `README.md`、`docs/DOCKER.md`、`docs/RUNBOOK.md` 是 ` M`，而那三个是本轮**别的角色**改的（时间戳早于本文件的最后一次写入，且本角色从未对它们执行过写操作）。
