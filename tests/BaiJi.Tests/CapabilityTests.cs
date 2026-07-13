using System.Globalization;
using System.Text.RegularExpressions;
using BaiJi.Core;
using Xunit;
using Xunit.Abstractions;

namespace BaiJi.Tests;

/// <summary>
/// A capability matrix exercised against the real bundled binaries (win-x64 in
/// CI, macOS locally). Every output is re-probed to prove it's a valid, decodable
/// artifact — not just a non-empty file. Sizes are logged as a capability report.
/// </summary>
[Trait("Category", "Capability")]
public class CapabilityTests
{
    private readonly ITestOutputHelper _log;
    private readonly ImageProcessor _image = new(TestSupport.Tools(), new ProcessRunner());
    private readonly VideoConverter _video = new(TestSupport.Tools(), new ProcessRunner());

    public CapabilityTests(ITestOutputHelper log) => _log = log;

    // ---- Images: compress, keep the input format -----------------------------

    [SkippableTheory]
    [InlineData("sample.jpg")]
    [InlineData("sample.png")]
    [InlineData("sample.gif")]
    public async Task Compresses_image_keeping_format(string name)
    {
        Skip.IfNot(TestSupport.HasTools && TestSupport.HasAssets);
        var dir = TestSupport.NewTempDir();
        try
        {
            var input = TestSupport.Asset(Path.Combine("images", name));
            var output = await _image.ProcessAsync(input, QualityPreset.Balanced, outputDirectory: dir);

            Assert.True(File.Exists(output));
            Assert.Equal(Path.GetExtension(name).ToLowerInvariant(), Path.GetExtension(output).ToLowerInvariant());
            var (w, h) = await Identify(output);
            Assert.True(w > 0 && h > 0, "output is not a decodable image");
            _log.WriteLine($"{name}: {Size(input)} -> {Size(output)}  ({w}x{h})");
        }
        finally { Directory.Delete(dir, true); }
    }

    // ---- Images: explicit format conversion ----------------------------------

    [SkippableTheory]
    [InlineData("sample.png", ImageFormat.Jpeg, "jpg")]
    [InlineData("sample.png", ImageFormat.Webp, "webp")]
    [InlineData("sample.jpg", ImageFormat.Png, "png")]
    [InlineData("sample.jpg", ImageFormat.Webp, "webp")]
    // Note: animated GIF -> a static format is intentionally not covered — magick
    // emits one file per frame, which isn't a single-output compression.
    public async Task Converts_image_format(string name, ImageFormat format, string ext)
    {
        Skip.IfNot(TestSupport.HasTools && TestSupport.HasAssets);
        var dir = TestSupport.NewTempDir();
        try
        {
            var input = TestSupport.Asset(Path.Combine("images", name));
            var output = await _image.ProcessAsync(input, QualityPreset.Balanced, format: format, outputDirectory: dir);

            Assert.Equal("." + ext, Path.GetExtension(output).ToLowerInvariant());
            var (w, h) = await Identify(output);
            Assert.True(w > 0 && h > 0, $"{name}->{ext} did not produce a valid image");
            _log.WriteLine($"{name} -> {ext}: {Size(output)} ({w}x{h})");
        }
        finally { Directory.Delete(dir, true); }
    }

    [SkippableTheory]
    [InlineData(QualityPreset.Smaller)]
    [InlineData(QualityPreset.Balanced)]
    [InlineData(QualityPreset.Clearer)]
    public async Task Every_quality_preset_produces_a_valid_jpeg(QualityPreset preset)
    {
        Skip.IfNot(TestSupport.HasTools && TestSupport.HasAssets);
        var dir = TestSupport.NewTempDir();
        try
        {
            var input = TestSupport.Asset(Path.Combine("images", "sample.jpg"));
            var output = await _image.ProcessAsync(input, preset, outputDirectory: dir);
            var (w, h) = await Identify(output);
            Assert.True(w > 0 && h > 0);
            _log.WriteLine($"jpg @ {preset}: {Size(output)}");
        }
        finally { Directory.Delete(dir, true); }
    }

    // ---- Video: every accepted container transcodes to a valid mp4 -----------

    [SkippableTheory]
    [InlineData("sample-no-audio.mp4")]
    [InlineData("sample-with-audio.mp4")]
    [InlineData("sample-with-audio.mkv")]
    [InlineData("sample-with-audio.mov")]
    public async Task Transcodes_video_container_to_mp4(string name)
    {
        Skip.IfNot(TestSupport.HasTools && TestSupport.HasAssets);
        var input = TestSupport.Asset(Path.Combine("videos", name));
        Skip.IfNot(File.Exists(input));
        var dir = TestSupport.NewTempDir();
        try
        {
            var probe = await _video.ProbeAsync(input);
            var output = await _video.ConvertVideoAsync(
                input, dir, new Rate.Crf(VideoConverter.CrfFor(QualityPreset.Balanced)),
                durationSeconds: probe?.DurationSeconds);

            Assert.EndsWith(".mp4", output);
            var outProbe = await _video.ProbeAsync(output);
            Assert.NotNull(outProbe);
            Assert.True(outProbe!.Value.DurationSeconds is > 0, "output video has no duration");
            _log.WriteLine($"{name}: {Size(input)} -> {Size(output)}");
        }
        finally { Directory.Delete(dir, true); }
    }

    [SkippableFact]
    public async Task Removes_audio_track_from_video()
    {
        Skip.IfNot(TestSupport.HasTools && TestSupport.HasAssets);
        var input = TestSupport.Asset(Path.Combine("videos", "sample-with-audio.mp4"));
        Skip.IfNot(File.Exists(input));
        var dir = TestSupport.NewTempDir();
        try
        {
            Assert.True((await _video.ProbeAsync(input))!.Value.HasAudio, "sample should have audio to start");
            var output = await _video.ConvertVideoAsync(
                input, dir, new Rate.Crf(30), removeAudio: true,
                durationSeconds: (await _video.ProbeAsync(input))?.DurationSeconds);
            Assert.False((await _video.ProbeAsync(output))!.Value.HasAudio, "audio was not stripped");
            _log.WriteLine($"remove-audio: {Size(input)} -> {Size(output)}");
        }
        finally { Directory.Delete(dir, true); }
    }

    [SkippableFact]
    public async Task Target_size_two_pass_lands_under_ceiling()
    {
        Skip.IfNot(TestSupport.HasTools && TestSupport.HasAssets);
        var input = TestSupport.Asset(Path.Combine("videos", "sample-with-audio.mp4"));
        Skip.IfNot(File.Exists(input));
        var dir = TestSupport.NewTempDir();
        try
        {
            var probe = await _video.ProbeAsync(input);
            Skip.IfNot(probe?.DurationSeconds is > 0);
            long target = Math.Max(MediaTask.FileSize(input) / 3, 50_000);
            var kbps = VideoConverter.TargetBitrateKbps(target, probe!.Value.DurationSeconds!.Value, probe.Value.HasAudio);
            var output = await _video.ConvertVideoAsync(
                input, dir, new Rate.TargetBitrate(kbps), durationSeconds: probe.Value.DurationSeconds);
            Assert.True(MediaTask.FileSize(output) < MediaTask.FileSize(input));
            _log.WriteLine($"target-size: {Size(input)} -> {Size(output)} (ceiling {target}B)");
        }
        finally { Directory.Delete(dir, true); }
    }

    // ---- helpers -------------------------------------------------------------

    private static string Size(string path) => $"{MediaTask.FileSize(path):n0}B";

    /// <summary>Runs `magick identify` to confirm the output decodes; returns pixel size.</summary>
    private async Task<(int W, int H)> Identify(string path)
    {
        var magick = TestSupport.Tools().MagickPath!;
        var result = await new ProcessRunner().RunAsync(magick, new[] { "identify", "-format", "%wx%h", path });
        var m = Regex.Match(result.Output, @"(\d+)x(\d+)");
        return m.Success
            ? (int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture))
            : (0, 0);
    }
}
