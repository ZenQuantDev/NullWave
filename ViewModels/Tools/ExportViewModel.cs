using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using NullWave.Helpers;
using NullWave.Services;
using NullWave.Models;
using NullWave.ViewModels.Base;
using Serilog;

namespace NullWave.ViewModels;

public class ExportViewModel : ViewModelBase
{
    private readonly LibraryService _library;
    private readonly ExportService _export;

    public ICommand ExportJsonCommand { get; }
    public ICommand ExportCsvCommand { get; }
    public ICommand ExportM3uCommand { get; }
    public ICommand ExportFolderCommand { get; }

    public ExportViewModel(LibraryService library, ExportService export)
    {
        _library = library;
        _export = export;

        ExportJsonCommand = new SafeRelayCommand(async () => await ExportAsync("json"), errorScope: "export");
        ExportCsvCommand = new SafeRelayCommand(async () => await ExportAsync("csv"), errorScope: "export");
        ExportM3uCommand = new SafeRelayCommand(async () => await ExportAsync("m3u8"), errorScope: "export");
        ExportFolderCommand = new SafeRelayCommand(async () => await ExportToFolderAsync(), errorScope: "export-folder");
    }

    private async Task ExportAsync(string format)
    {
        var window = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop ? desktop.MainWindow : null;
        if (window == null) { ErrorReporter.Report("Could not access application window.", "export"); return; }

        var file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Library",
            DefaultExtension = format,
            FileTypeChoices = new[] { new FilePickerFileType(format.ToUpper()) { Patterns = new[] { $"*.{format}" } } }
        });

        if (file == null) return;

        var path = file.Path.LocalPath;
        if (string.IsNullOrWhiteSpace(path)) { ErrorReporter.Report("Invalid file path selected.", "export"); return; }

        try
        {
            var tracks = _library.GetAll();
            if (format == "json") _export.ExportToJson(tracks, path);
            else if (format == "csv") _export.ExportToCsv(tracks, path);
            else if (format == "m3u8" || format == "m3u")
            {
                var result = M3uWriter.Build(tracks, path, new M3uWriteOptions(RelativePaths: true));
                M3uWriter.WriteFile(path, result.Text);
                ToastService.Instance.Show($"Exported {result.Written} tracks to M3U ({result.SkippedMissing} skipped missing files)", ToastType.Success, durationMs: 4000, scope: "export");
                return;
            }

            ToastService.Instance.Show($"Exported {tracks.Count} tracks to {Path.GetFileName(path)}", ToastType.Success, durationMs: 4000, scope: "export");
        }
        catch (Exception ex) { ErrorReporter.Report(ex, "Export failed. Please try again.", "export"); }
    }

    private async Task ExportToFolderAsync()
    {
        var window = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop ? desktop.MainWindow : null;
        if (window == null) return;

        var folders = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Destination Folder (e.g., USB Drive)",
            AllowMultiple = false
        });

        if (folders.Count == 0) return;
        var destDir = folders[0].Path.LocalPath;

        var tracks = _library.GetAll().Where(t => !string.IsNullOrEmpty(t.FilePath) && File.Exists(t.FilePath)).ToList();

        if (tracks.Count == 0)
        {
            ToastService.Instance.Show("No tracks with local files found to export.", ToastType.Warning, scope: "export-folder");
            return;
        }

        var activity = ToastService.Instance.StartLiveActivity("Exporting to Folder", $"Copying 0/{tracks.Count}...", isIndeterminate: false);

        try
        {
            var cts = new CancellationTokenSource();
            // Runs on a pool thread inside ExportService.
            var result = await _export.ExportToFolderAsync(tracks, destDir, cts.Token);

            ToastService.Instance.CompleteLiveActivity(
                activity,
                $"Export complete: {result.Copied} copied, {result.Skipped} skipped, {result.Failed} failed.",
                lingerMs: 5000,
                finalType: result.Failed > 0 ? ToastType.Warning : ToastType.Success);
        }
        catch (Exception ex)
        {
            ToastService.Instance.Dismiss(activity);
            ErrorReporter.Report(ex, "Folder export failed.", "export-folder");
        }
    }
}