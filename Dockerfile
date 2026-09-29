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

# curl 供 HEALTHCHECK 使用（aspnet 基础镜像默认不带）
RUN apt-get update \
 && apt-get install -y --no-install-recommends curl \
 && rm -rf /var/lib/apt/lists/*

WORKDIR /app

# 时区（短剧更新时间的展示依赖）
ENV TZ=Asia/Shanghai
RUN ln -snf /usr/share/zoneinfo/$TZ /etc/localtime && echo $TZ > /etc/timezone

COPY --from=api /app/publish .

# 数据目录（SQLite 库文件落在这里，compose 里挂成卷）
RUN mkdir -p /data
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

ENTRYPOINT ["dotnet", "ShortDrama.Api.dll"]
