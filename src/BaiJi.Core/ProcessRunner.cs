using System.Diagnostics;
using System.Text;

namespace BaiJi.Core;

public readonly record struct ProcessResult(int Status, string Output);

/// <summary>
/// Runs a bundled CLI tool off the calling thread and collects its output. This
/// is the C# port of the two Swift <c>run</c> helpers:
/// <list type="bullet">
///   <item>The image path collects stdout+stderr together into <c>Output</c>.</item>
///   <item>The video path streams stdout lines (ffmpeg <c>-progress</c>) to
///   <paramref name="onStdoutLine"/> while collecting stderr (the human log)
///   into <c>Output</c>.</item>
/// </list>
/// .NET's async output events + <see cref="Process.WaitForExitAsync"/> avoid the
/// pipe-buffer deadlock the Swift code guards against by draining manually.
/// </summary>
public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(
        string tool,
        IReadOnlyList<string> arguments,
        Action<string>? onStdoutLine = null,
        Action<IProcessHandle>? onStart = null,
        CancellationToken cancellationToken = default);
}

/// <summary>A cancel handle over a running process, mirroring Swift's ProcessBox usage.</summary>
public interface IProcessHandle
{
    void Terminate();
}

public sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(
        string tool,
        IReadOnlyList<string> arguments,
        Action<string>? onStdoutLine = null,
        Action<IProcessHandle>? onStart = null,
        CancellationToken cancellationToken = default)
    {
        using var process = new Process();
        process.StartInfo.FileName = tool;
        foreach (var arg in arguments) process.StartInfo.ArgumentList.Add(arg);
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.CreateNoWindow = true;

        // stderr is always collected into the returned output. stdout is either
        // streamed line-by-line (video progress) or, when no line callback is
        // given, folded into the same buffer (image tools log to both streams).
        var output = new StringBuilder();
        var outputLock = new object();

        void AppendLine(string? line)
        {
            if (line is null) return;
            lock (outputLock) output.AppendLine(line);
        }

        process.ErrorDataReceived += (_, e) => AppendLine(e.Data);
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            if (onStdoutLine is not null) onStdoutLine(e.Data);
            else AppendLine(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var handle = new Handle(process);
        onStart?.Invoke(handle);

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            handle.Terminate();
            throw;
        }

        // Ensure the async readers have flushed the final buffered lines.
        process.WaitForExit();

        lock (outputLock) return new ProcessResult(process.ExitCode, output.ToString());
    }

    private sealed class Handle : IProcessHandle
    {
        private readonly Process _process;
        public Handle(Process process) => _process = process;

        public void Terminate()
        {
            try
            {
                if (!_process.HasExited) _process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) { /* already exited */ }
        }
    }
}
