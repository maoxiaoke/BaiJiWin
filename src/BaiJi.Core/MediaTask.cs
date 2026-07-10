namespace BaiJi.Core;

public enum MediaKind
{
    Image,
    Video,
    Unsupported,
}

public enum TaskState
{
    Queued,
    Processing,
    Done,
    Failed,
    Cancelled,
    Unsupported,
}

/// <summary>
/// One file in the pipeline. Files dropped in a single gesture share a
/// <see cref="BatchId"/> and are presented as one card. Mutable status fields
/// mirror the Swift value-type-with-copy pattern but in place, since the C#
/// queue holds reference instances.
/// </summary>
public sealed class MediaTask
{
    public Guid Id { get; } = Guid.NewGuid();
    public Guid BatchId { get; }
    public string SourcePath { get; }
    public MediaKind Kind { get; }
    public long OriginalSize { get; }

    public TaskState State { get; set; }
    /// <summary>0..1 when known (video); null for indeterminate (image) while processing.</summary>
    public double? Progress { get; set; }
    public string? OutputPath { get; set; }
    public long? OutputSize { get; set; }
    public string? FailureMessage { get; set; }
    public string? FailureDetail { get; set; }

    public CompressionSettings Settings { get; set; }
    /// <summary>null while ffmpeg detection is still running (videos only).</summary>
    public bool? HasAudioTrack { get; set; }

    public MediaTask(Guid batchId, string sourcePath, MediaKind kind, long originalSize, TaskState state, CompressionSettings settings)
    {
        BatchId = batchId;
        SourcePath = sourcePath;
        Kind = kind;
        OriginalSize = originalSize;
        State = state;
        Settings = settings;
    }

    public bool IsProcessing => State == TaskState.Processing;

    public bool IsFinished => State is TaskState.Done or TaskState.Failed or TaskState.Cancelled or TaskState.Unsupported;

    /// <summary>Bytes saved as a fraction of the original, e.g. 0.63 for -63%.</summary>
    public double? Savings =>
        State == TaskState.Done && OutputSize is { } outSize && OriginalSize > 0
            ? 1.0 - (double)outSize / OriginalSize
            : null;

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "png", "jpg", "jpeg", "gif", "webp", "bmp", "tif", "tiff", "heic", "heif", "avif", "ico",
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "mp4", "mov", "mkv", "avi", "webm", "m4v", "wmv", "flv", "mpg", "mpeg", "3gp", "ts", "m2ts",
    };

    public static MediaKind KindOf(string path)
    {
        var ext = Path.GetExtension(path).TrimStart('.');
        if (string.IsNullOrEmpty(ext)) return MediaKind.Unsupported;
        if (ImageExtensions.Contains(ext)) return MediaKind.Image;
        if (VideoExtensions.Contains(ext)) return MediaKind.Video;
        return MediaKind.Unsupported;
    }

    public static long FileSize(string path) => OutputNaming.FileSize(path);
}

/// <summary>A batch = the files dropped in one gesture, presented as one card.</summary>
public sealed class BatchSummary
{
    public Guid Id { get; }
    public IReadOnlyList<MediaTask> Tasks { get; }

    public BatchSummary(Guid id, IReadOnlyList<MediaTask> tasks)
    {
        Id = id;
        Tasks = tasks;
    }

    public long TotalOriginalSize => Tasks.Sum(t => t.OriginalSize);
    public long TotalOutputSize => Tasks.Where(t => t.OutputSize is not null).Sum(t => t.OutputSize!.Value);
    public IEnumerable<MediaTask> DoneTasks => Tasks.Where(t => t.OutputPath is not null);
    public IEnumerable<MediaTask> FailedTasks => Tasks.Where(t => t.State == TaskState.Failed);
    public bool IsFinished => Tasks.All(t => t.IsFinished);

    /// <summary>Aggregate 0..1 across the batch (finished tasks count as 1).</summary>
    public double Progress
    {
        get
        {
            if (Tasks.Count == 0) return 0;
            var sum = Tasks.Sum(t => t.State switch
            {
                TaskState.Done or TaskState.Failed or TaskState.Cancelled or TaskState.Unsupported => 1.0,
                TaskState.Processing => t.Progress ?? 0.5,
                _ => 0.0,
            });
            return sum / Tasks.Count;
        }
    }

    public double? Savings
    {
        get
        {
            var done = DoneTasks.ToList();
            if (done.Count == 0) return null;
            var original = done.Sum(t => t.OriginalSize);
            if (original <= 0) return null;
            return 1.0 - (double)done.Sum(t => t.OutputSize ?? 0) / original;
        }
    }
}
