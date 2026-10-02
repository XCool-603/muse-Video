#!/bin/sh
# ============================================================================
# 容器入口：修正数据目录属主 → 降权运行。
#
# 为什么需要这一步：
#   /data 是数据卷。数据卷在**首次创建且为空**时才会继承镜像里该路径的属主，
#   而**已经存在的旧卷不会**。旧版本容器以 root 运行，卷里的 SQLite 文件属主
#   是 root；直接切成非 root 会写不进去（EACCES），表现就是升级后站点起不来。
#   所以这里以 root 做一次 chown，再 exec 降权。
#
# 设计成失败也不影响可用性：
#   拿不到 gosu、或 chown 失败时，退回以 root 继续运行（等于改动前的行为），
#   只在 stderr 告警。宁可少一层隔离，也不能让容器起不来。
# ============================================================================
set -eu

APP_USER=app
DATA_DIR=/data

if [ "$(id -u)" = "0" ]; then
    if [ -d "$DATA_DIR" ]; then
        chown -R "$APP_USER:$APP_USER" "$DATA_DIR" 2>/dev/null \
            || echo "警告：$DATA_DIR 属主修正失败，将以 root 继续运行" >&2
    fi

    if command -v gosu >/dev/null 2>&1; then
        exec gosu "$APP_USER" "$@"
    fi

    echo "警告：未找到 gosu，无法降权，以 root 继续运行" >&2
fi

# 末尾必须是 exec "$@"：否则 PID 1 是 shell，SIGTERM 到不了 dotnet，
# docker stop 要等超时才杀得掉。
exec "$@"
