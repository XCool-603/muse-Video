#!/usr/bin/env bash
# ============================================================================
# 短剧聚合平台 —— 一键部署（Linux / macOS）
#
#   ./deploy.sh                 默认 SQLite，端口 8080
#   ./deploy.sh -p 80           指定端口
#   ./deploy.sh --postgres      使用 PostgreSQL
#   ./deploy.sh --rebuild       强制重建（不用缓存）
#   ./deploy.sh --down          停止（保留数据）
#   ./deploy.sh --purge         停止并删除数据卷
# ============================================================================
set -euo pipefail

cd "$(dirname "$0")"

# ---------------------------------------------------------------- 默认值
PORT=8080
USE_POSTGRES=0
REBUILD=0
ACTION="up"

# ---------------------------------------------------------------- 参数解析
while [[ $# -gt 0 ]]; do
    case "$1" in
        -p|--port)      PORT="$2"; shift 2 ;;
        --postgres)     USE_POSTGRES=1; shift ;;
        --rebuild)      REBUILD=1; shift ;;
        --down)         ACTION="down"; shift ;;
        --purge)        ACTION="purge"; shift ;;
        -h|--help)
            sed -n '2,12p' "$0" | sed 's/^# \{0,1\}//'
            exit 0 ;;
        *) echo "未知参数：$1"; exit 1 ;;
    esac
done

# ---------------------------------------------------------------- 输出工具
C_RESET='\033[0m'; C_CYAN='\033[36m'; C_GREEN='\033[32m'
C_YELLOW='\033[33m'; C_RED='\033[31m'; C_MAGENTA='\033[35m'; C_GRAY='\033[90m'

step() { printf "\n${C_CYAN}▶ %s${C_RESET}\n" "$1"; }
ok()   { printf "  ${C_GREEN}✓ %s${C_RESET}\n" "$1"; }
warn() { printf "  ${C_YELLOW}! %s${C_RESET}\n" "$1"; }
err()  { printf "  ${C_RED}✗ %s${C_RESET}\n" "$1"; }

printf "${C_MAGENTA}
  ┌────────────────────────────────────────────┐
  │        短剧聚合平台 · 一键部署             │
  └────────────────────────────────────────────┘
${C_RESET}"

# ---------------------------------------------------------------- 1. 环境检查
step '检查 Docker 环境'

if ! command -v docker >/dev/null 2>&1; then
    err '未找到 docker 命令'
    cat <<'EOF'

  安装方式：
    Ubuntu/Debian  curl -fsSL https://get.docker.com | sh
    macOS          https://www.docker.com/products/docker-desktop/

EOF
    exit 1
fi
ok "docker 已安装：$(command -v docker)"

if ! docker info >/dev/null 2>&1; then
    err 'Docker 守护进程未运行，或当前用户无权限'
    cat <<'EOF'

  尝试：
    sudo systemctl start docker
    sudo usermod -aG docker $USER   # 加入 docker 组后需重新登录

EOF
    exit 1
fi
ok 'Docker 守护进程运行中'

if ! docker compose version >/dev/null 2>&1; then
    err '未找到 docker compose（v2）'
    echo
    exit 1
fi
ok "docker compose v$(docker compose version --short 2>/dev/null || echo '?')"

# ---------------------------------------------------------------- 2. 组装 compose 参数
COMPOSE_FILES=(-f docker-compose.yml)
if [[ "$USE_POSTGRES" -eq 1 ]]; then
    COMPOSE_FILES+=(-f docker-compose.postgres.yml)
fi

# ---------------------------------------------------------------- 3. 停止/清理
if [[ "$ACTION" == "down" || "$ACTION" == "purge" ]]; then
    step '停止服务'
    if [[ "$ACTION" == "purge" ]]; then
        warn '将删除数据卷，所有数据会丢失'
        docker compose "${COMPOSE_FILES[@]}" down -v
    else
        docker compose "${COMPOSE_FILES[@]}" down
    fi
    ok '已停止'
    exit 0
fi

# ---------------------------------------------------------------- 4. 生成 .env
step '准备环境变量'

if [[ ! -f .env ]]; then
    if [[ -f .env.example ]]; then
        cp .env.example .env
        ok '已从 .env.example 创建 .env'
    else
        touch .env
        ok '已创建 .env'
    fi
else
    ok '.env 已存在，沿用现有配置'
fi

# 默认 JWT 密钥 → 随机替换
if grep -q 'JWT_KEY=please-change-this-secret-key-in-production-2025' .env 2>/dev/null; then
    if command -v openssl >/dev/null 2>&1; then
        RANDOM_KEY="$(openssl rand -hex 48)"
    else
        RANDOM_KEY="$(head -c 48 /dev/urandom | od -An -tx1 | tr -d ' \n')"
    fi
    # 用 | 作分隔符，避免密钥里的 / 影响 sed
    sed -i.bak "s|JWT_KEY=please-change-this-secret-key-in-production-2025|JWT_KEY=${RANDOM_KEY}|" .env
    rm -f .env.bak
    ok '已生成随机 JWT 密钥'
else
    ok 'JWT 密钥已自定义'
fi

# 端口覆盖
if [[ "$PORT" != "8080" ]]; then
    if grep -q '^APP_PORT=' .env; then
        sed -i.bak "s|^APP_PORT=.*|APP_PORT=${PORT}|" .env
        rm -f .env.bak
    else
        printf 'APP_PORT=%s\n' "$PORT" >> .env
    fi
    ok "端口设为 $PORT"
fi

ACCESS_PASSWORD="$(grep -E '^ACCESS_PASSWORD=' .env | head -1 | cut -d= -f2- || true)"
ACCESS_PASSWORD="${ACCESS_PASSWORD:-遵纪守法世界和平}"
GATE_ENABLED="$(grep -E '^ACCESS_GATE_ENABLED=' .env | head -1 | cut -d= -f2- || true)"
GATE_ENABLED="${GATE_ENABLED:-true}"

# ---------------------------------------------------------------- 5. 构建
step '构建镜像（首次构建约 3-5 分钟，需要下载基础镜像）'

BUILD_ARGS=(compose "${COMPOSE_FILES[@]}" build)
if [[ "$REBUILD" -eq 1 ]]; then
    BUILD_ARGS+=(--no-cache)
fi

if ! docker "${BUILD_ARGS[@]}"; then
    err '镜像构建失败'
    cat <<'EOF'

  常见原因：
    - 网络无法访问 Docker Hub / NuGet / npm registry（国内可配置镜像加速）
    - 磁盘空间不足

EOF
    exit 1
fi
ok '镜像构建完成'

# ---------------------------------------------------------------- 6. 启动
step '启动服务'
docker compose "${COMPOSE_FILES[@]}" up -d
ok '容器已启动'

# ---------------------------------------------------------------- 7. 等待健康
step '等待服务就绪'

URL="http://localhost:${PORT}"
READY=0
for _ in $(seq 1 40); do
    if curl -fsS "${URL}/health" >/dev/null 2>&1; then
        READY=1
        break
    fi
    sleep 3
done

if [[ "$READY" -eq 1 ]]; then
    ok '服务已就绪'
else
    warn '健康检查超时，服务可能仍在初始化（首次启动要同步数据源）'
    printf "\n  查看日志： docker compose logs -f app\n\n"
fi

# ---------------------------------------------------------------- 8. 完成
printf "\n${C_GREEN}"
cat <<'EOF'
  ╔════════════════════════════════════════════╗
  ║  部署完成                                  ║
  ╚════════════════════════════════════════════╝
EOF
printf "${C_RESET}\n  访问地址   %s\n" "$URL"

if [[ "$GATE_ENABLED" == "true" ]]; then
    printf "${C_YELLOW}  访问口令   %s${C_RESET}\n" "$ACCESS_PASSWORD"
    printf "${C_GRAY}             （可在 .env 里改 ACCESS_PASSWORD，改完 docker compose up -d 生效）${C_RESET}\n"
fi

MODE='SQLite（数据卷 shortdrama-data）'
[[ "$USE_POSTGRES" -eq 1 ]] && MODE='PostgreSQL'

printf "\n  数据库     %s\n" "$MODE"
printf "  管理后台   %s/admin      （默认账号 admin / admin123）\n" "$URL"

printf "\n${C_GRAY}  常用命令
    docker compose logs -f app        查看日志
    docker compose restart app        重启
    ./deploy.sh --down                停止（保留数据）
    ./deploy.sh --purge               停止并清空数据
${C_RESET}\n"
