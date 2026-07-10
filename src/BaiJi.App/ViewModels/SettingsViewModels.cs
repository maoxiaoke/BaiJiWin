using BaiJi.App.Services;
using BaiJi.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BaiJi.App.ViewModels;

/// <summary>General preferences: language, output destination, copy-after-compress.</summary>
public sealed partial class GeneralViewModel : ObservableObject
{
    private readonly ISettingsStore _store = AppServices.Instance.Settings;

    public IReadOnlyList<(string Code, string Name)> Languages { get; } = new[]
    {
        ("en-US", "English"),
        ("zh-Hans", "简体中文"),
    };

    [ObservableProperty] private string _language;
    [ObservableProperty] private string _outputDirectory;
    [ObservableProperty] private string _customOutputDirectory;
    [ObservableProperty] private bool _copyToClipboard;

    public GeneralViewModel()
    {
        _language = _store.GetString("appLanguage") ?? "en-US";
        _outputDirectory = _store.GetString("outputDirectory") ?? "Same as input";
        _customOutputDirectory = _store.GetString("customOutputDirectory") ?? "";
        _copyToClipboard = _store.GetBool("copyToClipboardAfterCompression");
    }

    partial void OnLanguageChanged(string value)
    {
        _store.SetString("appLanguage", value);
        Loc.UseLanguage(value);
    }

    partial void OnOutputDirectoryChanged(string value) => _store.SetString("outputDirectory", value);
    partial void OnCustomOutputDirectoryChanged(string value) => _store.SetString("customOutputDirectory", value);
    partial void OnCopyToClipboardChanged(bool value) => _store.SetBool("copyToClipboardAfterCompression", value);
}

/// <summary>License pane: activate/deactivate via LemonSqueezy.</summary>
public sealed partial class LicenseViewModel : ObservableObject
{
    private readonly LicenseManager _manager = AppServices.Instance.License;

    [ObservableProperty] private string _licenseKeyInput = "";
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _isBusy;

    public string ActiveKey => _manager.LicenseKey;
    public string ExpiryDate => _manager.ExpiryDate;
    public string BuyUrl => LemonSqueezyClient.BuyUrl;
    public string ManageUrl => LemonSqueezyClient.ManageUrl;

    public LicenseViewModel()
    {
        _isActive = _manager.IsActive;
        _manager.StatusChanged += () => IsActive = _manager.IsActive;
    }

    [RelayCommand]
    private async Task ActivateAsync()
    {
        if (LicenseKeyInput.Length < 25) return;
        IsBusy = true;
        StatusMessage = Loc.Get("License_Activating");
        var result = await _manager.ActivateAsync(LicenseKeyInput);
        IsBusy = false;
        IsActive = _manager.IsActive;
        StatusMessage = result.Success ? Loc.Get("License_ActivatedOk") : result.ErrorMessage ?? "";
    }

    [RelayCommand]
    private async Task DeactivateAsync()
    {
        IsBusy = true;
        var result = await _manager.DeactivateAsync();
        IsBusy = false;
        IsActive = _manager.IsActive;
        if (!result.Success) StatusMessage = result.ErrorMessage ?? "";
    }
}

/// <summary>Updates pane: drives Velopack check/download/restart.</summary>
public sealed partial class UpdatesViewModel : ObservableObject
{
    private readonly UpdateService _updates = AppServices.Instance.Updates;
    private readonly ISettingsStore _store = AppServices.Instance.Settings;

    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _autoCheck;
    [ObservableProperty] private bool _canRestart;

    public string CurrentVersion => _updates.CurrentVersion;

    public UpdatesViewModel()
    {
        _autoCheck = _store.GetString("autoCheckUpdates") != bool.FalseString; // default on
        _updates.StateChanged += Refresh;
        Refresh();
    }

    partial void OnAutoCheckChanged(bool value) =>
        _store.SetString("autoCheckUpdates", value ? bool.TrueString : bool.FalseString);

    [RelayCommand]
    private Task CheckAsync() => _updates.CheckAsync();

    [RelayCommand]
    private Task DownloadAsync() => _updates.DownloadAndPrepareAsync();

    [RelayCommand]
    private void Restart() => _updates.ApplyAndRestart();

    private void Refresh()
    {
        CanRestart = _updates.State == UpdateState.ReadyToRestart;
        StatusText = _updates.State switch
        {
            UpdateState.Checking => "…",
            UpdateState.UpToDate => Loc.Get("Updates_UpToDate"),
            UpdateState.Available => Loc.Get("Updates_Available"),
            UpdateState.Downloading => Loc.Get("Updates_Downloading"),
            UpdateState.ReadyToRestart => Loc.Get("Updates_Restart"),
            UpdateState.Error => "!",
            _ => "",
        };
    }
}
