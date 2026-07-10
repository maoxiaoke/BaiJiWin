using System.Runtime.InteropServices;
using BaiJi.Core;
using Xunit;

namespace BaiJi.Tests;

public class ProcessRunnerTests
{
    private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    private static string Shell => IsWindows ? "cmd.exe" : "/bin/sh";
    private static string[] ShellArgs(string script) =>
        IsWindows ? new[] { "/c", script } : new[] { "-c", script };

    [Fact]
    public async Task Captures_stdout_and_exit_code_zero()
    {
        var runner = new ProcessRunner();
        var result = await runner.RunAsync(Shell, ShellArgs("echo hello"));
        Assert.Equal(0, result.Status);
        Assert.Contains("hello", result.Output);
    }

    [Fact]
    public async Task Non_zero_exit_is_reported()
    {
        var runner = new ProcessRunner();
        var result = await runner.RunAsync(Shell, ShellArgs("exit 3"));
        Assert.Equal(3, result.Status);
    }

    [Fact]
    public async Task Stdout_lines_are_streamed_when_a_callback_is_given()
    {
        var runner = new ProcessRunner();
        var lines = new List<string>();
        var result = await runner.RunAsync(Shell, ShellArgs("echo one; echo two"), onStdoutLine: lines.Add);
        Assert.Contains("one", lines);
        Assert.Contains("two", lines);
        // Streamed stdout must not also be folded into Output.
        Assert.DoesNotContain("one", result.Output);
    }

    [Fact]
    public async Task Cancellation_terminates_the_process()
    {
        if (IsWindows) return; // sleep semantics differ; covered on unix CI
        var runner = new ProcessRunner();
        using var cts = new CancellationTokenSource();
        var handles = new List<IProcessHandle>();
        var task = runner.RunAsync(Shell, ShellArgs("sleep 30"),
            onStart: handles.Add, cancellationToken: cts.Token);
        cts.CancelAfter(200);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.Single(handles);
    }
}
