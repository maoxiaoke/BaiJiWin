using BaiJi.Core;
using Xunit;

namespace BaiJi.Tests;

public class CompressionSettingsTests
{
    [Theory]
    [InlineData(QualityPreset.Smaller, "Low")]
    [InlineData(QualityPreset.Balanced, "Normal")]
    [InlineData(QualityPreset.Clearer, "High")]
    public void Preset_round_trips_through_raw_value(QualityPreset preset, string raw)
    {
        Assert.Equal(raw, preset.RawValue());
        Assert.Equal(preset, QualityPresetExtensions.FromRawValue(raw));
    }

    [Fact]
    public void Preset_from_unknown_raw_is_null()
    {
        Assert.Null(QualityPresetExtensions.FromRawValue("bogus"));
    }

    [Theory]
    [InlineData(ImageFormat.KeepOriginal, "")]
    [InlineData(ImageFormat.Jpeg, "jpg")]
    [InlineData(ImageFormat.Png, "png")]
    [InlineData(ImageFormat.Webp, "webp")]
    public void ImageFormat_round_trips(ImageFormat format, string raw)
    {
        Assert.Equal(raw, format.RawValue());
        if (raw != "") Assert.Equal(format, ImageFormatExtensions.FromRawValue(raw));
    }

    [Fact]
    public void OutputExtension_keeps_original_lowercased_or_overrides()
    {
        Assert.Equal("png", ImageFormat.KeepOriginal.OutputExtension("PNG"));
        Assert.Equal("jpg", ImageFormat.Jpeg.OutputExtension("png"));
    }

    [Fact]
    public void Current_reads_and_Save_writes_the_store()
    {
        var store = new InMemorySettingsStore();
        var settings = new CompressionSettings
        {
            Preset = QualityPreset.Clearer,
            TargetBytes = 25_000_000,
            RemoveAudio = true,
            ImageFormat = ImageFormat.Webp,
        };
        settings.Save(store);

        var loaded = CompressionSettings.Current(store);
        Assert.Equal(QualityPreset.Clearer, loaded.Preset);
        Assert.Equal(25_000_000, loaded.TargetBytes);
        Assert.True(loaded.RemoveAudio);
        Assert.Equal(ImageFormat.Webp, loaded.ImageFormat);
    }

    [Fact]
    public void Current_defaults_when_store_empty()
    {
        var loaded = CompressionSettings.Current(new InMemorySettingsStore());
        Assert.Equal(QualityPreset.Balanced, loaded.Preset);
        Assert.Null(loaded.TargetBytes);
        Assert.False(loaded.RemoveAudio);
        Assert.Equal(ImageFormat.KeepOriginal, loaded.ImageFormat);
    }

    [Fact]
    public void Zero_target_is_treated_as_no_limit()
    {
        var store = new InMemorySettingsStore();
        store.SetLong(CompressionSettings.SettingsTargetKey, 0);
        Assert.Null(CompressionSettings.Current(store).TargetBytes);
    }

    [Fact]
    public void Relevant_equality_isolates_image_and_video_fields()
    {
        var a = new CompressionSettings { Preset = QualityPreset.Balanced, RemoveAudio = false, ImageFormat = ImageFormat.Png };
        var videoOnlyDiff = a with { RemoveAudio = true };
        var imageOnlyDiff = a with { ImageFormat = ImageFormat.Jpeg };

        Assert.True(a.ImageRelevantEquals(videoOnlyDiff));   // audio doesn't affect images
        Assert.False(a.VideoRelevantEquals(videoOnlyDiff));
        Assert.True(a.VideoRelevantEquals(imageOnlyDiff));   // format doesn't affect video
        Assert.False(a.ImageRelevantEquals(imageOnlyDiff));
    }
}

public class OutputNamingTests
{
    [Fact]
    public void Adds_compressed_suffix()
    {
        var dir = TestSupport.NewTempDir();
        try
        {
            var result = OutputNaming.AvailableOutputPath(Path.Combine(dir, "photo.jpg"), dir, "compressed", "jpg");
            Assert.Equal(Path.Combine(dir, "photo-compressed.jpg"), result);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Collision_appends_finder_style_counter()
    {
        var dir = TestSupport.NewTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "photo-compressed.jpg"), "x");
            File.WriteAllText(Path.Combine(dir, "photo-compressed 2.jpg"), "x");
            var result = OutputNaming.AvailableOutputPath(Path.Combine(dir, "photo.jpg"), dir, "compressed", "jpg");
            Assert.Equal(Path.Combine(dir, "photo-compressed 3.jpg"), result);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Theory]
    [InlineData("photo-compressed", "photo-compressed.png")]
    [InlineData("photo-converted", "photo-compressed.png")]
    [InlineData("photo-compressed 2", "photo-compressed.png")]
    [InlineData("clip-converted 5", "clip-compressed.png")]
    public void Existing_suffix_is_stripped_before_reapplying(string stem, string expectedName)
    {
        var dir = TestSupport.NewTempDir();
        try
        {
            var result = OutputNaming.AvailableOutputPath(Path.Combine(dir, stem + ".png"), dir, "compressed", "png");
            Assert.Equal(Path.Combine(dir, expectedName), result);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void FileSize_of_missing_file_is_zero()
    {
        Assert.Equal(0, OutputNaming.FileSize(Path.Combine(TestSupport.NewTempDir(), "nope.bin")));
    }
}

public class MediaTaskTests
{
    [Theory]
    [InlineData("a.png", MediaKind.Image)]
    [InlineData("a.JPG", MediaKind.Image)]
    [InlineData("a.webp", MediaKind.Image)]
    [InlineData("a.mp4", MediaKind.Video)]
    [InlineData("a.MKV", MediaKind.Video)]
    [InlineData("a.txt", MediaKind.Unsupported)]
    [InlineData("noext", MediaKind.Unsupported)]
    public void KindOf_classifies_by_extension(string name, MediaKind expected)
    {
        Assert.Equal(expected, MediaTask.KindOf(name));
    }

    [Fact]
    public void Savings_and_finished_flags()
    {
        var t = new MediaTask(Guid.NewGuid(), "a.png", MediaKind.Image, 1000, TaskState.Done, new CompressionSettings())
        {
            OutputSize = 250,
        };
        Assert.Equal(0.75, t.Savings!.Value, 3);
        Assert.True(t.IsFinished);

        var queued = new MediaTask(Guid.NewGuid(), "a.png", MediaKind.Image, 1000, TaskState.Queued, new CompressionSettings());
        Assert.Null(queued.Savings);
        Assert.False(queued.IsFinished);
    }

    [Fact]
    public void BatchSummary_aggregates_progress_and_savings()
    {
        var b = Guid.NewGuid();
        var done = new MediaTask(b, "a.png", MediaKind.Image, 1000, TaskState.Done, new CompressionSettings()) { OutputSize = 400, OutputPath = "a-compressed.png" };
        var running = new MediaTask(b, "c.mp4", MediaKind.Video, 2000, TaskState.Processing, new CompressionSettings()) { Progress = 0.5 };
        var summary = new BatchSummary(b, new[] { done, running });

        Assert.Equal(3000, summary.TotalOriginalSize);
        Assert.Equal(400, summary.TotalOutputSize);
        Assert.False(summary.IsFinished);
        Assert.Equal(0.75, summary.Progress, 3); // (1 + 0.5) / 2
        Assert.Equal(0.6, summary.Savings!.Value, 3); // done-only: 1 - 400/1000
    }

    [Fact]
    public void BatchSummary_empty_progress_is_zero()
    {
        Assert.Equal(0, new BatchSummary(Guid.NewGuid(), Array.Empty<MediaTask>()).Progress);
    }
}

public class ClipboardSupportTests
{
    [Fact]
    public void Picks_first_supported_media_file()
    {
        var chosen = ClipboardSupport.FirstSupportedFile(new[] { "note.txt", "clip.mp4", "pic.png" });
        Assert.Equal("clip.mp4", chosen);
    }

    [Fact]
    public void Returns_null_when_nothing_supported()
    {
        Assert.Null(ClipboardSupport.FirstSupportedFile(new[] { "a.txt", "b.doc" }));
    }
}

public class InMemorySettingsStoreTests
{
    [Fact]
    public void Stores_and_removes_typed_values()
    {
        var s = new InMemorySettingsStore();
        s.SetString("a", "x"); s.SetLong("b", 42); s.SetBool("c", true);
        Assert.Equal("x", s.GetString("a"));
        Assert.Equal(42, s.GetLong("b"));
        Assert.True(s.GetBool("c"));

        s.Remove("a");
        Assert.Null(s.GetString("a"));
        Assert.Equal(0, s.GetLong("missing"));
        Assert.False(s.GetBool("missing"));
    }
}
