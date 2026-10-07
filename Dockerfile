# ============================================================================
# 短剧聚合平台 —— 单镜像多阶段构建
#
# 前端（Vue3 + Vite）与后端（.NET 10）打进同一个镜像，由 .NET 直接托管静态资源，
# 不需要额外的 Nginx 容器。这样 docker compose up 一条命令就能跑起来。
#
# 构建：docker build -t shortdrama .
# 运行：docker run -d -p 18080:8080 -v shortdrama-data:/data shortdrama
# ============================================================================


# ---------------------------------------------------------------------------
# 阶段 1：构建前端
# ---------------------------------------------------------------------------
FROM node:20-alpine AS web
WORKDIR /web

# 先只拷贝依赖清单，利用层缓存（依赖没变时跳过 npm ci）
COPY short-drama-web/package.json short-drama-web/package-lock.json ./
RUN npm ci --no-audit --no-fund

COPY short-drama-web/ ./
RUN npm run build


# ---------------------------------------------------------------------------
# 阶段 2：构建后端
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api
WORKDIR /src

# 先只拷贝 csproj，利用层缓存（代码改了但依赖没变时跳过 restore）
COPY ShortDrama.slnx ./
COPY ShortDrama.Domain/ShortDrama.Domain.csproj ShortDrama.Domain/
COPY ShortDrama.Application/ShortDrama.Application.csproj ShortDrama.Application/
COPY ShortDrama.Infrastructure/ShortDrama.Infrastructure.csproj ShortDrama.Infrastructure/
COPY ShortDrama.Api/ShortDrama.Api.csproj ShortDrama.Api/
RUN dotnet restore ShortDrama.Api/ShortDrama.Api.csproj

# 再拷贝源码
COPY ShortDrama.Domain/ ShortDrama.Domain/
COPY ShortDrama.Application/ ShortDrama.Application/
COPY ShortDrama.Infrastructure/ ShortDrama.Infrastructure/
COPY ShortDrama.Api/ ShortDrama.Api/

# 把前端产物放进 wwwroot，由 .NET 托管
COPY --from=web /web/dist/ ShortDrama.Api/wwwroot/

RUN dotnet publish ShortDrama.Api/ShortDrama.Api.csproj \
        -c Release \
        -o /app/publish \
        --no-restore \
        /p:UseAppHost=false


# ---------------------------------------------------------------------------
# 阶段 3：运行时
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

# OCI 标签：GHCR 的包页面会直接显示这些信息（构建工作流只推镜像，不会自己补标签）
LABEL org.opencontainers.image.title="短剧聚合平台" \
      org.opencontainers.image.description="跨平台短剧聚合搜索与播放：采集源实时聚合 + 去广告播放 + 本地种子增强" \
      org.opencontainers.image.source="https://github.com/XCool-603/muse-Video" \
      org.opencontainers.image.licenses="MIT"

# 三个包都在这一层装完（都必须在切用户之前）：
#   curl   —— HEALTHCHECK 用，aspnet 基础镜像默认不带
#   tzdata —— aspnet 基础镜像是 debian-slim，**不带时区库**。不装的话下面那句
#             ln -snf 只会建出一个断链，TZ 静默失效（时间仍按 UTC 走）
#   gosu   —— 入口脚本用它从 root 降权到 app
# DEBIAN_FRONTEND 必须设：tzdata 会弹交互式提问，非交互构建下不设会卡住构建。
ARG DEBIAN_FRONTEND=noninteractive
RUN apt-get update \
 && apt-get install -y --no-install-recommends curl tzdata gosu \
 && rm -rf /var/lib/apt/lists/*

WORKDIR /app

# 时区（短剧更新时间的展示依赖）
ENV TZ=Asia/Shanghai
RUN ln -snf /usr/share/zoneinfo/$TZ /etc/localtime && echo $TZ > /etc/timezone

# 运行期用户：直接用**镜像内置**的非 root 用户 app。
#
# ⚠️ 不要自己 groupadd / useradd 建一个叫 app 的用户。.NET 8+ 的官方 aspnet / runtime 镜像
#    已经内置了 app 用户与 app 组（UID/GID 1654）。`groupadd -g 10001 app` 会以
#    **exit 9（组名已存在）** 失败，而 Docker 只打印一行
#    "did not complete successfully: exit code: 9" —— 完全看不出是"名字撞了"。
#    （这正是 skill 里那条「官方镜像通常已经备好非 root 用户，直接用，别自己建」的实例。）
#
# 入口脚本按**用户名** app 操作（chown app:app / gosu app），所以不需要知道具体 UID；
# 内置用户的 UID 若将来变化，脚本也不用改。
#
# 顺带把 HOME 建出来并交给 app：官方镜像是 --no-create-home 建的 app 用户，
# 没有可写 HOME 时，任何写 ~/.cache、~/.aspnet 的组件都会失败（见 skill 1.3.2）。
RUN mkdir -p /data /home/app \
 && chown app:app /data /home/app

COPY --from=api --chown=app:app /app/publish .

# 入口脚本先修正 /data 属主（兼容升级前 root 属主的旧数据卷），再降权运行。
# --chmod=755 必须显式写：Windows 上 git 不保留可执行位，否则容器起来就
# permission denied: /usr/local/bin/entrypoint.sh。
COPY --chown=app:app --chmod=755 docker/entrypoint.sh /usr/local/bin/entrypoint.sh

# 构建期自检：把「权限没配对」这类问题在**构建时**就暴露出来，
# 而不是等容器起来才在运行时报 EACCES（那时排查成本高得多，也容易误判成代码问题）。
# 三条分别覆盖：内置用户是否存在、入口脚本可执行位、数据目录属主。
# 按**用户名**比较而不是数字 UID —— 内置用户的 UID 由基础镜像决定，不该在这里写死。
RUN id app >/dev/null \
 && test -x /usr/local/bin/entrypoint.sh \
 && test "$(stat -c '%U:%G' /data)" = "app:app" \
 && echo "自检通过：app 用户存在、入口脚本可执行、/data 属主为 app:app"

# 数据目录（SQLite 库文件落在这里，compose 里挂成卷）
# 命名卷**首次创建且为空**时会继承这里的属主；已存在的旧卷不会 —— 那由入口脚本兜底。
VOLUME ["/data"]

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://+:8080 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_NOLOGO=true \
    HOME=/home/app \
    Database__Provider=Sqlite \
    ConnectionStrings__Default="Data Source=/data/shortdrama.db"

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=40s --retries=3 \
    CMD curl -fsS http://localhost:8080/health || exit 1

# 入口脚本以 root 起步做一次 chown，随后 exec 降权到 app（细节见脚本内注释）。
# 想完全跳过这一步：在 compose 里设 user: "app"，
# 此时脚本以 app 身份启动，会自动跳过降权分支（前提是卷属主已经对）。
ENTRYPOINT ["/usr/local/bin/entrypoint.sh"]
CMD ["dotnet", "ShortDrama.Api.dll"]
