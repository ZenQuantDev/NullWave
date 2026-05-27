using System.Windows.Input;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using NullWave.Helpers;
using NullWave.Services;
using NullWave.ViewModels.Base;

namespace NullWave.ViewModels;

public class ExportViewModel : ViewModelBase
{
    private readonly LibraryService _library;
    private readonly ExportService _export;

    public ICommand ExportJsonCommand { get; }
    public ICommand ExportCsvCommand { get; }

    public ExportViewModel(LibraryService library, ExportService export)
    {
        _library = library;
        _export = export;
        ExportJsonCommand = new RelayCommand(async () => await ExportAsync("json"));
        ExportCsvCommand = new RelayCommand(async () => await ExportAsync("csv"));
    }

    private async System.Threading.Tasks.Task ExportAsync(string format)
    {
        var window = Application.Current?.ApplicationLifetime is
            IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow : null;
        if (window == null) return;

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

        if (file == null) return;
        var path = file.Path.LocalPath;
        if (string.IsNullOrWhiteSpace(path)) return;

        if (format == "json")
            _export.ExportToJson(_library.GetAll(), path);
        else
            _export.ExportToCsv(_library.GetAll(), path);
    }
}