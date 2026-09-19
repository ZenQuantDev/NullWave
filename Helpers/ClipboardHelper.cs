using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using NullWave.Models;
using Serilog;

namespace NullWave.Helpers;

public static class ClipboardHelper
{
    /// <summary>
    /// Copies a track's URL (or local file path) to the system clipboard.
    /// Returns true if successful, false otherwise.
    /// </summary>
    public static async Task<bool> CopyTrackLinkAsync(Track? track)
    {
        var url = track?.Url ?? track?.FilePath;
        if (string.IsNullOrEmpty(url)) return false;

        try
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
            {
                var clipboard = TopLevel.GetTopLevel(desktop.MainWindow)?.Clipboard;
                if (clipboard != null)
                {
                    await clipboard.SetTextAsync(url);
                    Log.Information("URL copied to clipboard: {Url}", url);
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            // Replaced garbled placeholder text with a clean, standard error log
            Log.Error(ex, "Failed to copy track URL to system clipboard.");
        }
        
        return false;
    }
}