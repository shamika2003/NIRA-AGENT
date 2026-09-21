using System.Diagnostics;
using System.Text;

namespace NIRAAgent.Capabilities;

internal static class NIRACapabilityProcessRunner
{
    public static async Task<NIRACapabilityHandlerResult> RunAsync(
        ProcessStartInfo startInfo, bool waitForExit, int timeoutSeconds, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        startInfo.UseShellExecute = false;
        startInfo.RedirectStandardOutput = waitForExit;
        startInfo.RedirectStandardError = waitForExit;
        startInfo.CreateNoWindow = waitForExit;
        using Process process = new() { StartInfo = startInfo };
        if (!process.Start()) throw new InvalidOperationException("Process start returned false.");
        int pid = process.Id;
        if (!waitForExit)
            return new()
            {
                Summary = $"Started process PID={pid} | FileName='{startInfo.FileName}'. Completion has not been observed.",
                Output = $"PID={pid}\nFileName={startInfo.FileName}\nCompletionObserved=False",
                ChangedSystemState = true
            };
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        Task<string> stdout = ReadBoundedAsync(process.StandardOutput, timeout.Token);
        Task<string> stderr = ReadBoundedAsync(process.StandardError, timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            await Task.WhenAll(stdout, stderr).WaitAsync(timeout.Token);
            int exitCode = process.ExitCode;
            return new()
            {
                Succeeded = exitCode == 0, ExitCode = exitCode,
                Summary = $"Process PID={pid} exited with code {exitCode}.",
                Output = $"PID={pid}\nExitCode={exitCode}\nSTDOUT:\n{await stdout}\nSTDERR:\n{await stderr}",
                ChangedSystemState = true
            };
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (Exception ex) { Debug.WriteLine($"[CapabilityProcess] Stop failed | PID={pid} | {ex.GetType().Name}"); }
            timeout.Cancel();
            try { await Task.WhenAll(stdout, stderr).WaitAsync(TimeSpan.FromSeconds(2)); } catch { }
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2)); } catch { }
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException($"Process PID={pid} exceeded {timeoutSeconds}s; termination was requested. Inspect its effects before retrying.");
        }
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken ct)
    {
        const int limit = 11000;
        StringBuilder output = new();
        char[] buffer = new char[4096];
        bool truncated = false;
        while (true)
        {
            int count = await reader.ReadAsync(buffer.AsMemory(), ct);
            if (count == 0) break;
            int take = Math.Min(count, limit - output.Length);
            if (take > 0) output.Append(buffer, 0, take);
            truncated |= take < count;
        }
        if (truncated) output.Append("\n[Output truncated; remaining bytes were drained.]");
        return output.ToString();
    }
}

