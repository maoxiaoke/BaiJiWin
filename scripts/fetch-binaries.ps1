<#
.SYNOPSIS
  Downloads the win-x64 media tools BaiJi shells out to (ffmpeg, magick, pngquant)
  and drops the self-contained executables into src\BaiJi.App\Tools\.

.DESCRIPTION
  Windows counterpart of the macOS repo's bundled binaries. Unlike macOS, all
  three tools ship as self-contained win-x64 builds, so there is no dylib/dll
  vendoring step:
    - ffmpeg : BtbN static GPL build (single ffmpeg.exe, libx264 included)
    - magick : ImageMagick portable Q16 x64 (single self-contained magick.exe)
    - pngquant: official pngquant-windows build (single pngquant.exe)

  Run on a Windows machine from the repo root:
      pwsh -File scripts\fetch-binaries.ps1
  Re-run to refresh. Use -Force to re-download even if present.
#>
[CmdletBinding()]
param(
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$toolsDir = Join-Path $root 'src\BaiJi.App\Tools'
$work = Join-Path ([System.IO.Path]::GetTempPath()) ("baiji-fetch-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $toolsDir | Out-Null
New-Item -ItemType Directory -Force -Path $work | Out-Null

function Save-File($url, $dest) {
    Write-Host "Downloading $url"
    Invoke-WebRequest -Uri $url -OutFile $dest -UseBasicParsing
}

function Need($name) {
    $exe = Join-Path $toolsDir "$name.exe"
    return $Force -or -not (Test-Path $exe)
}

# ---- ffmpeg (BtbN static GPL) --------------------------------------------
if (Need 'ffmpeg') {
    $zip = Join-Path $work 'ffmpeg.zip'
    Save-File 'https://github.com/BtbN/FFmpeg-Builds/releases/latest/download/ffmpeg-master-latest-win64-gpl.zip' $zip
    Expand-Archive -Path $zip -DestinationPath $work -Force
    $ff = Get-ChildItem -Path $work -Recurse -Filter 'ffmpeg.exe' | Select-Object -First 1
    Copy-Item $ff.FullName (Join-Path $toolsDir 'ffmpeg.exe') -Force
    Write-Host "  -> ffmpeg.exe"
} else { Write-Host "ffmpeg.exe present (skip; -Force to refresh)" }

# ---- ImageMagick portable (self-contained magick.exe) --------------------
if (Need 'magick') {
    # Scrape the archive index for the newest portable Q16 x64 zip.
    $index = Invoke-WebRequest -Uri 'https://imagemagick.org/archive/binaries/' -UseBasicParsing
    $match = ($index.Links.href | Where-Object { $_ -match 'ImageMagick-.*portable-Q16-x64\.zip$' } |
              Sort-Object -Descending | Select-Object -First 1)
    if (-not $match) { throw "Could not find an ImageMagick portable Q16 x64 zip in the archive index." }
    $url = "https://imagemagick.org/archive/binaries/$match"
    $zip = Join-Path $work 'magick.zip'
    Save-File $url $zip
    $dest = Join-Path $work 'magick'
    Expand-Archive -Path $zip -DestinationPath $dest -Force
    $mg = Get-ChildItem -Path $dest -Recurse -Filter 'magick.exe' | Select-Object -First 1
    Copy-Item $mg.FullName (Join-Path $toolsDir 'magick.exe') -Force
    Write-Host "  -> magick.exe"
} else { Write-Host "magick.exe present (skip; -Force to refresh)" }

# ---- pngquant ------------------------------------------------------------
if (Need 'pngquant') {
    $zip = Join-Path $work 'pngquant.zip'
    Save-File 'https://pngquant.org/pngquant-windows.zip' $zip
    Expand-Archive -Path $zip -DestinationPath $work -Force
    $pq = Get-ChildItem -Path $work -Recurse -Filter 'pngquant.exe' | Select-Object -First 1
    Copy-Item $pq.FullName (Join-Path $toolsDir 'pngquant.exe') -Force
    Write-Host "  -> pngquant.exe"
} else { Write-Host "pngquant.exe present (skip; -Force to refresh)" }

Remove-Item -Recurse -Force $work
Write-Host "`nDone. Tools in $toolsDir :"
Get-ChildItem $toolsDir | Format-Table Name, Length
