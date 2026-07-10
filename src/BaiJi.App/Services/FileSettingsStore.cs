using System.Text.Json;
using BaiJi.Core;

namespace BaiJi.App.Services;

/// <summary>
/// File-backed <see cref="ISettingsStore"/> for UNPACKAGED apps. WinUI's
/// <c>Windows.Storage.ApplicationData.Current</c> throws without package identity
/// (which Velopack-distributed apps don't have), so settings are persisted as a
/// JSON dictionary under %LOCALAPPDATA%\BaiJi\settings.json instead.
/// </summary>
public sealed class FileSettingsStore : ISettingsStore
{
    private readonly string _path;
    private readonly object _lock = new();
    private readonly Dictionary<string, string> _values;

    public FileSettingsStore()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BaiJi");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "settings.json");
        _values = Load(_path);
    }

    private static Dictionary<string, string> Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                       ?? new Dictionary<string, string>();
            }
        }
        catch { /* corrupt/unreadable — start fresh */ }
        return new Dictionary<string, string>();
    }

    private void Persist()
    {
        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(_values));
        }
        catch { /* best effort; a failed write must not crash the app */ }
    }

    public string? GetString(string key)
    {
        lock (_lock) return _values.TryGetValue(key, out var v) ? v : null;
    }

    public void SetString(string key, string value)
    {
        lock (_lock) { _values[key] = value; Persist(); }
    }

    public long GetLong(string key)
    {
        lock (_lock)
            return _values.TryGetValue(key, out var v) && long.TryParse(v, out var l) ? l : 0;
    }

    public void SetLong(string key, long value)
    {
        lock (_lock) { _values[key] = value.ToString(); Persist(); }
    }

    public bool GetBool(string key)
    {
        lock (_lock) return _values.TryGetValue(key, out var v) && v == bool.TrueString;
    }

    public void SetBool(string key, bool value)
    {
        lock (_lock) { _values[key] = value ? bool.TrueString : bool.FalseString; Persist(); }
    }

    public void Remove(string key)
    {
        lock (_lock) { if (_values.Remove(key)) Persist(); }
    }
}
