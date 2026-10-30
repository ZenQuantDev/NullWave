using System;
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

    public ExportViewModel(LibraryService library, ExportService export)
    {
        ExportM3uCommand = new SafeRelayCommand(async () => await ExportAsync("m3u8"), errorScope: "export");

        _library = library;
        _export = export;
        
        // D7 FIX: Use SafeRelayCommand for async operations (catches exceptions)
        ExportJsonCommand = new SafeRelayCommand(
            async () => await ExportAsync("json"),
            errorScope: "export"
        );
        ExportCsvCommand = new SafeRelayCommand(
            async () => await ExportAsync("csv"),
            errorScope: "export"
        );
    }

    private async System.Threading.Tasks.Task ExportAsync(string format)
    {
        var window = Application.Current?.ApplicationLifetime is
            IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow : null;
            
        if (window == null)
        {
            ErrorReporter.Report("Could not access application window.", "export");
            return;
        }

        var file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Library",
            DefaultExtension = format,
            FileTypeChoices = new[]
            {
                new FilePickerFileType(format.ToUpper())
                {
                    Patterns = new[] { $"*.{format}" }
                }
            }
        });

        if (file == null) return; // User cancelled
        
        var path = file.Path.LocalPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            ErrorReporter.Report("Invalid file path selected.", "export");
            return;
        }

        try
        {
            var tracks = _library.GetAll();
            if (format == "json")
            {
                _export.ExportToJson(tracks, path);
            }
            else if (format == "csv")
            {
                _export.ExportToCsv(tracks, path);
            }
            else if (format == "m3u8" || format == "m3u")
            {
                // M3U Export Logic
                var result = M3uWriter.Build(tracks, path, new M3uWriteOptions(RelativePaths: true));
                M3uWriter.WriteFile(path, result.Text);
                
                Log.Information("[Export] Exported {Count} tracks to M3U (skipped {Skipped} missing)", result.Written, result.SkippedMissing);
                ToastService.Instance.Show(
                    $"Exported {result.Written} tracks to M3U ({result.SkippedMissing} skipped missing files)",
                    ToastType.Success,
                    durationMs: 4000,
                    scope: "export"
                );
                return; // Return early to avoid the generic toast below
            }

            Log.Information("[Export] Successfully exported {Count} tracks to {Path}", tracks.Count, path);
            ToastService.Instance.Show(
                $"Exported {tracks.Count} tracks to {System.IO.Path.GetFileName(path)}",
                ToastType.Success,
                durationMs: 4000,
                scope: "export"
            );
        }
        catch (Exception ex)
        {
            // D18 FIX: Use ErrorReporter instead of raw ex.Message
            ErrorReporter.Report(ex, $"Export failed. Please try again.", "export");
        }
    }
}