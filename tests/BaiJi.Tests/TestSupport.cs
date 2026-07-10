using BaiJi.Core;

namespace BaiJi.Tests;

/// <summary>
/// Resolves the media tools and sample assets used by the integration/e2e tests.
/// On this repo's dev/CI macOS box the tools + TestAssets come from the sibling
/// <c>../BaiJi</c> checkout; both can be overridden with env vars so the same
/// tests run on a Windows box against the fetched win-x64 binaries.
/// </summary>
public static class TestSupport
{
    public static string? ToolsDirectory { get; } = ResolveToolsDirectory();
    public static string? AssetsDirectory { get; } = ResolveAssetsDirectory();

    public static bool HasTools =>
        ToolsDirectory is not null && new DirectoryToolLocator(ToolsDirectory).FFmpegPath is not null;

    public static bool HasAssets => AssetsDirectory is not null && Directory.Exists(AssetsDirectory);

    public static IToolLocator Tools() => new DirectoryToolLocator(ToolsDirectory!);

    public static string Asset(string relative) => Path.Combine(AssetsDirectory!, relative);

    /// <summary>A fresh temp directory for a test's outputs, cleaned by the caller.</summary>
    public static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "baiji-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string? RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (dir.Name is "BaiJiWins") return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }

    private static string? ResolveToolsDirectory()
    {
        var env = Environment.GetEnvironmentVariable("BAIJI_TOOLS_DIR");
        if (!string.IsNullOrEmpty(env) && Directory.Exists(env)) return env;

        var root = RepoRoot();
        if (root is null) return null;
        // Bundled win-x64 tools live under src/BaiJi.App/Tools once fetched.
        var win = Path.Combine(root, "src", "BaiJi.App", "Tools");
        if (new DirectoryToolLocator(win).FFmpegPath is not null) return win;
        // Fall back to the sibling macOS checkout's binaries for local dev.
        var mac = Path.Combine(Directory.GetParent(root)!.FullName, "BaiJi", "BaiJi");
        if (new DirectoryToolLocator(mac).FFmpegPath is not null) return mac;
        return null;
    }

    private static string? ResolveAssetsDirectory()
    {
        var env = Environment.GetEnvironmentVariable("BAIJI_TEST_ASSETS");
        if (!string.IsNullOrEmpty(env) && Directory.Exists(env)) return env;

        var root = RepoRoot();
        if (root is null) return null;
        var local = Path.Combine(root, "TestAssets");
        if (Directory.Exists(local)) return local;
        var sibling = Path.Combine(Directory.GetParent(root)!.FullName, "BaiJi", "TestAssets");
        return Directory.Exists(sibling) ? sibling : null;
    }
}

/// <summary>A scripted <see cref="IProcessRunner"/> for unit tests — no real subprocess.</summary>
public sealed class FakeProcessRunner : IProcessRunner
{
    private readonly Func<string, IReadOnlyList<string>, ProcessResult> _behavior;
    public readonly List<(string Tool, IReadOnlyList<string> Args)> Calls = new();
    /// <summary>Optional: lines to feed to onStdoutLine for each call.</summary>
    public Func<string, IReadOnlyList<string>, IEnumerable<string>>? StdoutLines { get; set; }
    /// <summary>Optional: side effect (e.g. write the output file) before returning.</summary>
    public Action<string, IReadOnlyList<string>>? SideEffect { get; set; }

    public FakeProcessRunner(Func<string, IReadOnlyList<string>, ProcessResult> behavior) => _behavior = behavior;

    public Task<ProcessResult> RunAsync(
        string tool, IReadOnlyList<string> arguments,
        Action<string>? onStdoutLine = null, Action<IProcessHandle>? onStart = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Calls.Add((tool, arguments));
        if (onStdoutLine is not null && StdoutLines is not null)
            foreach (var line in StdoutLines(tool, arguments)) onStdoutLine(line);
        SideEffect?.Invoke(tool, arguments);
        return Task.FromResult(_behavior(tool, arguments));
    }
}
