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
        // Size in DIPs scaled to the monitor DPI (Resize is in physical pixels),
        // or the content is clipped on HiDPI displays.
        var scale = GetDpiForWindow(hwnd) / 96.0;
        appWindow.Resize(new Windows.Graphics.SizeInt32((int)(680 * scale), (int)(540 * scale)));
        if (appWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter p)
            p.IsMaximizable = true; // resizable, so users can enlarge if needed

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

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

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
