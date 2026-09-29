<#
.SYNOPSIS
    短剧聚合平台 —— 一键部署（Windows / PowerShell）

.DESCRIPTION
    检查 Docker 环境 → 生成 .env（含随机 JWT 密钥）→ 构建镜像 → 启动 → 等待健康检查 → 输出访问信息

    自动更新：
      -Update          拉取最新代码 → 重建镜像 → 重启
      -Check           只检查有没有新版本，不动容器
      -InstallTask     注册每天定时自动更新（Windows 计划任务）
      -UninstallTask   移除定时自动更新

.PARAMETER Postgres
    使用 PostgreSQL 而非默认的 SQLite（会额外拉起 postgres 与 redis）

.PARAMETER Port
    对外端口，默认 8080

.PARAMETER Rebuild
    强制重新构建镜像（不使用缓存）

.PARAMETER Update
    拉取最新代码并重建重启（自动更新）

.PARAMETER Check
    只检查有没有新版本，不更新；退出码 0 = 已最新，10 = 有新版本，1 = 失败

.PARAMETER InstallTask
    注册每天定时自动更新的计划任务

.PARAMETER TaskTime
    定时更新的时间，HH:mm，默认 04:00

.PARAMETER UninstallTask
    移除定时自动更新的计划任务

.PARAMETER Down
    停止并移除容器（保留数据卷）

.PARAMETER Purge
    停止并移除容器，同时删除数据卷（会清空所有数据）

.EXAMPLE
    .\deploy.ps1
    .\deploy.ps1 -Port 80
    .\deploy.ps1 -Postgres
    .\deploy.ps1 -Update
    .\deploy.ps1 -Check
    .\deploy.ps1 -InstallTask -TaskTime 03:30
    .\deploy.ps1 -UninstallTask
    .\deploy.ps1 -Down
#>
[CmdletBinding()]
param(
    [switch]$Postgres,
    [int]$Port = 8080,
    [switch]$Rebuild,
    [switch]$Update,
    [switch]$Check,
    [switch]$InstallTask,
    [string]$TaskTime = '04:00',
    [switch]$UninstallTask,
    [switch]$Down,
    [switch]$Purge
)

$ErrorActionPreference = 'Stop'
Set-Location -Path $PSScriptRoot
# Set-Location 只改 PowerShell 自己的位置，不改 .NET 的进程工作目录。
# 不同步的话，[System.IO.File] 这类 .NET API 的相对路径会落到「启动本脚本时所在的目录」，
# 而不是项目目录 —— 从别处用绝对路径调用本脚本时，.env 会被写到错误的位置。
[System.IO.Directory]::SetCurrentDirectory($PSScriptRoot)

# ---------------------------------------------------------------- 常量
$TaskName  = 'ShortDramaAutoUpdate'
$LogDir    = Join-Path $PSScriptRoot 'logs'
$LogFile   = Join-Path $LogDir 'auto-update.log'
$LockFile  = Join-Path $env:TEMP 'shortdrama-deploy.lock'
# -Check 的退出码，方便接监控/通知
$ExitUpToDate        = 0
$ExitUpdateAvailable = 10

$script:GitBranch  = ''
$script:GitRemote  = ''
$script:GitUpstream = ''
$script:UpdatedFrom = ''
$script:UpdatedTo   = ''
$script:AccessPassword = '遵纪守法世界和平'
$script:GateEnabled    = 'true'
$script:Url            = ''
# 步骤结果标记：docker 的输出会进入函数输出流，所以用变量而不是返回值传结果
$script:Ok             = $true
# 更新锁持有的文件句柄（见 Enter-DeployLock）
$script:LockStream     = $null

# ---------------------------------------------------------------- 输出工具
function Write-Step($msg) { Write-Host "`n▶ $msg" -ForegroundColor Cyan }
function Write-Ok($msg)   { Write-Host "  ✓ $msg" -ForegroundColor Green }
function Write-Warn($msg) { Write-Host "  ! $msg" -ForegroundColor Yellow }
function Write-Err($msg)  { Write-Host "  ✗ $msg" -ForegroundColor Red }

# 运行外部命令并同时拿到输出与退出码。
# 原生命令的 stderr 在 $ErrorActionPreference='Stop' 下会变成终止错误，所以这里临时降级。
function Invoke-Native {
    param([string]$Exe, [string[]]$Arguments)

    $prev = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & $Exe @Arguments 2>&1 | ForEach-Object { "$_" }
        $code = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $prev
    }

    return [pscustomobject]@{ Output = @($output); Code = $code }
}

# 取 git 命令输出的第一行有效内容；命令失败返回空串
function Get-GitLine {
    param([string[]]$Arguments)
    $r = Invoke-Native git $Arguments
    if ($r.Code -ne 0) { return '' }
    $line = $r.Output | Where-Object { $_ -ne '' } | Select-Object -First 1
    if ($null -eq $line) { return '' }
    return $line.Trim()
}

# .env 必须写成无 BOM 的 UTF-8：
# PS 5.1 的 Set-Content -Encoding UTF8 会写入 BOM，docker compose 解析 .env 时
# 会把 BOM 当成第一个变量名的一部分，导致第一行配置失效。
function Write-EnvFile {
    param([string]$Path, [string]$Content)
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $Content, $utf8NoBom)
}

# ============================================================================
# 自动更新
# ============================================================================

# 拉取远端状态并与本地比较。只读，不改动工作区。
#   返回 0  = 有新版本（$script:UpdatedFrom / $script:UpdatedTo 已填好）
#   返回 10 = 已是最新，无需更新
#   返回 1  = 失败（原因已打印）
function Test-GitUpdate {
    if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
        Write-Err '未找到 git 命令，无法自动更新'
        Write-Err '请安装 git，或改用「下载源码 + .\deploy.ps1 -Rebuild」的方式'
        return 1
    }

    $inside = Invoke-Native git @('rev-parse', '--is-inside-work-tree')
    if ($inside.Code -ne 0) {
        Write-Err "当前目录不是 git 仓库：$PSScriptRoot"
        Write-Err 'ZIP 下载的源码没有版本信息，无法自动更新'
        return 1
    }

    $script:GitBranch = Get-GitLine @('rev-parse', '--abbrev-ref', 'HEAD')
    if ([string]::IsNullOrWhiteSpace($script:GitBranch)) {
        Write-Err '无法确定当前分支，自动更新中止'
        return 1
    }
    if ($script:GitBranch -eq 'HEAD') {
        Write-Err '当前处于游离 HEAD 状态，无法自动更新'
        return 1
    }

    $script:GitRemote = Get-GitLine @('config', '--get', "branch.$($script:GitBranch).remote")
    if ([string]::IsNullOrWhiteSpace($script:GitRemote)) { $script:GitRemote = 'origin' }
    $script:GitUpstream = "$($script:GitRemote)/$($script:GitBranch)"

    Write-Step "检查更新（分支 $($script:GitBranch)）"

    $fetch = Invoke-Native git @('fetch', '--prune', '--quiet', $script:GitRemote)
    if ($fetch.Code -ne 0) {
        Write-Err "无法访问远端 $($script:GitRemote)"
        Write-Host @'

  常见原因：
    - 远端是 SSH 地址（git@github.com:...），但当前账号没有对应私钥
      → 换成 HTTPS 地址，或配置可用的 SSH key
    - 仓库属主与执行用户不一致，git 出于安全拒绝操作
      → git config --global --add safe.directory "<仓库绝对路径>"
    - 计划任务在未登录状态下运行，拿不到凭据管理器里的凭据
      → 保持「只在用户登录时运行」，或改用 HTTPS + PAT

'@ -ForegroundColor Yellow
        return 1
    }

    $localSha  = Get-GitLine @('rev-parse', 'HEAD')
    $remoteSha = Get-GitLine @('rev-parse', '--verify', '--quiet', $script:GitUpstream)
    if ([string]::IsNullOrWhiteSpace($remoteSha)) {
        Write-Err "远端没有分支 $($script:GitUpstream)"
        return 1
    }

    if ($localSha -eq $remoteSha) {
        $short = Get-GitLine @('rev-parse', '--short', 'HEAD')
        $when  = Get-GitLine @('log', '-1', '--format=%cd', '--date=format:%Y-%m-%d %H:%M')
        Write-Ok "已是最新版本（$short · $when）"
        return 10
    }

    # 本地有远端没有的提交 → 分叉。自动更新绝不覆盖，交给用户决定。
    $ancestor = Invoke-Native git @('merge-base', '--is-ancestor', $localSha, $remoteSha)
    if ($ancestor.Code -ne 0) {
        Write-Err '本地分支与远端已分叉，自动更新不会覆盖本地提交'
        Write-Err "请手动处理： git pull --rebase $($script:GitRemote) $($script:GitBranch)"
        return 1
    }

    $script:UpdatedFrom = Get-GitLine @('rev-parse', '--short', 'HEAD')
    $script:UpdatedTo   = Get-GitLine @('rev-parse', '--short', $script:GitUpstream)
    return 0
}

# 快进拉取最新代码（-Update 用）。
#   返回 0 = 已更新；10 = 已是最新；1 = 失败
function Update-FromGit {
    $rc = Test-GitUpdate
    if ($rc -ne 0) { return $rc }

    # 有未提交的修改时不拉取：既避免 pull 冲突，也避免把半成品代码构建进镜像
    $dirty = (Invoke-Native git @('status', '--porcelain', '--untracked-files=no')).Output |
             Where-Object { $_ -ne '' }
    if ($dirty) {
        Write-Err '工作区有未提交的修改，自动更新不会覆盖它们'
        Write-Err '请先 git stash 或 git commit，再重试'
        return 1
    }

    Write-Step '拉取最新代码'
    $pull = Invoke-Native git @('pull', '--ff-only', '--quiet', $script:GitRemote, $script:GitBranch)
    if ($pull.Code -ne 0) {
        Write-Err '拉取失败'
        $pull.Output | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray }
        return 1
    }

    $script:UpdatedTo = Get-GitLine @('rev-parse', '--short', 'HEAD')
    Write-Ok "代码已更新：$($script:UpdatedFrom) → $($script:UpdatedTo)"
    return 0
}

# 尝试取得更新锁。成功返回 $true，已有更新在跑返回 $false。
# 用文件独占句柄，而不是「文件是否存在」：
# 无论正常退出还是进程崩溃，句柄都会被系统释放，不会留下需要人工清理的僵尸锁。
function Enter-DeployLock {
    try {
        $script:LockStream = [System.IO.File]::Open(
            $LockFile,
            [System.IO.FileMode]::OpenOrCreate,
            [System.IO.FileAccess]::ReadWrite,
            [System.IO.FileShare]::None)
        return $true
    } catch [System.IO.IOException] {
        return $false
    }
}

function Exit-DeployLock {
    if ($script:LockStream) {
        try { $script:LockStream.Dispose() } catch { }
        $script:LockStream = $null
    }
}

# ---------------------------------------------------------------- 定时自动更新
function Install-AutoUpdateTask {
    if ($TaskTime -notmatch '^([0-9]{1,2}):([0-9]{2})$') {
        Write-Err "时间格式应为 HH:mm，收到：$TaskTime"
        return 1
    }
    $h = [int]$Matches[1]
    $m = [int]$Matches[2]
    if ($h -gt 23) { Write-Err "小时应在 0-23：$TaskTime"; return 1 }
    if ($m -gt 59) { Write-Err "分钟应在 0-59：$TaskTime"; return 1 }

    $exe = (Get-Command pwsh -ErrorAction SilentlyContinue).Source
    if (-not $exe) { $exe = (Get-Command powershell -ErrorAction SilentlyContinue).Source }
    if (-not $exe) {
        Write-Err '找不到 pwsh / powershell，无法注册计划任务'
        return 1
    }

    $scriptPath = Join-Path $PSScriptRoot 'deploy.ps1'
    $at         = (Get-Date).Date.AddHours($h).AddMinutes($m)

    try {
        $action = New-ScheduledTaskAction -Execute $exe `
            -Argument "-NoProfile -ExecutionPolicy Bypass -File `"$scriptPath`" -Update"
        $trigger  = New-ScheduledTaskTrigger -Daily -At $at
        # IgnoreNew：上一次更新还没跑完时，新的一次直接跳过，不叠加
        # ExecutionTimeLimit：给整次更新设上限。万一 git fetch 卡住，
        # 任务会被计划程序掐掉并释放更新锁，不会让自动更新静默停摆
        $settings = New-ScheduledTaskSettingsSet -MultipleInstances IgnoreNew `
            -StartWhenAvailable -ExecutionTimeLimit (New-TimeSpan -Hours 1)

        Register-ScheduledTask -TaskName $TaskName `
            -Action $action -Trigger $trigger -Settings $settings `
            -Description '短剧聚合平台：定时拉取最新代码并重建重启' `
            -Force | Out-Null
    } catch {
        Write-Err "注册计划任务失败：$($_.Exception.Message)"
        Write-Err '可改用「任务计划程序」手动创建，或按 docs/DOCKER.md 里的手动方式'
        return 1
    }

    New-Item -ItemType Directory -Path $LogDir -Force | Out-Null

    Write-Ok "已注册定时自动更新：每天 $($at.ToString('HH:mm'))"
    Write-Host "  任务名称   $TaskName" -ForegroundColor Gray
    Write-Host "  更新日志   $LogFile" -ForegroundColor Gray
    Write-Host "  查看       Get-ScheduledTask -TaskName $TaskName" -ForegroundColor Gray
    return 0
}

function Uninstall-AutoUpdateTask {
    $task = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    if (-not $task) {
        Write-Warn "未找到计划任务 $TaskName，无需移除"
        return 0
    }
    try {
        Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
    } catch {
        Write-Err "移除计划任务失败：$($_.Exception.Message)"
        return 1
    }
    Write-Ok '已移除定时自动更新'
    return 0
}

# ============================================================================
# 部署
# ============================================================================

function Invoke-PrepareEnv {
    Write-Step '准备环境变量'

    # 一律用绝对路径，不依赖当前目录
    $envPath = Join-Path $PSScriptRoot '.env'
    $envSample = Join-Path $PSScriptRoot '.env.example'

    if (-not (Test-Path $envPath)) {
        if (Test-Path $envSample) {
            Copy-Item $envSample $envPath
            Write-Ok '已从 .env.example 创建 .env'
        } else {
            New-Item -ItemType File -Path $envPath -Force | Out-Null
            Write-Ok '已创建 .env'
        }
    } else {
        Write-Ok '.env 已存在，沿用现有配置'
    }

    # 若 JWT 密钥仍是默认值，自动替换为随机值
    $envContent = Get-Content $envPath -Raw -Encoding UTF8
    if ($envContent -match 'JWT_KEY=please-change-this-secret-key-in-production-2025') {
        $randomKey = -join ((1..48) | ForEach-Object { '{0:x}' -f (Get-Random -Minimum 0 -Maximum 16) })
        $envContent = $envContent -replace 'JWT_KEY=please-change-this-secret-key-in-production-2025', "JWT_KEY=$randomKey"
        Write-EnvFile -Path $envPath -Content $envContent
        Write-Ok '已生成随机 JWT 密钥'
    } else {
        Write-Ok 'JWT 密钥已自定义'
    }

    # 端口覆盖
    if ($Port -ne 8080) {
        $envContent = Get-Content $envPath -Raw -Encoding UTF8
        if ($envContent -match 'APP_PORT=\d+') {
            $envContent = $envContent -replace 'APP_PORT=\d+', "APP_PORT=$Port"
        } else {
            $envContent = "APP_PORT=$Port`n$envContent"
        }
        Write-EnvFile -Path $envPath -Content $envContent
        Write-Ok "端口设为 $Port"
    }

    # 读取最终配置用于展示
    $finalEnv = @{}
    foreach ($line in (Get-Content $envPath -Encoding UTF8)) {
        if ($line -match '^\s*#') { continue }
        if ($line -notmatch '=') { continue }
        $k, $v = $line -split '=', 2
        if ($null -ne $k -and $null -ne $v) { $finalEnv[$k.Trim()] = $v.Trim() }
    }

    if ($finalEnv['ACCESS_PASSWORD']) { $script:AccessPassword = $finalEnv['ACCESS_PASSWORD'] }
    if ($finalEnv['ACCESS_GATE_ENABLED']) { $script:GateEnabled = $finalEnv['ACCESS_GATE_ENABLED'] }
    $script:Url = "http://localhost:$Port"
}

function Invoke-BuildImage {
    Write-Step '构建镜像（首次构建约 3-5 分钟，需要下载基础镜像）'

    $buildArgs = @('compose') + $ComposeFiles + @('build')
    if ($Rebuild) { $buildArgs += '--no-cache' }

    # 结果写在 $script:Ok 里，不用返回值：
    # docker 的输出会进入函数输出流，和返回值混在一起会判断错
    $script:Ok = $true
    & docker @buildArgs
    if ($LASTEXITCODE -ne 0) {
        Write-Err '镜像构建失败'
        Write-Host "`n  常见原因：`n    - 网络无法访问 Docker Hub / NuGet / npm registry`n    - 磁盘空间不足`n" -ForegroundColor Yellow
        $script:Ok = $false
        return
    }
    Write-Ok '镜像构建完成'
}

function Invoke-StartServices {
    Write-Step '启动服务'

    $script:Ok = $true
    & docker compose @ComposeFiles up -d
    if ($LASTEXITCODE -ne 0) {
        Write-Err '启动失败'
        $script:Ok = $false
        return
    }
    Write-Ok '容器已启动'
}

function Wait-Health {
    Write-Step '等待服务就绪'

    $ready = $false
    $deadline = (Get-Date).AddSeconds(120)

    while ((Get-Date) -lt $deadline) {
        try {
            $resp = Invoke-WebRequest "$($script:Url)/health" -UseBasicParsing -TimeoutSec 5
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
}

function Write-Summary {
    $mode = if ($Postgres) { 'PostgreSQL' } else { 'SQLite（数据卷 shortdrama-data）' }

    Write-Host @"

  ╔════════════════════════════════════════════╗
  ║  部署完成                                  ║
  ╚════════════════════════════════════════════╝

  访问地址   $($script:Url)
"@ -ForegroundColor Green

    if ($script:GateEnabled -eq 'true') {
        Write-Host "  访问口令   $($script:AccessPassword)" -ForegroundColor Yellow
        Write-Host "             （可在 .env 里改 ACCESS_PASSWORD，改完 docker compose up -d 生效）" -ForegroundColor DarkGray
    }

    Write-Host @"

  数据库     $mode
  管理后台   $($script:Url)/admin      （默认账号 admin / admin123）
"@ -ForegroundColor Gray

    if ($script:UpdatedFrom) {
        Write-Host "`n  版本变更   $($script:UpdatedFrom) → $($script:UpdatedTo)" -ForegroundColor Gray
        Write-Host "  回滚       git reset --hard $($script:UpdatedFrom); .\deploy.ps1 -Rebuild" -ForegroundColor DarkGray
    }

    Write-Host @"

  自动更新
    .\deploy.ps1 -Update              立即拉取最新代码并重建重启
    .\deploy.ps1 -Check               只检查有没有新版本
    .\deploy.ps1 -InstallTask         注册每天 04:00 定时自动更新

  常用命令
    docker compose logs -f app        查看日志
    docker compose restart app        重启
    .\deploy.ps1 -Down                停止（保留数据）
    .\deploy.ps1 -Purge               停止并清空数据

"@ -ForegroundColor Gray
}

# ============================================================================
# 入口
# ============================================================================

Write-Host @"

  ┌────────────────────────────────────────────┐
  │        短剧聚合平台 · 一键部署             │
  └────────────────────────────────────────────┘
"@ -ForegroundColor Magenta

# ---------------------------------------------------------------- 定时自动更新
# 不依赖 Docker，所以放在环境检查之前
if ($InstallTask) {
    exit (Install-AutoUpdateTask)
}

if ($UninstallTask) {
    exit (Uninstall-AutoUpdateTask)
}

# ---------------------------------------------------------------- 只检查更新
# 也不依赖 Docker，可在没跑容器的机器上用来做版本巡检
if ($Check) {
    $rc = Test-GitUpdate

    if ($rc -eq 10) {
        Write-Host "`n  当前已是最新版本`n" -ForegroundColor Green
        exit $ExitUpToDate
    }
    if ($rc -ne 0) { exit 1 }

    $dirty = (Invoke-Native git @('status', '--porcelain', '--untracked-files=no')).Output |
             Where-Object { $_ -ne '' }
    if ($dirty) {
        Write-Warn '工作区有未提交的修改，-Update 会拒绝执行'
    }

    Write-Host "`n  发现新版本：$($script:UpdatedFrom) → $($script:UpdatedTo)" -ForegroundColor Yellow
    Write-Host "  执行 .\deploy.ps1 -Update 即可更新`n" -ForegroundColor DarkGray
    exit $ExitUpdateAvailable
}

# ---------------------------------------------------------------- 环境检查
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

# ---------------------------------------------------------------- 组装 compose 参数
$ComposeFiles = @('-f', 'docker-compose.yml')
if ($Postgres) { $ComposeFiles += @('-f', 'docker-compose.postgres.yml') }

# ---------------------------------------------------------------- 停止/清理
if ($Down -or $Purge) {
    Write-Step '停止服务'
    if ($Purge) {
        Write-Warn '将删除数据卷，所有数据会丢失'
        & docker compose @ComposeFiles down -v
    } else {
        & docker compose @ComposeFiles down
    }
    Write-Ok '已停止'
    exit 0
}

# ---------------------------------------------------------------- 自动更新
if ($Update) {
    # 防止计划任务与手动更新叠在一起跑
    if (-not (Enter-DeployLock)) {
        Write-Warn '另一次部署/更新正在进行，本次跳过'
        exit 0
    }

    try {
        $rc = Update-FromGit

        if ($rc -eq 10) {
            Write-Host "`n  已是最新版本，无需更新`n" -ForegroundColor Green
            exit 0
        }
        if ($rc -ne 0) { exit 1 }

        Invoke-PrepareEnv

        # 先构建再切换：构建失败时旧容器仍在跑，站点不中断
        Invoke-BuildImage
        if (-not $script:Ok) {
            Write-Err '新版本构建失败，正在运行的旧版本未受影响'
            Write-Err "回滚代码： git reset --hard $($script:UpdatedFrom)"
            exit 1
        }

        Invoke-StartServices
        if (-not $script:Ok) { exit 1 }
        Wait-Health

        # 清掉被替换下来的旧镜像层（只删 dangling，不碰其他镜像）
        & docker image prune -f *> $null

        Write-Summary
        exit 0
    } finally {
        Exit-DeployLock
    }
}

# ---------------------------------------------------------------- 首次部署 / 重新部署
Invoke-PrepareEnv

Invoke-BuildImage
if (-not $script:Ok) { exit 1 }

Invoke-StartServices
if (-not $script:Ok) { exit 1 }

Wait-Health

Write-Summary
