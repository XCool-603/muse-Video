#!/usr/bin/env bash
# ============================================================================
# 短剧聚合平台 —— 一键部署（Linux / macOS）
#
#   ./deploy.sh                  默认 SQLite，端口 18080
#   ./deploy.sh -p 80            指定端口
#   ./deploy.sh --postgres       使用 PostgreSQL
#   ./deploy.sh --rebuild        强制重建（不用缓存）
#   ./deploy.sh --update         拉取最新代码 → 重建镜像 → 重启（自动更新）
#   ./deploy.sh --check          只检查有没有新版本，不动容器
#   ./deploy.sh --install-cron [HH:MM]   安装定时自动更新，默认 04:00
#   ./deploy.sh --uninstall-cron 移除定时自动更新
#   ./deploy.sh --down           停止（保留数据）
#   ./deploy.sh --purge          停止并删除数据卷（清空数据）
# ============================================================================
set -euo pipefail

cd "$(dirname "$0")"
ROOT="$(pwd)"

# ---------------------------------------------------------------- 默认值
PORT=18080
USE_POSTGRES=0
REBUILD=0
ACTION="up"
CRON_AT="04:00"

UPDATED_FROM=""
UPDATED_TO=""
GIT_BRANCH=""
GIT_REMOTE=""
GIT_UPSTREAM=""
URL=""
ACCESS_PASSWORD="遵纪守法世界和平"
GATE_ENABLED="true"
# 部署模式：source = 本地构建；image = 拉 GHCR 预构建镜像
DEPLOY_MODE="source"
DEPLOY_IMAGE=""

# 定时自动更新写入 crontab 的标记，卸载时按它整行删除
CRON_MARK="# shortdrama-auto-update"
LOG_DIR="$ROOT/logs"
LOG_FILE="$LOG_DIR/auto-update.log"
# 更新锁放临时目录，避免污染工作区、也避免把仓库弄成「有未提交修改」
LOCK_FILE="${TMPDIR:-/tmp}/shortdrama-deploy.lock"

# --check 的退出码，方便接监控/通知
EXIT_UP_TO_DATE=0
EXIT_UPDATE_AVAILABLE=10

# 联网 git 操作的超时秒数（见 git_net）
GIT_TIMEOUT=300

# ---------------------------------------------------------------- 颜色与输出
# 输出重定向到文件（例如定时更新的日志）时不带颜色码，日志才干净可读
if [[ -t 1 ]]; then
    C_RESET='\033[0m'; C_CYAN='\033[36m'; C_GREEN='\033[32m'
    C_YELLOW='\033[33m'; C_RED='\033[31m'; C_MAGENTA='\033[35m'; C_GRAY='\033[90m'
else
    C_RESET=''; C_CYAN=''; C_GREEN=''; C_YELLOW=''; C_RED=''; C_MAGENTA=''; C_GRAY=''
fi

step() { printf "\n${C_CYAN}▶ %s${C_RESET}\n" "$1"; }
ok()   { printf "  ${C_GREEN}✓ %s${C_RESET}\n" "$1"; }
warn() { printf "  ${C_YELLOW}! %s${C_RESET}\n" "$1"; }
err()  { printf "  ${C_RED}✗ %s${C_RESET}\n" "$1"; }

# ============================================================================
# 自动更新
# ============================================================================

# 联网的 git 操作（fetch / pull）加超时。
# 无人值守时这一点很关键：git fetch 卡住会一直占着更新锁，
# 后续每一次定时更新都会「跳过」，自动更新等于静默停摆。
git_net() {
    # 用 --version 确认拿到的是 GNU coreutils 的 timeout：
    # Windows 自带的 timeout.exe 同名但完全不同（它不执行命令），误用会直接报错
    if timeout --version >/dev/null 2>&1; then
        timeout "$GIT_TIMEOUT" git "$@"
    else
        git "$@"
    fi
}

# 拉取远端状态并与本地比较。只读，不改动工作区。
#   返回 0  = 有新版本（UPDATED_FROM / UPDATED_TO 已填好）
#   返回 10 = 已是最新，无需更新
#   返回 1  = 失败（原因已打印）
git_check_update() {
    if ! command -v git >/dev/null 2>&1; then
        err '未找到 git 命令，无法自动更新'
        err '请安装 git，或改用「下载源码 + ./deploy.sh --rebuild」的方式'
        return 1
    fi

    if ! git rev-parse --is-inside-work-tree >/dev/null 2>&1; then
        err "当前目录不是 git 仓库：$ROOT"
        err 'ZIP 下载的源码没有版本信息，无法自动更新'
        return 1
    fi

    GIT_BRANCH="$(git rev-parse --abbrev-ref HEAD)"
    if [[ "$GIT_BRANCH" == "HEAD" ]]; then
        err '当前处于游离 HEAD 状态，无法自动更新'
        return 1
    fi

    GIT_REMOTE="$(git config --get "branch.$GIT_BRANCH.remote" || true)"
    GIT_REMOTE="${GIT_REMOTE:-origin}"
    GIT_UPSTREAM="$GIT_REMOTE/$GIT_BRANCH"

    step "检查更新（分支 $GIT_BRANCH）"

    if ! git_net fetch --prune --quiet "$GIT_REMOTE"; then
        err "无法访问远端 $GIT_REMOTE（超过 ${GIT_TIMEOUT} 秒无响应，或认证失败）"
        cat <<'EOF'

  常见原因：
    - 远端是 SSH 地址（git@github.com:...），但定时任务环境里没有对应私钥
      → 换成 HTTPS 地址，或给该用户配置可用的 SSH key
    - 仓库属主与执行用户不一致，git 出于安全拒绝操作
      → git config --global --add safe.directory "<仓库绝对路径>"
    - 网络不通 / 端口被墙：SSH 的 22 端口经常被挡
      → 改用 HTTPS 地址

EOF
        return 1
    fi

    local local_sha remote_sha
    local_sha="$(git rev-parse HEAD)"
    if ! remote_sha="$(git rev-parse --verify --quiet "$GIT_UPSTREAM")"; then
        err "远端没有分支 $GIT_UPSTREAM"
        return 1
    fi

    if [[ "$local_sha" == "$remote_sha" ]]; then
        ok "已是最新版本（$(git rev-parse --short HEAD) · $(git log -1 --format=%cd --date=format:'%Y-%m-%d %H:%M')）"
        return 10
    fi

    # 本地有远端没有的提交 → 分叉。自动更新绝不覆盖，交给用户决定。
    if ! git merge-base --is-ancestor "$local_sha" "$remote_sha"; then
        err '本地分支与远端已分叉，自动更新不会覆盖本地提交'
        err "请手动处理： git pull --rebase $GIT_REMOTE $GIT_BRANCH"
        return 1
    fi

    UPDATED_FROM="$(git rev-parse --short HEAD)"
    UPDATED_TO="$(git rev-parse --short "$GIT_UPSTREAM")"
    return 0
}

# 快进拉取最新代码（--update 用）。
#   返回 0 = 已更新；10 = 已是最新；1 = 失败
git_pull_latest() {
    local rc=0
    git_check_update || rc=$?

    if [[ "$rc" -ne 0 ]]; then
        return "$rc"
    fi

    # 有未提交的修改时不拉取：既避免 pull 冲突，也避免把半成品代码构建进镜像
    if [[ -n "$(git status --porcelain --untracked-files=no)" ]]; then
        err '工作区有未提交的修改，自动更新不会覆盖它们'
        err '请先 git stash 或 git commit，再重试'
        return 1
    fi

    step '拉取最新代码'
    if ! git_net pull --ff-only --quiet "$GIT_REMOTE" "$GIT_BRANCH"; then
        err '拉取失败'
        return 1
    fi

    UPDATED_TO="$(git rev-parse --short HEAD)"
    ok "代码已更新：$UPDATED_FROM → $UPDATED_TO"
    return 0
}

# 安装 / 移除定时自动更新（crontab）
# 用单引号包住路径，cron 执行时才能正确还原带空格/特殊字符的目录名。
# 不用 printf %q：这里只需要处理单引号一种情况，单引号包裹更直观、也不挑 bash 版本。
shell_quote() {
    printf "'%s'" "$(printf '%s' "$1" | sed "s/'/'\\\\''/g")"
}

# PATH 必须写进去：cron 的默认 PATH 很短，通常找不到 docker / git
cron_line() {
    printf '%s %s * * * cd %s && PATH=%s bash ./deploy.sh --update >> %s 2>&1 %s' \
        "$1" "$2" "$(shell_quote "$ROOT")" "$(shell_quote "$PATH")" \
        "$(shell_quote "$LOG_FILE")" "$CRON_MARK"
}

install_cron() {
    local h m
    if [[ ! "$CRON_AT" =~ ^([0-9]{1,2}):([0-9]{2})$ ]]; then
        err "时间格式应为 HH:MM，收到：$CRON_AT"
        return 1
    fi
    # 10# 前缀按十进制解析，避免 08 / 09 被当成非法八进制
    h=$((10#${BASH_REMATCH[1]}))
    m=$((10#${BASH_REMATCH[2]}))
    if (( h > 23 )); then err "小时应在 0-23：$CRON_AT"; return 1; fi
    if (( m > 59 )); then err "分钟应在 0-59：$CRON_AT"; return 1; fi

    if ! command -v crontab >/dev/null 2>&1; then
        err '未找到 crontab（本机没有 cron）'
        err '可改用 systemd timer，或按 docs/DOCKER.md 里的「外部调度」方式'
        return 1
    fi

    mkdir -p "$LOG_DIR"

    local tmp
    tmp="$(mktemp)"
    # 先删掉旧条目，保证重复执行不会叠加
    crontab -l 2>/dev/null | grep -vF "$CRON_MARK" > "$tmp" || true
    cron_line "$m" "$h" >> "$tmp"

    if ! crontab "$tmp"; then
        rm -f "$tmp"
        err '写入 crontab 失败'
        return 1
    fi
    rm -f "$tmp"

    ok "已安装定时自动更新：每天 $CRON_AT"
    printf "  更新日志   %s\n" "$LOG_FILE"
    printf "${C_GRAY}  查看计划   crontab -l | grep shortdrama${C_RESET}\n"
}

uninstall_cron() {
    if ! command -v crontab >/dev/null 2>&1; then
        err '未找到 crontab'
        return 1
    fi

    local tmp
    tmp="$(mktemp)"
    crontab -l 2>/dev/null | grep -vF "$CRON_MARK" > "$tmp" || true
    crontab "$tmp"
    rm -f "$tmp"

    ok '已移除定时自动更新'
}

# ============================================================================
# 部署
# ============================================================================

# 读取 .env 里的变量值（文件不存在或变量未配置则返回空）
env_var() {
    if [[ -f "$ROOT/.env" ]]; then
        grep -E "^$1=" "$ROOT/.env" 2>/dev/null | head -1 | cut -d= -f2- || true
    fi
}

prepare_env() {
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
        local random_key
        if command -v openssl >/dev/null 2>&1; then
            random_key="$(openssl rand -hex 48)"
        else
            random_key="$(head -c 48 /dev/urandom | od -An -tx1 | tr -d ' \n')"
        fi
        # 用 | 作分隔符，避免密钥里的 / 影响 sed
        sed -i.bak "s|JWT_KEY=please-change-this-secret-key-in-production-2025|JWT_KEY=${random_key}|" .env
        rm -f .env.bak
        ok '已生成随机 JWT 密钥'
    else
        ok 'JWT 密钥已自定义'
    fi

    # 端口覆盖
    if [[ "$PORT" != "18080" ]]; then
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
    URL="http://localhost:${PORT}"

    # 部署模式：.env 里配了 SHORTDRAMA_IMAGE 就走镜像模式。
    # 环境变量优先于 .env，与 docker compose 的取值规则一致。
    DEPLOY_IMAGE="${SHORTDRAMA_IMAGE:-$(env_var SHORTDRAMA_IMAGE)}"
    if [[ -n "$DEPLOY_IMAGE" ]]; then
        DEPLOY_MODE="image"
        ok "镜像模式：$DEPLOY_IMAGE"
    else
        DEPLOY_MODE="source"
    fi
}

build_image() {
    if [[ "$DEPLOY_MODE" == "image" ]]; then
        err '当前是镜像模式，不会在本机构建'
        err '要改回本地构建，把 .env 里的 SHORTDRAMA_IMAGE 清空即可'
        return 1
    fi

    step '构建镜像（首次构建约 3-5 分钟，需要下载基础镜像）'

    local args=(compose "${COMPOSE_FILES[@]}" build)
    if [[ "$REBUILD" -eq 1 ]]; then
        args+=(--no-cache)
    fi

    if ! docker "${args[@]}"; then
        err '镜像构建失败'
        cat <<'EOF'

  常见原因：
    - 网络无法访问 Docker Hub / NuGet / npm registry（国内可配置镜像加速）
    - 磁盘空间不足

EOF
        return 1
    fi
    ok '镜像构建完成'
}

start_services() {
    step '启动服务'

    # 镜像模式下加 --no-build：compose 文件里仍有 build 段，
    # 不加这个参数时 docker compose 可能在本机重新构建
    local args=(compose "${COMPOSE_FILES[@]}" up -d)
    if [[ "$DEPLOY_MODE" == "image" ]]; then
        args+=(--no-build)
    fi

    if ! docker "${args[@]}"; then
        err '启动失败'
        return 1
    fi
    ok '容器已启动'
}

# 镜像模式：拉取预构建镜像。返回 0 = 有新镜像，10 = 已是最新，1 = 失败
pull_image() {
    step "拉取预构建镜像（$DEPLOY_IMAGE）"

    local before after
    before="$(docker image inspect --format '{{.Id}}' "$DEPLOY_IMAGE" 2>/dev/null || true)"

    if ! docker compose "${COMPOSE_FILES[@]}" pull app; then
        err '镜像拉取失败'
        err '若是私有包，需要先登录： docker login ghcr.io'
        return 1
    fi

    after="$(docker image inspect --format '{{.Id}}' "$DEPLOY_IMAGE" 2>/dev/null || true)"

    if [[ -n "$before" && "$before" == "$after" ]]; then
        ok '镜像已是最新'
        return 10
    fi

    [[ -n "$before" ]] && ok '已拉到新镜像'
    return 0
}

wait_health() {
    if ! command -v curl >/dev/null 2>&1; then
        warn '未找到 curl，跳过健康检查'
        return 0
    fi

    step '等待服务就绪'

    local i
    for i in $(seq 1 40); do
        if curl -fsS "${URL}/health" >/dev/null 2>&1; then
            ok '服务已就绪'
            return 0
        fi
        sleep 3
    done

    warn '健康检查超时，服务可能仍在初始化（首次启动要同步数据源）'
    printf "\n  查看日志： docker compose logs -f app\n\n"
    return 1
}

print_summary() {
    local mode='SQLite（数据卷 shortdrama-data）'
    if [[ "$USE_POSTGRES" -eq 1 ]]; then
        mode='PostgreSQL'
    fi

    local deploy='源码模式（本机构建）'
    if [[ "$DEPLOY_MODE" == "image" ]]; then
        deploy="镜像模式（$DEPLOY_IMAGE）"
    fi

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

    printf "\n  部署方式   %s\n" "$deploy"
    printf "  数据库     %s\n" "$mode"
    printf "  管理后台   %s/admin      （默认账号 admin / admin123）\n" "$URL"

    if [[ -n "$UPDATED_FROM" ]]; then
        printf "\n  版本变更   %s → %s\n" "$UPDATED_FROM" "$UPDATED_TO"
        printf "${C_GRAY}  回滚       git reset --hard %s && ./deploy.sh --rebuild${C_RESET}\n" "$UPDATED_FROM"
    fi

    printf "\n${C_GRAY}  自动更新
    ./deploy.sh --update              立即更新到最新版本
    ./deploy.sh --check               只检查有没有新版本（源码模式）
    ./deploy.sh --install-cron        安装每天 04:00 定时自动更新

  常用命令
    docker compose logs -f app        查看日志
    docker compose restart app        重启
    ./deploy.sh --down                停止（保留数据）
    ./deploy.sh --purge               停止并清空数据
${C_RESET}\n"
}

# ============================================================================
# 入口
# ============================================================================

printf "${C_MAGENTA}
  ┌────────────────────────────────────────────┐
  │        短剧聚合平台 · 一键部署             │
  └────────────────────────────────────────────┘
${C_RESET}"

# ---------------------------------------------------------------- 参数解析
while [[ $# -gt 0 ]]; do
    case "$1" in
        -p|--port)      PORT="${2:?--port 需要一个端口号}"; shift 2 ;;
        --postgres)     USE_POSTGRES=1; shift ;;
        --rebuild)      REBUILD=1; shift ;;
        --update)       ACTION="update"; shift ;;
        --check)        ACTION="check"; shift ;;
        --install-cron)
            ACTION="install-cron"; shift
            if [[ $# -gt 0 && "$1" =~ ^[0-9]{1,2}:[0-9]{2}$ ]]; then
                CRON_AT="$1"; shift
            fi ;;
        --uninstall-cron) ACTION="uninstall-cron"; shift ;;
        --down)         ACTION="down"; shift ;;
        --purge)        ACTION="purge"; shift ;;
        -h|--help)      sed -n '2,15p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
        *) err "未知参数：$1（用 -h 查看帮助）"; exit 1 ;;
    esac
done

# ---------------------------------------------------------------- 定时自动更新
# 不依赖 Docker，所以放在环境检查之前
if [[ "$ACTION" == "install-cron" ]]; then
    install_cron
    exit $?
fi

if [[ "$ACTION" == "uninstall-cron" ]]; then
    uninstall_cron
    exit $?
fi

# ---------------------------------------------------------------- 只检查更新
# 也不依赖 Docker，可在没跑容器的机器上用来做版本巡检
if [[ "$ACTION" == "check" ]]; then
    # 镜像模式不看 git：版本由镜像标签决定，直接 --update 拉一次即可
    check_image="${SHORTDRAMA_IMAGE:-$(env_var SHORTDRAMA_IMAGE)}"
    if [[ -n "$check_image" ]]; then
        warn '当前是镜像模式（.env 里配了 SHORTDRAMA_IMAGE）'
        printf "${C_GRAY}  镜像模式下直接执行 ./deploy.sh --update，它只做 docker pull，很快${C_RESET}\n\n"
        exit "$EXIT_UP_TO_DATE"
    fi

    rc=0
    git_check_update || rc=$?

    if [[ "$rc" -eq 10 ]]; then
        printf "\n${C_GREEN}  当前已是最新版本${C_RESET}\n\n"
        exit "$EXIT_UP_TO_DATE"
    fi
    if [[ "$rc" -ne 0 ]]; then
        exit 1
    fi

    if [[ -n "$(git status --porcelain --untracked-files=no)" ]]; then
        warn '工作区有未提交的修改，--update 会拒绝执行'
    fi

    printf "\n${C_YELLOW}  发现新版本：%s → %s${C_RESET}\n" "$UPDATED_FROM" "$UPDATED_TO"
    printf "${C_GRAY}  执行 ./deploy.sh --update 即可更新${C_RESET}\n\n"
    exit "$EXIT_UPDATE_AVAILABLE"
fi

# ---------------------------------------------------------------- 环境检查
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

# ---------------------------------------------------------------- 组装 compose 参数
COMPOSE_FILES=(-f docker-compose.yml)
if [[ "$USE_POSTGRES" -eq 1 ]]; then
    COMPOSE_FILES+=(-f docker-compose.postgres.yml)
fi

# ---------------------------------------------------------------- 停止/清理
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

# ---------------------------------------------------------------- 自动更新
if [[ "$ACTION" == "update" ]]; then
    # 防止定时任务与手动更新叠在一起跑
    if command -v flock >/dev/null 2>&1; then
        exec 9>"$LOCK_FILE"
        if ! flock -n 9; then
            warn '另一次部署/更新正在进行，本次跳过'
            exit 0
        fi
    fi

    # 先读配置确定部署模式（prepare_env 只动 .env，不构建）
    prepare_env

    if [[ "$DEPLOY_MODE" == "image" ]]; then
        # ---- 镜像模式：只拉镜像，不在本机编译 ----
        rc=0
        pull_image || rc=$?

        if [[ "$rc" -eq 10 ]]; then
            printf "\n${C_GREEN}  已是最新版本，无需更新${C_RESET}\n\n"
            exit 0
        fi
        if [[ "$rc" -ne 0 ]]; then
            exit 1
        fi

        start_services || exit 1
        wait_health || true
        docker image prune -f >/dev/null 2>&1 || true
        print_summary
        exit 0
    fi

    # ---- 源码模式：拉代码 + 本地构建 ----
    rc=0
    git_pull_latest || rc=$?

    if [[ "$rc" -eq 10 ]]; then
        printf "\n${C_GREEN}  已是最新版本，无需更新${C_RESET}\n\n"
        exit 0
    fi
    if [[ "$rc" -ne 0 ]]; then
        exit 1
    fi

    # 先构建再切换：构建失败时旧容器仍在跑，站点不中断
    if ! build_image; then
        err '新版本构建失败，正在运行的旧版本未受影响'
        err "回滚代码： git reset --hard $UPDATED_FROM"
        exit 1
    fi

    start_services
    wait_health || true

    # 清掉被替换下来的旧镜像层（只删 dangling，不碰其他镜像）
    docker image prune -f >/dev/null 2>&1 || true

    print_summary
    exit 0
fi

# ---------------------------------------------------------------- 首次部署 / 重新部署
prepare_env

if [[ "$DEPLOY_MODE" == "image" ]]; then
    # 镜像模式：拉镜像后直接起，不编译
    rc=0
    pull_image || rc=$?
    if [[ "$rc" -ne 0 && "$rc" -ne 10 ]]; then
        exit 1
    fi
else
    build_image || exit 1
fi

start_services || exit 1
wait_health || true
print_summary
