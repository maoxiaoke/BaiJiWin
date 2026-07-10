namespace BaiJi.Core;

/// <summary>
/// Worker signatures the queue drives. They are injected so unit tests can run
/// the queue without a license server or the bundled binaries.
/// </summary>
public delegate Task<string> ImageWorker(string sourcePath, CompressionSettings settings, string outputDir, CancellationToken ct);

public delegate Task<string> VideoWorker(
    string sourcePath, CompressionSettings settings, string outputDir, VideoProbe? probe,
    Action<IProcessHandle> onStart, Action<double> onProgress, CancellationToken ct);

public delegate Task<VideoProbe?> VideoProber(string sourcePath, CancellationToken ct);

/// <summary>
/// Drives the drop-slot model: files dropped together form a batch, the newest
/// batch owns the stage, and results are taken away by drag / copy / save (or
/// left next to the original, the default). This is the platform-agnostic port
/// of macOS's <c>MediaQueueViewModel</c>; the WinUI ViewModel wraps it and
/// forwards <see cref="Changed"/> to the view.
/// </summary>
public sealed class MediaQueue
{
    private readonly List<MediaTask> _tasks = new();
    private readonly ISettingsStore _store;
    private readonly Action<Action> _post;

    private bool _isDraining;
    private Task _drainTask = Task.CompletedTask;
    private IProcessHandle? _runningHandle;
    private Guid? _runningTaskId;
    private CancellationTokenSource? _runningCts;
    private readonly HashSet<Guid> _cancelledTaskIds = new();
    private (List<MediaTask> Tasks, int Index)? _undoableRemoval;
    private CancellationTokenSource? _undoExpiry;

    // Injectable gates and workers.
    public Func<bool> IsLicensed { get; set; } = () => true;
    public ImageWorker ImageWorker { get; set; }
    public VideoWorker VideoWorker { get; set; }
    public VideoProber VideoProber { get; set; }
    /// <summary>Copies a finished image to the clipboard; returns success. Platform-supplied.</summary>
    public Func<string, bool> CopyImageToClipboard { get; set; } = _ => false;
    /// <summary>Base directory for the opt-in staging area (wiped each launch).</summary>
    public string StagingDirectory { get; set; } =
        Path.Combine(Path.GetTempPath(), "BaiJi", "Staging");

    // Observable-ish state raised to the UI.
    public event Action? Changed;
    public event Action? LicenseRequired;
    public int AutoCopyCount { get; private set; }
    public Guid? StaleBatchId { get; private set; }
    public IReadOnlyList<MediaTask> Tasks => _tasks;
    public (IReadOnlyList<MediaTask> Tasks, int Index)? UndoableRemoval =>
        _undoableRemoval is { } u ? (u.Tasks, u.Index) : null;

    public MediaQueue(
        ISettingsStore store,
        ImageProcessor imageProcessor,
        VideoConverter videoConverter,
        Action<Action>? post = null)
    {
        _store = store;
        _post = post ?? (a => a());

        ImageWorker = (path, settings, dir, ct) =>
            imageProcessor.ProcessAsync(path, settings.Preset, settings.TargetBytes, settings.ImageFormat, dir, ct);

        VideoWorker = async (path, settings, dir, probe, onStart, onProgress, ct) =>
        {
            var removeAudio = settings.RemoveAudio && probe?.HasAudio != false;

            // No size target (or unknown duration) → constant-quality preset encode.
            if (settings.TargetBytes is not { } target || probe?.DurationSeconds is not { } duration || duration <= 0)
            {
                return await videoConverter.ConvertVideoAsync(
                    path, dir, new Rate.Crf(VideoConverter.CrfFor(settings.Preset)),
                    removeAudio, probe?.DurationSeconds, onStart, onProgress, ct).ConfigureAwait(false);
            }

            // Target-size path: two-pass at the estimated bitrate, then one
            // corrective pass if the estimate overshot the ceiling.
            var keepAudio = probe?.HasAudio == true && !removeAudio;
            var kbps = VideoConverter.TargetBitrateKbps(target, duration, keepAudio);
            var output = await videoConverter.ConvertVideoAsync(
                path, dir, new Rate.TargetBitrate(kbps), removeAudio, duration, onStart, onProgress, ct).ConfigureAwait(false);

            var size = MediaTask.FileSize(output);
            if (size > target && size > 0)
            {
                // Scale the bitrate down proportionally and try once more.
                kbps = Math.Max((int)(kbps * (double)target / size * 0.92), 50);
                TryDelete(output);
                output = await videoConverter.ConvertVideoAsync(
                    path, dir, new Rate.TargetBitrate(kbps), removeAudio, duration, onStart, onProgress, ct).ConfigureAwait(false);
            }
            return output;
        };

        VideoProber = (path, ct) => videoConverter.ProbeAsync(path, ct);
    }

    // MARK: - Batches

    public IReadOnlyList<BatchSummary> Batches()
    {
        var order = new List<Guid>();
        var grouped = new Dictionary<Guid, List<MediaTask>>();
        foreach (var task in _tasks)
        {
            if (!grouped.ContainsKey(task.BatchId))
            {
                order.Add(task.BatchId);
                grouped[task.BatchId] = new List<MediaTask>();
            }
            grouped[task.BatchId].Add(task);
        }
        return order.Select(id => new BatchSummary(id, grouped[id])).ToList();
    }

    /// <summary>The batch that owns the stage.</summary>
    public BatchSummary? LatestBatch() => Batches().LastOrDefault();

    public long TotalSavedBytes =>
        _tasks.Where(t => t.OutputSize is not null)
              .Sum(t => Math.Max(t.OriginalSize - t.OutputSize!.Value, 0));

    // MARK: - Enqueue (the single admission gate)

    public void Enqueue(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return;
        if (!IsLicensed())
        {
            LicenseRequired?.Invoke();
            return;
        }

        // A new batch takes over the single stage. Cancel any predecessor still
        // running, then drop all predecessors.
        foreach (var batch in Batches().Where(b => !b.IsFinished).ToList())
            CancelBatch(batch.Id);
        _tasks.Clear();
        StaleBatchId = null;

        var settings = CompressionSettings.Current(_store);
        var batchId = Guid.NewGuid();
        foreach (var path in paths)
        {
            var kind = MediaTask.KindOf(path);
            _tasks.Add(new MediaTask(
                batchId, path, kind, MediaTask.FileSize(path),
                kind == MediaKind.Unsupported ? TaskState.Unsupported : TaskState.Queued,
                settings));
        }
        RaiseChanged();
        DrainQueue();
    }

    // MARK: - Processing

    /// <summary>Re-runs a whole batch (or just its failed tasks) with the current settings.</summary>
    public void ReprocessBatch(Guid batchId, bool onlyFailed = false)
    {
        if (StaleBatchId == batchId) StaleBatchId = null;
        var settings = CompressionSettings.Current(_store);
        foreach (var task in _tasks.Where(t => t.BatchId == batchId))
        {
            if (task.IsProcessing || task.Kind == MediaKind.Unsupported) continue;
            if (onlyFailed && task.State != TaskState.Failed) continue;
            // Delete the prior output so the re-run reuses the same name.
            if (task.OutputPath is { } prev) TryDelete(prev);
            task.Settings = settings;
            task.OutputPath = null;
            task.OutputSize = null;
            task.State = TaskState.Queued;
        }
        RaiseChanged();
        DrainQueue();
    }

    /// <summary>
    /// Reacts to an app-wide settings change: if a relevant field changed, mark
    /// the finished batch stale so its card offers an explicit "reprocess with new
    /// settings" button — nothing re-runs on its own.
    /// </summary>
    public void SettingsChanged()
    {
        var batch = LatestBatch();
        if (batch is null || !batch.IsFinished) return;
        var batchSettings = batch.Tasks.FirstOrDefault()?.Settings;
        if (batchSettings is null) return;

        var current = CompressionSettings.Current(_store);
        var hasVideo = batch.Tasks.Any(t => t.Kind == MediaKind.Video);
        var hasImage = batch.Tasks.Any(t => t.Kind == MediaKind.Image);

        var videoChanged = hasVideo && !batchSettings.VideoRelevantEquals(current);
        var imageChanged = hasImage && !batchSettings.ImageRelevantEquals(current);
        if (videoChanged || imageChanged)
        {
            StaleBatchId = batch.Id;
            RaiseChanged();
        }
    }

    public void CancelBatch(Guid batchId)
    {
        foreach (var task in _tasks.Where(t => t.BatchId == batchId))
        {
            if (task.State == TaskState.Queued)
            {
                task.State = TaskState.Cancelled;
            }
            else if (task.State == TaskState.Processing && task.Id == _runningTaskId)
            {
                // Mark it so the outcome is classified as cancelled and its output
                // discarded; terminate the running subprocess so it stops now.
                _cancelledTaskIds.Add(task.Id);
                _runningCts?.Cancel();
                _runningHandle?.Terminate();
            }
        }
        RaiseChanged();
    }

    /// <summary>Serial drain: one task at a time, in enqueue order.</summary>
    private void DrainQueue()
    {
        if (_isDraining) return;
        _isDraining = true;
        _drainTask = DrainLoopAsync();
    }

    private async Task DrainLoopAsync()
    {
        while (true)
        {
            var task = _tasks.FirstOrDefault(t => t.State == TaskState.Queued);
            if (task is null) break;
            await ProcessAsync(task).ConfigureAwait(false);
        }
        _isDraining = false;
    }

    private async Task ProcessAsync(MediaTask task)
    {
        var id = task.Id;
        var cts = new CancellationTokenSource();
        _runningCts = cts;
        _runningTaskId = id;
        _runningHandle = null;
        task.State = TaskState.Processing;
        task.Progress = task.Kind == MediaKind.Video ? 0 : null;
        RaiseChanged();

        // Probe the video HERE, before choosing the encode rate.
        VideoProbe? probe = null;
        if (task.Kind == MediaKind.Video)
        {
            probe = await VideoProber(task.SourcePath, cts.Token).ConfigureAwait(false);
            task.HasAudioTrack = probe?.HasAudio;
            RaiseChanged();
        }

        try
        {
            string outputPath;
            switch (task.Kind)
            {
                case MediaKind.Image:
                    outputPath = await ImageWorker(
                        task.SourcePath, task.Settings, OutputDirectoryFor(task.SourcePath), cts.Token).ConfigureAwait(false);
                    break;
                case MediaKind.Video:
                    outputPath = await VideoWorker(
                        task.SourcePath, task.Settings, OutputDirectoryFor(task.SourcePath), probe,
                        handle => _runningHandle = handle,
                        fraction => _post(() => SetProgress(fraction, id)),
                        cts.Token).ConfigureAwait(false);
                    break;
                default:
                    FinishRun(id, cts);
                    return;
            }

            if (_cancelledTaskIds.Remove(id))
            {
                TryDelete(outputPath);
                task.State = TaskState.Cancelled;
            }
            else
            {
                task.State = TaskState.Done;
                task.OutputPath = outputPath;
                task.OutputSize = MediaTask.FileSize(outputPath);
                task.Progress = null;
                if (task.Kind == MediaKind.Image
                    && _store.GetBool("copyToClipboardAfterCompression")
                    && CopyImageToClipboard(outputPath))
                {
                    AutoCopyCount++;
                }
            }
        }
        catch (Exception error)
        {
            if (_cancelledTaskIds.Remove(id)
                || error is OperationCanceledException
                || (error is VideoConversionException { Kind: VideoConversionErrorKind.Cancelled }))
            {
                task.State = TaskState.Cancelled;
            }
            else if (error is ImageProcessingException ipe)
            {
                task.State = TaskState.Failed;
                task.FailureMessage = ipe.Kind.ToString();
                task.FailureDetail = ipe.Detail;
            }
            else if (error is VideoConversionException { Kind: VideoConversionErrorKind.ConversionFailed } vce)
            {
                task.State = TaskState.Failed;
                task.FailureMessage = $"VideoConversionFailed:{vce.ExitCode}";
                task.FailureDetail = vce.Detail;
            }
            else
            {
                task.State = TaskState.Failed;
                task.FailureMessage = error.Message;
                task.FailureDetail = (error as VideoConversionException)?.Detail ?? "";
            }
        }

        FinishRun(id, cts);
        RaiseChanged();
    }

    private void FinishRun(Guid id, CancellationTokenSource cts)
    {
        _cancelledTaskIds.Remove(id);
        _runningHandle = null;
        _runningTaskId = null;
        if (ReferenceEquals(_runningCts, cts)) _runningCts = null;
        cts.Dispose();
    }

    /// <summary>
    /// Progress callbacks can arrive after the task already finished — they must
    /// never overwrite a terminal status. Coalesced to ~1% steps to avoid render
    /// thrash, mirroring the macOS behavior.
    /// </summary>
    private void SetProgress(double fraction, Guid taskId)
    {
        var task = _tasks.FirstOrDefault(t => t.Id == taskId);
        if (task is null || task.State != TaskState.Processing) return;
        if (task.Progress is { } current && fraction < 1 && Math.Abs(fraction - current) < 0.01) return;
        task.Progress = fraction;
        RaiseChanged();
    }

    // MARK: - Output destination

    public void CleanStagingDirectory()
    {
        try { if (Directory.Exists(StagingDirectory)) Directory.Delete(StagingDirectory, true); }
        catch { /* best effort */ }
    }

    /// <summary>
    /// Default is "next to the original" so results are never silently lost.
    /// Staging is only used when the user explicitly opts into it.
    /// </summary>
    public string OutputDirectoryFor(string inputPath)
    {
        switch (_store.GetString("outputDirectory"))
        {
            case "Staging":
                Directory.CreateDirectory(StagingDirectory);
                return StagingDirectory;
            case "Custom":
                var custom = _store.GetString("customOutputDirectory");
                if (!string.IsNullOrEmpty(custom) && Directory.Exists(custom)) return custom;
                return Path.GetDirectoryName(inputPath) ?? StagingDirectory;
            default: // "Same as input" — the safe default (no silent data loss)
                return Path.GetDirectoryName(inputPath) ?? StagingDirectory;
        }
    }

    /// <summary>Copies a batch's outputs to a user-chosen folder ("Save to…").</summary>
    public void Export(Guid batchId, string directory)
    {
        foreach (var task in _tasks.Where(t => t.BatchId == batchId))
        {
            if (task.OutputPath is not { } output) continue;
            var destination = OutputNaming.AvailableOutputPath(
                task.SourcePath, directory, "compressed", Path.GetExtension(output).TrimStart('.'));
            File.Copy(output, destination);
        }
    }

    // MARK: - Removal with undo

    public void RemoveBatch(Guid batchId)
    {
        var removed = _tasks.Where(t => t.BatchId == batchId).ToList();
        if (removed.Count == 0) return;
        if (removed.Any(t => t.IsProcessing)) CancelBatch(batchId);
        var index = _tasks.FindIndex(t => t.BatchId == batchId);
        if (index < 0) index = 0;
        _tasks.RemoveAll(t => t.BatchId == batchId);
        _undoableRemoval = (removed, index);
        RaiseChanged();

        _undoExpiry?.Cancel();
        _undoExpiry = new CancellationTokenSource();
        var token = _undoExpiry.Token;
        _ = Task.Delay(5000, token).ContinueWith(t =>
        {
            if (t.IsCanceled) return;
            _post(() =>
            {
                _undoableRemoval = null;
                RaiseChanged();
            });
        }, TaskScheduler.Default);
    }

    public void UndoRemove()
    {
        if (_undoableRemoval is not { } removal) return;
        // A restored task that was mid-flight (or queued) when removed must be
        // re-queued and the drain restarted.
        var restored = removal.Tasks.Select(task =>
        {
            if (!task.IsFinished)
            {
                task.State = TaskState.Queued;
                task.Progress = null;
            }
            return task;
        }).ToList();
        _tasks.InsertRange(Math.Min(removal.Index, _tasks.Count), restored);
        _undoableRemoval = null;
        _undoExpiry?.Cancel();
        RaiseChanged();
        DrainQueue();
    }

    // MARK: - Test/host support

    /// <summary>Awaits until the serial drain is idle (for tests and shutdown).</summary>
    public async Task WaitForIdleAsync()
    {
        while (true)
        {
            var t = _drainTask;
            if (t.IsCompleted) return;
            await t.ConfigureAwait(false);
        }
    }

    private void RaiseChanged() => Changed?.Invoke();

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best effort */ }
    }
}
