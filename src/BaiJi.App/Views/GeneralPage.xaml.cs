using BaiJi.App.Services;
using BaiJi.App.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace BaiJi.App.Views;

public sealed partial class GeneralPage : Page
{
    private readonly GeneralViewModel _vm = new();

    public GeneralPage()
    {
        InitializeComponent();

        LanguageLabel.Text = Loc.Get("Settings_Language");
        OutputLabel.Text = Loc.Get("Settings_OutputDirectory");
        CopyLabel.Text = Loc.Get("Settings_CopyToClipboard");
        ChooseButton.Content = Loc.Get("Settings_Choose");

        LanguageCombo.ItemsSource = _vm.Languages.Select(l => new { l.Code, l.Name }).ToList();
        LanguageCombo.SelectedIndex = Math.Max(0, _vm.Languages.ToList().FindIndex(l => l.Code == _vm.Language));

        OutputCombo.ItemsSource = new[]
        {
            Loc.Get("Gear_DestinationSameAsInput"),
            Loc.Get("Gear_DestinationStaging"),
            Loc.Get("Gear_DestinationCustom"),
        };
        OutputCombo.SelectedIndex = _vm.OutputDirectory switch
        {
            "Staging" => 1,
            "Custom" => 2,
            _ => 0,
        };
        CustomPath.Text = _vm.CustomOutputDirectory;
        CopyToggle.IsOn = _vm.CopyToClipboard;
        UpdateCustomRow();
    }

    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LanguageCombo.SelectedIndex >= 0)
            _vm.Language = _vm.Languages[LanguageCombo.SelectedIndex].Code;
    }

    private void OnOutputChanged(object sender, SelectionChangedEventArgs e)
    {
        _vm.OutputDirectory = OutputCombo.SelectedIndex switch { 1 => "Staging", 2 => "Custom", _ => "Same as input" };
        UpdateCustomRow();
    }

    private void UpdateCustomRow() =>
        CustomRow.Visibility = _vm.OutputDirectory == "Custom"
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;

    private async void OnChooseFolder(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null)
        {
            _vm.CustomOutputDirectory = folder.Path;
            CustomPath.Text = folder.Path;
        }
    }

    private void OnCopyToggled(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) =>
        _vm.CopyToClipboard = CopyToggle.IsOn;
}
