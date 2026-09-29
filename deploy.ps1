<#
.SYNOPSIS
    短剧聚合平台 —— 一键部署（Windows / PowerShell）

.DESCRIPTION
    检查 Docker 环境 → 生成 .env（含随机 JWT 密钥）→ 构建镜像 → 启动 → 等待健康检查 → 输出访问信息

.PARAMETER Postgres
    使用 PostgreSQL 而非默认的 SQLite（会额外拉起 postgres 与 redis）

.PARAMETER Port
    对外端口，默认 8080

.PARAMETER Rebuild
    强制重新构建镜像（不使用缓存）

.PARAMETER Down
    停止并移除容器（保留数据卷）

.PARAMETER Purge
    停止并移除容器，同时删除数据卷（会清空所有数据）

.EXAMPLE
    .\deploy.ps1
    .\deploy.ps1 -Port 80
    .\deploy.ps1 -Postgres
    .\deploy.ps1 -Down
#>
[CmdletBinding()]
param(
    [switch]$Postgres,
    [int]$Port = 8080,
    [switch]$Rebuild,
    [switch]$Down,
    [switch]$Purge
)

$ErrorActionPreference = 'Stop'
Set-Location -Path $PSScriptRoot

# ---------------------------------------------------------------- 输出工具
function Write-Step($msg) { Write-Host "`n▶ $msg" -ForegroundColor Cyan }
function Write-Ok($msg)   { Write-Host "  ✓ $msg" -ForegroundColor Green }
function Write-Warn($msg) { Write-Host "  ! $msg" -ForegroundColor Yellow }
function Write-Err($msg)  { Write-Host "  ✗ $msg" -ForegroundColor Red }

Write-Host @"

  ┌────────────────────────────────────────────┐
  │        短剧聚合平台 · 一键部署             │
  └────────────────────────────────────────────┘
"@ -ForegroundColor Magenta

# ---------------------------------------------------------------- 1. 环境检查
Write-Step '检查 Docker 环境'

$docker = Get-Command docker -ErrorAction SilentlyContinue
if (-not $docker) {
    Write-Err '未找到 docker 命令'
    Write-Host @"

  请先安装 Docker Desktop：
    https://www.docker.com/products/docker-desktop/

  安装后重启终端，再运行本脚本。

"@ -ForegroundColor Yellow
    exit 1
}
Write-Ok "docker 已安装：$($docker.Source)"

# 守护进程是否在跑
& docker info *> $null
if ($LASTEXITCODE -ne 0) {
    Write-Err 'Docker 守护进程未运行'
    Write-Host "`n  请先启动 Docker Desktop，等待托盘图标变为运行中，再重试。`n" -ForegroundColor Yellow
    exit 1
}
Write-Ok 'Docker 守护进程运行中'

# compose v2
& docker compose version *> $null
if ($LASTEXITCODE -ne 0) {
    Write-Err '未找到 docker compose（v2）'
    Write-Host "`n  本脚本依赖 Docker Compose V2（docker compose 子命令）。`n" -ForegroundColor Yellow
    exit 1
}
$composeVersion = (& docker compose version --short) 2>$null
Write-Ok "docker compose v$composeVersion"

# ---------------------------------------------------------------- 2. 停止/清理
$composeFiles = @('-f', 'docker-compose.yml')
if ($Postgres) { $composeFiles += @('-f', 'docker-compose.postgres.yml') }

if ($Down -or $Purge) {
    Write-Step '停止服务'
    if ($Purge) {
        Write-Warn '将删除数据卷，所有数据会丢失'
        & docker compose @composeFiles down -v
    } else {
        & docker compose @composeFiles down
    }
    Write-Ok '已停止'
    exit 0
}

# ---------------------------------------------------------------- 3. 生成 .env
Write-Step '准备环境变量'

if (-not (Test-Path '.env')) {
    if (Test-Path '.env.example') {
        Copy-Item '.env.example' '.env'
        Write-Ok '已从 .env.example 创建 .env'
    } else {
        New-Item -ItemType File -Path '.env' -Force | Out-Null
        Write-Ok '已创建 .env'
    }
} else {
    Write-Ok '.env 已存在，沿用现有配置'
}

# 若 JWT 密钥仍是默认值，自动替换为随机值
$envContent = Get-Content '.env' -Raw -Encoding UTF8
if ($envContent -match 'JWT_KEY=please-change-this-secret-key-in-production-2025') {
    $randomKey = -join ((1..48) | ForEach-Object { '{0:x}' -f (Get-Random -Minimum 0 -Maximum 16) })
    $envContent = $envContent -replace 'JWT_KEY=please-change-this-secret-key-in-production-2025', "JWT_KEY=$randomKey"
    Set-Content '.env' -Value $envContent -Encoding UTF8 -NoNewline
    Write-Ok '已生成随机 JWT 密钥'
} else {
    Write-Ok 'JWT 密钥已自定义'
}

# 端口覆盖
if ($Port -ne 8080) {
    $envContent = Get-Content '.env' -Raw -Encoding UTF8
    if ($envContent -match 'APP_PORT=\d+') {
        $envContent = $envContent -replace 'APP_PORT=\d+', "APP_PORT=$Port"
    } else {
        $envContent = "APP_PORT=$Port`n$envContent"
    }
    Set-Content '.env' -Value $envContent -Encoding UTF8 -NoNewline
    Write-Ok "端口设为 $Port"
}

# 读取最终配置用于展示
$finalEnv = @{}
Get-Content '.env' -Encoding UTF8 | Where-Object { $_ -match '^\s*[^#].*=' } | ForEach-Object {
    $k, $v = $_ -split '=', 2
    $finalEnv[$k.Trim()] = $v.Trim()
}
$accessPassword = if ($finalEnv['ACCESS_PASSWORD']) { $finalEnv['ACCESS_PASSWORD'] } else { '遵纪守法世界和平' }
$gateEnabled    = if ($finalEnv['ACCESS_GATE_ENABLED']) { $finalEnv['ACCESS_GATE_ENABLED'] } else { 'true' }

# ---------------------------------------------------------------- 4. 构建
Write-Step '构建镜像（首次构建约 3-5 分钟，需要下载基础镜像）'

$buildArgs = @('compose') + $composeFiles + @('build')
if ($Rebuild) { $buildArgs += '--no-cache' }

& docker @buildArgs
if ($LASTEXITCODE -ne 0) {
    Write-Err '镜像构建失败'
    Write-Host "`n  常见原因：`n    - 网络无法访问 Docker Hub / NuGet / npm registry`n    - 磁盘空间不足`n" -ForegroundColor Yellow
    exit 1
}
Write-Ok '镜像构建完成'

# ---------------------------------------------------------------- 5. 启动
Write-Step '启动服务'

& docker compose @composeFiles up -d
if ($LASTEXITCODE -ne 0) {
    Write-Err '启动失败'
    exit 1
}
Write-Ok '容器已启动'

# ---------------------------------------------------------------- 6. 等待健康
Write-Step '等待服务就绪'

$url = "http://localhost:$Port"
$ready = $false
$deadline = (Get-Date).AddSeconds(120)

while ((Get-Date) -lt $deadline) {
    try {
        $resp = Invoke-WebRequest "$url/health" -UseBasicParsing -TimeoutSec 5
        if ($resp.StatusCode -eq 200) { $ready = $true; break }
    } catch {
        Start-Sleep -Seconds 3
    }
}

if ($ready) {
    Write-Ok '服务已就绪'
} else {
    Write-Warn '健康检查超时，服务可能仍在初始化（首次启动要同步数据源）'
    Write-Host "`n  查看日志： docker compose logs -f app`n" -ForegroundColor Yellow
}

# ---------------------------------------------------------------- 7. 完成
Write-Host @"

  ╔════════════════════════════════════════════╗
  ║  部署完成                                  ║
  ╚════════════════════════════════════════════╝

  访问地址   $url
"@ -ForegroundColor Green

if ($gateEnabled -eq 'true') {
    Write-Host "  访问口令   $accessPassword" -ForegroundColor Yellow
    Write-Host "             （可在 .env 里改 ACCESS_PASSWORD，改完 docker compose up -d 生效）" -ForegroundColor DarkGray
}

$mode = if ($Postgres) { 'PostgreSQL' } else { 'SQLite（数据卷 shortdrama-data）' }
Write-Host @"

  数据库     $mode
  管理后台   $url/admin      （默认账号 admin / admin123）

  常用命令
    docker compose logs -f app        查看日志
    docker compose restart app        重启
    .\deploy.ps1 -Down                停止（保留数据）
    .\deploy.ps1 -Purge               停止并清空数据

"@ -ForegroundColor Gray
