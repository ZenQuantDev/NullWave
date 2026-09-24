using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using NullWave.Helpers;
using NullWave.Helpers.Logging;
using NullWave.Models;
using NullWave.Services;
using NullWave.Services.Integration;
using NullWave.Services.Plugins;
using NullWave.Services.Security;
using NullWave.Services.SmartSorting;
using Serilog;

namespace NullWave.ViewModels;

public partial class MainViewModel
{
    private void InitializeServices()
    {
        _identity = new IdentityService(_keyStore);
        _prefsService = new PreferencesService();
        _isSidebarCollapsed = _prefsService.Current.SidebarCollapsed;

        ThemeService.Instance.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ThemeService.Instance.SidebarWidthPx))
                Dispatcher.UIThread.Post(() => OnPropertyChanged(nameof(SidebarWidth)));
        };

        _secureDelete = new SecureDeleteService(_keyStore);
        _config = new ConfigService(_keyStore);
        _lastFm = new LastFmService(_config);
        _metadata = new MetadataService(_config, _lastFm);
        _spotifyBridge = new SpotifyBridgeService(_config);

        var dbService = new DatabaseService(_prefsService);
        _library = new LibraryService(dbService, _metadata, _prefsService);
        _playlists = new PlaylistService(dbService, _library);
        _library.LibraryChanged += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            Library?.Refresh();
            RadioLibrary?.Refresh();
            AudiobookLibrary?.Refresh();
            OnPropertyChanged(nameof(ActiveTrackCount));
        });

        var albumArtService = new AlbumArtService(_lastFm);
        _downloadService = new DownloadService(_library, _prefsService, albumArtService);
        DownloadManager = new DownloadManagerViewModel();
        DownloadManager.AttachService(_downloadService);
        
        _localAI = new LocalAIService();
        _enrichment = new LastFmEnrichmentService(_lastFm, _library, _localAI, _prefsService);
        _weatherService = new WeatherService(_keyStore);
        _moodPlaylist = new MoodPlaylistService(_weatherService, _localAI, _library);

        _plugins = new PluginManager();
        _plugins.Register(new YtDlpDownloadProvider(_downloadService, _prefsService));
        _plugins.Register(new LastFmMetadataProvider(_lastFm, _prefsService));
        _plugins.Register(new OpenWeatherProvider(_weatherService, _prefsService));
        _plugins.Register(new OllamaAIProvider(_localAI, _prefsService));

        Settings = new SettingsViewModel(_keyStore, _secureDelete, _prefsService, _localAI, _plugins);
        Settings.ClearYtDlpCacheRequested += OnClearYtDlpCacheRequested;
    }

    private void InitializeChildViewModels()
    {
        Input = new TrackInputViewModel(_library, _metadata, _urlParser, _downloadService, _spotifyBridge, Settings, new AlbumArtService(_lastFm));
        
        Library = new LibraryViewModel(_library, _localAI);
        Library.ExcludedMediaTypes.Add(MediaType.Radio);
        Library.ExcludedMediaTypes.Add(MediaType.Audiobook);
        Library.Refresh();
        
        RadioLibrary = new LibraryViewModel(_library, _localAI) { MediaTypeFilter = MediaType.Radio };
        AudiobookLibrary = new LibraryViewModel(_library, _localAI) { MediaTypeFilter = MediaType.Audiobook };
        
        Library.BulkAddToPlaylistRequested += tracks => _ = AddTracksToPlaylistAsync(tracks);
        Library.AddToPlaylistRequested += track => _ = AddTracksToPlaylistAsync(new[] { track }.ToList());
        RadioLibrary.AddToPlaylistRequested += track => _ = AddTracksToPlaylistAsync(new[] { track }.ToList());
        AudiobookLibrary.AddToPlaylistRequested += track => _ = AddTracksToPlaylistAsync(new[] { track }.ToList());

        _aiPlaylistsFolder = _playlists.GetAllFolders().FirstOrDefault(f => f.Name == "AI Playlists") ?? _playlists.CreateFolder("AI Playlists");
        Library.AiPlaylistRequested += OnAiPlaylistRequested;

        Playlist = new PlaylistViewModel(_playlists);
        Playlist.RequestCreatePlaylistName = async () =>
        {
            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop || desktop.MainWindow == null) return null;
            return await new Views.CreatePlaylistDialog().ShowDialog<string?>(desktop.MainWindow);
        };
        Playlist.RequestCreateFolderName = async () =>
        {
            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop || desktop.MainWindow == null) return null;
            return await new Views.CreateFolderDialog().ShowDialog<string?>(desktop.MainWindow);
        };

        Export = new ExportViewModel(_library, _export);
        Detail = new TrackDetailViewModel(_library, _plugins);
        Import = new ImportViewModel(_library, _metadata);
        Settings.ImportExistingLibraryRequested += () => Import.ImportFolderCommand.Execute(null);
        
        Player = new PlayerViewModel(_playbackService, _downloadService, _library, Settings, _metadata);
        Profile = new UserProfileViewModel(_library, _prefsService, _identity);
        Queue = new QueueViewModel(_library);

        Nav = new NavigationViewModel(
            _prefsService, _playlists,
            NavigateLibraryCommand, NavigatePlaylistsCommand, NavigateRadioCommand, NavigateAudiobooksCommand,
            navigateToPlaylist: playlistId =>
            {
                CurrentPage = "Playlists";
                Playlist.SelectById(playlistId);
                Nav.SetPlaylistActive(playlistId);
                LogNav($"PinnedPlaylist:{playlistId}");
            });
    }

    private void WireCoreEvents()
    {
        Profile.TagClickedRequested += tag =>
        {
            GlobalSearchQuery = "tag:" + tag;
            CurrentPage = "Library";
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lt)
                foreach (var w in lt.Windows.OfType<Views.ProfileWindow>()) w.Close();
        };
        Profile.PlayTrackByIdRequested += id =>
        {
            var t = _library.GetAll().FirstOrDefault(t => t.Id == id);
            if (t != null) Player.PlayTrack(t);
        };

        Queue.PlayTrackRequested += Player.PlayTrack;
        RadioLibrary.PlayTrackRequested += Player.PlayTrack;
        AudiobookLibrary.PlayTrackRequested += Player.PlayTrack;
        RadioLibrary.TrackDetailRequested += t => Detail.OpenFor(t);
        AudiobookLibrary.TrackDetailRequested += t => Detail.OpenFor(t);

        Detail.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Detail.IsOpen))
            {
                OnPropertyChanged(nameof(ActiveRightPanelWidth));
                if (Detail.IsOpen) Queue.IsOpen = false;
            }
        };

        Queue.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Queue.IsOpen))
            {
                OnPropertyChanged(nameof(ActiveRightPanelWidth));
                if (Queue.IsOpen) Detail.IsOpen = false;
            }
        };

        Playlist.PinRequested += p => Nav.PinPlaylist(p.Id, p.Name);
        Playlist.UnpinRequested += p => Nav.UnpinPlaylist(p.Id);
        Playlist.PlayAllRequested += playlist => { if (playlist?.Tracks.Count > 0) Player.PlayPlaylist(playlist); };
        Playlist.PlaylistsChanged += () => Nav.Rebuild();
        Playlist.AttachNavigation(Nav);

        Library.NavigateToLibraryRequested += () => CurrentPage = "Library";
        Library.TrackDetailRequested += track => Detail.OpenFor(track);
        Library.PlayTrackRequested += Player.PlayTrack;
        Import.ImportCompleted += () => { Library.Refresh(); Library.RefreshArtistGroups(); };
        Input.TrackMetadataUpdated += Library.Refresh;

        Player.PlaySelectedTrackRequested += () =>
        {
            if (Library.SelectedTrack != null) Player.PlayTrack(Library.SelectedTrack);
            else if (Library.Tracks.Count > 0) Player.PlayTrack(Library.Tracks[0]);
        };
    }

    private void WireIntegrationEvents()
    {
        Input.PlaylistImportRequested += (playlistUrl) =>
        {
            if (_plugins.Get<YtDlpDownloadProvider>() is not { } ytDlpProvider || !ytDlpProvider.SupportsUrl(playlistUrl))
            {
                ToastService.Instance.Show("Downloads are disabled. Enable yt-dlp in Settings.", ToastType.Warning);
                return;
            }
            _ = _downloadService.DownloadPlaylistAsync(playlistUrl, onTrackReady: (downloadedTrack) =>
            {
                if (_plugins.Get<LastFmMetadataProvider>() is { }) _enrichment.EnrichAsync(downloadedTrack);
                Dispatcher.UIThread.Post(() => Library.Refresh());
            });
        };

        Input.RadioSiteRequested += async (stationName, channels) =>
        {
            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop || desktop.MainWindow == null) return;
            var chosen = await new Views.Dialogs.RadioMoodDialog(channels).ShowDialog<List<RadioChannel>>(desktop.MainWindow);
            if (chosen == null || chosen.Count == 0) return;

            int added = 0;
            foreach (var channel in chosen)
            {
                var stream = channel.StreamUrl;
                if (string.IsNullOrWhiteSpace(stream)) stream = await RadioStreamResolver.ResolveAsync($"{stationName} {channel.Name}");
                if (string.IsNullOrWhiteSpace(stream)) { ToastService.Instance.Show(string.Format(LocalizationService.Instance["Radio_Resolved_Failed"], channel.Name), ToastType.Warning); continue; }

                _library.Add(new Track { Title = $"{stationName} - {channel.Name}", Artist = stationName, Url = stream, Source = TrackSource.Unknown, MediaType = MediaType.Radio });
                added++;
            }
            Library.Refresh(); RadioLibrary.Refresh();
            if (added > 0) ToastService.Instance.Show(string.Format(LocalizationService.Instance["Radio_StationAdded"], added), ToastType.Success);
        };

        Input.TrackAdded += () =>
        {
            var track = _library.GetAll().LastOrDefault();
            if (track == null) return;
            if (track.MediaType != MediaType.Radio && _plugins.Get<LastFmMetadataProvider>() is { }) _enrichment.EnrichAsync(track);
            Dispatcher.UIThread.Post(() => Library.Refresh());
        };

        _downloadService.DownloadCompleted += (_, _, isInteractive) => Dispatcher.UIThread.Post(() =>
        {
            Library.Refresh(); Library.RefreshArtistGroups();
            if (isInteractive) ToastService.Instance.Show("Track download completed.", ToastType.Success, scope: "download");
        });

        _downloadService.DownloadFailed += (_, _, isInteractive) => Dispatcher.UIThread.Post(() =>
        {
            Library.Refresh();
            if (isInteractive) ToastService.Instance.Show("Track download failed.", ToastType.Error, scope: "download");
        });

        _downloadService.PlaylistBatchStarted += totalTracks =>
        {
            var activity = ToastService.Instance.StartLiveActivity("Downloading Playlist", totalTracks > 0 ? $"0/{totalTracks}" : "Fetching metadata...", isIndeterminate: totalTracks == 0);
            lock (_playlistBatches) { _playlistBatches.Add((activity, totalTracks)); }
        };

        _downloadService.PlaylistBatchProgress += (completed, total, skipped) =>
        {
            LiveNotification? activity;
            lock (_playlistBatches)
            {
                var match = _playlistBatches.FirstOrDefault(b => b.Total == total && b.Total > 0);
                activity = match != default ? match.Activity : _playlistBatches.LastOrDefault().Activity;
            }
            if (activity == null) return;
            ToastService.Instance.UpdateLiveActivity(activity, $"Downloading: {completed}/{total} (Skipped: {skipped})", total > 0 ? (completed + skipped) * 100.0 / total : 0, false);
        };

        _downloadService.PlaylistBatchCompleted += (completed, failed, skipped) =>
        {
            LiveNotification? activity;
            lock (_playlistBatches)
            {
                activity = _playlistBatches.Count > 0 ? _playlistBatches[0].Activity : null;
                if (_playlistBatches.Count > 0) _playlistBatches.RemoveAt(0);
            }
            ToastService.Instance.CompleteLiveActivity(activity, $"Bulk complete: {completed} downloaded, {failed} failed, {skipped} skipped.");
            Dispatcher.UIThread.Post(() => Library.Refresh());
        };

        Player.TrackScrobbleRequested += async (title, artist, playedAt) =>
        {
            if (!Settings.ScrobbleToLastFm) return;
            if (_plugins.Get<LastFmMetadataProvider>() is not { } lastFmProvider || !lastFmProvider.IsConfiguredForScrobbling) return;
            if (!await lastFmProvider.ScrobbleAsync(title, artist, playedAt))
                ToastService.Instance.Show($"Scrobble failed for '{title}'.", ToastType.Warning, scope: "scrobble");
        };

        Settings.LastFmConnectRequested += async () =>
        {
            try
            {
                var auth = new LastFmAuthService(_config.GetLastFmApiKey(), _config.GetLastFmApiSecret());
                if (!auth.IsConfigured) { Settings.ReportLastFmAuthFailed("Set API key first."); return; }
                var token = await auth.GetRequestTokenAsync();
                if (string.IsNullOrEmpty(token)) { Settings.ReportLastFmAuthFailed("Token request failed."); return; }
                
                Process.Start(new ProcessStartInfo { FileName = auth.GetAuthUrl(token), UseShellExecute = true });
                Settings.ReportLastFmAwaitingAuth();
                _pendingLastFmToken = token; _pendingLastFmAuth = auth;
            }
            catch (Exception ex) { Settings.ReportLastFmAuthFailed(ex.Message); }
        };

        Settings.LastFmConfirmAuthRequested += async () =>
        {
            if (_pendingLastFmAuth == null || string.IsNullOrEmpty(_pendingLastFmToken)) { Settings.ReportLastFmAuthFailed("Click Connect first."); return; }
            var result = await _pendingLastFmAuth.GetSessionKeyAsync(_pendingLastFmToken);
            if (!result.Success) { Settings.ReportLastFmAuthFailed(result.Error ?? "Approve in browser first."); return; }
            
            _keyStore.SaveKey("LastFm:SessionKey", result.SessionKey);
            _keyStore.SaveKey("LastFm:Username", result.Username);
            _pendingLastFmToken = null; _pendingLastFmAuth = null;
            _lastFm = new LastFmService(_config);
            Settings.ReportLastFmConnected(result.Username);
        };

        Settings.LastFmDisconnectRequested += () =>
        {
            _keyStore.DeleteKey("LastFm:SessionKey"); _keyStore.DeleteKey("LastFm:Username");
            _lastFm = new LastFmService(_config);
            Settings.ReportLastFmDisconnected();
        };

        Settings.GenerateMoodPlaylistRequested += () => _ = RunMoodPlaylistAsync(forceRefresh: true);
        Settings.GenerateTagMoodPlaylistRequested += () => _ = RunMoodPlaylistAsync(forceRefresh: true, forceTags: true);

        Settings.ExportUntaggedTracksRequested += async () =>
        {
            var untagged = _library.GetAll().Where(t => t.Tags == null || t.Tags.Count == 0).ToList();
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
                await Settings.ReportExportReadyAsync(untagged, desktop.MainWindow);
        };

        Settings.ImportAiTagsRequested += async () =>
        {
            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop || desktop.MainWindow == null) return null;
            var files = await desktop.MainWindow.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = "Import AI Tag JSON", AllowMultiple = false,
                FileTypeFilter = new[] { new Avalonia.Platform.Storage.FilePickerFileType("JSON") { Patterns = new[] { "*.json", "*.txt", "*.md" } } }
            });
            if (files.Count == 0) { Settings.ReportImportComplete(0, 0); return null; }

            string jsonContent;
            await using (var stream = await files[0].OpenReadAsync())
            using (var reader = new System.IO.StreamReader(stream)) jsonContent = await reader.ReadToEndAsync();

            var results = new ExternalAITagService().ParseImportedJson(jsonContent);
            if (results.Count == 0) { Settings.ReportImportFailed("No valid entries."); return null; }

            int applied = 0;
            foreach (var result in results)
            {
                var track = _library.GetAll().FirstOrDefault(t => t.Id == result.Id);
                if (track == null || result.Tags.Count == 0) continue;
                track.Tags ??= new List<string>();
                foreach (var tag in result.Tags) if (!track.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase)) track.Tags.Add(tag);
                _library.Update(track); applied++;
            }
            Library.Refresh();
            Settings.ReportImportComplete(applied, results.Count);
            return null;
        };
    }

    private void WireAIAndPowerEvents()
    {
        _localAI.FallbackNotice += message => Dispatcher.UIThread.Post(() => ToastService.Instance.Show(message, ToastType.Warning));
        
        Settings.AIFeaturesEnabledChanged += enabled =>
        {
            if (!enabled) { Settings.StopHealthCheck(); ToastService.Instance.Show("Local AI offline.", ToastType.Info, scope: "ai"); }
            else
            {
                var activity = ToastService.Instance.StartLiveActivity("Local AI", "Pinging...", isIndeterminate: true, scope: "ai");
                _ = Task.Run(async () =>
                {
                    bool running = await _localAI.PingAsync();
                    Dispatcher.UIThread.Post(() =>
                    {
                        ToastService.Instance.Show(running ? "AI connected!" : "AI unreachable.", running ? ToastType.Success : ToastType.Warning, scope: "ai");
                        Settings.SetAIServiceState(running ? AIServiceState.Running : AIServiceState.Stopped);
                        Settings.StartAIHealthCheck();
                    });
                });
            }
        };

        _powerState = new PowerStateService();
        _powerState.PowerStateChanged += state =>
        {
            _localAI.OnPowerStateChanged(state);
            Dispatcher.UIThread.Post(() => Settings.PowerStateLabel = state == PowerState.AC ? "Plugged In" : "On Battery");
        };
        Settings.PowerModelsChanged += (b, p, a) => _localAI.ConfigurePowerModels(b, p, a);
        _localAI.ConfigurePowerModels(Settings.BatteryModel, Settings.PerformanceModel, Settings.AutoPowerModelSwitch);
        _powerState.StartPolling();

        Settings.MaxConcurrentDownloadsChanged += limit => _downloadService.UpdateConcurrencyLimit(limit);
        _downloadService.UpdateConcurrencyLimit(Settings.MaxConcurrentDownloads);
        Player.UpdateSkipPenaltyCap(Settings.SkipPenaltyCap);

        _enrichment.BackfillCompleted += () =>
        {
            if (_initialMoodPlaylistRun) return;
            _initialMoodPlaylistRun = true;
            if (_prefsService.Current.AutoGenerateMoodPlaylist) _ = RunMoodPlaylistAsync(forceRefresh: false);
        };

        _ = Task.Run(async () =>
        {
            await Task.Delay(3000);
            if (!_prefsService.Current.AutoGenerateMoodPlaylist) return;
            if (PowerStateService.ReadPowerState() != PowerState.Battery) _enrichment.BackfillAsync();
            else { _initialMoodPlaylistRun = true; _ = RunMoodPlaylistAsync(forceRefresh: false); }
        });
    }

    private void OnClearYtDlpCacheRequested()
    {
        try
        {
            var psi = new ProcessStartInfo("yt-dlp", "--rm-cache-dir") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using var process = Process.Start(psi);
            if (process != null)
            {
                process.WaitForExit();
                if (process.ExitCode == 0) ToastService.Instance.Show("yt-dlp cache cleared!", ToastType.Success, 5000, scope: "maintenance");
                else ToastService.Instance.Show($"Cache clear failed: {process.StandardError.ReadToEnd()}", ToastType.Error, 5000, scope: "maintenance");
            }
        }
        catch (Exception ex) { ToastService.Instance.Show($"Cache clear failed: {ex.Message}", ToastType.Error, 5000, scope: "maintenance"); }
    }

    private async Task RunMoodPlaylistAsync(bool forceRefresh, bool forceTags = false)
    {
        var activity = ToastService.Instance.StartLiveActivity("Mood Playlist", "Fetching weather...", isIndeterminate: true, scope: "mood-gen");
        try
        {
            if (_plugins.Get<IWeatherProvider>() is not { }) { ToastService.Instance.CompleteLiveActivity(activity, "Weather unavailable.", finalType: ToastType.Warning); return; }
            var lat = Settings.Latitude; var lon = Settings.Longitude;
            if (lat == 0 && lon == 0) { ToastService.Instance.CompleteLiveActivity(activity, "No location set.", finalType: ToastType.Warning); return; }

            _localAI.CurrentModel = Settings.SelectedModel;
            bool useAi = !forceTags && Settings.AIFeaturesEnabled && Settings.UseLocalAI;
            var result = await _moodPlaylist.GenerateAsync(lat, lon, useAi, forceRefresh);

            if (!result.Success) { ToastService.Instance.CompleteLiveActivity(activity, $"Failed: {result.FailureReason}", finalType: ToastType.Error); return; }

            var oldMoodPlaylists = _playlists.GetAll().Where(p => p.Name.StartsWith("Mood:") || p.Name == "Mood Mix").ToList();
            bool wasPinned = oldMoodPlaylists.Any(p => Nav.IsPlaylistPinned(p.Id));
            foreach (var old in oldMoodPlaylists) _playlists.Remove(old.Id);

            var playlist = _playlists.Create("Mood Mix", $"{result.Mood} · {result.WeatherCondition}, {result.TemperatureC:F0}°C · auto-generated");
            foreach (var track in result.Tracks) _playlists.AddTrack(playlist.Id, track);
            if (wasPinned) Nav.PinPlaylist(playlist.Id, playlist.Name);

            Playlist.Refresh(); Nav.RefreshPlaylistLists();
            Settings.ReportMoodPlaylistGenerated(result.Tracks.Count, result.Mood);
            ToastService.Instance.CompleteLiveActivity(activity, $"Generated 'Mood Mix' ({result.Tracks.Count} tracks).");
        }
        catch (Exception) { ToastService.Instance.CompleteLiveActivity(activity, "Mood generation failed.", finalType: ToastType.Error); }
    }

    private void OnAiPlaylistRequested(string query, List<Track> tracks)
    {
        if (tracks.Count == 0) return;
        var playlistName = _playlists.NameExists($"AI: {query}") ? $"AI: {query} ({DateTime.Now:HH:mm})" : $"AI: {query}";
        var playlist = _playlists.Create(playlistName, $"Generated by AI prompt: {query}", _aiPlaylistsFolder?.Id);
        foreach (var track in tracks) _playlists.AddTrack(playlist.Id, track);
        
        Playlist.Refresh(); Playlist.SelectById(playlist.Id); Nav.RefreshPlaylistLists(); CurrentPage = "Playlists";
        Dispatcher.UIThread.Post(() => GlobalSearchQuery = string.Empty);
    }
}