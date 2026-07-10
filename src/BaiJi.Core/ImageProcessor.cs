namespace BaiJi.Core;

public enum ImageProcessingErrorKind
{
    ToolNotFound,
    CompressionFailed,
}

public sealed class ImageProcessingException : Exception
{
    public ImageProcessingErrorKind Kind { get; }
    public string Detail { get; }

    public ImageProcessingException(ImageProcessingErrorKind kind, string detail = "")
        : base(kind.ToString())
    {
        Kind = kind;
        Detail = detail;
    }
}

/// <summary>
/// Compresses images with the bundled pngquant / ImageMagick, off the calling
/// thread. Semantics match macOS v4: the output keeps the input's format — no
/// silent format conversion — and quality comes from the human preset, optionally
/// tightened until the result fits under <c>targetBytes</c>.
/// </summary>
public sealed class ImageProcessor
{
    private readonly IToolLocator _tools;
    private readonly IProcessRunner _runner;

    public ImageProcessor(IToolLocator tools, IProcessRunner runner)
    {
        _tools = tools;
        _runner = runner;
    }

    /// <summary>
    /// Quality ladders per preset. The first entry is the preset's nominal
    /// quality; the rest are the fallback steps used when a target size is set.
    /// </summary>
    public static string[] MagickQualityLadder(QualityPreset preset) => preset switch
    {
        QualityPreset.Smaller => new[] { "50", "35", "25" },
        QualityPreset.Balanced => new[] { "85", "65", "45", "30" },
        // 92, not 100: quality 100 routinely *inflates* already-compressed JPEGs.
        QualityPreset.Clearer => new[] { "92", "85", "65", "45" },
        _ => new[] { "85", "65", "45", "30" },
    };

    public static string[] PngquantQualityLadder(QualityPreset preset) => preset switch
    {
        QualityPreset.Smaller => new[] { "25-50", "10-30" },
        QualityPreset.Balanced => new[] { "50-85", "25-50", "10-30" },
        QualityPreset.Clearer => new[] { "85-100", "50-85", "25-50" },
        _ => new[] { "50-85", "25-50", "10-30" },
    };

    /// <summary>
    /// Compresses <paramref name="inputPath"/> into <paramref name="outputDirectory"/>.
    /// Output format follows <paramref name="format"/> (default: keep the input
    /// format). When <paramref name="targetBytes"/> is set, walks down the quality
    /// ladder until the output fits (or returns the smallest attempt).
    /// </summary>
    public async Task<string> ProcessAsync(
        string inputPath,
        QualityPreset preset,
        long? targetBytes = null,
        ImageFormat format = ImageFormat.KeepOriginal,
        string outputDirectory = "",
        CancellationToken cancellationToken = default)
    {
        var pngquantPath = _tools.PngquantPath;
        var magickPath = _tools.MagickPath;
        if (pngquantPath is null || magickPath is null)
            throw new ImageProcessingException(ImageProcessingErrorKind.ToolNotFound);

        var inputExt = Path.GetExtension(inputPath).TrimStart('.').ToLowerInvariant();
        var ext = format.OutputExtension(inputExt);
        var outputPath = OutputNaming.AvailableOutputPath(inputPath, outputDirectory, "compressed", ext);

        // pngquant both reads and writes PNG only; use it just for png→png. Every
        // other combination (incl. format conversion) goes through magick.
        var isPng = ext == "png" && inputExt == "png";

        var ladder = isPng ? PngquantQualityLadder(preset) : MagickQualityLadder(preset);
        var steps = targetBytes is null ? new[] { ladder[0] } : ladder;

        var lastError = "";
        for (var index = 0; index < steps.Length; index++)
        {
            var quality = steps[index];
            ProcessResult result;
            if (isPng)
            {
                result = await _runner.RunAsync(pngquantPath, new[]
                {
                    $"--quality={quality}", "--output", outputPath, "--force",
                    // --skip-if-larger would fail the run when pngquant can't
                    // shrink; only use it when we have magick as a fallback.
                    inputPath,
                }, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            else
            {
                result = await _runner.RunAsync(magickPath, new[]
                {
                    inputPath, "-quality", quality, outputPath,
                }, cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            if (result.Status == 0)
            {
                var size = OutputNaming.FileSize(outputPath);
                if (targetBytes is null) return outputPath;
                if (size <= targetBytes || index == steps.Length - 1) return outputPath;
                // Target not met at this quality; step down.
            }
            else if (isPng)
            {
                // pngquant can refuse (e.g. exotic PNGs). Fall back to magick at
                // this ladder step's magick-equivalent quality — keep honoring the
                // preset and the target ladder instead of bailing at 85.
                lastError = result.Output;
                var magickLadder = MagickQualityLadder(preset);
                var magickQ = magickLadder[Math.Min(index, magickLadder.Length - 1)];
                var fallback = await _runner.RunAsync(magickPath, new[]
                {
                    inputPath, "-quality", magickQ, outputPath,
                }, cancellationToken: cancellationToken).ConfigureAwait(false);
                if (fallback.Status == 0)
                {
                    var size = OutputNaming.FileSize(outputPath);
                    if (targetBytes is null || size <= targetBytes || index == steps.Length - 1)
                        return outputPath;
                    // fits worse than target — continue down the ladder
                }
                else
                {
                    lastError += "\n---\n" + fallback.Output;
                    break;
                }
            }
            else
            {
                lastError = result.Output;
                break;
            }
        }

        throw new ImageProcessingException(ImageProcessingErrorKind.CompressionFailed, lastError);
    }
}
