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
            var sb = new System.Text.StringBuilder();
            sb.Append('[').Append(source).Append("] ").Append(DateTimeOffset.Now.ToString("o")).Append('\n');
            for (var e = ex; e is not null; e = e.InnerException)
            {
                sb.Append(e.GetType().FullName).Append(": ").Append(e.Message).Append('\n');
                sb.Append("  HRESULT=0x").Append(e.HResult.ToString("X8")).Append('\n');
                foreach (System.Collections.DictionaryEntry d in e.Data)
                    sb.Append("  Data[").Append(d.Key).Append("]=").Append(d.Value).Append('\n');
            }
            sb.Append(ex).Append("\n\n");
            File.AppendAllText(LogPath, sb.ToString());
        }
        catch { /* logging must never throw */ }
    }
}
