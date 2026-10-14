using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Serilog;

namespace NullWave.Helpers;

/// <summary>
/// Cross-platform helper for opening folders, files, and URLs in the system shell.
/// Replaces scattered Process.Start calls throughout the codebase.
/// </summary>
public static class ShellHelper
{
    /// <summary>
    /// Opens a folder in the system file explorer.
    /// Creates the folder if it doesn't exist.
    /// </summary>
    public static void OpenFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            Log.Warning("[ShellHelper] OpenFolder called with null/empty path");
            return;
        }

        try
        {
            Directory.CreateDirectory(path);
            
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true,
                    Verb = "open"
                });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "xdg-open",
                    Arguments = path,
                    UseShellExecute = false
                });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "open",
                    Arguments = path,
                    UseShellExecute = false
                });
            }
            else
            {
                Log.Warning("[ShellHelper] Unsupported OS platform for OpenFolder");
            }

            Log.Information("[ShellHelper] Opened folder: {Path}", path);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[ShellHelper] Failed to open folder: {Path}", path);
        }
    }

    /// <summary>
    /// Opens a file with the system's default application.
    /// </summary>
    public static void OpenFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            Log.Warning("[ShellHelper] OpenFile called with null/empty path");
            return;
        }

        if (!File.Exists(path))
        {
            Log.Warning("[ShellHelper] OpenFile: file does not exist: {Path}", path);
            return;
        }

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "xdg-open",
                    Arguments = path,
                    UseShellExecute = false
                });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "open",
                    Arguments = path,
                    UseShellExecute = false
                });
            }
            else
            {
                Log.Warning("[ShellHelper] Unsupported OS platform for OpenFile");
            }

            Log.Information("[ShellHelper] Opened file: {Path}", path);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[ShellHelper] Failed to open file: {Path}", path);
        }
    }

    /// <summary>
    /// Opens a URL in the system's default browser.
    /// </summary>
    public static void OpenUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            Log.Warning("[ShellHelper] OpenUrl called with null/empty URL");
            return;
        }

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "xdg-open",
                    Arguments = url,
                    UseShellExecute = false
                });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "open",
                    Arguments = url,
                    UseShellExecute = false
                });
            }
            else
            {
                Log.Warning("[ShellHelper] Unsupported OS platform for OpenUrl");
            }

            Log.Information("[ShellHelper] Opened URL: {Url}", url);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[ShellHelper] Failed to open URL: {Url}", url);
        }
    }

    /// <summary>
    /// Reveals a file in the system file explorer (selects the file).
    /// Only supported on Windows; falls back to opening the parent folder on other platforms.
    /// </summary>
    public static void RevealInExplorer(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            Log.Warning("[ShellHelper] RevealInExplorer called with null/empty path");
            return;
        }

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{path}\"",
                    UseShellExecute = false
                });
            }
            else
            {
                // Fallback: open the parent folder
                var parentDir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(parentDir))
                {
                    OpenFolder(parentDir);
                }
            }

            Log.Information("[ShellHelper] Revealed in explorer: {Path}", path);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[ShellHelper] Failed to reveal in explorer: {Path}", path);
        }
    }
}