namespace BaiJi.App.Services;

/// <summary>
/// Writes unhandled exceptions to %LOCALAPPDATA%\BaiJi\crash.log. Unpackaged
/// apps that throw during startup otherwise exit with no visible diagnostic, so
/// this is the difference between "won't open" and an actionable stack trace.
/// </summary>
public static class CrashLog
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BaiJi", "crash.log");

    public static void Install()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Write(e.ExceptionObject as Exception, "AppDomain");
        TaskScheduler.UnobservedTaskException += (_, e) => Write(e.Exception, "UnobservedTask");
    }

    public static void Write(Exception? ex, string source)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, $"[{source}] {DateTimeOffset.Now:o}\n{ex}\n\n");
        }
        catch { /* logging must never throw */ }
    }
}
