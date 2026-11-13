using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NullWave.Models;
using NullWave.Services;
using System.Globalization;

namespace NullWave.ViewModels.Dialogs;

public partial class M3uImportDialogViewModel : ObservableObject
{
    private readonly M3uImporter _importer;
    private readonly string _m3uPath;
    private readonly M3uImportPreview _preview;

    [ObservableProperty] private string _playlistName = string.Empty;
    [ObservableProperty] private string _summaryText = string.Empty;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private IReadOnlyList<M3uImportMatch> _matches = new List<M3uImportMatch>();

    public bool CanImport => Matches.Count > 0 && !string.IsNullOrWhiteSpace(PlaylistName);
    public M3uImportResult? DialogResult { get; private set; }

    public M3uImportDialogViewModel(M3uImporter importer, string m3uPath)
    {
        _importer = importer;
        _m3uPath = m3uPath;
        _preview = importer.Preview(m3uPath);
        
        Matches = _preview.Matches;
        PlaylistName = _preview.PlaylistName ?? System.IO.Path.GetFileNameWithoutExtension(m3uPath);
        SummaryText = string.Format(CultureInfo.CurrentCulture,
            LocalizationService.Instance["M3uImport_Summary"],
            _preview.TotalEntries, _preview.MatchedCount, _preview.MissingCount);
        StatusText = _preview.MissingCount > 0 
            ? LocalizationService.Instance["M3uImport_MissingStatus"]
            : LocalizationService.Instance["M3uImport_AllFoundStatus"];
    }

    [RelayCommand]
    private void Cancel()
    {
        DialogResult = null;
        CloseRequested?.Invoke();
    }

    [RelayCommand]
    private void Import()
    {
        // Pass the preview and the user-chosen name back to the caller
        DialogResult = new M3uImportResult(PlaylistName, _preview);
        CloseRequested?.Invoke();
    }

    public event Action? CloseRequested;
    public sealed record M3uImportResult(string PlaylistName, M3uImportPreview Preview);
}