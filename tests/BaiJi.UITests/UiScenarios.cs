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

    private static string SamplePng =>
        Environment.GetEnvironmentVariable("BAIJI_SAMPLE_PNG")
        ?? throw new InvalidOperationException("BAIJI_SAMPLE_PNG not set");

    [Fact]
    public void Launch_shows_the_drop_slot()
    {
        AppSession.SeedSettings(null);
        using var s = new AppSession();
        // Layout panels aren't surfaced to UIA; assert on the slot's text instead.
        Assert.NotNull(s.Find("SlotTitle"));
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

        // Wait for the DONE state: the "Save to…" button is only shown once a
        // result exists (it's Collapsed while queued/processing).
        var done = Retry.WhileNull(
            () => s.Find("SaveButton", 2),
            TimeSpan.FromSeconds(40), TimeSpan.FromMilliseconds(500)).Result;
        var headline = s.Find("CardHeadline", 2)?.AsLabel().Text ?? "";
        s.Shot("ui-04-result");
        Assert.NotNull(done);
        Assert.False(string.IsNullOrWhiteSpace(headline), "result card headline never populated");
    }

    [Fact]
    public void Compress_png_via_pngquant_shows_a_result_card()
    {
        // PNG goes through pngquant (not magick) — exercises the pngquant.exe +
        // bundled VC++ runtime path that a jpg would skip.
        AppSession.SeedSettings(new() { ["licenseStatus"] = "Active", ["licenseKey"] = "TEST" });
        using var s = new AppSession(fileArg: SamplePng);

        var done = Retry.WhileNull(
            () => s.Find("SaveButton", 2),
            TimeSpan.FromSeconds(40), TimeSpan.FromMilliseconds(500)).Result;
        var headline = s.Find("CardHeadline", 2)?.AsLabel().Text ?? "";
        s.Shot("ui-06-png-result");
        Assert.NotNull(done);
        Assert.False(string.IsNullOrWhiteSpace(headline), "png result card headline never populated");
    }

    [Fact]
    public void Compress_without_license_prompts_for_activation()
    {
        AppSession.SeedSettings(null); // no license
        using var s = new AppSession(fileArg: SampleImage);

        // The license ContentDialog's primary button ("Manage license") is a
        // reliable UIA anchor for "the dialog is up".
        var dialogButton = Retry.WhileNull(
            () => s.MainWindow.FindFirstDescendant(cf => cf.ByName("Manage license")),
            TimeSpan.FromSeconds(12), TimeSpan.FromMilliseconds(300)).Result;
        s.Shot("ui-05-license-required");
        Assert.NotNull(dialogButton);
    }
}
