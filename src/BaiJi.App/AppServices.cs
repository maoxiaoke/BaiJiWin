using BaiJi.App.Services;
using BaiJi.Core;
using Microsoft.UI.Dispatching;

namespace BaiJi.App;

/// <summary>
/// Tiny composition root. Wires the platform services into the Core objects and
/// marshals queue callbacks (progress, auto-copy) back onto the UI thread.
/// </summary>
public sealed class AppServices
{
    public ISettingsStore Settings { get; }
    public IToolLocator Tools { get; }
    public WindowsClipboard Clipboard { get; }
    public LicenseManager License { get; }
    public UpdateService Updates { get; }
    public MediaQueue Queue { get; }

    private static AppServices? _instance;
    public static AppServices Instance => _instance ??= new AppServices();

    private AppServices()
    {
        var dispatcher = DispatcherQueue.GetForCurrentThread();
        Action<Action> post = action =>
        {
            if (dispatcher is null || dispatcher.HasThreadAccess) action();
            else dispatcher.TryEnqueue(() => action());
        };

        Settings = new FileSettingsStore();
        Tools = new BundledToolLocator();
        Clipboard = new WindowsClipboard();
        License = new LicenseManager(Settings, new LemonSqueezyClient(new HttpClient()));
        Updates = new UpdateService();

        var imageProcessor = new ImageProcessor(Tools, new ProcessRunner());
        var videoConverter = new VideoConverter(Tools, new ProcessRunner());
        Queue = new MediaQueue(Settings, imageProcessor, videoConverter, post)
        {
            IsLicensed = () => License.IsActive,
            CopyImageToClipboard = path => Clipboard.CopyImage(path),
        };

        // Staging area is wiped on every launch (opt-in ephemeral results).
        Queue.CleanStagingDirectory();
    }
}
