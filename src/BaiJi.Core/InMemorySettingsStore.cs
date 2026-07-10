using System.Collections.Concurrent;
using System.Globalization;

namespace BaiJi.Core;

/// <summary>
/// A thread-safe in-memory <see cref="ISettingsStore"/>. Used by tests and as a
/// fallback; the WinUI app backs the same interface with ApplicationData.
/// </summary>
public sealed class InMemorySettingsStore : ISettingsStore
{
    private readonly ConcurrentDictionary<string, string> _values = new();

    public string? GetString(string key) => _values.TryGetValue(key, out var v) ? v : null;
    public void SetString(string key, string value) => _values[key] = value;

    public long GetLong(string key) =>
        _values.TryGetValue(key, out var v) && long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) ? l : 0;
    public void SetLong(string key, long value) => _values[key] = value.ToString(CultureInfo.InvariantCulture);

    public bool GetBool(string key) => _values.TryGetValue(key, out var v) && v == bool.TrueString;
    public void SetBool(string key, bool value) => _values[key] = value ? bool.TrueString : bool.FalseString;

    public void Remove(string key) => _values.TryRemove(key, out _);
}
