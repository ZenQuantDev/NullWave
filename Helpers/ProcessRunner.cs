using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace NullWave.Helpers;

public record ProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut,
    bool Canceled
);

public static class ProcessRunner
{
    /// <summary>
    /// The Workhorse. Safely captures all stdout/stderr to strings without deadlocking.
    /// </summary>
    public static async Task<ProcessResult> RunAsync(
        string executable,
        string arguments = "",
        TimeSpan? timeout = null,
        CancellationToken ct = default,
        bool resolveViaPath = true)
    {
        var exePath = resolveViaPath ? PlatformHelper.ResolveExecutable(executable) : executable;
        
        var psi = new ProcessStartInfo(exePath, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        using var process = new Process { StartInfo = psi };
        
        // Always hook up both streams to prevent OS buffer deadlocks, even if we just append to strings
        process.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }
        catch (Exception ex)
        {
            Log.Warning("[ProcessRunner] Failed to start {Exe}: {Msg}", executable, ex.Message);
            return new ProcessResult(-1, "", ex.Message, false, false);
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (timeout.HasValue) cts.CancelAfter(timeout.Value);

        try
        {
            await process.WaitForExitAsync(cts.Token);
            // Required to ensure async stream handlers finish flushing before we read the strings
            process.WaitForExit(); 
            
            return new ProcessResult(process.ExitCode, stdout.ToString(), stderr.ToString(), false, false);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); } catch { /* ignore teardown errors */ }
            }
            
            bool timedOut = timeout.HasValue && !ct.IsCancellationRequested;
            bool canceled = !timedOut;
            
            return new ProcessResult(-1, stdout.ToString(), stderr.ToString(), timedOut, canceled);
        }
    }

    /// <summary>
    /// The Streamer. Fires events line-by-line without buffering to strings.
    /// </summary>
    public static async Task<ProcessResult> RunWithStreamingAsync(
        string executable,
        string arguments,
        Action<string>? onStdOutLine = null,
        Action<string>? onStdErrLine = null,
        TimeSpan? timeout = null,
        CancellationToken ct = default,
        bool resolveViaPath = true)
    {
        var exePath = resolveViaPath ? PlatformHelper.ResolveExecutable(executable) : executable;
        
        var psi = new ProcessStartInfo(exePath, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };
        
        // Always read both streams to prevent deadlocks, invoke callbacks if provided
        process.OutputDataReceived += (_, e) => { if (e.Data != null) onStdOutLine?.Invoke(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) onStdErrLine?.Invoke(e.Data); };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }
        catch (Exception ex)
        {
            Log.Warning("[ProcessRunner] Failed to start {Exe}: {Msg}", executable, ex.Message);
            return new ProcessResult(-1, "", ex.Message, false, false);
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (timeout.HasValue) cts.CancelAfter(timeout.Value);

        try
        {
            await process.WaitForExitAsync(cts.Token);
            process.WaitForExit();
            return new ProcessResult(process.ExitCode, "", "", false, false);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
            }
            
            bool timedOut = timeout.HasValue && !ct.IsCancellationRequested;
            bool canceled = !timedOut;
            
            return new ProcessResult(-1, "", "", timedOut, canceled);
        }
    }

    /// <summary>
    /// The Quick Check. Fire-and-forget, discards output, just checks exit code.
    /// </summary>
    public static async Task<bool> CheckExistsAsync(
        string executable, 
        string arguments = "--version", 
        int timeoutMs = 3000)
    {
        var result = await RunAsync(
            executable, 
            arguments, 
            TimeSpan.FromMilliseconds(timeoutMs), 
            CancellationToken.None, 
            resolveViaPath: true);
            
        return !result.TimedOut && !result.Canceled && result.ExitCode == 0;
    }
}