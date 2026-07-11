using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using Xunit;

namespace BaiJi.UITests;

[Collection("ui")]
public class UiScenarios
{
    private static string SampleImage =>
        Environment.GetEnvironmentVariable("BAIJI_SAMPLE_IMAGE")
        ?? throw new InvalidOperationException("BAIJI_SAMPLE_IMAGE not set");

    [Fact]
    public void Launch_shows_the_drop_slot()
    {
        AppSession.SeedSettings(null);
        using var s = new AppSession();
        Assert.NotNull(s.Find("DropSlot"));
        s.Shot("ui-01-launch");
    }

    [Fact]
    public void Settings_opens_and_every_pane_renders()
    {
        AppSession.SeedSettings(null);
        using var s = new AppSession();

        s.Require("SettingsButton").AsButton().Invoke();
        var settings = s.FindSettingsWindow();
        Assert.NotNull(settings);
        s.Shot("ui-02a-settings-general");

        foreach (var (nav, name) in new[] { ("NavLicense", "ui-02b-license"), ("NavUpdates", "ui-02c-updates"),
                                            ("NavInfo", "ui-02d-info"), ("NavGeneral", "ui-02e-general") })
        {
            var item = Retry.WhileNull(
                () => settings!.FindFirstDescendant(cf => cf.ByAutomationId(nav)),
                TimeSpan.FromSeconds(5)).Result;
            Assert.NotNull(item);
            item!.Click();
            Wait.UntilInputIsProcessed();
            Thread.Sleep(400);
            // The content frame should hold at least one interactive/text element.
            Assert.NotEmpty(settings!.FindAllDescendants(cf => cf.ByControlType(ControlType.Text)));
            s.Shot(name);
        }
    }

    [Fact]
    public void Quick_settings_persist_across_relaunch()
    {
        AppSession.SeedSettings(null);
        string chosenQuality;
        using (var s = new AppSession())
        {
            var quality = s.Require("QualityCombo").AsComboBox();
            // Pick an item different from the current selection.
            quality.Expand();
            Thread.Sleep(200);
            var target = quality.Items.First(i => i.Text != quality.SelectedItem?.Text);
            chosenQuality = target.Text;
            target.Select();
            quality.Collapse();
            Wait.UntilInputIsProcessed();
            Thread.Sleep(400);
            s.Shot("ui-03a-quality-changed");
        }

        using (var s2 = new AppSession())
        {
            var quality = s2.Require("QualityCombo").AsComboBox();
            Assert.Equal(chosenQuality, quality.SelectedItem?.Text);
            s2.Shot("ui-03b-quality-persisted");
        }
    }

    [Fact]
    public void Compress_image_via_cli_shows_a_result_card()
    {
        // Pre-seed an active license so the queue processes without the gate.
        AppSession.SeedSettings(new() { ["licenseStatus"] = "Active", ["licenseKey"] = "TEST" });
        using var s = new AppSession(fileArg: SampleImage);

        var card = s.Find("Card", timeoutSeconds: 20);
        Assert.NotNull(card);

        // The headline should eventually show the savings / output figure.
        var headline = "";
        var gotResult = Retry.WhileFalse(() =>
        {
            headline = s.Find("CardHeadline")?.AsLabel().Text ?? "";
            return !string.IsNullOrWhiteSpace(headline);
        }, TimeSpan.FromSeconds(40), TimeSpan.FromMilliseconds(500)).Result;
        s.Shot("ui-04-result");
        Assert.True(gotResult, "result card headline never populated");
    }

    [Fact]
    public void Compress_without_license_prompts_for_activation()
    {
        AppSession.SeedSettings(null); // no license
        using var s = new AppSession(fileArg: SampleImage);

        // A ContentDialog should appear over the main window.
        var dialog = Retry.WhileNull(
            () => s.MainWindow.FindFirstDescendant(cf => cf.ByControlType(ControlType.Window).Or(cf.ByClassName("Popup")))
                  ?? s.MainWindow.ModalWindows.FirstOrDefault(),
            TimeSpan.FromSeconds(10)).Result;
        s.Shot("ui-05-license-required");
        Assert.NotNull(dialog);
    }
}
