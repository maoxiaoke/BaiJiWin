namespace BaiJi.Core;

/// <summary>
/// The three human-language quality tiers. The string values match the legacy
/// macOS "defaultQuality" values so a settings store shared in spirit keeps the
/// same vocabulary ("Low"/"Normal"/"High").
/// </summary>
public enum QualityPreset
{
    Smaller,
    Balanced,
    Clearer,
}

public static class QualityPresetExtensions
{
    public static string RawValue(this QualityPreset preset) => preset switch
    {
        QualityPreset.Smaller => "Low",
        QualityPreset.Balanced => "Normal",
        QualityPreset.Clearer => "High",
        _ => "Normal",
    };

    public static QualityPreset? FromRawValue(string? raw) => raw switch
    {
        "Low" => QualityPreset.Smaller,
        "Normal" => QualityPreset.Balanced,
        "High" => QualityPreset.Clearer,
        _ => null,
    };
}

/// <summary>
/// Output format for images. The default keeps the input format — converting is
/// an explicit choice, not a silent default. Videos always output mp4 and ignore
/// this.
/// </summary>
public enum ImageFormat
{
    KeepOriginal,
    Jpeg,
    Png,
    Webp,
}

public static class ImageFormatExtensions
{
    public static string RawValue(this ImageFormat format) => format switch
    {
        ImageFormat.KeepOriginal => "",
        ImageFormat.Jpeg => "jpg",
        ImageFormat.Png => "png",
        ImageFormat.Webp => "webp",
        _ => "",
    };

    public static ImageFormat FromRawValue(string? raw) => raw switch
    {
        "jpg" => ImageFormat.Jpeg,
        "png" => ImageFormat.Png,
        "webp" => ImageFormat.Webp,
        _ => ImageFormat.KeepOriginal,
    };

    /// <summary>The file extension to write, given the input's extension.</summary>
    public static string OutputExtension(this ImageFormat format, string inputExtension) =>
        format == ImageFormat.KeepOriginal ? inputExtension.ToLowerInvariant() : format.RawValue();
}

/// <summary>
/// Everything the user can influence about a compression, in their language.
/// Codec parameters (CRF, pngquant ranges, bitrates) are derived internally.
/// </summary>
public sealed record CompressionSettings
{
    public QualityPreset Preset { get; init; } = QualityPreset.Balanced;

    /// <summary>Hard output ceiling ("≤ 25 MB for WeChat"); null = no limit.</summary>
    public long? TargetBytes { get; init; }

    /// <summary>Strip audio from videos.</summary>
    public bool RemoveAudio { get; init; }

    /// <summary>Image output format; videos are unaffected.</summary>
    public ImageFormat ImageFormat { get; init; } = ImageFormat.KeepOriginal;

    public const string SettingsTargetKey = "targetSizeBytes";
    public const string SettingsRemoveAudioKey = "defaultRemoveAudio";
    public const string SettingsPresetKey = "defaultQuality";
    public const string SettingsFormatKey = "defaultImageFormat";

    /// <summary>Snapshot of the current app-wide settings, taken at enqueue time.</summary>
    public static CompressionSettings Current(ISettingsStore store)
    {
        var preset = QualityPresetExtensions.FromRawValue(store.GetString(SettingsPresetKey))
            ?? QualityPreset.Balanced;
        var target = store.GetLong(SettingsTargetKey);
        return new CompressionSettings
        {
            Preset = preset,
            TargetBytes = target > 0 ? target : null,
            RemoveAudio = store.GetBool(SettingsRemoveAudioKey),
            ImageFormat = ImageFormatExtensions.FromRawValue(store.GetString(SettingsFormatKey)),
        };
    }

    public void Save(ISettingsStore store)
    {
        store.SetString(SettingsPresetKey, Preset.RawValue());
        store.SetLong(SettingsTargetKey, TargetBytes ?? 0);
        store.SetBool(SettingsRemoveAudioKey, RemoveAudio);
        store.SetString(SettingsFormatKey, ImageFormat.RawValue());
    }

    /// <summary>
    /// Equal in the fields that actually change an image's output — so tweaking a
    /// video-only knob never marks image results stale, and vice versa.
    /// </summary>
    public bool ImageRelevantEquals(CompressionSettings other) =>
        Preset == other.Preset && TargetBytes == other.TargetBytes && ImageFormat == other.ImageFormat;

    public bool VideoRelevantEquals(CompressionSettings other) =>
        Preset == other.Preset && TargetBytes == other.TargetBytes && RemoveAudio == other.RemoveAudio;
}
