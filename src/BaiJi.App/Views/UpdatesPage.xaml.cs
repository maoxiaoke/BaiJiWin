using BaiJi.App.Services;
using BaiJi.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BaiJi.App.Views;

public sealed partial class UpdatesPage : Page
{
    private readonly UpdatesViewModel _vm = new();

    public UpdatesPage()
    {
        InitializeComponent();

        VersionText.Text = $"{Loc.Get("Updates_CurrentVersion")} {_vm.CurrentVersion}";
        AutoLabel.Text = Loc.Get("Updates_Auto");
        CheckButton.Content = Loc.Get("Updates_Check");
        RestartButton.Content = Loc.Get("Updates_Restart");
        AutoToggle.IsOn = _vm.AutoCheck;

        _vm.PropertyChanged += (_, _) => Refresh();
        Refresh();
    }

    private void Refresh()
    {
        StatusText.Text = _vm.StatusText;
        RestartButton.Visibility = _vm.CanRestart ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnAutoToggled(object sender, RoutedEventArgs e) => _vm.AutoCheck = AutoToggle.IsOn;

    private async void OnCheck(object sender, RoutedEventArgs e)
    {
        _vm.CheckCommand.Execute(null);
        await Task.CompletedTask;
        // If an update is found, prefetch it so "Restart to update" can appear.
        _vm.DownloadCommand.Execute(null);
    }

    private void OnRestart(object sender, RoutedEventArgs e) => _vm.RestartCommand.Execute(null);
}
