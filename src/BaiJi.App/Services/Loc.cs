using Microsoft.Windows.ApplicationModel.Resources;

namespace BaiJi.App.Services;

/// <summary>
/// Thin wrapper over the WinUI resource loader (Strings\{lang}\Resources.resw).
/// The app-language override ("appLanguage") is applied at startup via
/// <see cref="Microsoft.Windows.Globalization.ApplicationLanguages"/>.
/// </summary>
public static class Loc
{
    private static readonly ResourceManager Manager = new();
    private static ResourceContext? _context;

    public static void UseLanguage(string? bcp47)
    {
        _context = Manager.CreateResourceContext();
        if (!string.IsNullOrEmpty(bcp47))
            _context.QualifierValues["Language"] = bcp47;
    }

    public static string Get(string key)
    {
        var candidate = _context is null
            ? Manager.MainResourceMap.TryGetValue($"Resources/{key}")
            : Manager.MainResourceMap.TryGetValue($"Resources/{key}", _context);
        return candidate?.ValueAsString ?? key;
    }

    /// <summary>Formats a resource that contains {0}-style placeholders.</summary>
    public static string Format(string key, params object[] args) => string.Format(Get(key), args);
}
