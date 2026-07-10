using BaiJi.App.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace BaiJi.App;

/// <summary>
/// Custom entry point. Velopack's startup hook must run first so install/update
/// events (first-run shortcuts, post-update migration) are handled before any UI.
/// </summary>
public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Handles Velopack lifecycle events; returns immediately in normal runs.
        UpdateService.RunStartupHook();

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(p =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
    }
}
