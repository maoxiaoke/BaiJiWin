# BaiJi for Windows

A native Windows (C# + WinUI 3) build of **BaiJi**, the offline image/video
converter & compressor. It's the Windows counterpart of the macOS app in
[`../BaiJi`](../BaiJi), keeping the same behavior while re-homing the
platform-specific pieces.

## Features (parity with macOS)

- **Drop slot** interface — drag images/videos onto the window, or click to pick.
- **Paste** with `Ctrl+V` — files copied in Explorer, or raw bitmaps (screenshots).
- **Image compression** via pngquant (PNG→PNG) and ImageMagick (everything else),
  with `Smaller / Balanced / Clearer` presets and an optional target file size.
- **Video compression** via ffmpeg libx264 — CRF for presets, two-pass bitrate
  for a hard size ceiling; optional audio removal.
- **Batches** — files dropped together are one card; serial processing queue.
- **Results** — copy to clipboard, "Save to…", or leave next to the original.
- **Output routing** — next to input (default), a wiped staging folder, or custom.
- **Licensing** — LemonSqueezy activation (same endpoints as macOS).
- **In-app updates** — Velopack, pulling from GitHub Releases (the Sparkle analogue).
- **Localization** — English + 简体中文; light/dark mode.

## Requirements

- Windows 10 1809 (build 17763) or later, x64.
- To build: .NET 8 SDK + the Windows App SDK workload; Windows for the app itself.

## Quick start

```powershell
git clone git@github.com:maoxiaoke/BaiJiWin.git
cd BaiJiWin
pwsh -File scripts\fetch-binaries.ps1      # pull ffmpeg/magick/pngquant (win-x64)
dotnet run --project src\BaiJi.App\BaiJi.App.csproj -c Debug -r win-x64
```

## Project structure

| Path | What |
|------|------|
| `src/BaiJi.Core` | All logic; plain .NET 8, no UI. Unit-tested to >90%. |
| `src/BaiJi.App` | WinUI 3 shell (views, view models, platform services). |
| `tests/BaiJi.Tests` | xUnit — unit + real integration/e2e via the bundled tools. |
| `scripts` | `fetch-binaries.ps1`, `release.ps1`, `smoke-test.ps1`. |

See [`CLAUDE.md`](CLAUDE.md) for build/test/release details and the design notes
behind the Core/App split.

## License / Contact

Same as the macOS BaiJi. Copyright © 2024 nazha.
