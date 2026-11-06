using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Serilog;

namespace NullWave.Helpers;

/// <summary>
/// Cross-platform helper to send files to the OS recycle bin/trash instead of permanent deletion.
/// Windows: Uses VisualBasic FileSystem API (SendToRecycleBin)
/// Linux: Uses gio trash (GNOME) or kioclient (KDE)
/// macOS: Uses osascript to move to Trash
/// Fallback: Moves to a .trash folder in the same directory
/// </summary>
public static class RecycleBin
{
    /// <summary>
    /// Sends a file to the OS recycle bin. Returns true if successful, false otherwise.
    /// Never throws - all errors are logged and return false.
    /// </summary>
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

        // Security: Resolve to absolute path and validate
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(filePath);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[RecycleBin] Invalid path: {Path}", filePath);
            return false;
        }

        // Prevent path traversal attacks
        if (fullPath.Contains(".."))
        {
            Log.Warning("[RecycleBin] Blocked path traversal attempt: {Path}", fullPath);
            return false;
        }

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return SendToRecycleBinWindows(fullPath);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return SendToRecycleBinLinux(fullPath);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return SendToRecycleBinMacOS(fullPath);
            }
            else
            {
                Log.Warning("[RecycleBin] Unsupported platform, using fallback");
                return FallbackRecycle(fullPath);
            }
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
            // Use SHFileOperation with FOF_ALLOWUNDO to send to Recycle Bin
            var fileOp = new SHFILEOPSTRUCT
            {
                wFunc = FO_DELETE,
                pFrom = filePath + '\0' + '\0', // Double null terminator required
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
            else
            {
                Log.Warning("[RecycleBin] Windows SHFileOperation failed with code {Code}", result);
                return FallbackRecycle(filePath);
            }
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
            // Try gio trash first (GNOME/GTK standard)
            var gioResult = RunProcess("gio", $"trash \"{filePath}\"");
            if (gioResult.ExitCode == 0)
            {
                Log.Information("[RecycleBin] Successfully recycled (gio): {Path}", filePath);
                return true;
            }

            // Fallback to kioclient (KDE)
            var kdeResult = RunProcess("kioclient5", $"move \"{filePath}\" trash:/");
            if (kdeResult.ExitCode == 0)
            {
                Log.Information("[RecycleBin] Successfully recycled (KDE): {Path}", filePath);
                return true;
            }

            // Fallback to trash-cli if available
            var trashCliResult = RunProcess("trash-put", $"\"{filePath}\"");
            if (trashCliResult.ExitCode == 0)
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
            // Use osascript to move to Trash via Finder
            var script = $"tell application \"Finder\" to delete POSIX file \"{filePath}\"";
            var result = RunProcess("osascript", $"-e \"{script}\"");
            
            if (result.ExitCode == 0)
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
            // Create a .trash folder in the same directory
            var dir = Path.GetDirectoryName(filePath);
            if (string.IsNullOrEmpty(dir))
            {
                Log.Error("[RecycleBin] Cannot determine parent directory for {Path}", filePath);
                return false;
            }

            var trashDir = Path.Combine(dir, ".trash");
            Directory.CreateDirectory(trashDir);

            // Generate unique filename with UTC timestamp to prevent collisions
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

    private static (int ExitCode, string Output) RunProcess(string fileName, string arguments)
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit(5000); // 5 second timeout

            return (process.ExitCode, output + error);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[RecycleBin] Process execution failed: {FileName} {Args}", fileName, arguments);
            return (-1, ex.Message);
        }
    }
}