using BaiJi.Core;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BaiJi.App.ViewModels;

/// <summary>
/// Backs MainWindow: exposes the latest batch (the card on stage) and the
/// enqueue/cancel/reprocess/remove actions. Thin over Core's <see cref="MediaQueue"/>.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly MediaQueue _queue = AppServices.Instance.Queue;

    [ObservableProperty] private BatchSummary? _latestBatch;
    [ObservableProperty] private bool _hasBatch;
    [ObservableProperty] private bool _isStale;

    public event Action? LicenseRequired;

    public MainViewModel()
    {
        _queue.Changed += OnQueueChanged;
        _queue.LicenseRequired += () => LicenseRequired?.Invoke();
        OnQueueChanged();
    }

    private void OnQueueChanged()
    {
        LatestBatch = _queue.LatestBatch();
        HasBatch = LatestBatch is not null;
        IsStale = _queue.StaleBatchId is not null && _queue.StaleBatchId == LatestBatch?.Id;
        OnPropertyChanged(nameof(LatestBatch)); // re-push even when the same instance mutated
    }

    public void Enqueue(IReadOnlyList<string> paths) => _queue.Enqueue(paths);

    public async Task PasteAsync()
    {
        var path = await AppServices.Instance.Clipboard.ImportAsync();
        if (path is not null) _queue.Enqueue(new[] { path });
    }

    public void Cancel(Guid batchId) => _queue.CancelBatch(batchId);
    public void Remove(Guid batchId) => _queue.RemoveBatch(batchId);
    public void Undo() => _queue.UndoRemove();
    public void Reprocess(Guid batchId, bool onlyFailed = false) => _queue.ReprocessBatch(batchId, onlyFailed);
    public void SettingsChanged() => _queue.SettingsChanged();
    public void Export(Guid batchId, string directory) => _queue.Export(batchId, directory);

    public int AutoCopyCount => _queue.AutoCopyCount;
}
