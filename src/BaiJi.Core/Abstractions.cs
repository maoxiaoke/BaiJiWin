namespace BaiJi.Core;

/// <summary>
/// Key/value persistence for user preferences. Mirrors macOS's UserDefaults.
/// The WinUI app backs this with ApplicationData local settings; tests use an
/// in-memory implementation.
/// </summary>
public interface ISettingsStore
{
    string? GetString(string key);
    void SetString(string key, string value);
    long GetLong(string key);
    void SetLong(string key, long value);
    bool GetBool(string key);
    void SetBool(string key, bool value);
    void Remove(string key);
}

/// <summary>
/// Resolves the on-disk paths of the bundled media tools. On Windows this points
/// at the fetched win-x64 binaries copied next to the app; in tests it points at
/// whatever platform binaries are available (e.g. the macOS ones in ../BaiJi).
/// </summary>
public interface IToolLocator
{
    /// <summary>Full path to the ffmpeg executable, or null if unavailable.</summary>
    string? FFmpegPath { get; }

    /// <summary>Full path to the ImageMagick "magick" executable, or null.</summary>
    string? MagickPath { get; }

    /// <summary>Full path to the pngquant executable, or null.</summary>
    string? PngquantPath { get; }
}

/// <summary>
/// A tool locator over a fixed directory. Looks for the given basenames, trying
/// the ".exe" suffix first (Windows) then the bare name (macOS/Linux) so the same
/// Core works in Windows production and macOS test runs.
/// </summary>
public sealed class DirectoryToolLocator : IToolLocator
{
    private readonly string _directory;

    public DirectoryToolLocator(string directory) => _directory = directory;

    public string? FFmpegPath => Resolve("ffmpeg");
    public string? MagickPath => Resolve("magick");
    public string? PngquantPath => Resolve("pngquant");

    private string? Resolve(string name)
    {
        foreach (var candidate in new[] { name + ".exe", name })
        {
            var path = Path.Combine(_directory, candidate);
            if (File.Exists(path)) return path;
        }
        return null;
    }
}
