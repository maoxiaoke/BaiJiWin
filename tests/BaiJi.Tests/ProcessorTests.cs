using BaiJi.Core;
using Xunit;

namespace BaiJi.Tests;

public class ImageProcessorUnitTests
{
    [Fact]
    public void Magick_ladders_match_the_presets()
    {
        Assert.Equal(new[] { "50", "35", "25" }, ImageProcessor.MagickQualityLadder(QualityPreset.Smaller));
        Assert.Equal(new[] { "85", "65", "45", "30" }, ImageProcessor.MagickQualityLadder(QualityPreset.Balanced));
        Assert.Equal(new[] { "92", "85", "65", "45" }, ImageProcessor.MagickQualityLadder(QualityPreset.Clearer));
    }

    [Fact]
    public void Pngquant_ladders_match_the_presets()
    {
        Assert.Equal(new[] { "25-50", "10-30" }, ImageProcessor.PngquantQualityLadder(QualityPreset.Smaller));
        Assert.Equal(new[] { "50-85", "25-50", "10-30" }, ImageProcessor.PngquantQualityLadder(QualityPreset.Balanced));
        Assert.Equal(new[] { "85-100", "50-85", "25-50" }, ImageProcessor.PngquantQualityLadder(QualityPreset.Clearer));
    }

    [Fact]
    public async Task Missing_tools_throw_ToolNotFound()
    {
        var tools = new DirectoryToolLocator(TestSupport.NewTempDir()); // empty dir
        var proc = new ImageProcessor(tools, new FakeProcessRunner((_, _) => new ProcessResult(0, "")));
        var ex = await Assert.ThrowsAsync<ImageProcessingException>(() =>
            proc.ProcessAsync("a.png", QualityPreset.Balanced, outputDirectory: TestSupport.NewTempDir()));
        Assert.Equal(ImageProcessingErrorKind.ToolNotFound, ex.Kind);
    }

    [Fact]
    public async Task Png_input_uses_pngquant_without_a_target()
    {
        var dir = TestSupport.NewTempDir();
        var tools = FakeTools(dir);
        var runner = new FakeProcessRunner((_, args) => new ProcessResult(0, ""))
        {
            SideEffect = (_, args) => File.WriteAllText(args[^1], "out"), // pngquant --output <path>
        };
        var proc = new ImageProcessor(tools, runner);

        var output = await proc.ProcessAsync(Path.Combine(dir, "a.png"), QualityPreset.Balanced, outputDirectory: dir);

        Assert.Single(runner.Calls);
        Assert.Contains("pngquant", runner.Calls[0].Tool);
        Assert.Contains("--quality=50-85", runner.Calls[0].Args); // first ladder step, no target
        Assert.EndsWith("a-compressed.png", output);
    }

    [Fact]
    public async Task Non_png_uses_magick_and_target_walks_the_ladder_until_it_fits()
    {
        var dir = TestSupport.NewTempDir();
        var tools = FakeTools(dir);
        // magick writes to args[^1]; simulate: first two steps too big, third fits.
        var step = 0;
        var runner = new FakeProcessRunner((_, _) => new ProcessResult(0, ""))
        {
            SideEffect = (_, args) =>
            {
                var size = step switch { 0 => 5000, 1 => 3000, _ => 900 };
                step++;
                File.WriteAllBytes(args[^1], new byte[size]);
            },
        };
        var proc = new ImageProcessor(tools, runner);

        var output = await proc.ProcessAsync(
            Path.Combine(dir, "a.jpg"), QualityPreset.Balanced, targetBytes: 1000, outputDirectory: dir);

        Assert.Equal(3, runner.Calls.Count); // walked 85 -> 65 -> 45
        Assert.Equal(new[] { "85", "65", "45" }, runner.Calls.Select(c => c.Args[2]).ToArray());
        Assert.True(new FileInfo(output).Length <= 1000);
    }

    [Fact]
    public async Task Magick_failure_surfaces_CompressionFailed_with_detail()
    {
        var dir = TestSupport.NewTempDir();
        var tools = FakeTools(dir);
        var runner = new FakeProcessRunner((_, _) => new ProcessResult(1, "magick: boom"));
        var proc = new ImageProcessor(tools, runner);

        var ex = await Assert.ThrowsAsync<ImageProcessingException>(() =>
            proc.ProcessAsync(Path.Combine(dir, "a.jpg"), QualityPreset.Balanced, outputDirectory: dir));
        Assert.Equal(ImageProcessingErrorKind.CompressionFailed, ex.Kind);
        Assert.Contains("boom", ex.Detail);
    }

    [Fact]
    public async Task Pngquant_refusal_falls_back_to_magick()
    {
        var dir = TestSupport.NewTempDir();
        var tools = FakeTools(dir);
        var runner = new FakeProcessRunner((tool, _) => tool.Contains("pngquant") ? new ProcessResult(99, "refused") : new ProcessResult(0, ""))
        {
            SideEffect = (tool, args) => { if (tool.Contains("magick")) File.WriteAllText(args[^1], "ok"); },
        };
        var proc = new ImageProcessor(tools, runner);

        var output = await proc.ProcessAsync(Path.Combine(dir, "a.png"), QualityPreset.Balanced, outputDirectory: dir);
        Assert.True(File.Exists(output));
        Assert.Contains(runner.Calls, c => c.Tool.Contains("magick"));
    }

    private static IToolLocator FakeTools(string dir)
    {
        // Create empty marker files so DirectoryToolLocator resolves paths.
        foreach (var t in new[] { "magick", "pngquant", "ffmpeg" })
            File.WriteAllText(Path.Combine(dir, t), "");
        return new DirectoryToolLocator(dir);
    }
}

public class VideoConverterUnitTests
{
    [Theory]
    [InlineData(QualityPreset.Smaller, 34)]
    [InlineData(QualityPreset.Balanced, 30)]
    [InlineData(QualityPreset.Clearer, 23)]
    public void Crf_maps_the_presets(QualityPreset preset, int crf) => Assert.Equal(crf, VideoConverter.CrfFor(preset));

    [Fact]
    public void Crf_args_are_clamped_to_the_safe_range()
    {
        var args = VideoConverter.MakeFFmpegArguments("in.mp4", "out.mp4", new Rate.Crf(5), removeAudio: false);
        var idx = args.IndexOf("-crf");
        Assert.Equal("18", args[idx + 1]); // clamped up from 5
        Assert.Contains("libx264", args);
        Assert.Contains("faststart", args);
        Assert.DoesNotContain("-an", args);
    }

    [Fact]
    public void Remove_audio_adds_an_flag()
    {
        var args = VideoConverter.MakeFFmpegArguments("in.mp4", "out.mp4", new Rate.Crf(30), removeAudio: true);
        Assert.Contains("-an", args);
    }

    [Fact]
    public void Target_bitrate_args_set_bv_maxrate_bufsize()
    {
        var args = VideoConverter.MakeFFmpegArguments("in.mp4", "out.mp4", new Rate.TargetBitrate(800), removeAudio: false);
        Assert.Contains("-b:v", args);
        Assert.Contains("800k", args);
        Assert.Contains("1600k", args); // bufsize = 2x
    }

    [Fact]
    public void First_pass_is_a_throwaway_analysis_pass()
    {
        var args = VideoConverter.MakeFFmpegArguments("in.mp4", "out.mp4", new Rate.TargetBitrate(800), removeAudio: false, pass: (1, "/tmp/log"));
        Assert.Contains("-pass", args);
        Assert.Contains("1", args);
        Assert.Contains(VideoConverter.NullSink, args);
        Assert.Contains("-an", args); // analysis drops audio
    }

    [Fact]
    public void TargetBitrate_math_backs_out_audio_and_overhead()
    {
        // 10 MB over 60s, keeping audio: (10e6*8/1000/60)*0.95 - 128
        var kbps = VideoConverter.TargetBitrateKbps(10_000_000, 60, includesAudio: true);
        Assert.Equal((int)(10_000_000 * 8.0 / 1000 / 60 * 0.95 - 128), kbps);
        Assert.True(kbps >= 50);
    }

    [Fact]
    public void TargetBitrate_floors_at_50_and_handles_zero_duration()
    {
        Assert.Equal(50, VideoConverter.TargetBitrateKbps(1, 3600, includesAudio: true));
        Assert.Equal(0, VideoConverter.TargetBitrateKbps(1_000_000, 0, includesAudio: false));
    }

    [Fact]
    public void Detects_audio_stream_from_probe_text()
    {
        Assert.True(VideoConverter.ContainsAudioStream("Stream #0:1: Audio: aac"));
        Assert.False(VideoConverter.ContainsAudioStream("Stream #0:0: Video: h264"));
    }

    [Fact]
    public void Parses_duration_and_out_time()
    {
        Assert.Equal(83.45, VideoConverter.ParseDuration("Duration: 00:01:23.45, start: 0")!.Value, 2);
        Assert.Null(VideoConverter.ParseDuration("no duration here"));
        Assert.Equal(5.123456, VideoConverter.ParseOutTime("out_time=00:00:05.123456")!.Value, 5);
        Assert.Null(VideoConverter.ParseOutTime("frame=10"));
    }
}
