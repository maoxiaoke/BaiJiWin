using BaiJi.Core;
using Windows.Storage;

namespace BaiJi.App.Services;

/// <summary>
/// <see cref="ISettingsStore"/> backed by the app's roaming/local settings —
/// the Windows analogue of macOS's UserDefaults. Values are stored as strings so
/// the same keys survive across launches.
/// </summary>
public sealed class AppDataSettingsStore : ISettingsStore
{
    private readonly ApplicationDataContainer _container = ApplicationData.Current.LocalSettings;

    public string? GetString(string key) => _container.Values.TryGetValue(key, out var v) ? v as string : null;
    public void SetString(string key, string value) => _container.Values[key] = value;

    public long GetLong(string key) =>
        _container.Values.TryGetValue(key, out var v) && long.TryParse(v as string, out var l) ? l : 0;
    public void SetLong(string key, long value) => _container.Values[key] = value.ToString();

    public bool GetBool(string key) =>
        _container.Values.TryGetValue(key, out var v) && v as string == bool.TrueString;
    public void SetBool(string key, bool value) => _container.Values[key] = value ? bool.TrueString : bool.FalseString;

    public void Remove(string key) => _container.Values.Remove(key);
}
