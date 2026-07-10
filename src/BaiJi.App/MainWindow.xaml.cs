using BaiJi.App.Services;
using BaiJi.App.ViewModels;
using BaiJi.Core;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;

namespace BaiJi.App;

public sealed partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();
    private readonly QuickSettingsViewModel _quick = new();
    private DispatcherTimer? _reprocessDebounce;
    private int _lastAutoCopyCount;

    public MainWindow()
    {
        InitializeComponent();
        ConfigureWindow();
        LocalizeStatic();
        PopulateQuickControls();

        _vm.LicenseRequired += ShowLicenseRequired;
        AppServices.Instance.Queue.Changed += Render;
        _quick.Changed += OnQuickSettingsPersisted;

        // Ctrl+V paste anywhere in the window.
        var paste = new KeyboardAccelerator { Key = VirtualKey.V, Modifiers = VirtualKeyModifiers.Control };
        paste.Invoked += async (_, e) => { e.Handled = true; await _vm.PasteAsync(); };
        RootGrid.KeyboardAccelerators.Add(paste);

        Render();
    }

    private void ConfigureWindow()
    {
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        appWindow.Resize(new Windows.Graphics.SizeInt32(340, 460));
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
        }
        Title = "BaiJi";
    }

    private void LocalizeStatic()
    {
        SlotTitle.Text = Loc.Get("Slot_Title");
        SlotSubtitle.Text = Loc.Get("Slot_Subtitle");
        CopyLabel.Text = Loc.Get("Card_Copy");
        SaveButton.Content = Loc.Get("Card_SaveTo");
    }

    // MARK: - Quick settings

    private void PopulateQuickControls()
    {
        QualityCombo.ItemsSource = _quick.Presets.Select(QuickSettingsViewModel.PresetLabel).ToList();
        QualityCombo.SelectedIndex = _quick.Presets.ToList().IndexOf(_quick.Preset);

        TargetCombo.ItemsSource = _quick.Targets.Select(t => Loc.Get(t.Key)).ToList();
        TargetCombo.SelectedIndex = Math.Max(0, _quick.Targets.ToList().FindIndex(t => t.Bytes == _quick.TargetBytes));

        FormatCombo.ItemsSource = _quick.Formats.Select(QuickSettingsViewModel.FormatLabel).ToList();
        FormatCombo.SelectedIndex = _quick.Formats.ToList().IndexOf(_quick.ImageFormat);
    }

    private void OnQuickChanged(object sender, SelectionChangedEventArgs e)
    {
        if (QualityCombo.SelectedIndex >= 0) _quick.Preset = _quick.Presets[QualityCombo.SelectedIndex];
        if (TargetCombo.SelectedIndex >= 0) _quick.TargetBytes = _quick.Targets[TargetCombo.SelectedIndex].Bytes;
        if (FormatCombo.SelectedIndex >= 0) _quick.ImageFormat = _quick.Formats[FormatCombo.SelectedIndex];
    }

    private void OnQuickSettingsPersisted()
    {
        // Debounce so dragging through several knobs fires one re-evaluation.
        _reprocessDebounce?.Stop();
        _reprocessDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _reprocessDebounce.Tick += (_, _) =>
        {
            _reprocessDebounce!.Stop();
            _vm.SettingsChanged();
            Render();
        };
        _reprocessDebounce.Start();
    }

    // MARK: - Drop / paste / pick

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = Loc.Get("Slot_Title");
        }
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        var items = await e.DataView.GetStorageItemsAsync();
        var paths = items.OfType<StorageFile>().Select(f => f.Path).ToList();
        if (paths.Count > 0) _vm.Enqueue(paths);
    }

    private async void OnChooseFiles(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker { ViewMode = PickerViewMode.Thumbnail };
        foreach (var ext in new[] { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".tif", ".tiff",
                     ".mp4", ".mov", ".mkv", ".avi", ".webm", ".m4v" })
            picker.FileTypeFilter.Add(ext);
        InitPicker(picker);
        var files = await picker.PickMultipleFilesAsync();
        if (files.Count > 0) _vm.Enqueue(files.Select(f => f.Path).ToList());
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        var output = _vm.LatestBatch?.DoneTasks.FirstOrDefault()?.OutputPath;
        if (output is not null && AppServices.Instance.Clipboard.CopyImage(output))
            ShowToast(Loc.Get("Toast_Copied"));
    }

    private async void OnSaveTo(object sender, RoutedEventArgs e)
    {
        var batch = _vm.LatestBatch;
        if (batch is null) return;
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        InitPicker(picker);
        var folder = await picker.PickSingleFolderAsync();
        if (folder is null) return;
        try
        {
            _vm.Export(batch.Id, folder.Path);
            ShowToast(Loc.Get("Card_Saved"));
        }
        catch
        {
            ShowToast(Loc.Get("Card_SaveFailed"));
        }
    }

    private void OnReprocess(object sender, RoutedEventArgs e)
    {
        if (_vm.LatestBatch is { } batch) _vm.Reprocess(batch.Id);
    }

    private void OnRemove(object sender, RoutedEventArgs e)
    {
        if (_vm.LatestBatch is { } batch) _vm.Remove(batch.Id);
    }

    private void OnOpenSettings(object sender, RoutedEventArgs e)
    {
        var window = new SettingsWindow();
        window.Activate();
    }

    private void InitPicker(object picker)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
    }

    // MARK: - Rendering

    private void Render()
    {
        var batch = _vm.LatestBatch;
        if (batch is null)
        {
            DropSlot.Visibility = Visibility.Visible;
            Card.Visibility = Visibility.Collapsed;
            return;
        }

        DropSlot.Visibility = Visibility.Collapsed;
        Card.Visibility = Visibility.Visible;

        var processing = !batch.IsFinished;
        CardProgress.Visibility = processing ? Visibility.Visible : Visibility.Collapsed;
        CardProgress.IsActive = processing;
        if (processing)
        {
            CardProgress.IsIndeterminate = batch.Tasks.All(t => t.Kind != MediaKind.Video);
            CardProgress.Value = batch.Progress * 100;
        }

        var done = batch.DoneTasks.ToList();
        var failed = batch.FailedTasks.ToList();

        if (processing)
        {
            CardHeadline.Text = $"{(int)(batch.Progress * 100)}%";
            CardDetail.Text = "";
        }
        else if (done.Count > 0)
        {
            var savings = batch.Savings ?? 0;
            CardHeadline.Text = savings > 0 ? $"−{(int)(savings * 100)}%" : Human(batch.TotalOutputSize);
            CardDetail.Text = $"{Human(batch.TotalOriginalSize)} → {Human(batch.TotalOutputSize)}";
        }
        else if (failed.Count > 0)
        {
            CardHeadline.Text = "⚠";
            CardDetail.Text = failed[0].FailureDetail ?? Loc.Get("Error_CompressionFailed");
        }
        else
        {
            CardHeadline.Text = Loc.Get("Queue_StatusUnsupported");
            CardDetail.Text = "";
        }

        var hasImageOutput = done.Any(t => t.Kind == MediaKind.Image);
        CopyButton.Visibility = hasImageOutput ? Visibility.Visible : Visibility.Collapsed;
        SaveButton.Visibility = done.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        ReprocessButton.Visibility = _vm.IsStale ? Visibility.Visible : Visibility.Collapsed;
        ReprocessButton.Content = Loc.Get("Card_ReprocessStale");

        MaybeShowAutoCopyToast();
    }

    private void MaybeShowAutoCopyToast()
    {
        if (_vm.AutoCopyCount > _lastAutoCopyCount)
        {
            _lastAutoCopyCount = _vm.AutoCopyCount;
            ShowToast(Loc.Get("Toast_CompressedAndCopied"));
        }
    }

    private void ShowToast(string message)
    {
        Toast.Message = message;
        Toast.IsOpen = true;
    }

    private async void ShowLicenseRequired()
    {
        var dialog = new ContentDialog
        {
            Title = Loc.Get("License_RequiredTitle"),
            Content = Loc.Get("License_RequiredMessage"),
            PrimaryButtonText = Loc.Get("License_Manage"),
            CloseButtonText = Loc.Get("Common_Cancel"),
            XamlRoot = RootGrid.XamlRoot,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            new SettingsWindow(selectTab: 1).Activate();
    }

    private static string Human(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        return $"{size:0.#} {units[unit]}";
    }
}
