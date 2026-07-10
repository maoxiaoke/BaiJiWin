using Velopack;
using Velopack.Sources;

namespace BaiJi.App.Services;

public enum UpdateState { Idle, Checking, UpToDate, Available, Downloading, ReadyToRestart, Error }

/// <summary>
/// The Windows equivalent of the macOS Sparkle updater. Velopack pulls releases
/// from the GitHub repo (maoxiaoke/BaiJiWin), downloads deltas, and applies them
/// with an automatic relaunch — the same in-app update UX as Sparkle.
/// </summary>
public sealed class UpdateService
{
    // Releases are published to the same GitHub repo as the source.
    private const string ReleasesRepoUrl = "https://github.com/maoxiaoke/BaiJiWin";

    private readonly UpdateManager _manager;
    private UpdateInfo? _pending;

    public UpdateState State { get; private set; } = UpdateState.Idle;
    public event Action? StateChanged;

    public UpdateService()
    {
        _manager = new UpdateManager(new GithubSource(ReleasesRepoUrl, accessToken: null, prerelease: false));
    }

    public string CurrentVersion => _manager.CurrentVersion?.ToString() ?? "—";

    /// <summary>Velopack's hook: must run before the UI so install/update events are handled.</summary>
    public static void RunStartupHook()
    {
        VelopackApp.Build().Run();
    }

    public async Task CheckAsync()
    {
        if (!_manager.IsInstalled)
        {
            // Running from a dev build (not a Velopack install) — nothing to do.
            Set(UpdateState.Idle);
            return;
        }
        try
        {
            Set(UpdateState.Checking);
            _pending = await _manager.CheckForUpdatesAsync().ConfigureAwait(false);
            Set(_pending is null ? UpdateState.UpToDate : UpdateState.Available);
        }
        catch
        {
            Set(UpdateState.Error);
        }
    }

    public async Task DownloadAndPrepareAsync()
    {
        if (_pending is null) return;
        try
        {
            Set(UpdateState.Downloading);
            await _manager.DownloadUpdatesAsync(_pending).ConfigureAwait(false);
            Set(UpdateState.ReadyToRestart);
        }
        catch
        {
            Set(UpdateState.Error);
        }
    }

    public void ApplyAndRestart()
    {
        if (_pending is not null) _manager.ApplyUpdatesAndRestart(_pending);
    }

    private void Set(UpdateState state)
    {
        State = state;
        StateChanged?.Invoke();
    }
}
