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

    public ICommand ImportFolderCommand { get; }
    public event Action? ImportCompleted;

    private static readonly string[] SupportedExtensions = { ".mp3", ".flac", ".wav", ".ogg", ".m4a", ".aac" };

    public ImportViewModel(LibraryService library, MetadataService metadata)
    {
        _library = library;
        _metadata = metadata;
        ImportFolderCommand = new RelayCommand(async () => await ImportFolderAsync());
    }

    private async Task ImportFolderAsync()
    {
        var folderPath = await DialogService.Instance.PickFolderAsync("Select Folder to Import");
        if (string.IsNullOrEmpty(folderPath)) return;

        LiveNotification? activity = null;

        try
        {
            NullActionLogger.ImportStarted(folderPath, "ImportViewModel");
            var stopwatch = Stopwatch.StartNew();

            var includeSubfolders = await AskIncludeSubfoldersAsync();
            var searchOption = includeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

            var files = Directory.GetFiles(folderPath, "*.*", searchOption)
                .Where(f => SupportedExtensions.Contains(Path.GetExtension(f).ToLower()))
                .ToList();

            if (files.Count == 0)
            {
                ToastService.Instance.Show("No supported audio files found in that folder.", ToastType.Warning);
                NullActionLogger.ImportFailed(folderPath, "No supported audio files found.", "ImportViewModel");
                return;
            }

            // N3: Ask Import Mode (Copy vs Keep in Place)
            var copyToLibrary = await AskCopyToLibraryAsync();
            var destDir = copyToLibrary ? NullWavePaths.DownloadsDir : null;
            if (copyToLibrary) Directory.CreateDirectory(destDir!);

            activity = ToastService.Instance.StartLiveActivity(
                "Importing Folder",
                $"Importing 0/{files.Count}...",
                isIndeterminate: false);

            int added = 0, skipped = 0;

            for (int i = 0; i < files.Count; i++)
            {
                var originalPath = files[i];
                var activePath = originalPath;

                // N3: Execute Copy if requested
                if (copyToLibrary)
                {
                    var (rawT, rawA, _) = _metadata.FetchFromLocalFile(originalPath);
                    var ext = Path.GetExtension(originalPath);
                    var safeName = FileNamer.Generate(rawA, rawT, Guid.NewGuid().ToString(), ext);
                    var targetPath = Path.Combine(destDir!, safeName);

                    if (File.Exists(targetPath)) 
                        targetPath = Path.Combine(destDir!, $"{Path.GetFileNameWithoutExtension(safeName)}_{i}{ext}");

                    File.Copy(originalPath, targetPath, overwrite: false);
                    // Track ID isn't generated yet, so we log Guid.Empty for imports
                    FileOperationJournal.Instance.Record(FileOpType.Copy, originalPath, targetPath, Guid.Empty);
                    activePath = targetPath;
                }

                var (title, artist, duration) = _metadata.FetchFromLocalFile(activePath);
                
                if (string.IsNullOrWhiteSpace(artist) || artist == "Unknown")
                {
                    var split = NullWave.Helpers.TrackNormalizer.TrySplit(title);
                    if (split != null) { artist = split.Value.Artist; title = split.Value.Title; }
                }

                var track = new Track
                {
                    Title = title,
                    Artist = artist,
                    FilePath = activePath,
                    Source = TrackSource.Local,
                    Duration = duration
                };
                var (album, trackNum) = _metadata.FetchAlbumAndTrackNumber(activePath);
                track.Album = album;
                track.TrackNumber = trackNum;

                if (!_library.IsDuplicate(track))
                {
                    _library.Add(track);
                    added++;
                    NullActionLogger.TrackAdded(track.FilePath, "LocalFolder", "ImportViewModel");
                }
                else
                {
                    skipped++;
                }

                ToastService.Instance.UpdateLiveActivity(
                    activity,
                    message: $"Importing {i + 1}/{files.Count}... ({added} added, {skipped} skipped)",
                    progressValue: (i + 1) * 100.0 / files.Count,
                    isIndeterminate: false);

                await Task.Delay(1); 
            }

            stopwatch.Stop();
            var modeText = copyToLibrary ? "copied to library" : "kept in place";
            ToastService.Instance.CompleteLiveActivity(
                activity, $"Import complete ({modeText}) - {added} added, {skipped} skipped.");

            NullActionLogger.ImportCompleted(folderPath, $"{added} tracks added", stopwatch.ElapsedMilliseconds, "ImportViewModel");
            Log.Information("Folder import complete: {Added} added, {Skipped} skipped from {Path}", added, skipped, folderPath);

            ImportCompleted?.Invoke();
        }
        catch (Exception ex)
        {
            if (activity != null) ToastService.Instance.Dismiss(activity);
            ToastService.Instance.Show($"Folder import failed: {ex.Message}", ToastType.Error);

            NullActionLogger.ImportFailed(folderPath, ex.Message, "ImportViewModel");
            NullActionLogger.Error("ImportViewModel", ex, $"Failed while executing folder batch processing layout for: {folderPath}");
        }
    }

    private static async Task<bool> AskIncludeSubfoldersAsync()
    {
        var dialog = new Views.ConfirmDialog("Import Subfolders?", "Include all subfolders in the import?");
        return await DialogService.Instance.ShowDialogAsync<bool>(dialog);
    }

    private static async Task<bool> AskCopyToLibraryAsync()
    {
        var dialog = new Views.ConfirmDialog(
            "Import Mode", 
            "Copy these files into your NullWave library folder?\n\n" +
            "Yes = Copy files (Safe, managed by NullWave)\n" +
            "No = Keep in place (Files stay where they are)");
        return await DialogService.Instance.ShowDialogAsync<bool>(dialog);
    }
}