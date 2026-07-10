using BaiJi.App.Services;
using BaiJi.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.System;

namespace BaiJi.App.Views;

public sealed partial class LicensePage : Page
{
    private readonly LicenseViewModel _vm = new();

    public LicensePage()
    {
        InitializeComponent();

        GetKeyLink.Content = Loc.Get("License_Get");
        KeyBox.PlaceholderText = Loc.Get("License_EnterKey");
        ActivateButton.Content = Loc.Get("License_Activate");
        StatusRowLabel.Text = Loc.Get("License_ActivatedStatus");
        ManageButton.Content = Loc.Get("License_Manage");
        DeactivateButton.Content = Loc.Get("License_Deactivate");

        _vm.PropertyChanged += (_, _) => Refresh();
        Refresh();
    }

    private void Refresh()
    {
        InactivePanel.Visibility = _vm.IsActive ? Visibility.Collapsed : Visibility.Visible;
        ActivePanel.Visibility = _vm.IsActive ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Text = _vm.StatusMessage;
        ActivateButton.IsEnabled = _vm.LicenseKeyInput.Length >= 25 && !_vm.IsBusy;

        StatusRowValue.Text = Loc.Get("License_ActivatedStatus");
        KeyRow.Text = $"{Loc.Get("License_Key")}: {_vm.ActiveKey}";
        ExpiryRow.Text = $"{Loc.Get("License_ExpiryDate")}: {_vm.ExpiryDate}";
    }

    private void OnKeyChanged(object sender, TextChangedEventArgs e)
    {
        _vm.LicenseKeyInput = KeyBox.Text;
        ActivateButton.IsEnabled = _vm.LicenseKeyInput.Length >= 25 && !_vm.IsBusy;
    }

    private void OnActivate(object sender, RoutedEventArgs e) => _vm.ActivateCommand.Execute(null);

    private async void OnDeactivate(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = Loc.Get("License_Deactivate"),
            Content = Loc.Get("License_DeactivateConfirm"),
            PrimaryButtonText = Loc.Get("License_Deactivate"),
            CloseButtonText = Loc.Get("Common_Cancel"),
            XamlRoot = XamlRoot,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            _vm.DeactivateCommand.Execute(null);
    }

    private async void OnGetKey(object sender, RoutedEventArgs e) =>
        await Launcher.LaunchUriAsync(new Uri(_vm.BuyUrl));

    private async void OnManage(object sender, RoutedEventArgs e) =>
        await Launcher.LaunchUriAsync(new Uri(_vm.ManageUrl));
}
