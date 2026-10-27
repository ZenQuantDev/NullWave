using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NullWave.Models;
using NullWave.Services;

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

    public Playlist? ImportedPlaylist { get; private set; }

    public M3uImportDialogViewModel(M3uImporter importer, string m3uPath)
    {
        _importer = importer;
        _m3uPath = m3uPath;
        _preview = importer.Preview(m3uPath);
        
        Matches = _preview.Matches;
        PlaylistName = _preview.PlaylistName ?? System.IO.Path.GetFileNameWithoutExtension(m3uPath);
        SummaryText = $"Found {_preview.TotalEntries} entries: {_preview.MatchedCount} matched, {_preview.MissingCount} missing";
        StatusText = _preview.MissingCount > 0 
            ? "Missing tracks will be skipped during import." 
            : "All tracks found in library.";
    }

    [RelayCommand]
    private void Cancel()
    {
        ImportedPlaylist = null;
        CloseRequested?.Invoke();
    }

    [RelayCommand]
    private void Import()
    {
        // The actual import happens in the calling code (PlaylistViewModel)
        // which has access to PlaylistService
        CloseRequested?.Invoke();
    }

    public event Action? CloseRequested;
}