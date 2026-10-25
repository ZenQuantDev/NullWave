using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Serilog;

namespace NullWave.Services;

/// <summary>
/// Centralized dialog and file picker service.
/// Eliminates boilerplate window lookup code scattered across ViewModels.
/// Must be called from the UI thread (it does not marshal).
/// </summary>
public class DialogService
{
    public static DialogService Instance { get; } = new();

    private DialogService() { }

    /// <summary>Gets the main application window, or null if not available.</summary>
    public Window? MainWindow
    {
        get
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                return desktop.MainWindow;
            return null;
        }
    }


    // FILE PICKERS


    /// <summary>Opens a file picker dialog. Returns the selected file path, or null if cancelled.</summary>
    public async Task<string?> PickFileAsync(string title, params string[] patterns)
    {
        var window = MainWindow;
        if (window == null)
        {
            Log.Warning("[DialogService] PickFileAsync: MainWindow not available");
            return null;
        }

        try
        {
            var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
                FileTypeFilter = CreateFileTypes(patterns)
            });

            return files.Count > 0 ? files[0].Path.LocalPath : null;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[DialogService] PickFileAsync failed");
            return null;
        }
    }

    /// <summary>Opens a multi-file picker dialog. Returns selected file paths.</summary>
    public async Task<IReadOnlyList<string>> PickFilesAsync(string title, params string[] patterns)
    {
        var window = MainWindow;
        if (window == null)
        {
            Log.Warning("[DialogService] PickFilesAsync: MainWindow not available");
            return Array.Empty<string>();
        }

        try
        {
            var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = true,
                FileTypeFilter = CreateFileTypes(patterns)
            });

            return files.Select(f => f.Path.LocalPath).Where(p => !string.IsNullOrEmpty(p)).ToList();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[DialogService] PickFilesAsync failed");
            return Array.Empty<string>();
        }
    }

    /// <summary>Opens a save file dialog. Returns the selected path, or null if cancelled.</summary>
    public async Task<string?> SaveFileAsync(string title, string defaultExtension, string? suggestedFileName = null, params string[] patterns)
    {
        var window = MainWindow;
        if (window == null)
        {
            Log.Warning("[DialogService] SaveFileAsync: MainWindow not available");
            return null;
        }

        try
        {
            var options = new FilePickerSaveOptions
            {
                Title = title,
                DefaultExtension = defaultExtension,
                FileTypeChoices = CreateFileTypes(patterns)
            };

            if (!string.IsNullOrEmpty(suggestedFileName))
                options.SuggestedFileName = suggestedFileName;

            var file = await window.StorageProvider.SaveFilePickerAsync(options);
            return file?.Path.LocalPath;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[DialogService] SaveFileAsync failed");
            return null;
        }
    }

    /// <summary>Opens a folder picker dialog. Returns the selected folder path, or null if cancelled.</summary>
    public async Task<string?> PickFolderAsync(string title)
    {
        var window = MainWindow;
        if (window == null)
        {
            Log.Warning("[DialogService] PickFolderAsync: MainWindow not available");
            return null;
        }

        try
        {
            var folders = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = title,
                AllowMultiple = false
            });

            return folders.Count > 0 ? folders[0].Path.LocalPath : null;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[DialogService] PickFolderAsync failed");
            return null;
        }
    }


    // WINDOW MANAGEMENT


    /// <summary>Shows a window (non-modal).</summary>
    public void ShowWindow(Window window)
    {
        var main = MainWindow;
        if (main != null)
            window.Show(main);
        else
            window.Show();
    }

    /// <summary>Shows a window modally over the main window (falls back to non-modal if there is none).</summary>
    public void ShowModal(Window window)
    {
        var main = MainWindow;
        if (main != null) _ = window.ShowDialog(main);
        else window.Show();
    }

    /// <summary>Shows a modal dialog and returns the result.</summary>
    public async Task<T?> ShowDialogAsync<T>(Window dialog)
    {
        var main = MainWindow;
        if (main == null)
        {
            Log.Warning("[DialogService] ShowDialogAsync: MainWindow not available");
            return default;
        }

        try
        {
            return await dialog.ShowDialog<T>(main);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[DialogService] ShowDialogAsync failed");
            return default;
        }
    }

    /// <summary>Shows a modal dialog (no return value).</summary>
    public async Task ShowDialogAsync(Window dialog)
    {
        var main = MainWindow;
        if (main == null)
        {
            Log.Warning("[DialogService] ShowDialogAsync: MainWindow not available");
            return;
        }

        try
        {
            await dialog.ShowDialog(main);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[DialogService] ShowDialogAsync failed");
        }
    }


    // HELPERS


    private static IReadOnlyList<FilePickerFileType> CreateFileTypes(string[] patterns)
    {
        if (patterns == null || patterns.Length == 0)
            return Array.Empty<FilePickerFileType>();

        // Group patterns by extension type
        var groups = new Dictionary<string, List<string>>();
        
        foreach (var pattern in patterns)
        {
            var ext = System.IO.Path.GetExtension(pattern).ToLowerInvariant();
            var typeName = ext.TrimStart('.') switch
            {
                "jpg" or "jpeg" => "Images",
                "png" => "Images",
                "webp" => "Images",
                "mp3" or "flac" or "wav" or "ogg" or "m4a" or "aac" or "opus" => "Audio",
                "json" => "JSON",
                "txt" or "md" => "Text",
                "db" => "Database",
                _ => "Files"
            };

            if (!groups.ContainsKey(typeName))
                groups[typeName] = new List<string>();
            
            if (!groups[typeName].Contains(pattern))
                groups[typeName].Add(pattern);
        }

        return groups.Select(g => new FilePickerFileType(g.Key) { Patterns = g.Value }).ToList();
    }

}