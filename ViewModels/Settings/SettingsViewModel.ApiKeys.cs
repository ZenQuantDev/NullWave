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
    #region API Keys Properties
    private string _youtubeApiKey = string.Empty;
    public string YouTubeApiKey
    {
        get => _youtubeApiKey;
        set { if (SetProperty(ref _youtubeApiKey, value)) { OnPropertyChanged(nameof(IsYouTubeKeyValid)); } }
    }

    private string _spotifyClientId = string.Empty;
    public string SpotifyClientId { get => _spotifyClientId; set => SetProperty(ref _spotifyClientId, value); }

    private string _spotifyClientSecret = string.Empty;
    public string SpotifyClientSecret { get => _spotifyClientSecret; set => SetProperty(ref _spotifyClientSecret, value); }

    private string _soundCloudClientId = string.Empty;
    public string SoundCloudClientId { get => _soundCloudClientId; set => SetProperty(ref _soundCloudClientId, value); }

    private string _lastFmApiKey = string.Empty;
    public string LastFmApiKey
    {
        get => _lastFmApiKey;
        set { if (SetProperty(ref _lastFmApiKey, value)) { OnPropertyChanged(nameof(IsLastFmKeyValid)); } }
    }

    private string _lastFmApiSecret = string.Empty;
    public string LastFmApiSecret { get => _lastFmApiSecret; set => SetProperty(ref _lastFmApiSecret, value); }

    private string _openWeatherApiKey = string.Empty;
    public string OpenWeatherApiKey
    {
        get => _openWeatherApiKey;
        set { if (SetProperty(ref _openWeatherApiKey, value)) { OnPropertyChanged(nameof(IsOpenWeatherKeyValid)); } }
    }

    public bool IsYouTubeKeyValid => !string.IsNullOrWhiteSpace(YouTubeApiKey) && YouTubeApiKey.StartsWith("AIza", System.StringComparison.OrdinalIgnoreCase) && YouTubeApiKey.Length >= 30;
    public bool IsLastFmKeyValid => !string.IsNullOrWhiteSpace(LastFmApiKey) && LastFmApiKey.Length == 32;
    public bool IsOpenWeatherKeyValid => !string.IsNullOrWhiteSpace(OpenWeatherApiKey) && OpenWeatherApiKey.Length == 32;
    #endregion

    #region LastFm State
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LastFmStateLabel))]
    [NotifyPropertyChangedFor(nameof(IsLastFmConnected))]
    [NotifyPropertyChangedFor(nameof(IsLastFmAwaitingAuth))]
    private LastFmConnectionState _lastFmState = LastFmConnectionState.Disconnected;

    [ObservableProperty] private string _lastFmUsername = string.Empty;
    [ObservableProperty] private string _lastFmStatusMessage = string.Empty;

    public bool IsLastFmConnected => LastFmState == LastFmConnectionState.Connected;
    public bool IsLastFmAwaitingAuth => LastFmState == LastFmConnectionState.AwaitingAuth;
    
    public string LastFmStateLabel => LastFmState switch
    {
        LastFmConnectionState.Connected => string.Format(L("Settings_Dynamic_LastFm_Connected"), LastFmUsername),
        LastFmConnectionState.AwaitingAuth => L("Settings_Dynamic_LastFm_Awaiting"),
        LastFmConnectionState.Error => L("Settings_Dynamic_LastFm_Error"),
        _ => L("Settings_Dynamic_LastFm_Disconnected")
    };
    #endregion

    #region Commands
    [RelayCommand] private void SaveKeys()
    {
        if (!string.IsNullOrWhiteSpace(YouTubeApiKey)) _keyStore.SaveKey("YouTube", YouTubeApiKey);
        if (!string.IsNullOrWhiteSpace(SpotifyClientId)) _keyStore.SaveKey("Spotify:ClientId", SpotifyClientId);
        if (!string.IsNullOrWhiteSpace(SpotifyClientSecret)) _keyStore.SaveKey("Spotify:ClientSecret", SpotifyClientSecret);
        if (!string.IsNullOrWhiteSpace(SoundCloudClientId)) _keyStore.SaveKey("SoundCloud", SoundCloudClientId);
        if (!string.IsNullOrWhiteSpace(LastFmApiKey)) _keyStore.SaveKey("LastFm", LastFmApiKey);
        if (!string.IsNullOrWhiteSpace(LastFmApiSecret)) _keyStore.SaveKey("LastFm:Secret", LastFmApiSecret);
        if (!string.IsNullOrWhiteSpace(OpenWeatherApiKey)) _keyStore.SaveKey("OpenWeather", OpenWeatherApiKey);
    }

    [RelayCommand] private void DeleteApiKeys()
    {
        _secureDelete.DeleteApiKeys();
        YouTubeApiKey = SpotifyClientId = SpotifyClientSecret = SoundCloudClientId = LastFmApiKey = LastFmApiSecret = OpenWeatherApiKey = string.Empty;
    }

    [RelayCommand] private void DeleteLogs() => _secureDelete.DeleteLogs();
    
    [RelayCommand] private void DeleteEverything()
    {
        _secureDelete.DeleteEverything();
        YouTubeApiKey = SpotifyClientId = SpotifyClientSecret = SoundCloudClientId = LastFmApiKey = LastFmApiSecret = OpenWeatherApiKey = string.Empty;
    }

    [RelayCommand] private void ResetPreferences()
    {
        var prefsPath = System.IO.Path.Combine(NullWave.Helpers.NullWavePaths.DataDir, "prefs.json");
        if (System.IO.File.Exists(prefsPath)) { try { System.IO.File.Delete(prefsPath); } catch { } }
        ToastService.Instance.Show("Preferences reset to defaults. Please restart NullWave to apply.", ToastType.Success);
    }

    [RelayCommand] private void LastFmConnect()
    {
        LastFmState = LastFmConnectionState.AwaitingAuth;
        LastFmStatusMessage = L("Settings_Dynamic_LastFm_AuthMsg");
        LastFmConnectRequested?.Invoke();
    }
    
    [RelayCommand] private void LastFmConfirmAuth() => LastFmConfirmAuthRequested?.Invoke();
    [RelayCommand] private void LastFmDisconnect() => LastFmDisconnectRequested?.Invoke();
    #endregion

    #region Reporters
    public void ReportLastFmAwaitingAuth() { LastFmState = LastFmConnectionState.AwaitingAuth; LastFmStatusMessage = L("Settings_Dynamic_LastFm_AuthMsg"); }
    public void ReportLastFmConnected(string username)
    {
        LastFmUsername = username;
        LastFmState = LastFmConnectionState.Connected;
        LastFmStatusMessage = string.Empty;
        ToastService.Instance.Show($"Connected to Last.fm as {username}", ToastType.Success);
    }
    public void ReportLastFmDisconnected()
    {
        LastFmUsername = string.Empty;
        LastFmState = LastFmConnectionState.Disconnected;
        LastFmStatusMessage = string.Empty;
        ToastService.Instance.Show("Disconnected from Last.fm", ToastType.Info);
    }
    public void ReportLastFmAuthFailed(string reason)
    {
        LastFmState = LastFmConnectionState.Error;
        LastFmStatusMessage = reason;
        ToastService.Instance.Show($"Last.fm: {reason}", ToastType.Error);
    }
    #endregion
}