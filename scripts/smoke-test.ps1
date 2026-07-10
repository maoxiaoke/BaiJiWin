<#
.SYNOPSIS
  Windows smoke + e2e verification. Runs the full test suite (unit + integration)
  against the fetched win-x64 binaries, then builds and launches the app so a
  human can confirm the drop/paste/compress flow end-to-end.

.DESCRIPTION
  On Windows the integration tests in BaiJi.Tests auto-discover the bundled tools
  under src\BaiJi.App\Tools (via BAIJI_TOOLS_DIR) and the TestAssets folder, so
  the same e2e compression paths verified on the macOS dev box run here against
  the real Windows binaries.
#>
[CmdletBinding()]
param([switch]$SkipLaunch)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

# 1. Make sure the win-x64 tools exist so integration tests run for real.
if (-not (Test-Path (Join-Path $root 'src\BaiJi.App\Tools\ffmpeg.exe'))) {
    & (Join-Path $PSScriptRoot 'fetch-binaries.ps1')
}
$env:BAIJI_TOOLS_DIR = Join-Path $root 'src\BaiJi.App\Tools'

# Provide sample assets if this repo doesn't carry its own TestAssets.
$assets = Join-Path $root 'TestAssets'
if (Test-Path $assets) { $env:BAIJI_TEST_ASSETS = $assets }

# 2. Unit + integration + e2e tests.
Write-Host "== Running test suite (unit + integration + e2e) =="
dotnet test (Join-Path $root 'tests\BaiJi.Tests\BaiJi.Tests.csproj') -c Release `
    --collect:"XPlat Code Coverage" --results-directory (Join-Path $root 'TestResults')

# 3. Build the WinUI app.
Write-Host "== Building the WinUI app =="
dotnet build (Join-Path $root 'src\BaiJi.App\BaiJi.App.csproj') -c Debug -r win-x64 -p:Platform=x64

# 4. Launch for a manual smoke check unless suppressed.
if (-not $SkipLaunch) {
    Write-Host "== Launching BaiJi — drop an image/video, then Ctrl+V a screenshot =="
    dotnet run --project (Join-Path $root 'src\BaiJi.App\BaiJi.App.csproj') -c Debug -r win-x64 -p:Platform=x64
}
