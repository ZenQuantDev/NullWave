using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using NullWave.Helpers;
using NullWave.Helpers.Logging;
using NullWave.Models;
using NullWave.Services;
using NullWave.ViewModels.Base;
using Serilog;

namespace NullWave.ViewModels;

public class ImportViewModel : ViewModelBase
{
    private readonly LibraryService _library;
    private readonly MetadataService _metadata;
    private bool _isImporting;
    private int _importProgress;
    private int _importTotal;
    private string _importStatus = string.Empty;

    public bool IsImporting
    {
        get => _isImporting;
        set { _isImporting = value; OnPropertyChanged(); }
    }

    public int ImportProgress
    {
        get => _importProgress;
        set { _importProgress = value; OnPropertyChanged(); }
    }

    public int ImportTotal
    {
        get => _importTotal;
        set { _importTotal = value; OnPropertyChanged(); }
    }

    public string ImportStatus
    {
        get => _importStatus;
        set { _importStatus = value; OnPropertyChanged(); }
    }

    public ICommand ImportFolderCommand { get; }

    public event Action? ImportCompleted;

    private static readonly string[] SupportedExtensions =
        { ".mp3", ".flac", ".wav", ".ogg", ".m4a", ".aac" };

    public ImportViewModel(LibraryService library, MetadataService metadata)
    {
        _library = library;
        _metadata = metadata;
        ImportFolderCommand = new RelayCommand(async () => await ImportFolderAsync());
    }

    private async Task ImportFolderAsync()
    {
        var window = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow : null;
        if (window == null) return;

        // Pick folder
        var folders = await window.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = "Select Folder to Import",
                AllowMultiple = false
            });

        if (folders.Count == 0) return;
        var folderPath = folders[0].Path.LocalPath;

        try
        {
            // 1. Log the initiation of the import stream
            NullActionLogger.ImportStarted(folderPath, "ImportViewModel");
            var stopwatch = Stopwatch.StartNew();

            // Ask about subfolders
            var includeSubfolders = await AskIncludeSubfoldersAsync(window);

            // Collect files
            var searchOption = includeSubfolders
                ? SearchOption.AllDirectories
                : SearchOption.TopDirectoryOnly;

            var files = Directory.GetFiles(folderPath, "*.*", searchOption)
                .Where(f => SupportedExtensions.Contains(Path.GetExtension(f).ToLower()))
                .ToList();

            if (files.Count == 0)
            {
                ImportStatus = "No supported audio files found.";
                NullActionLogger.ImportFailed(folderPath, "No supported audio files found.", "ImportViewModel");
                return;
            }

            // Import with progress
            IsImporting = true;
            ImportTotal = files.Count;
            ImportProgress = 0;
            int added = 0;
            int skipped = 0;

            foreach (var filePath in files)
            {
                ImportStatus = $"Importing {ImportProgress + 1}/{ImportTotal}...";

                var (title, artist) = _metadata.FetchFromLocalFile(filePath);

                var track = new Track
                {
                    Title = title,
                    Artist = artist,
                    FilePath = filePath,
                    Source = TrackSource.Local
                };

                if (!_library.IsDuplicate(track))
                {
                    _library.Add(track);
                    added++;

                    // 2. Log every individual local track commitment
                    NullActionLogger.TrackAdded(track.FilePath, "LocalFolder", "ImportViewModel");
                }
                else
                {
                    skipped++;
                }

                ImportProgress++;
                // Yield to UI thread
                await Task.Delay(1);
            }

            stopwatch.Stop();
            ImportStatus = $"Done - {added} added, {skipped} skipped (duplicates).";
            IsImporting = false;

            // 3. Log the successful batch execution completion metric
            NullActionLogger.ImportCompleted(folderPath, $"{added} tracks added", stopwatch.ElapsedMilliseconds, "ImportViewModel");
            Log.Information("Folder import complete: {Added} added, {Skipped} skipped from {Path}",
                added, skipped, folderPath);

            ImportCompleted?.Invoke();
        }
        catch (Exception ex)
        {
            IsImporting = false;
            ImportStatus = "An unexpected error occurred during execution.";
            
            // 4. Trace the unhandled faults straight to your error channel files
            NullActionLogger.ImportFailed(folderPath, ex.Message, "ImportViewModel");
            NullActionLogger.Error("ImportViewModel", ex, $"Failed while executing folder batch processing layout for: {folderPath}");
        }
    }

    private static async Task<bool> AskIncludeSubfoldersAsync(Avalonia.Controls.Window window)
    {
        var dialog = new Views.ConfirmDialog(
            "Import Subfolders?",
            "Include all subfolders in the import?");
        var result = await dialog.ShowDialog<bool>(window);
        return result;
    }
}