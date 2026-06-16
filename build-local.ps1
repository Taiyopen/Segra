#Requires -Version 5.1
<#
.SYNOPSIS
  建置前端 (Vite)、複製到 wwwroot、發行 Segra（Windows x64）。

.EXAMPLE
  .\build-local.ps1

.EXAMPLE
  .\build-local.ps1 -NoSelfContained
#>
param(
    [string] $Configuration = "Release",
    [string] $Runtime = "win-x64",
    [string] $Output = "publish",
    [switch] $NoSelfContained
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root = $PSScriptRoot
Set-Location $root

function Initialize-DotNetSdk {
    $userDotNetRoot = Join-Path $env:USERPROFILE ".dotnet"
    if (Test-Path $userDotNetRoot) {
        $env:DOTNET_ROOT = $userDotNetRoot
        $env:PATH = "$userDotNetRoot;$env:PATH"
    }

    $sdkVersion = & dotnet --version 2>$null
    if ($sdkVersion) {
        $sdkVersion = $sdkVersion.Trim()
    }
    if ($sdkVersion -notmatch '^10\.') {
        throw @"
需要 .NET 10 SDK 才能建置此專案（目前: $sdkVersion）。

安裝方式（擇一）:
  winget install Microsoft.DotNet.SDK.10
  或 https://dotnet.microsoft.com/download/dotnet/10.0
"@
    }

    Write-Host "Using .NET SDK $sdkVersion" -ForegroundColor DarkGray
}

Initialize-DotNetSdk

Write-Host "=== Building Frontend ===" -ForegroundColor Cyan
Push-Location (Join-Path $root "Frontend")
try {
    if (Get-Command bun -ErrorAction SilentlyContinue) {
        & bun run build
    }
    else {
        & npm run build
    }
    if ($LASTEXITCODE -ne 0) {
        throw "Frontend build failed (exit code $LASTEXITCODE)."
    }
}
finally {
    Pop-Location
}

Write-Host "=== Copying Frontend to wwwroot ===" -ForegroundColor Cyan
$wwwroot = Join-Path $root "wwwroot"
$dist = Join-Path $root "Frontend\dist"
$embeddedWebroot = Join-Path $root "Resources\wwwroot"
if (-not (Test-Path $dist)) {
    throw "Frontend build output not found: $dist"
}
if (Test-Path $wwwroot) {
    Remove-Item $wwwroot -Recurse -Force
}
New-Item -ItemType Directory -Path $wwwroot | Out-Null
Copy-Item -Path (Join-Path $dist "*") -Destination $wwwroot -Recurse -Force

Write-Host "=== Syncing Frontend to Resources/wwwroot (embedded fallback) ===" -ForegroundColor Cyan
if (Test-Path $embeddedWebroot) {
    Remove-Item $embeddedWebroot -Recurse -Force
}
New-Item -ItemType Directory -Path $embeddedWebroot | Out-Null
Copy-Item -Path (Join-Path $dist "*") -Destination $embeddedWebroot -Recurse -Force

Write-Host "=== Publishing Backend ===" -ForegroundColor Cyan
$publishArgs = @(
    "publish",
    "Segra.csproj",
    "-c", $Configuration,
    "-r", $Runtime,
    "-o", $Output
)
if (-not $NoSelfContained) {
    $publishArgs += "--self-contained"
}
& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$outDir = Join-Path $root $Output
Write-Host ""
Write-Host "=== Done ===" -ForegroundColor Green
Write-Host "Output: $outDir\"
Write-Host "Executable: $(Join-Path $outDir 'Segra.exe')"
