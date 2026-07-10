using BaiJi.App.Services;
using Microsoft.UI.Xaml.Controls;

namespace BaiJi.App.Views;

public sealed partial class InfoPage : Page
{
    public InfoPage()
    {
        InitializeComponent();
        SubtitleText.Text = Loc.Get("AppSubtitle");
        VersionText.Text = Loc.Format("Info_Version", AppServices.Instance.Updates.CurrentVersion);
        CopyrightText.Text = Loc.Get("Info_Copyright");
    }
}
