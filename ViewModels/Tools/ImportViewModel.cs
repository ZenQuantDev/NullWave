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

public record ImportResult(int Added, int Skipped, int Failed);

public class ImportViewModel : ViewModelBase
{
    private readonly LibraryService _library;
    private readonly MetadataService _metadata;
    private readonly PreferencesService _prefs;

    public ICommand ImportFolderCommand { get; }
    public event Action? ImportCompleted;

    private static readonly string[] SupportedExtensions = { ".mp3", ".flac", ".wav", ".ogg", ".m4a", ".aac" };

    public ImportViewModel(LibraryService library, MetadataService metadata, PreferencesService prefs)
    {
        _library = library;
        _metadata = metadata;
        _prefs = prefs;
        ImportFolderCommand = new RelayCommand(async () => await ImportFolderAsync());
    }

    /// <summary>
    /// The shared import pipeline. Used by both the Main Window's "Import Folder"
    /// and the File Browser's "Add to Library" actions.
    /// </summary>
    public async Task<ImportResult> ImportFilesAsync(
        IEnumerable<string> filePaths,
        string sourceLabel = "LocalFiles",
        bool? forceKeepInPlace = null)
    {
        var files = filePaths.ToList();
        if (files.Count == 0) return new ImportResult(0, 0, 0);

        // File Browser explicitly promises "file stays in place", so callers can force that.
        // Normal folder import still honors the saved preference / Ask prompt.
        bool copyToLibrary;
        if (forceKeepInPlace.HasValue)
            copyToLibrary = !forceKeepInPlace.Value;
        else
            copyToLibrary = await ResolveCopyDecisionAsync();

        var destDir = copyToLibrary ? NullWavePaths.DownloadsDir : null;
        if (copyToLibrary) Directory.CreateDirectory(destDir!);

        LiveNotification? activity = null;
        if (files.Count > 1)
        {
            activity = ToastService.Instance.StartLiveActivity(
                "Importing Files",
                $"Importing 0/{files.Count}...",
                isIndeterminate: false);
        }

        int added = 0, skipped = 0, failed = 0;

        for (int i = 0; i < files.Count; i++)
        {
            var originalPath = files[i];
            try
            {
                var (rawTitle, rawArtist, duration) = _metadata.FetchFromLocalFile(originalPath);
                string title = rawTitle;
                string artist = rawArtist;

                if (string.IsNullOrWhiteSpace(artist) || artist == "Unknown")
                {
                    var split = TrackNormalizer.TrySplit(rawTitle);
                    if (split != null) { artist = split.Value.Artist; title = split.Value.Title; }
                }

                var track = new Track
                {
                    Title = title,
                    Artist = artist,
                    FilePath = originalPath,
                    Source = TrackSource.Local,
                    Duration = duration
                };
                var (album, trackNum) = _metadata.FetchAlbumAndTrackNumber(originalPath);
                track.Album = album;
                track.TrackNumber = trackNum;

                if (_library.IsDuplicate(track))
                {
                    // Skip BEFORE copying - otherwise re-imports pile up orphan copies.
                    skipped++;
                }
                else
                {
                    if (copyToLibrary)
                    {
                        var ext = Path.GetExtension(originalPath);
                        var safeName = FileNamer.Generate(artist, title, track.Id.ToString(), ext);
                        var targetPath = Path.Combine(destDir!, safeName);

                        if (File.Exists(targetPath))
                            targetPath = Path.Combine(destDir!, $"{Path.GetFileNameWithoutExtension(safeName)}_{i}{ext}");

                        File.Copy(originalPath, targetPath, overwrite: false);
                        FileOperationJournal.Instance.Record(FileOpType.Copy, originalPath, targetPath, track.Id);
                        track.FilePath = targetPath;
                    }

                    if (_library.Add(track)) added++;
                    else failed++;

                    NullActionLogger.TrackAdded(track.FilePath, sourceLabel, nameof(ImportViewModel));
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[Import] Failed to process {Path}", originalPath);
                failed++;
            }

            if (activity != null)
            {
                ToastService.Instance.UpdateLiveActivity(
                    activity,
                    message: $"Importing {i + 1}/{files.Count}... ({added} added, {skipped} skipped)",
                    progressValue: (i + 1) * 100.0 / files.Count,
                    isIndeterminate: false);
            }

            if (files.Count > 1) await Task.Delay(1); // Yield to UI
        }

        if (activity != null)
        {
            var modeText = copyToLibrary ? "copied to library" : "kept in place";
            ToastService.Instance.CompleteLiveActivity(
                activity, $"Import complete ({modeText}) - {added} added, {skipped} skipped.");
        }

        return new ImportResult(added, skipped, failed);
    }

    private async Task ImportFolderAsync()
    {
        var folderPath = await DialogService.Instance.PickFolderAsync("Select Folder to Import");
        if (string.IsNullOrEmpty(folderPath)) return;

        try
        {
            NullActionLogger.ImportStarted(folderPath, nameof(ImportViewModel));
            var stopwatch = Stopwatch.StartNew();

            var includeSubfolders = await AskIncludeSubfoldersAsync();
            var searchOption = includeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

            var files = Directory.GetFiles(folderPath, "*.*", searchOption)
                .Where(f => SupportedExtensions.Contains(Path.GetExtension(f).ToLower()))
                .ToList();

            if (files.Count == 0)
            {
                ToastService.Instance.Show("No supported audio files found in that folder.", ToastType.Warning);
                NullActionLogger.ImportFailed(folderPath, "No supported audio files found.", nameof(ImportViewModel));
                return;
            }

            // Delegate to the shared pipeline
            var result = await ImportFilesAsync(files, "LocalFolder");

            stopwatch.Stop();
            NullActionLogger.ImportCompleted(folderPath, $"{result.Added} tracks added", stopwatch.ElapsedMilliseconds, nameof(ImportViewModel));
            Log.Information("Folder import complete: {Added} added, {Skipped} skipped from {Path}", result.Added, result.Skipped, folderPath);

            ImportCompleted?.Invoke();
        }
        catch (Exception ex)
        {
            ToastService.Instance.Show($"Folder import failed: {ex.Message}", ToastType.Error);
            NullActionLogger.ImportFailed(folderPath, ex.Message, nameof(ImportViewModel));
            NullActionLogger.Error(nameof(ImportViewModel), ex, $"Failed while executing folder batch processing layout for: {folderPath}");
        }
    }

    /// <summary>
    /// Decides whether to copy files based on the saved ImportMode pref. Only prompts
    /// when the mode is "Ask"; "Copy" and "Keep" short-circuit the dialog.
    /// </summary>
    private async Task<bool> ResolveCopyDecisionAsync()
    {
        var mode = _prefs.Current.ImportMode;
        if (string.Equals(mode, "Copy", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(mode, "Keep", StringComparison.OrdinalIgnoreCase)) return false;

        // "Ask" or unrecognized value: prompt the user.
        var dialog = new Views.ConfirmDialog(
            "Import Mode",
            "Copy these files into your NullWave library folder?\n\n" +
            "Yes = Copy files (Safe, managed by NullWave)\n" +
            "No = Keep in place (Files stay where they are)");
        return await DialogService.Instance.ShowDialogAsync<bool>(dialog);
    }

    private static async Task<bool> AskIncludeSubfoldersAsync()
    {
        var dialog = new Views.ConfirmDialog("Import Subfolders?", "Include all subfolders in the import?");
        return await DialogService.Instance.ShowDialogAsync<bool>(dialog);
    }
}