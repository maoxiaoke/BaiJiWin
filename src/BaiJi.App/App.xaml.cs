using BaiJi.App.Services;
using Microsoft.UI.Xaml;
using Windows.Globalization;

namespace BaiJi.App;

public partial class App : Application
{
    public static Window? MainWindow { get; private set; }

    public App()
    {
        InitializeComponent();

        // Apply the saved language override before any UI is created.
        var lang = AppServices.Instance.Settings.GetString("appLanguage");
        if (!string.IsNullOrEmpty(lang))
        {
            ApplicationLanguages.PrimaryLanguageOverride = lang;
            Loc.UseLanguage(lang);
        }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow = new MainWindow();
        MainWindow.Activate();

        // Kick off a background update check if the user hasn't opted out.
        if (AppServices.Instance.Settings.GetString("autoCheckUpdates") != bool.FalseString)
            _ = AppServices.Instance.Updates.CheckAsync();
    }
}
