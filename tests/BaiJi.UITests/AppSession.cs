using System.Diagnostics;
using System.Text.Json;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;
using FlaUI.UIA3;

namespace BaiJi.UITests;

// UI tests share one desktop + screen capture, so they must not run in parallel.
[CollectionDefinition("ui", DisableParallelization = true)]
public class UiCollection { }

/// <summary>
/// Launches the published BaiJi.exe and exposes helpers to drive it via UI
/// Automation and capture screenshots.
/// </summary>
public sealed class AppSession : IDisposable
{
    public Application App { get; }
    public UIA3Automation Automation { get; }
    public Window MainWindow { get; }

    private static string ExePath =>
        Environment.GetEnvironmentVariable("BAIJI_APP_EXE")
        ?? throw new InvalidOperationException("BAIJI_APP_EXE not set");

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BaiJi", "settings.json");

    public static string ShotDir =>
        Environment.GetEnvironmentVariable("BAIJI_SHOT_DIR") ?? Path.GetTempPath();

    /// <summary>Pre-seed the app's settings.json (e.g. an active license) before launch.</summary>
    public static void SeedSettings(Dictionary<string, string>? values)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        if (values is null) { if (File.Exists(SettingsPath)) File.Delete(SettingsPath); return; }
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(values));
    }

    public AppSession(string? fileArg = null)
    {
        var psi = new ProcessStartInfo(ExePath);
        if (fileArg is not null) psi.ArgumentList.Add(fileArg);
        App = Application.Launch(psi);
        Automation = new UIA3Automation();
        MainWindow = App.GetMainWindow(Automation, TimeSpan.FromSeconds(30))
                     ?? throw new InvalidOperationException("main window did not appear");
    }

    public AutomationElement? Find(string automationId, int timeoutSeconds = 10) =>
        Retry.WhileNull(
            () => MainWindow.FindFirstDescendant(cf => cf.ByAutomationId(automationId)),
            TimeSpan.FromSeconds(timeoutSeconds), TimeSpan.FromMilliseconds(200)).Result;

    public AutomationElement Require(string automationId, int timeoutSeconds = 10) =>
        Find(automationId, timeoutSeconds) ?? throw new InvalidOperationException($"element '{automationId}' not found");

    /// <summary>Finds the separate Settings window (same process, distinct title).</summary>
    public Window? FindSettingsWindow(int timeoutSeconds = 10) =>
        Retry.WhileNull(() =>
        {
            var w = Automation.GetDesktop()
                .FindAllChildren(cf => cf.ByControlType(ControlType.Window))
                .FirstOrDefault(x => x.Properties.ProcessId.ValueOrDefault == App.ProcessId
                                     && x.Properties.NativeWindowHandle.ValueOrDefault != MainWindow.Properties.NativeWindowHandle.ValueOrDefault);
            return w?.AsWindow();
        }, TimeSpan.FromSeconds(timeoutSeconds), TimeSpan.FromMilliseconds(250)).Result;

    public void Shot(string name)
    {
        Directory.CreateDirectory(ShotDir);
        Wait.UntilInputIsProcessed();
        Thread.Sleep(600); // let animations settle
        Capture.Screen().ToFile(Path.Combine(ShotDir, name + ".png"));
    }

    public void Dispose()
    {
        try { Automation.Dispose(); } catch { }
        try { App.Close(); } catch { }
        try { if (!App.HasExited) App.Kill(); } catch { }
    }
}
