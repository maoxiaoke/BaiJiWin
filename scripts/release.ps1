<#
.SYNOPSIS
  Builds, publishes, and Velopack-packages BaiJi for win-x64, then (optionally)
  uploads the release to GitHub (maoxiaoke/BaiJiWin). Windows equivalent of the
  macOS repo's scripts/release.sh (archive + notarize + appcast).

.EXAMPLE
  pwsh -File scripts\release.ps1 -Version 1.0.0 -Notes "First Windows release"
  pwsh -File scripts\release.ps1 -Version 1.1.0 -Notes "..." -Upload -Token $env:GH_TOKEN
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$Version,
    [string]$Notes = "",
    [switch]$Upload,
    [string]$Token = $env:GITHUB_TOKEN,
    [string]$RepoUrl = "https://github.com/maoxiaoke/BaiJiWin"
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$app = Join-Path $root 'src\BaiJi.App\BaiJi.App.csproj'
$publishDir = Join-Path $root 'artifacts\publish'
$releasesDir = Join-Path $root 'artifacts\releases'

# 0. Ensure bundled tools are present.
if (-not (Test-Path (Join-Path $root 'src\BaiJi.App\Tools\ffmpeg.exe'))) {
    Write-Host "Tools missing — running fetch-binaries.ps1"
    & (Join-Path $PSScriptRoot 'fetch-binaries.ps1')
}

# 1. Ensure the Velopack CLI is installed.
if (-not (Get-Command 'vpk' -ErrorAction SilentlyContinue)) {
    dotnet tool install -g vpk
    $env:PATH += ";$env:USERPROFILE\.dotnet\tools"
}

# 2. Publish a self-contained win-x64 build.
Remove-Item -Recurse -Force $publishDir -ErrorAction SilentlyContinue
# -p:Platform=x64 is required — WinUI's XAML compiler crashes on AnyCPU.
dotnet publish $app -c Release -r win-x64 -p:Platform=x64 --self-contained true `
    /p:Version=$Version -o $publishDir

# 3. Pack with Velopack (produces installer + delta + RELEASES feed).
New-Item -ItemType Directory -Force -Path $releasesDir | Out-Null
# --releaseNotes takes a markdown FILE, not inline text.
$notesArgs = @()
if ($Notes) {
    $notesFile = Join-Path $releasesDir 'release-notes.md'
    Set-Content -Path $notesFile -Value $Notes -Encoding utf8
    $notesArgs = @('--releaseNotes', $notesFile)
}
vpk pack `
    --packId BaiJiWin `
    --packVersion $Version `
    --packDir $publishDir `
    --mainExe BaiJi.exe `
    --packTitle "BaiJi" `
    --runtime win-x64 `
    --outputDir $releasesDir `
    @notesArgs

Write-Host "`nPackaged $Version into $releasesDir"

# 4. Optionally upload to GitHub Releases (this is what the app's updater reads).
if ($Upload) {
    if (-not $Token) { throw "-Upload requires -Token or `$env:GITHUB_TOKEN" }
    vpk upload github `
        --repoUrl $RepoUrl `
        --token $Token `
        --outputDir $releasesDir `
        --tag "v$Version" `
        --releaseName "BaiJi v$Version" `
        --publish
    Write-Host "Uploaded v$Version to $RepoUrl"
}
