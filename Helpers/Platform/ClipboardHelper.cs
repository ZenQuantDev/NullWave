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
    public static async Task<bool> CopyTrackLinkAsync(Track? track, PreferencesService prefs, IdentityService identity)
    {
        if (track == null) return false;

        string textToCopy = (prefs.Current.EnableTrackSharing && !string.IsNullOrWhiteSpace(identity.Fingerprint))
            ? ShareLink.Track(identity.Fingerprint, track.Id)
            : track.Url ?? track.FilePath ?? "";

        if (string.IsNullOrEmpty(textToCopy)) return false;
        return await SetTextAsync(textToCopy);
    }

    public static async Task<bool> SetTextAsync(string text)
    {
        try
        {
            var clipboard = GetClipboard();
            if (clipboard != null)
            {
                await clipboard.SetTextAsync(text);
                Log.Debug("Copied to clipboard: {Text}", text);
                return true;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to copy text to system clipboard.");
        }
        return false;
    }

    public static async Task<string?> GetTextAsync()
    {
        var clipboard = GetClipboard();
        if (clipboard == null) return null;

        try
        {
            var type = clipboard.GetType();
            
            // Probe 1: GetTextAsync() (Standard Avalonia 11)
            var getTextAsync = type.GetMethod("GetTextAsync", Type.EmptyTypes);
            if (getTextAsync != null)
            {
                if (getTextAsync.Invoke(clipboard, null) is Task task)
                {
                    await task.ConfigureAwait(false);
                    return task.GetType().GetProperty("Result")?.GetValue(task) as string;
                }
            }

            // Probe 2: TryGetDataAsync() -> TryGetTextAsync() (Avalonia 12)
            var getDataAsync = type.GetMethod("TryGetDataAsync", Type.EmptyTypes);
            if (getDataAsync != null)
            {
                if (getDataAsync.Invoke(clipboard, null) is Task dataTask)
                {
                    await dataTask.ConfigureAwait(false);
                    var data = dataTask.GetType().GetProperty("Result")?.GetValue(dataTask);
                    if (data != null)
                    {
                        var tryGetText = data.GetType().GetMethod("TryGetTextAsync", Type.EmptyTypes);
                        if (tryGetText != null)
                        {
                            if (tryGetText.Invoke(data, null) is Task textTask)
                            {
                                await textTask.ConfigureAwait(false);
                                return textTask.GetType().GetProperty("Result")?.GetValue(textTask) as string;
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[ClipboardHelper] Failed to read text from clipboard.");
        }
        return null;
    }

    private static IClipboard? GetClipboard()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
        {
            return TopLevel.GetTopLevel(desktop.MainWindow)?.Clipboard;
        }
        return null;
    }
}