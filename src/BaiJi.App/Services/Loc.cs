using Microsoft.Windows.ApplicationModel.Resources;

namespace BaiJi.App.Services;

/// <summary>
/// Thin wrapper over the WinUI resource loader (Strings\{lang}\Resources.resw).
/// Every lookup is guarded so a missing or unreadable resources.pri degrades to
/// the raw key (readable English-ish text) instead of crashing the app — which
/// matters most for unpackaged startup.
/// </summary>
public static class Loc
{
    private static readonly ResourceManager? Manager = TryCreateManager();
    private static ResourceContext? _context;

    private static ResourceManager? TryCreateManager()
    {
        try { return new ResourceManager(); }
        catch (Exception ex) { CrashLog.Write(ex, "Loc.ResourceManager"); return null; }
    }

    public static void UseLanguage(string? bcp47)
    {
        if (Manager is null) return;
        try
        {
            _context = Manager.CreateResourceContext();
            if (!string.IsNullOrEmpty(bcp47))
                _context.QualifierValues["Language"] = bcp47;
        }
        catch (Exception ex) { CrashLog.Write(ex, "Loc.UseLanguage"); }
    }

    public static string Get(string key)
    {
        if (Manager is null) return key;
        try
        {
            var candidate = _context is null
                ? Manager.MainResourceMap.TryGetValue($"Resources/{key}")
                : Manager.MainResourceMap.TryGetValue($"Resources/{key}", _context);
            return candidate?.ValueAsString ?? key;
        }
        catch
        {
            return key;
        }
    }

    /// <summary>Formats a resource that contains {0}-style placeholders.</summary>
    public static string Format(string key, params object[] args)
    {
        try { return string.Format(Get(key), args); }
        catch { return Get(key); }
    }
}
