# ============================================================================
# 短剧聚合平台 —— 单镜像多阶段构建
#
# 前端（Vue3 + Vite）与后端（.NET 10）打进同一个镜像，由 .NET 直接托管静态资源，
# 不需要额外的 Nginx 容器。这样 docker compose up 一条命令就能跑起来。
#
# 构建：docker build -t shortdrama .
# 运行：docker run -d -p 8080:8080 -v shortdrama-data:/data shortdrama
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

# 运行期用户：业务进程不该以 root 跑。UID/GID 固定，便于与宿主对齐。
ARG UID=10001
ARG GID=10001
RUN groupadd -g "${GID}" app \
 && useradd -m -u "${UID}" -g app -s /usr/sbin/nologin app \
 && install -d -o app -g app /data

COPY --from=api --chown=app:app /app/publish .

# 入口脚本先修正 /data 属主（兼容升级前 root 属主的旧数据卷），再降权运行。
# --chmod=755 必须显式写：Windows 上 git 不保留可执行位，否则容器起来就
# permission denied: /usr/local/bin/entrypoint.sh。
COPY --chown=app:app --chmod=755 docker/entrypoint.sh /usr/local/bin/entrypoint.sh

# 数据目录（SQLite 库文件落在这里，compose 里挂成卷）
VOLUME ["/data"]

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://+:8080 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_NOLOGO=true \
    Database__Provider=Sqlite \
    ConnectionStrings__Default="Data Source=/data/shortdrama.db"

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=40s --retries=3 \
    CMD curl -fsS http://localhost:8080/health || exit 1

# 入口脚本以 root 起步做一次 chown，随后 exec 降权到 app（细节见脚本内注释）。
# 想完全跳过这一步：在 compose 里设 user: "10001:10001"，
# 此时脚本以 app 身份启动，会自动跳过降权分支（前提是卷属主已经对）。
ENTRYPOINT ["/usr/local/bin/entrypoint.sh"]
CMD ["dotnet", "ShortDrama.Api.dll"]
