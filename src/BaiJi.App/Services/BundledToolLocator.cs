using BaiJi.Core;

namespace BaiJi.App.Services;

/// <summary>
/// Resolves the ffmpeg/magick/pngquant executables bundled next to the app under
/// <c>Tools\</c> (populated by scripts/fetch-binaries.ps1 and copied to output).
/// </summary>
public sealed class BundledToolLocator : IToolLocator
{
    private readonly DirectoryToolLocator _inner;

    public BundledToolLocator()
    {
        var toolsDir = Path.Combine(AppContext.BaseDirectory, "Tools");
        _inner = new DirectoryToolLocator(toolsDir);
    }

    public string? FFmpegPath => _inner.FFmpegPath;
    public string? MagickPath => _inner.MagickPath;
    public string? PngquantPath => _inner.PngquantPath;
}
