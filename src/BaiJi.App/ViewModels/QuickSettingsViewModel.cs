using BaiJi.App.Services;
using BaiJi.Core;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BaiJi.App.ViewModels;

/// <summary>
/// The always-visible quick controls (macOS's SettingsSummaryView): quality,
/// target size, image format, remove-audio. Writes through to the settings store
/// and asks the queue to re-evaluate staleness (debounced by the view).
/// </summary>
public sealed partial class QuickSettingsViewModel : ObservableObject
{
    private readonly ISettingsStore _store = AppServices.Instance.Settings;

    public IReadOnlyList<QualityPreset> Presets { get; } =
        new[] { QualityPreset.Smaller, QualityPreset.Balanced, QualityPreset.Clearer };

    public IReadOnlyList<ImageFormat> Formats { get; } =
        new[] { ImageFormat.KeepOriginal, ImageFormat.Jpeg, ImageFormat.Png, ImageFormat.Webp };

    // (label-key, bytes) target presets; 0 = no limit.
    public IReadOnlyList<(string Key, long Bytes)> Targets { get; } = new[]
    {
        ("Target_None", 0L),
        ("Target_5MB", 5L * 1024 * 1024),
        ("Target_Email", 10L * 1024 * 1024),
        ("Target_WeChat", 25L * 1024 * 1024),
    };

    [ObservableProperty] private QualityPreset _preset;
    [ObservableProperty] private long _targetBytes;
    [ObservableProperty] private ImageFormat _imageFormat;
    [ObservableProperty] private bool _removeAudio;

    public event Action? Changed;

    public QuickSettingsViewModel()
    {
        var s = CompressionSettings.Current(_store);
        _preset = s.Preset;
        _targetBytes = s.TargetBytes ?? 0;
        _imageFormat = s.ImageFormat;
        _removeAudio = s.RemoveAudio;
    }

    partial void OnPresetChanged(QualityPreset value) => Persist();
    partial void OnTargetBytesChanged(long value) => Persist();
    partial void OnImageFormatChanged(ImageFormat value) => Persist();
    partial void OnRemoveAudioChanged(bool value) => Persist();

    private void Persist()
    {
        new CompressionSettings
        {
            Preset = Preset,
            TargetBytes = TargetBytes > 0 ? TargetBytes : null,
            ImageFormat = ImageFormat,
            RemoveAudio = RemoveAudio,
        }.Save(_store);
        Changed?.Invoke();
    }

    public static string PresetLabel(QualityPreset p) => p switch
    {
        QualityPreset.Smaller => Loc.Get("Preset_Smaller"),
        QualityPreset.Balanced => Loc.Get("Preset_Balanced"),
        QualityPreset.Clearer => Loc.Get("Preset_Clearer"),
        _ => p.ToString(),
    };

    public static string FormatLabel(ImageFormat f) => f switch
    {
        ImageFormat.KeepOriginal => Loc.Get("Params_FormatKeep"),
        ImageFormat.Jpeg => "JPEG",
        ImageFormat.Png => "PNG",
        ImageFormat.Webp => "WebP",
        _ => f.ToString(),
    };
}
