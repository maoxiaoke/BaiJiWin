using System.Globalization;
using System.Text.RegularExpressions;

namespace BaiJi.Core;

public enum VideoConversionErrorKind
{
    FFmpegNotFound,
    ConversionFailed,
    ProcessRunError,
    InvalidInputFile,
    Cancelled,
}

public sealed class VideoConversionException : Exception
{
    public VideoConversionErrorKind Kind { get; }
    public int ExitCode { get; }
    public string Detail { get; }

    public VideoConversionException(VideoConversionErrorKind kind, int exitCode = 0, string detail = "")
        : base(kind.ToString())
    {
        Kind = kind;
        ExitCode = exitCode;
        Detail = detail;
    }
}

/// <summary>What a single <c>ffmpeg -i</c> probe run learns about the input.</summary>
public readonly record struct VideoProbe(bool HasAudio, double? DurationSeconds);

/// <summary>How the encoder decides quality: constant quality or a bitrate ceiling.</summary>
public abstract record Rate
{
    public sealed record Crf(int Value) : Rate;
    public sealed record TargetBitrate(int Kbps) : Rate;
}

/// <summary>
/// Compresses video to H.264 mp4 using the bundled ffmpeg. Faithful port of the
/// macOS VideoConverter: the preset path is a single CRF encode; the target-size
/// path is a two-pass bitrate encode (progress spans both passes: 0-0.5, 0.5-1).
/// </summary>
public sealed class VideoConverter
{
    private readonly IToolLocator _tools;
    private readonly IProcessRunner _runner;

    public VideoConverter(IToolLocator tools, IProcessRunner runner)
    {
        _tools = tools;
        _runner = runner;
    }

    public const int DefaultCrf = 30;
    /// <summary>libx264 accepts 0-51; we cap at 18-51 (above ~40 looks bad, below 18 wastes space).</summary>
    public static readonly (int Lower, int Upper) CrfRange = (18, 51);

    /// <summary>Human preset → CRF. The presets are the only quality control the UI exposes.</summary>
    public static int CrfFor(QualityPreset preset) => preset switch
    {
        QualityPreset.Smaller => 34,
        QualityPreset.Balanced => 30,
        QualityPreset.Clearer => 23,
        _ => 30,
    };

    /// <summary>
    /// Video bitrate (kbps) that lands <paramref name="targetBytes"/> for
    /// <paramref name="durationSeconds"/> of footage, minus an audio allowance and
    /// a 5% mux-overhead margin.
    /// </summary>
    public static int TargetBitrateKbps(long targetBytes, double durationSeconds, bool includesAudio)
    {
        if (durationSeconds <= 0) return 0;
        var totalKbps = targetBytes * 8.0 / 1000.0 / durationSeconds;
        var audioAllowance = includesAudio ? 128.0 : 0.0;
        var videoKbps = totalKbps * 0.95 - audioAllowance;
        return Math.Max((int)videoKbps, 50);
    }

    /// <summary>
    /// Builds the ffmpeg argument list for an mp4 compression. <paramref name="pass"/>
    /// is non-null only for two-pass bitrate encodes (1 = analysis pass).
    /// </summary>
    public static List<string> MakeFFmpegArguments(
        string inputPath,
        string outputPath,
        Rate rate,
        bool removeAudio,
        (int Number, string LogPrefix)? pass = null)
    {
        var arguments = new List<string> { "-y", "-hide_banner", "-i", inputPath, "-c:v", "libx264" };

        switch (rate)
        {
            case Rate.Crf crf:
                var clamped = Math.Min(Math.Max(crf.Value, CrfRange.Lower), CrfRange.Upper);
                arguments.AddRange(new[] { "-crf", clamped.ToString(CultureInfo.InvariantCulture) });
                break;
            case Rate.TargetBitrate tb:
                arguments.AddRange(new[]
                {
                    "-b:v", $"{tb.Kbps}k", "-maxrate", $"{tb.Kbps}k", "-bufsize", $"{tb.Kbps * 2}k",
                });
                break;
        }

        if (pass is { } p)
            arguments.AddRange(new[] { "-pass", p.Number.ToString(CultureInfo.InvariantCulture), "-passlogfile", p.LogPrefix });

        if (pass?.Number == 1)
        {
            // Analysis pass: no audio, no container, throwaway output.
            arguments.AddRange(new[] { "-preset", "superfast", "-pix_fmt", "yuv420p", "-an", "-f", "null", NullSink });
            return arguments;
        }

        arguments.AddRange(new[]
        {
            "-tag:v", "avc1", "-movflags", "faststart",
            "-pix_fmt", "yuv420p", "-profile:v", "high", "-preset", "superfast",
        });
        if (removeAudio) arguments.Add("-an");
        arguments.Add(outputPath);
        return arguments;
    }

    /// <summary>Platform null sink for the throwaway analysis pass.</summary>
    public static string NullSink =>
        OperatingSystem.IsWindows() ? "NUL" : "/dev/null";

    /// <summary>Returns true if ffmpeg's probe output mentions an audio stream.</summary>
    public static bool ContainsAudioStream(string ffmpegOutput) =>
        ffmpegOutput.Split('\n').Any(line => line.Contains("Stream #") && line.Contains("Audio:"));

    /// <summary>Parses "Duration: 00:01:23.45" from ffmpeg's probe output.</summary>
    public static double? ParseDuration(string ffmpegOutput)
    {
        var match = Regex.Match(ffmpegOutput, @"Duration: (\d+):(\d{2}):(\d{2}(?:\.\d+)?)");
        if (!match.Success) return null;
        return double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) * 3600
             + double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) * 60
             + double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
    }

    /// <summary>Parses "out_time=00:00:05.123456" from ffmpeg -progress output into seconds.</summary>
    public static double? ParseOutTime(string line)
    {
        const string prefix = "out_time=";
        if (!line.StartsWith(prefix, StringComparison.Ordinal)) return null;
        var value = line[prefix.Length..];
        var parts = value.Split(':');
        if (parts.Length != 3) return null;
        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var h) ||
            !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var m) ||
            !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var s))
            return null;
        return h * 3600 + m * 60 + s;
    }

    /// <summary>
    /// Probes the input with the bundled ffmpeg. Returns null when the probe
    /// couldn't run.
    /// </summary>
    public async Task<VideoProbe?> ProbeAsync(string inputPath, CancellationToken cancellationToken = default)
    {
        var ffmpegPath = _tools.FFmpegPath;
        if (ffmpegPath is null) return null;

        try
        {
            // `ffmpeg -i input` with no output exits non-zero but still prints the
            // stream info we need to stderr.
            var result = await _runner.RunAsync(
                ffmpegPath, new[] { "-hide_banner", "-i", inputPath },
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return new VideoProbe(ContainsAudioStream(result.Output), ParseDuration(result.Output));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Compresses a video file to H.264 mp4. The preset path is a single CRF
    /// encode; the target-size path is a two-pass bitrate encode.
    /// </summary>
    public async Task<string> ConvertVideoAsync(
        string inputPath,
        string outputDirectory,
        Rate? rate = null,
        bool removeAudio = false,
        double? durationSeconds = null,
        Action<IProcessHandle>? onStart = null,
        Action<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        rate ??= new Rate.Crf(DefaultCrf);
        var ffmpegPath = _tools.FFmpegPath
            ?? throw new VideoConversionException(VideoConversionErrorKind.FFmpegNotFound);

        // Unified "-compressed" suffix across images and videos.
        var outputPath = OutputNaming.AvailableOutputPath(inputPath, outputDirectory, "compressed", "mp4");

        var isTwoPass = rate is Rate.TargetBitrate;
        var logPrefix = Path.Combine(Path.GetTempPath(), $"baiji-2pass-{Guid.NewGuid():N}");

        try
        {
            var passes = isTwoPass
                ? new (int Number, string LogPrefix)?[] { (1, logPrefix), (2, logPrefix) }
                : new (int Number, string LogPrefix)?[] { null };

            for (var passIndex = 0; passIndex < passes.Length; passIndex++)
            {
                var pass = passes[passIndex];
                var arguments = MakeFFmpegArguments(inputPath, outputPath, rate, removeAudio, pass);
                // Machine-readable progress on stdout; keep human logs on stderr.
                arguments.InsertRange(1, new[] { "-progress", "pipe:1", "-nostats" });

                var passCount = (double)passes.Length;
                var passBase = passIndex / passCount;

                ProcessResult result;
                try
                {
                    result = await _runner.RunAsync(
                        ffmpegPath, arguments,
                        onStdoutLine: line =>
                        {
                            if (progress is null || durationSeconds is not { } duration || duration <= 0) return;
                            var outTime = ParseOutTime(line);
                            if (outTime is null) return;
                            var withinPass = Math.Min(Math.Max(outTime.Value / duration, 0), 1);
                            progress(passBase + withinPass / passCount);
                        },
                        onStart: onStart,
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    TryDelete(outputPath);
                    throw new VideoConversionException(VideoConversionErrorKind.Cancelled);
                }
                catch (Exception ex)
                {
                    throw new VideoConversionException(VideoConversionErrorKind.ProcessRunError, detail: ex.Message);
                }

                if (result.Status != 0)
                {
                    // A terminated (cancelled) run leaves a partial file behind.
                    TryDelete(outputPath);
                    throw new VideoConversionException(
                        VideoConversionErrorKind.ConversionFailed, result.Status, result.Output);
                }
            }

            return outputPath;
        }
        finally
        {
            if (isTwoPass)
            {
                // ffmpeg writes "<prefix>-0.log" (+ .mbtree) next to the prefix.
                foreach (var suffix in new[] { "-0.log", "-0.log.mbtree" })
                    TryDelete(logPrefix + suffix);
            }
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best effort */ }
    }
}
