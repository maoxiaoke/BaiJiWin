using BaiJi.Core;
using Xunit;

namespace BaiJi.Tests;

public class VideoConverterErrorPathTests
{
    private static IToolLocator FakeTools(string dir)
    {
        File.WriteAllText(Path.Combine(dir, "ffmpeg"), "");
        return new DirectoryToolLocator(dir);
    }

    [Fact]
    public async Task Missing_ffmpeg_throws_FFmpegNotFound()
    {
        var tools = new DirectoryToolLocator(TestSupport.NewTempDir()); // empty
        var conv = new VideoConverter(tools, new FakeProcessRunner((_, _) => new ProcessResult(0, "")));
        var ex = await Assert.ThrowsAsync<VideoConversionException>(() =>
            conv.ConvertVideoAsync("in.mp4", TestSupport.NewTempDir()));
        Assert.Equal(VideoConversionErrorKind.FFmpegNotFound, ex.Kind);
    }

    [Fact]
    public async Task Nonzero_exit_throws_ConversionFailed_and_deletes_partial_output()
    {
        var dir = TestSupport.NewTempDir();
        var tools = FakeTools(dir);
        var runner = new FakeProcessRunner((_, args) => new ProcessResult(69, "ffmpeg exploded"))
        {
            SideEffect = (_, args) => File.WriteAllText(args[^1], "partial"), // leaves a partial file
        };
        var conv = new VideoConverter(tools, runner);

        var ex = await Assert.ThrowsAsync<VideoConversionException>(() =>
            conv.ConvertVideoAsync(Path.Combine(dir, "in.mp4"), dir));
        Assert.Equal(VideoConversionErrorKind.ConversionFailed, ex.Kind);
        Assert.Equal(69, ex.ExitCode);
        Assert.Contains("exploded", ex.Detail);
        Assert.Empty(Directory.GetFiles(dir, "*-compressed.mp4"));
    }

    [Fact]
    public async Task Cancellation_maps_to_Cancelled_error()
    {
        var dir = TestSupport.NewTempDir();
        var tools = FakeTools(dir);
        var runner = new FakeProcessRunner((_, _) => new ProcessResult(0, ""));
        // Force the runner to observe an already-cancelled token.
        var conv = new VideoConverter(tools, runner);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var ex = await Assert.ThrowsAsync<VideoConversionException>(() =>
            conv.ConvertVideoAsync(Path.Combine(dir, "in.mp4"), dir, cancellationToken: cts.Token));
        Assert.Equal(VideoConversionErrorKind.Cancelled, ex.Kind);
    }

    [Fact]
    public async Task Generic_runner_exception_maps_to_ProcessRunError()
    {
        var dir = TestSupport.NewTempDir();
        var tools = FakeTools(dir);
        var runner = new ThrowingRunner(new InvalidOperationException("spawn failed"));
        var conv = new VideoConverter(tools, runner);

        var ex = await Assert.ThrowsAsync<VideoConversionException>(() =>
            conv.ConvertVideoAsync(Path.Combine(dir, "in.mp4"), dir));
        Assert.Equal(VideoConversionErrorKind.ProcessRunError, ex.Kind);
        Assert.Contains("spawn failed", ex.Detail);
    }

    [Fact]
    public async Task Two_pass_runs_both_passes_and_reports_progress_across_them()
    {
        var dir = TestSupport.NewTempDir();
        var tools = FakeTools(dir);
        var runner = new FakeProcessRunner((_, args) => new ProcessResult(0, ""))
        {
            // Emit a mid-encode progress line on each pass.
            StdoutLines = (_, _) => new[] { "out_time=00:00:05.000000" },
            SideEffect = (_, args) => { if (args[^1].EndsWith(".mp4")) File.WriteAllBytes(args[^1], new byte[100]); },
        };
        var conv = new VideoConverter(tools, runner);
        var progresses = new List<double>();

        var output = await conv.ConvertVideoAsync(
            Path.Combine(dir, "in.mp4"), dir, new Rate.TargetBitrate(800),
            durationSeconds: 10, progress: progresses.Add);

        Assert.True(File.Exists(output));
        Assert.Equal(2, runner.Calls.Count); // analysis pass + real pass
        // pass 1 covers 0..0.5, pass 2 covers 0.5..1 → 5/10 within each pass.
        Assert.Contains(progresses, p => p is > 0 and < 0.5);
        Assert.Contains(progresses, p => p is >= 0.5 and < 1);
    }

    [Fact]
    public void VideoConversionException_carries_code_and_detail()
    {
        var ex = new VideoConversionException(VideoConversionErrorKind.ConversionFailed, 12, "why");
        Assert.Equal(12, ex.ExitCode);
        Assert.Equal("why", ex.Detail);
    }

    private sealed class ThrowingRunner : IProcessRunner
    {
        private readonly Exception _ex;
        public ThrowingRunner(Exception ex) => _ex = ex;
        public Task<ProcessResult> RunAsync(string tool, IReadOnlyList<string> arguments,
            Action<string>? onStdoutLine = null, Action<IProcessHandle>? onStart = null,
            CancellationToken cancellationToken = default) => throw _ex;
    }
}

public class MediaQueueCancellationTests
{
    private static (MediaQueue Queue, InMemorySettingsStore Store, string Dir) NewQueue()
    {
        var dir = TestSupport.NewTempDir();
        var store = new InMemorySettingsStore();
        var tools = new DirectoryToolLocator(dir);
        var runner = new FakeProcessRunner((_, _) => new ProcessResult(0, ""));
        var queue = new MediaQueue(store, new ImageProcessor(tools, runner), new VideoConverter(tools, runner));
        return (queue, store, dir);
    }

    private static string MakeFile(string dir, string name)
    {
        var path = Path.Combine(dir, name);
        File.WriteAllBytes(path, new byte[1000]);
        return path;
    }

    [Fact]
    public async Task Cancelling_a_running_video_marks_it_cancelled_and_discards_output()
    {
        var (queue, _, dir) = NewQueue();
        queue.VideoProber = (_, _) => Task.FromResult<VideoProbe?>(new VideoProbe(false, 10));

        var started = new TaskCompletionSource();
        queue.VideoWorker = async (src, _, outDir, _, onStart, _, ct) =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct); // block until cancelled
            var outPath = OutputNaming.AvailableOutputPath(src, outDir, "compressed", "mp4");
            File.WriteAllBytes(outPath, new byte[10]);
            return outPath;
        };

        queue.Enqueue(new[] { MakeFile(dir, "clip.mp4") });
        await started.Task; // ensure the task is mid-flight
        var batchId = queue.LatestBatch()!.Id;
        queue.CancelBatch(batchId);
        await queue.WaitForIdleAsync();

        Assert.Equal(TaskState.Cancelled, queue.LatestBatch()!.Tasks[0].State);
        Assert.Empty(Directory.GetFiles(dir, "*-compressed.mp4"));
    }

    [Fact]
    public async Task A_new_drop_cancels_a_still_running_predecessor_batch()
    {
        var (queue, _, dir) = NewQueue();
        queue.ImageWorker = FakeImage();
        queue.VideoProber = (_, _) => Task.FromResult<VideoProbe?>(new VideoProbe(false, 10));

        var firstStarted = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        queue.VideoWorker = async (src, _, outDir, _, _, _, ct) =>
        {
            firstStarted.TrySetResult();
            await Task.WhenAny(release.Task, Task.Delay(Timeout.Infinite, ct));
            ct.ThrowIfCancellationRequested();
            var outPath = OutputNaming.AvailableOutputPath(src, outDir, "compressed", "mp4");
            File.WriteAllBytes(outPath, new byte[10]);
            return outPath;
        };

        queue.Enqueue(new[] { MakeFile(dir, "clip.mp4") });
        await firstStarted.Task;

        // Second drop takes over the single stage while the first is still running.
        queue.Enqueue(new[] { MakeFile(dir, "b.png") });
        await queue.WaitForIdleAsync();

        Assert.Single(queue.Batches());
        Assert.EndsWith("b.png", queue.LatestBatch()!.Tasks[0].SourcePath);
        Assert.Equal(TaskState.Done, queue.LatestBatch()!.Tasks[0].State);
    }

    private static ImageWorker FakeImage() => (src, _, outDir, _) =>
    {
        var outPath = OutputNaming.AvailableOutputPath(src, outDir, "compressed", Path.GetExtension(src).TrimStart('.'));
        File.WriteAllBytes(outPath, new byte[100]);
        return Task.FromResult(outPath);
    };
}
