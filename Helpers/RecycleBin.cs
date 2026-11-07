using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Serilog;

namespace NullWave.Helpers;

public static class RecycleBin
{
    public static bool SendToRecycleBin(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            Log.Warning("[RecycleBin] Cannot recycle null or empty path");
            return false;
        }

        if (!File.Exists(filePath))
        {
            Log.Debug("[RecycleBin] File does not exist, nothing to recycle: {Path}", filePath);
            return false;
        }

        string fullPath;
        try { fullPath = Path.GetFullPath(filePath); }
        catch (Exception ex) { Log.Warning(ex, "[RecycleBin] Invalid path: {Path}", filePath); return false; }

        // NOTE: we do NOT reject paths containing ".." here. GetFullPath already resolved
        // real traversal, and the literal ".." can appear in legitimate filenames like
        // "Wait....mp3" (truncated by FileNamer) or "Song..remix.mp3".

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return SendToRecycleBinWindows(fullPath);
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))   return SendToRecycleBinLinux(fullPath);
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))     return SendToRecycleBinMacOS(fullPath);
            return FallbackRecycle(fullPath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[RecycleBin] Failed to recycle {Path}", fullPath);
            return false;
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT FileOp);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public int wFunc;
        public string pFrom;
        public string? pTo;
        public int fFlags;
        public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    private const int FO_DELETE = 0x0003;
    private const int FOF_ALLOWUNDO = 0x0040;
    private const int FOF_NOCONFIRMATION = 0x0010;
    private const int FOF_SILENT = 0x0004;

    private static bool SendToRecycleBinWindows(string filePath)
    {
        try
        {
            var fileOp = new SHFILEOPSTRUCT
            {
                wFunc = FO_DELETE,
                pFrom = filePath + '\0' + '\0',
                pTo = null,
                fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT,
                fAnyOperationsAborted = false,
                hNameMappings = IntPtr.Zero,
                lpszProgressTitle = null
            };

            int result = SHFileOperation(ref fileOp);
            if (result == 0 && !fileOp.fAnyOperationsAborted)
            {
                Log.Information("[RecycleBin] Successfully recycled (Windows): {Path}", filePath);
                return true;
            }
            Log.Warning("[RecycleBin] Windows SHFileOperation failed with code {Code}", result);
            return FallbackRecycle(filePath);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[RecycleBin] Windows recycle failed, using fallback");
            return FallbackRecycle(filePath);
        }
    }

    private static bool SendToRecycleBinLinux(string filePath)
    {
        try
        {
            // ArgumentList avoids shell injection (titles with " ' ` $ etc).
            if (RunProcess("gio", new[] { "trash", filePath }) == 0)
            {
                Log.Information("[RecycleBin] Successfully recycled (gio): {Path}", filePath);
                return true;
            }
            if (RunProcess("kioclient5", new[] { "move", filePath, "trash:/" }) == 0)
            {
                Log.Information("[RecycleBin] Successfully recycled (KDE): {Path}", filePath);
                return true;
            }
            if (RunProcess("trash-put", new[] { filePath }) == 0)
            {
                Log.Information("[RecycleBin] Successfully recycled (trash-cli): {Path}", filePath);
                return true;
            }
            Log.Warning("[RecycleBin] No Linux trash command available, using fallback");
            return FallbackRecycle(filePath);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[RecycleBin] Linux recycle failed, using fallback");
            return FallbackRecycle(filePath);
        }
    }

    private static bool SendToRecycleBinMacOS(string filePath)
    {
        try
        {
            // Pass the path via argv, not string interpolation. This closes the
            // AppleScript injection hole for titles containing quote characters.
            if (RunProcess("osascript", new[]
                {
                    "-e", "on run argv",
                    "-e", "tell application \"Finder\" to delete (POSIX file (item 1 of argv))",
                    "-e", "end run",
                    filePath
                }) == 0)
            {
                Log.Information("[RecycleBin] Successfully recycled (macOS): {Path}", filePath);
                return true;
            }
            Log.Warning("[RecycleBin] macOS osascript failed, using fallback");
            return FallbackRecycle(filePath);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[RecycleBin] macOS recycle failed, using fallback");
            return FallbackRecycle(filePath);
        }
    }

    private static bool FallbackRecycle(string filePath)
    {
        try
        {
            var dir = Path.GetDirectoryName(filePath);
            if (string.IsNullOrEmpty(dir))
            {
                Log.Error("[RecycleBin] Cannot determine parent directory for {Path}", filePath);
                return false;
            }

            var trashDir = Path.Combine(dir, ".trash");
            Directory.CreateDirectory(trashDir);

            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
            var fileName = Path.GetFileName(filePath);
            var trashPath = Path.Combine(trashDir, $"{timestamp}_{fileName}");

            File.Move(filePath, trashPath);
            Log.Information("[RecycleBin] Fallback recycle to {TrashPath}", trashPath);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[RecycleBin] Fallback recycle failed for {Path}", filePath);
            return false;
        }
    }

    // Injection-safe: uses ArgumentList, never a single shell-parsed string.
    private static int RunProcess(string fileName, string[] arguments)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var a in arguments) psi.ArgumentList.Add(a);

            using var process = Process.Start(psi);
            if (process == null) return -1;

            // Read streams asynchronously so a hung child process doesn't block us past the timeout.
            var outTask = process.StandardOutput.ReadToEndAsync();
            var errTask = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(5000))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return -1;
            }

            // After the process exits, drain remaining output.
            outTask.Wait(1000);
            errTask.Wait(1000);
            return process.ExitCode;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[RecycleBin] Process execution failed: {FileName}", fileName);
            return -1;
        }
    }
}