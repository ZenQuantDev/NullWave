using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NullWave.Helpers;
using NullWave.Helpers.Logging;
using NullWave.Models;
using NullWave.Services;
using NullWave.Services.Plugins;
using NullWave.Services.Security;
using NullWave.Services.SmartSorting;
using NullWave.ViewModels.Settings;
using Serilog;
using Serilog.Events;

namespace NullWave.ViewModels;

public partial class SettingsViewModel
{
    [ObservableProperty] private string _externalAIStatus = string.Empty;
    [ObservableProperty] private string _moodPlaylistStatus = string.Empty;

    [RelayCommand] private void GenerateMoodPlaylist()
    {
        MoodPlaylistStatus = L("Settings_Dynamic_Mood_Generating");
        GenerateMoodPlaylistRequested?.Invoke();
    }
    
    [RelayCommand] private void GenerateTagMoodPlaylist() => GenerateTagMoodPlaylistRequested?.Invoke();
    [RelayCommand] private void RefreshWeather() => RefreshWeatherRequested?.Invoke();
    
    [RelayCommand] private async Task ExportUntaggedTracksAsync()
    {
        ExternalAIStatus = L("Settings_Dynamic_Export_Preparing");
        ExportUntaggedTracksRequested?.Invoke();
        await Task.CompletedTask;
    }
    
    [RelayCommand] private async Task ImportAiTagsAsync()
    {
        var task = ImportAiTagsRequested?.Invoke();
        if (task != null) await task;
    }

    #region Reporters
    public void ReportMoodPlaylistGenerated(int trackCount, string mood) { MoodPlaylistStatus = string.Format(L("Settings_Dynamic_Mood_Success"), trackCount, mood); }
    public void ReportMoodPlaylistFailed(string reason) { MoodPlaylistStatus = string.Format(L("Settings_Dynamic_Mood_Failed"), reason); }

    public async Task ReportExportReadyAsync(IEnumerable<Track> tracks, Window parentWindow)
    {
        var trackList = tracks.ToList();
        if (trackList.Count == 0) { ExternalAIStatus = L("Settings_Dynamic_Export_NoTracks"); return; }
        var format = ExportFormat ?? "txt";
        var timestamp = DateTime.Now.ToString("ddMMyyyy_HHmm");
        var baseFileName = $"nullwave_ai_prompt_{timestamp}.{format}";
        var chunks = _externalAI.GenerateChunked(trackList, format, baseFileName);
        
        var sp = new FilePickerSaveOptions
        {
            Title = chunks.Count > 1 ? $"Save AI Prompt - Part 1 of {chunks.Count}" : "Save AI Tagging Prompt",
            SuggestedFileName = chunks[0].FileName,
            FileTypeChoices = new[] { new FilePickerFileType("Text") { Patterns = new[] { "*.txt" } }, new FilePickerFileType("Markdown") { Patterns = new[] { "*.md" } }, new FilePickerFileType("JSON") { Patterns = new[] { "*.json" } } }
        };
        
        int savedCount = 0;
        foreach (var (content, fileName) in chunks)
        {
            sp.SuggestedFileName = fileName;
            if (savedCount > 0) sp.Title = $"Save AI Prompt - Part {savedCount + 1} of {chunks.Count}";
            var file = await parentWindow.StorageProvider.SaveFilePickerAsync(sp);
            if (file == null) { ExternalAIStatus = savedCount == 0 ? L("Settings_Dynamic_Export_Cancelled") : string.Format(L("Settings_Dynamic_Export_Partial"), savedCount, chunks.Count); return; }
            try { await using var stream = await file.OpenWriteAsync(); await using var writer = new StreamWriter(stream); await writer.WriteAsync(content); savedCount++; }
            catch (Exception ex) { ExternalAIStatus = string.Format(L("Settings_Dynamic_Export_FailedPart"), savedCount + 1, ex.Message); return; }
        }
        ExternalAIStatus = chunks.Count > 1 ? string.Format(L("Settings_Dynamic_Export_SuccessMulti"), trackList.Count, chunks.Count) : string.Format(L("Settings_Dynamic_Export_SuccessSingle"), trackList.Count, chunks[0].FileName);
        ToastService.Instance.Show(ExternalAIStatus, ToastType.Success);
    }

    public void ReportImportComplete(int applied, int total)
    {
        if (applied == 0 && total == 0) { ExternalAIStatus = L("Settings_Dynamic_Import_Cancelled"); return; }
        ExternalAIStatus = total == 0 ? L("Settings_Dynamic_Import_NoMatch") : string.Format(L("Settings_Dynamic_Import_Success"), applied, total);
        ToastService.Instance.Show(ExternalAIStatus, applied > 0 ? ToastType.Success : ToastType.Warning);
    }

    public void ReportImportFailed(string reason)
    {
        ExternalAIStatus = string.Format(L("Settings_Dynamic_Import_Failed"), reason);
        ToastService.Instance.Show(ExternalAIStatus, ToastType.Error);
    }
    #endregion
}