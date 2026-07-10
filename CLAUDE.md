# CLAUDE.md

Guidance for working in the **BaiJiWin** repo (the Windows-native BaiJi).

## What this is

BaiJiWin is the Windows port of BaiJi — an image/video format converter &
compressor. It's a **C# + WinUI 3** desktop app (Windows App SDK) that mirrors
the macOS app in `../BaiJi`. Like macOS, it shells out to bundled CLI tools —
`ffmpeg`, `magick` (ImageMagick), `pngquant` — for the actual processing.
Distributed **unpackaged** with **Velopack** (the Sparkle analogue).

The macOS original (`../BaiJi`) is a SwiftUI/AppKit app; this port keeps the same
behavior and semantics (quality ladders, CRF mapping, target-size two-pass
encode, batch/queue model, output naming) but re-homes the platform pieces.

## Layout

```
src/BaiJi.Core/   Platform-agnostic logic (net8.0). No UI/Windows deps.
                  Compression pipeline, queue, licensing, settings.
src/BaiJi.App/    WinUI 3 shell (net8.0-windows). Thin views over Core.
                  Tools\  <- ffmpeg/magick/pngquant land here (git-ignored)
                  Strings\{en-US,zh-Hans}\Resources.resw  <- localization
tests/BaiJi.Tests/  xUnit. Unit + integration/e2e (shells out to real tools).
scripts/          fetch-binaries.ps1, release.ps1, smoke-test.ps1
TestAssets/       Sample images/videos for the e2e tests.
```

Key Core types map 1:1 to the macOS files:
`ImageProcessor`/`VideoConverter`/`MediaQueue`/`CompressionSettings`/`MediaTask`
mirror the Swift classes of the same names; `LemonSqueezyClient`+`LicenseManager`
port `LicensePreferencesView`; `ProcessRunner` ports the Swift `run` helpers.

## The Core/App split (why the coverage story works)

WinUI XAML/code-behind can't be meaningfully unit-tested, so **all logic lives in
`BaiJi.Core` and the ViewModels**, and the Views are kept near-logicless. The
xUnit suite covers Core at **>90% line coverage**. Because the process-invocation
and orchestration logic is identical on every OS (only the binary path differs),
the integration/e2e tests run for real against whatever tools are available —
the win-x64 binaries on Windows, or the macOS binaries in `../BaiJi/BaiJi` on a
Mac dev box — proving the actual compression pipeline, not just mocks.

## Build & test

```powershell
# One-time: fetch the win-x64 media tools into src\BaiJi.App\Tools\
pwsh -File scripts\fetch-binaries.ps1

# Core + integration + e2e tests (run on any OS; uses real binaries)
dotnet test tests\BaiJi.Tests\BaiJi.Tests.csproj -c Release --collect:"XPlat Code Coverage"

# Build/run the WinUI app (Windows only). -p:Platform=x64 is REQUIRED — WinUI's
# XAML compiler crashes on the default AnyCPU. Use dotnet (NOT Framework MSBuild,
# which skips the XAML markup pass). WindowsAppSDK is pinned to 1.8 (1.6 crashes
# XamlCompiler on current VS/.NET toolchains).
dotnet build src\BaiJi.App\BaiJi.App.csproj -c Debug -r win-x64 -p:Platform=x64
dotnet run   --project src\BaiJi.App\BaiJi.App.csproj -c Debug -r win-x64 -p:Platform=x64

# Full smoke: tests + build + launch
pwsh -File scripts\smoke-test.ps1
```

> The Core library and the whole test suite build & run on macOS/Linux too
> (net8.0). Only `BaiJi.App` requires Windows — WinUI 3 has no cross-platform
> build. Don't `dotnet build BaiJiWin.sln` on a Mac; it will fail on the App.

Tests resolve tools via `BAIJI_TOOLS_DIR` and assets via `BAIJI_TEST_ASSETS`
(both auto-discovered by `TestSupport`; see it for the fallbacks).

## CI / GitHub Actions

Because WinUI 3 only builds on Windows, CI is where the app is actually compiled
and the real e2e tests run:

- `.github/workflows/ci.yml` — on push/PR:
  - **core-tests** (ubuntu): Core unit tests + a hard **>85% line coverage gate**
    (coverlet, scoped to `[BaiJi.Core]`).
  - **windows** (windows-latest): fetches the win-x64 tools (cached), runs the
    full suite incl. **real integration/e2e against those binaries**, then builds
    `BaiJi.App`. This is the check that the WinUI shell compiles.
- `.github/workflows/release.yml` — on a `v*` tag (or manual dispatch): publishes,
  `vpk pack`, and `vpk upload github` to this repo's Releases (uses `GITHUB_TOKEN`,
  `contents: write`). That Release is what the in-app updater consumes.

## Releasing (Velopack)

```powershell
pwsh -File scripts\release.ps1 -Version 1.0.0 -Notes "what changed"
# add -Upload -Token <gh-pat> to publish to GitHub (maoxiaoke/BaiJiWin)
```

`release.ps1` publishes a self-contained win-x64 build, runs `vpk pack` (installer
+ delta + `RELEASES` feed), and optionally `vpk upload github`. The app's
`UpdateService` reads releases from `https://github.com/maoxiaoke/BaiJiWin`.
`VelopackApp.Build().Run()` runs first thing in `Program.Main` — never remove it.

## Bundled tools (vs. macOS)

Unlike macOS (where `magick` needed dylib vendoring), all three Windows tools are
**self-contained single exes**: ffmpeg (BtbN static GPL, libx264 in), ImageMagick
portable Q16 x64, pngquant win build. `fetch-binaries.ps1` pulls them from the
official sources into `src\BaiJi.App\Tools\`, which the csproj copies to output;
`BundledToolLocator` resolves them at runtime next to `BaiJi.exe`.

## Conventions

- Latest C#/.NET 8, WinUI 3; strict MVVM (logic in Core/ViewModels, thin Views).
- Support light/dark via theme resources. Localize via `Loc.Get(key)` + `.resw`
  (en-US + zh-Hans, mirroring the macOS `Localizable.xcstrings` set).
- Concise, readable code; match surrounding style. No placeholders/TODOs.
- Commit/push or release only when asked; x64-only for now.
