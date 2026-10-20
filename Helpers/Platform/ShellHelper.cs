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
    /// Opens a folder in the system file explorer. Creates it if missing.
    /// </summary>
    public static void OpenFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        try
        {
            Directory.CreateDirectory(path);
            Launch(path, isUrl: false, isFolder: true);
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
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;

        try
        {
            Launch(path, isUrl: false, isFolder: false);
            Log.Information("[ShellHelper] Opened file: {Path}", path);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[ShellHelper] Failed to open file: {Path}", path);
        }
    }

    /// <summary>
    /// Opens a URL in the system's default browser.
    /// SECURITY: Only allows http:// or https:// schemes to prevent local file execution.
    /// </summary>
    public static void OpenUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;

        // Validate scheme strictly to prevent shell injection / arbitrary file launch
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            Log.Warning("[ShellHelper] Rejected non-http(s) URL: {Url}", url);
            return;
        }

        try
        {
            Launch(url, isUrl: true, isFolder: false);
            Log.Information("[ShellHelper] Opened URL: {Url}", url);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[ShellHelper] Failed to open URL: {Url}", url);
        }
    }

    /// <summary>
    /// Reveals a file in the system file explorer (selects the file).
    /// Only supported on Windows; falls back to opening the parent folder elsewhere.
    /// </summary>
    public static void RevealInExplorer(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

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
                var parentDir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(parentDir))
                    OpenFolder(parentDir);
            }

            Log.Information("[ShellHelper] Revealed in explorer: {Path}", path);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[ShellHelper] Failed to reveal in explorer: {Path}", path);
        }
    }

    private static void Launch(string target, bool isUrl, bool isFolder)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Windows ShellExecute handles URLs, files, and folders securely
            Process.Start(new ProcessStartInfo
            {
                FileName = target,
                UseShellExecute = true,
                Verb = isFolder ? "open" : null
            });
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // ArgumentList prevents word-splitting issues with spaces in paths
            var psi = new ProcessStartInfo("xdg-open")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add(target);
            Process.Start(psi);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var psi = new ProcessStartInfo("open")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add(target);
            Process.Start(psi);
        }
        else
        {
            Log.Warning("[ShellHelper] Unsupported OS platform");
        }
    }
}