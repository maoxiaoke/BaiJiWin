using BaiJi.Core;
using Xunit;

namespace BaiJi.Tests;

public class MediaQueueTests
{
    private static (MediaQueue Queue, InMemorySettingsStore Store, string Dir) NewQueue()
    {
        var dir = TestSupport.NewTempDir();
        var store = new InMemorySettingsStore();
        var tools = new DirectoryToolLocator(dir); // no binaries; workers are overridden
        var runner = new FakeProcessRunner((_, _) => new ProcessResult(0, ""));
        var queue = new MediaQueue(store, new ImageProcessor(tools, runner), new VideoConverter(tools, runner));
        return (queue, store, dir);
    }

    private static string MakeFile(string dir, string name, int size = 1000)
    {
        var path = Path.Combine(dir, name);
        File.WriteAllBytes(path, new byte[size]);
        return path;
    }

    /// <summary>Image worker that just writes a smaller output file next to the input.</summary>
    private static ImageWorker FakeImage(int outSize = 300) => (src, _, outDir, _) =>
    {
        var outPath = OutputNaming.AvailableOutputPath(src, outDir, "compressed", Path.GetExtension(src).TrimStart('.'));
        File.WriteAllBytes(outPath, new byte[outSize]);
        return Task.FromResult(outPath);
    };

    [Fact]
    public async Task Enqueue_processes_an_image_to_done()
    {
        var (queue, _, dir) = NewQueue();
        queue.ImageWorker = FakeImage();
        var input = MakeFile(dir, "a.png");

        queue.Enqueue(new[] { input });
        await queue.WaitForIdleAsync();

        var batch = queue.LatestBatch()!;
        Assert.True(batch.IsFinished);
        Assert.Equal(TaskState.Done, batch.Tasks[0].State);
        Assert.True(File.Exists(batch.Tasks[0].OutputPath));
        Assert.Equal(700, queue.TotalSavedBytes); // 1000 - 300
    }

    [Fact]
    public void Enqueue_without_license_raises_the_gate_and_adds_nothing()
    {
        var (queue, _, dir) = NewQueue();
        queue.IsLicensed = () => false;
        var raised = false;
        queue.LicenseRequired += () => raised = true;

        queue.Enqueue(new[] { MakeFile(dir, "a.png") });

        Assert.True(raised);
        Assert.Empty(queue.Tasks);
    }

    [Fact]
    public async Task A_new_drop_takes_over_the_stage_dropping_predecessors()
    {
        var (queue, _, dir) = NewQueue();
        queue.ImageWorker = FakeImage();

        queue.Enqueue(new[] { MakeFile(dir, "a.png") });
        await queue.WaitForIdleAsync();
        queue.Enqueue(new[] { MakeFile(dir, "b.png") });
        await queue.WaitForIdleAsync();

        Assert.Single(queue.Batches());
        Assert.EndsWith("b.png", queue.LatestBatch()!.Tasks[0].SourcePath);
    }

    [Fact]
    public async Task Multiple_files_in_one_drop_form_one_batch()
    {
        var (queue, _, dir) = NewQueue();
        queue.ImageWorker = FakeImage();

        queue.Enqueue(new[] { MakeFile(dir, "a.png"), MakeFile(dir, "b.jpg"), MakeFile(dir, "note.txt") });
        await queue.WaitForIdleAsync();

        var batch = queue.LatestBatch()!;
        Assert.Equal(3, batch.Tasks.Count);
        Assert.Equal(TaskState.Unsupported, batch.Tasks.Single(t => t.SourcePath.EndsWith("note.txt")).State);
        Assert.Equal(2, batch.Tasks.Count(t => t.State == TaskState.Done));
    }

    [Fact]
    public async Task Failed_worker_marks_task_failed_with_detail()
    {
        var (queue, _, dir) = NewQueue();
        queue.ImageWorker = (_, _, _, _) => throw new ImageProcessingException(ImageProcessingErrorKind.CompressionFailed, "bad pixels");
        queue.Enqueue(new[] { MakeFile(dir, "a.png") });
        await queue.WaitForIdleAsync();

        var task = queue.LatestBatch()!.Tasks[0];
        Assert.Equal(TaskState.Failed, task.State);
        Assert.Equal("bad pixels", task.FailureDetail);
    }

    [Fact]
    public async Task Reprocess_only_failed_reruns_just_the_failures()
    {
        var (queue, _, dir) = NewQueue();
        var failFirst = true;
        queue.ImageWorker = (src, _, outDir, _) =>
        {
            if (src.EndsWith("a.png") && failFirst)
            {
                failFirst = false;
                throw new ImageProcessingException(ImageProcessingErrorKind.CompressionFailed, "x");
            }
            var outPath = OutputNaming.AvailableOutputPath(src, outDir, "compressed", "png");
            File.WriteAllBytes(outPath, new byte[100]);
            return Task.FromResult(outPath);
        };

        queue.Enqueue(new[] { MakeFile(dir, "a.png"), MakeFile(dir, "b.png") });
        await queue.WaitForIdleAsync();
        var batchId = queue.LatestBatch()!.Id;
        Assert.Single(queue.LatestBatch()!.FailedTasks);

        queue.ReprocessBatch(batchId, onlyFailed: true);
        await queue.WaitForIdleAsync();

        Assert.Empty(queue.LatestBatch()!.FailedTasks);
        Assert.All(queue.LatestBatch()!.Tasks, t => Assert.Equal(TaskState.Done, t.State));
    }

    [Fact]
    public async Task Settings_change_marks_finished_batch_stale()
    {
        var (queue, store, dir) = NewQueue();
        queue.ImageWorker = FakeImage();
        new CompressionSettings { Preset = QualityPreset.Balanced }.Save(store);

        queue.Enqueue(new[] { MakeFile(dir, "a.png") });
        await queue.WaitForIdleAsync();
        Assert.Null(queue.StaleBatchId);

        new CompressionSettings { Preset = QualityPreset.Clearer }.Save(store);
        queue.SettingsChanged();
        Assert.Equal(queue.LatestBatch()!.Id, queue.StaleBatchId);
    }

    [Fact]
    public async Task Video_only_setting_change_does_not_stale_an_image_batch()
    {
        var (queue, store, dir) = NewQueue();
        queue.ImageWorker = FakeImage();
        new CompressionSettings { RemoveAudio = false }.Save(store);

        queue.Enqueue(new[] { MakeFile(dir, "a.png") });
        await queue.WaitForIdleAsync();

        new CompressionSettings { RemoveAudio = true }.Save(store); // video-only knob
        queue.SettingsChanged();
        Assert.Null(queue.StaleBatchId);
    }

    [Fact]
    public async Task Auto_copy_fires_when_preference_is_on_and_copy_succeeds()
    {
        var (queue, store, dir) = NewQueue();
        queue.ImageWorker = FakeImage();
        store.SetBool("copyToClipboardAfterCompression", true);
        var copied = new List<string>();
        queue.CopyImageToClipboard = p => { copied.Add(p); return true; };

        queue.Enqueue(new[] { MakeFile(dir, "a.png") });
        await queue.WaitForIdleAsync();

        Assert.Equal(1, queue.AutoCopyCount);
        Assert.Single(copied);
    }

    [Fact]
    public async Task Remove_and_undo_restores_the_batch()
    {
        var (queue, _, dir) = NewQueue();
        queue.ImageWorker = FakeImage();
        queue.Enqueue(new[] { MakeFile(dir, "a.png") });
        await queue.WaitForIdleAsync();
        var batchId = queue.LatestBatch()!.Id;

        queue.RemoveBatch(batchId);
        Assert.Empty(queue.Tasks);
        Assert.NotNull(queue.UndoableRemoval);

        queue.UndoRemove();
        await queue.WaitForIdleAsync();
        Assert.Single(queue.Batches());
        Assert.Equal(batchId, queue.LatestBatch()!.Id);
    }

    [Fact]
    public void Output_directory_honors_the_preference()
    {
        var (queue, store, dir) = NewQueue();
        var input = Path.Combine(dir, "sub", "a.png");

        store.SetString("outputDirectory", "Same as input");
        Assert.Equal(Path.GetDirectoryName(input), queue.OutputDirectoryFor(input));

        var custom = TestSupport.NewTempDir();
        store.SetString("outputDirectory", "Custom");
        store.SetString("customOutputDirectory", custom);
        Assert.Equal(custom, queue.OutputDirectoryFor(input));

        store.SetString("customOutputDirectory", Path.Combine(dir, "does-not-exist"));
        Assert.Equal(Path.GetDirectoryName(input), queue.OutputDirectoryFor(input)); // invalid → fall back
    }

    [Fact]
    public void Staging_directory_is_created_on_demand_and_cleaned()
    {
        var (queue, store, _) = NewQueue();
        queue.StagingDirectory = TestSupport.NewTempDir() + "/staging";
        store.SetString("outputDirectory", "Staging");

        var resolved = queue.OutputDirectoryFor("/anywhere/a.png");
        Assert.True(Directory.Exists(resolved));

        queue.CleanStagingDirectory();
        Assert.False(Directory.Exists(queue.StagingDirectory));
    }

    [Fact]
    public async Task Export_copies_outputs_to_a_chosen_folder()
    {
        var (queue, _, dir) = NewQueue();
        queue.ImageWorker = FakeImage();
        queue.Enqueue(new[] { MakeFile(dir, "a.png") });
        await queue.WaitForIdleAsync();

        var dest = TestSupport.NewTempDir();
        queue.Export(queue.LatestBatch()!.Id, dest);
        Assert.Single(Directory.GetFiles(dest, "*.png"));
    }

    [Fact]
    public async Task Video_task_probes_records_audio_and_reports_progress()
    {
        var (queue, _, dir) = NewQueue();
        queue.VideoProber = (_, _) => Task.FromResult<VideoProbe?>(new VideoProbe(true, 10));
        var progresses = new List<double>();
        queue.VideoWorker = (src, _, outDir, probe, onStart, onProgress, _) =>
        {
            Assert.True(probe!.Value.HasAudio);
            onProgress(0.5);
            onProgress(1.0);
            var outPath = OutputNaming.AvailableOutputPath(src, outDir, "compressed", "mp4");
            File.WriteAllBytes(outPath, new byte[200]);
            return Task.FromResult(outPath);
        };
        queue.Changed += () =>
        {
            var p = queue.LatestBatch()?.Tasks.FirstOrDefault()?.Progress;
            if (p is { } v) progresses.Add(v);
        };

        queue.Enqueue(new[] { MakeFile(dir, "clip.mp4") });
        await queue.WaitForIdleAsync();

        var task = queue.LatestBatch()!.Tasks[0];
        Assert.Equal(TaskState.Done, task.State);
        Assert.True(task.HasAudioTrack);
        Assert.Contains(progresses, v => v >= 0.5);
    }
}
