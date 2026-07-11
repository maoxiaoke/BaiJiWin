using BaiJi.App.Services;
using Microsoft.UI.Xaml;
using Windows.Globalization;

namespace BaiJi.App;

public partial class App : Application
{
    public static Window? MainWindow { get; private set; }

    /// <summary>File paths passed on the command line, enqueued after launch.</summary>
    public static string[] StartupFileArgs { get; set; } = Array.Empty<string>();

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) => CrashLog.Write(e.Exception, "WinUI");

        // Apply the saved language override before any UI is created. Guarded:
        // PrimaryLanguageOverride can be finicky in unpackaged apps and must not
        // take the whole app down.
        try
        {
            var lang = AppServices.Instance.Settings.GetString("appLanguage");
            if (!string.IsNullOrEmpty(lang))
            {
                ApplicationLanguages.PrimaryLanguageOverride = lang;
                Loc.UseLanguage(lang);
            }
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex, "LanguageInit");
        }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow = new MainWindow();
        MainWindow.Activate();

        // Enqueue any files passed on the command line (Open-with / e2e tests).
        if (StartupFileArgs.Length > 0)
            AppServices.Instance.Queue.Enqueue(StartupFileArgs);

        // Kick off a background update check if the user hasn't opted out.
        if (AppServices.Instance.Settings.GetString("autoCheckUpdates") != bool.FalseString)
            _ = AppServices.Instance.Updates.CheckAsync();
    }
}
