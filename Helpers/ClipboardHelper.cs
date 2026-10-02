using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using NullWave.Models;
using NullWave.Services;
using NullWave.Services.Security;
using Serilog;

namespace NullWave.Helpers;

public static class ClipboardHelper
{
    /// <summary>
    /// Copies a track's share link (if enabled) or standard URL/file path to the system clipboard.
    /// </summary>
    public static async Task<bool> CopyTrackLinkAsync(Track? track, PreferencesService prefs, IdentityService identity)
    {
        if (track == null) return false;

        string textToCopy;
        
        // FIX: If track sharing is enabled, generate a nullwave:// deep link
        if (prefs.Current.EnableTrackSharing && !string.IsNullOrWhiteSpace(identity.Fingerprint))
        {
            textToCopy = ShareLink.Track(identity.Fingerprint, track.Id);
        }
        else
        {
            // Fallback to standard URL or file path
            textToCopy = track.Url ?? track.FilePath ?? "";
        }

        if (string.IsNullOrEmpty(textToCopy)) return false;

        try
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
            {
                var clipboard = TopLevel.GetTopLevel(desktop.MainWindow)?.Clipboard;
                if (clipboard != null)
                {
                    await clipboard.SetTextAsync(textToCopy);
                    Log.Information("Copied to clipboard: {Url}", textToCopy);
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to copy track link to system clipboard.");
        }
        
        return false;
    }
}