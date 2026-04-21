<#
.SYNOPSIS
    一键发布 SmartLogisticsTracker (API + MAUI Client + Docker).

.DESCRIPTION
    支持清理、发布、打包 zip、构建 Docker 镜像。
    发布目标可组合：api / client-win / client-android / docker / all

.PARAMETER Target
    发布目标。默认 all（api + client-win + client-android）。
    可选 docker：仅构建 Docker 镜像。

.PARAMETER Configuration
    构建配置 Debug|Release。默认 Release。

.PARAMETER Runtime
    API 自包含发布的 RID。示例: win-x64, linux-x64, linux-arm64, osx-arm64。

.PARAMETER SelfContained
    API 是否自包含（包含 .NET 运行时）。默认 $true。

.PARAMETER Version
    注入程序集与镜像的版本号。默认 1.0.0。

.PARAMETER OutputRoot
    产物根目录。默认 .\publish。

.PARAMETER DockerImage
    Docker 镜像名。默认 smartlogistics-api。

.PARAMETER DockerPlatform
    Docker 目标平台。默认 linux/amd64。多平台：linux/amd64,linux/arm64（需要 buildx）。

.PARAMETER Clean
    发布前清空 OutputRoot 和各项目 bin/obj。

.PARAMETER Zip
    每个 .NET 目标产物额外打包为 zip。

.PARAMETER SkipRestore
    跳过 dotnet restore。

.EXAMPLE
    .\publish.ps1
    .\publish.ps1 -Target api -Runtime linux-x64 -Zip
    .\publish.ps1 -Clean -Version 1.2.3 -Zip
    .\publish.ps1 -Target docker -Version 1.2.3
    .\publish.ps1 -Target docker -DockerPlatform "linux/amd64,linux/arm64"
#>
[CmdletBinding()]
param(
    [ValidateSet('api', 'client-win', 'client-android', 'docker', 'all')]
    [string]$Target = 'all',

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [string]$Runtime = 'win-x64',

    [bool]$SelfContained = $true,

    [string]$Version = '1.0.0',

    [string]$OutputRoot = (Join-Path $PSScriptRoot 'publish'),

    [string]$DockerImage = 'smartlogistics-api',

    [string]$DockerPlatform = 'linux/amd64',

    [switch]$Clean,
    [switch]$Zip,
    [switch]$SkipRestore
)

$ErrorActionPreference = 'Stop'
$script:repoRoot    = $PSScriptRoot
$script:apiProj     = Join-Path $repoRoot 'src\SmartLogisticsTracker.Api\SmartLogisticsTracker.Api.csproj'
$script:apiDir      = Join-Path $repoRoot 'src\SmartLogisticsTracker.Api'
$script:clientProj  = Join-Path $repoRoot 'src\SmartLogisticsTracker.Client\SmartLogisticsTracker.Client.csproj'
$script:slnFile     = Join-Path $repoRoot 'SmartLogisticsTracker.slnx'
$script:dockerfile  = Join-Path $apiDir 'Dockerfile'
$script:results     = [System.Collections.Generic.List[object]]::new()
$script:failed      = [System.Collections.Generic.List[string]]::new()

function Write-Step($msg) { Write-Host ""; Write-Host "==> $msg" -ForegroundColor Cyan }
function Write-Ok($msg)   { Write-Host "  ✔ $msg" -ForegroundColor Green }
function Write-Warn2($msg){ Write-Host "  ⚠ $msg" -ForegroundColor Yellow }
function Write-Err2($msg) { Write-Host "  ✘ $msg" -ForegroundColor Red }

function Invoke-Checked {
    param([string]$File, [string[]]$Arguments)
    Write-Host "  > $File $($Arguments -join ' ')" -ForegroundColor DarkGray
    & $File @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed (exit $LASTEXITCODE): $File $($Arguments -join ' ')"
    }
}

function Test-Tool {
    param([string]$Name, [string]$VersionArg = '--version')
    try {
        $v = (& $Name $VersionArg 2>$null) | Select-Object -First 1
        Write-Host "  $Name`: $v" -ForegroundColor DarkGray
        return $true
    } catch {
        return $false
    }
}

function Clear-Output {
    Write-Step "清理产物与 bin/obj"
    if (Test-Path $OutputRoot) {
        Remove-Item $OutputRoot -Recurse -Force
        Write-Ok "已删除 $OutputRoot"
    }
    Get-ChildItem -Path (Join-Path $repoRoot 'src') -Recurse -Directory `
        -Include 'bin','obj' -Force -ErrorAction SilentlyContinue |
        ForEach-Object { Remove-Item $_.FullName -Recurse -Force -ErrorAction SilentlyContinue }
    Write-Ok "已清理 bin/obj"
}

function New-Zip {
    param([string]$Source, [string]$Name)
    if (-not $Zip) { return }
    $zipPath = Join-Path $OutputRoot "$Name-$Version.zip"
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Compress-Archive -Path (Join-Path $Source '*') -DestinationPath $zipPath -CompressionLevel Optimal
    Write-Ok "已打包 $zipPath"
}

function Add-Result {
    param([string]$Target, [string]$Output, [TimeSpan]$Duration, [bool]$Ok)
    $results.Add([pscustomobject]@{
        Target   = $Target
        Output   = $Output
        Duration = $Duration
        Ok       = $Ok
    })
}

function Publish-Api {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    Write-Step "发布 API (rid=$Runtime, self-contained=$SelfContained, version=$Version)"
    $out = Join-Path $OutputRoot 'api'
    $args = @(
        'publish', $apiProj,
        '-c', $Configuration,
        '-r', $Runtime,
        "--self-contained=$($SelfContained.ToString().ToLower())",
        '-p:PublishSingleFile=true',
        '-p:PublishTrimmed=false',
        "-p:Version=$Version",
        "-p:AssemblyVersion=$Version.0",
        "-p:FileVersion=$Version.0",
        '-o', $out
    )
    Invoke-Checked 'dotnet' $args
    New-Zip -Source $out -Name "api-$Runtime"
    $sw.Stop()
    Add-Result 'api' $out $sw.Elapsed $true
    Write-Ok "API 发布完成：$out ($([int]$sw.Elapsed.TotalSeconds)s)"
}

function Publish-ClientWin {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    Write-Step "发布 Client (Windows)"
    $out = Join-Path $OutputRoot 'client-win'
    $args = @(
        'publish', $clientProj,
        '-c', $Configuration,
        '-f', 'net10.0-windows10.0.19041.0',
        '-p:WindowsPackageType=None',
        "-p:ApplicationDisplayVersion=$Version",
        '-o', $out
    )
    Invoke-Checked 'dotnet' $args
    New-Zip -Source $out -Name 'client-win'
    $sw.Stop()
    Add-Result 'client-win' $out $sw.Elapsed $true
    Write-Ok "Windows 客户端发布完成：$out ($([int]$sw.Elapsed.TotalSeconds)s)"
}

function Publish-ClientAndroid {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    Write-Step "发布 Client (Android APK)"
    $out = Join-Path $OutputRoot 'client-android'
    $args = @(
        'publish', $clientProj,
        '-c', $Configuration,
        '-f', 'net10.0-android',
        '-p:AndroidPackageFormat=apk',
        "-p:ApplicationDisplayVersion=$Version",
        '-o', $out
    )
    Invoke-Checked 'dotnet' $args
    New-Zip -Source $out -Name 'client-android'
    $sw.Stop()
    Add-Result 'client-android' $out $sw.Elapsed $true
    Write-Ok "Android 客户端发布完成：$out ($([int]$sw.Elapsed.TotalSeconds)s)"
}

function Build-Docker {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    Write-Step "构建 Docker 镜像 ($DockerImage`:$Version, platform=$DockerPlatform)"
    if (-not (Test-Tool 'docker')) { throw "未检测到 docker CLI。" }

    $isMulti = $DockerPlatform -match ','
    $baseArgs = @(
        '-f', $dockerfile,
        '--build-arg', "VERSION=$Version",
        '-t', "$DockerImage`:$Version",
        '-t', "$DockerImage`:latest",
        $repoRoot
    )

    if ($isMulti) {
        Write-Warn2 "多平台构建需要 docker buildx 且通常需要 --push 或 --output"
        $args = @('buildx', 'build', '--platform', $DockerPlatform) + $baseArgs + @('--load')
        try { Invoke-Checked 'docker' $args }
        catch {
            Write-Warn2 "--load 对多平台不受支持，请改用 --push。回退到单平台 linux/amd64。"
            $args = @('buildx', 'build', '--platform', 'linux/amd64') + $baseArgs + @('--load')
            Invoke-Checked 'docker' $args
        }
    } else {
        $args = @('buildx', 'build', '--platform', $DockerPlatform) + $baseArgs + @('--load')
        Invoke-Checked 'docker' $args
    }

    $sw.Stop()
    Add-Result 'docker' "$DockerImage`:$Version" $sw.Elapsed $true
    Write-Ok "Docker 镜像构建完成：$DockerImage`:$Version ($([int]$sw.Elapsed.TotalSeconds)s)"
}

function Try-Task {
    param([string]$Name, [scriptblock]$Action)
    try { & $Action }
    catch {
        Write-Err2 "$Name 失败: $($_.Exception.Message)"
        $script:failed.Add($Name)
        Add-Result $Name '' ([TimeSpan]::Zero) $false
    }
}

# --------------------------------------------------------------
#  Main
# --------------------------------------------------------------
$totalSw = [Diagnostics.Stopwatch]::StartNew()

Write-Host "SmartLogisticsTracker Publish" -ForegroundColor Yellow
Write-Host "  Target        : $Target"
Write-Host "  Configuration : $Configuration"
Write-Host "  Runtime (API) : $Runtime"
Write-Host "  SelfContained : $SelfContained"
Write-Host "  Version       : $Version"
Write-Host "  OutputRoot    : $OutputRoot"
Write-Host "  Zip           : $Zip"
Write-Host "  Clean         : $Clean"
if ($Target -eq 'docker' -or $Target -eq 'all') {
    Write-Host "  DockerImage   : $DockerImage"
    Write-Host "  DockerPlatform: $DockerPlatform"
}

if (-not (Test-Tool 'dotnet')) { throw "未检测到 dotnet CLI，请先安装 .NET SDK。" }

if ($Clean) { Clear-Output }
if (-not (Test-Path $OutputRoot)) { New-Item -ItemType Directory -Path $OutputRoot | Out-Null }

$needsDotnet = $Target -in @('api', 'client-win', 'client-android', 'all')
if ($needsDotnet -and -not $SkipRestore) {
    Write-Step "还原 NuGet 包"
    Invoke-Checked 'dotnet' @('restore', $slnFile)
}

switch ($Target) {
    'api'            { Try-Task 'api'            { Publish-Api } }
    'client-win'     { Try-Task 'client-win'     { Publish-ClientWin } }
    'client-android' { Try-Task 'client-android' { Publish-ClientAndroid } }
    'docker'         { Try-Task 'docker'         { Build-Docker } }
    'all' {
        Try-Task 'api'            { Publish-Api }
        Try-Task 'client-win'     { Publish-ClientWin }
        Try-Task 'client-android' { Publish-ClientAndroid }
    }
}

$totalSw.Stop()

Write-Host ""
Write-Host "====== Summary ======" -ForegroundColor Yellow
$results | Format-Table -AutoSize @(
    @{ Name='Target';  Expression={ $_.Target } },
    @{ Name='Status';  Expression={ if ($_.Ok) {'OK'} else {'FAIL'} } },
    @{ Name='Time(s)'; Expression={ [int]$_.Duration.TotalSeconds } },
    @{ Name='Output';  Expression={ $_.Output } }
) | Out-Host

Write-Host "总耗时: $([int]$totalSw.Elapsed.TotalSeconds)s" -ForegroundColor DarkGray
Write-Host "产物目录: $OutputRoot"

if ($failed.Count -gt 0) {
    Write-Err2 "以下目标失败: $($failed -join ', ')"
    exit 1
}

Write-Host "全部完成 ✔" -ForegroundColor Green
