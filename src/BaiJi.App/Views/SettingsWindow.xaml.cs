using BaiJi.App.Services;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Controls;

namespace BaiJi.App.Views;

public sealed partial class SettingsWindow : Microsoft.UI.Xaml.Window
{
    public SettingsWindow(int selectTab = 0)
    {
        InitializeComponent();
        Title = "BaiJi — " + Loc.Get("Settings_General");

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(hwnd));
        appWindow.Resize(new Windows.Graphics.SizeInt32(640, 480));

        GeneralItem.Content = Loc.Get("Settings_General");
        LicenseItem.Content = Loc.Get("Settings_License");
        UpdatesItem.Content = Loc.Get("Settings_Updates");
        InfoItem.Content = Loc.Get("Settings_Info");

        Nav.SelectedItem = selectTab switch
        {
            1 => LicenseItem,
            2 => UpdatesItem,
            3 => InfoItem,
            _ => GeneralItem,
        };
    }

    private void OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var tag = (args.SelectedItem as NavigationViewItem)?.Tag as string;
        var pageType = tag switch
        {
            "license" => typeof(LicensePage),
            "updates" => typeof(UpdatesPage),
            "info" => typeof(InfoPage),
            _ => typeof(GeneralPage),
        };
        ContentFrame.Navigate(pageType);
    }
}
