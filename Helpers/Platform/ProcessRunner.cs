using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.ComponentModel; 
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
    public static Task<ProcessResult> RunAsync(
        string executable, IList<string> arguments,
        TimeSpan? timeout = null, CancellationToken ct = default, bool resolveViaPath = true)
        => RunCoreAsync(executable, arguments, null, null, true, timeout, ct, resolveViaPath);

    /// <summary>
    /// The Streamer. Fires events line-by-line without buffering to strings.
    /// </summary>
    public static Task<ProcessResult> RunWithStreamingAsync(
        string executable, IList<string> arguments,
        Action<string>? onStdOutLine = null, Action<string>? onStdErrLine = null,
        TimeSpan? timeout = null, CancellationToken ct = default, bool resolveViaPath = true)
        => RunCoreAsync(executable, arguments, onStdOutLine, onStdErrLine, false, timeout, ct, resolveViaPath);

    private static async Task<ProcessResult> RunCoreAsync(
        string executable, IList<string> arguments,
        Action<string>? onOut, Action<string>? onErr, bool capture,
        TimeSpan? timeout, CancellationToken ct, bool resolveViaPath)
    {
        var exePath = resolveViaPath ? PlatformHelper.ResolveExecutable(executable) : executable;
        
        var psi = new ProcessStartInfo(exePath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) psi.ArgumentList.Add(argument);

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        using var process = new Process { StartInfo = psi };
        
        // Always hook up both streams to prevent OS buffer deadlocks
        process.OutputDataReceived += (_, e) => 
        { 
            if (e.Data == null) return; 
            if (capture) stdout.AppendLine(e.Data); 
            onOut?.Invoke(e.Data); 
        };
        process.ErrorDataReceived += (_, e) => 
        { 
            if (e.Data == null) return; 
            if (capture) stderr.AppendLine(e.Data); 
            onErr?.Invoke(e.Data); 
        };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }
        catch (Win32Exception wex) when (wex.NativeErrorCode is 2 or 3)
        {
            Log.Debug("[ProcessRunner] {Exe} not present on this machine", executable);
            return new ProcessResult(-1, "", wex.Message, false, false);
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
            process.WaitForExit(); // Required to ensure async stream handlers finish flushing
            
            return new ProcessResult(process.ExitCode, stdout.ToString(), stderr.ToString(), false, false);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); } catch { /* ignore teardown errors */ }
            }
            
            bool timedOut = timeout.HasValue && !ct.IsCancellationRequested;
            return new ProcessResult(-1, stdout.ToString(), stderr.ToString(), timedOut, !timedOut);
        }
    }

    /// <summary>
    /// The Quick Check. Fire-and-forget, discards output, just checks exit code.
    /// </summary>
    public static async Task<bool> CheckExistsAsync(
        string executable,
        IList<string>? arguments = null,
        int timeoutMs = 3000)
    {
        var result = await RunAsync(
            executable, 
            arguments ?? new[] { "--version" },
            TimeSpan.FromMilliseconds(timeoutMs), 
            CancellationToken.None, 
            resolveViaPath: true);
            
        return !result.TimedOut && !result.Canceled && result.ExitCode == 0;
    }
}