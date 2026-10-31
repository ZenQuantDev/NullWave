using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Diagnostics;
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
        _prefsService = PreferencesService.Shared;
        _effectsTier = new EffectsTierResolver(_prefsService);
        _effectsTier.PropertyChanged += OnEffectsTierPropertyChanged;
        
        WallpaperService.Instance.MinimalTier = CurrentEffectsTier == EffectsTier.Minimal;
        _isSidebarCollapsed = _prefsService.Current.SidebarCollapsed;

        ThemeService.Instance.PropertyChanged += OnThemeServicePropertyChanged;

        _secureDelete = new SecureDeleteService(_keyStore);
        _config = new ConfigService(_keyStore);
        _lastFm = new LastFmService(_config);
        _metadata = new MetadataService(_config, _lastFm);
        _spotifyBridge = new SpotifyBridgeService(_config);

        var dbService = new DatabaseService(_prefsService);
        _library = new LibraryService(dbService, _metadata, _prefsService);
        _playlists = new PlaylistService(dbService, _library);
        _library.LibraryChanged += OnLibraryChanged;

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

        // Provider registry (M3 foundation) - ASSIGN TO FIELD, not local variable
        _providerRegistry = new ProviderRegistry(_prefsService);

        Settings = new SettingsViewModel(_keyStore, _secureDelete, _prefsService, _localAI, _plugins, _providerRegistry);
        Settings.ClearYtDlpCacheRequested += OnClearYtDlpCacheRequested;
        Settings.DevEffectsTierOverrideChanged += tier => _effectsTier.SetDevOverride(tier);
        Settings.EffectsTierPreferencesChanged += () =>
        {
            ApplyBatteryEffectsTier(_powerState.Current);
            _effectsTier.NotifyPreferencesChanged();
        };
    }

    private readonly HashSet<string> _activePlaylistImports = new();

    // --- Extracted Singleton Event Handlers ---
    private void OnLibraryChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var timer = _libRefreshTimer;
            if (timer == null)
            {
                timer = _libRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    Library?.Refresh();
                    RadioLibrary?.Refresh();
                    AudiobookLibrary?.Refresh();
                    OnPropertyChanged(nameof(ActiveTrackCount));
                };
            }
            timer.Stop();
            timer.Start();
        });
    }

    private void OnThemeServicePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ThemeService.Instance.SidebarWidthPx))
            Dispatcher.UIThread.Post(() => OnPropertyChanged(nameof(SidebarWidth)));
    }

    private void OnEffectsTierPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(EffectsTierResolver.Current)) return;
        Dispatcher.UIThread.Post(() =>
        {
            OnPropertyChanged(nameof(CurrentEffectsTier));
            OnPropertyChanged(nameof(CanAnimateSeekWave));
            WallpaperService.Instance.MinimalTier = CurrentEffectsTier == EffectsTier.Minimal;
        });
    }

    private void InitializeChildViewModels()
    {
        Input = new TrackInputViewModel(_library, _metadata, _urlParser, _downloadService, _spotifyBridge, Settings, new AlbumArtService(_lastFm));
        
        Library = new LibraryViewModel(_library, _localAI, _prefsService, _identity);
        Library.IsAiPromptEnabled = true;
        Library.ExcludedMediaTypes.Add(MediaType.Radio);
        Library.ExcludedMediaTypes.Add(MediaType.Audiobook);
        Library.Refresh();
        
        RadioLibrary = new LibraryViewModel(_library, _localAI, _prefsService, _identity) { MediaTypeFilter = MediaType.Radio };
        AudiobookLibrary = new LibraryViewModel(_library, _localAI, _prefsService, _identity) { MediaTypeFilter = MediaType.Audiobook };
        
        Library.BulkAddToPlaylistRequested += tracks => _ = AddTracksToPlaylistAsync(tracks);
        Library.AddToPlaylistRequested += track => _ = AddTracksToPlaylistAsync(new[] { track }.ToList());
        RadioLibrary.AddToPlaylistRequested += track => _ = AddTracksToPlaylistAsync(new[] { track }.ToList());
        AudiobookLibrary.AddToPlaylistRequested += track => _ = AddTracksToPlaylistAsync(new[] { track }.ToList());

        _aiPlaylistsFolder = _playlists.GetAllFolders().FirstOrDefault(f => f.Name == "AI Playlists") ?? _playlists.CreateFolder("AI Playlists");
        Library.AiPlaylistRequested += OnAiPlaylistRequested;

        // FIX: Moved SmartPlaylistService creation up here so it can be passed to PlaylistViewModel
        var smartPlaylistService = new SmartPlaylistService(_library, TimeProvider.System);

        Playlist = new PlaylistViewModel(_playlists, smartPlaylistService);
        Playlist.RequestCreatePlaylistName = async () =>
        {
            var dialog = new Views.CreatePlaylistDialog();
            return await DialogService.Instance.ShowDialogAsync<string?>(dialog);
        };

        Playlist.RequestCreateFolderName = async () =>
        {
            var dialog = new Views.CreateFolderDialog();
            return await DialogService.Instance.ShowDialogAsync<string?>(dialog);
        };
        Export = new ExportViewModel(_library, _export);
        Detail = new TrackDetailViewModel(_library, _plugins, _identity, _prefsService);
        Import = new ImportViewModel(_library, _metadata);
        Settings.ImportExistingLibraryRequested += () => Import.ImportFolderCommand.Execute(null);
        
        Player = new PlayerViewModel(_playbackService, _downloadService, _library, Settings, _metadata);

        Profile = new UserProfileViewModel(_library, _prefsService, _identity);
        Queue = new QueueViewModel(_library);

        Nav = new NavigationViewModel(
            _prefsService, _playlists, smartPlaylistService,
            () => NavigateLibraryCommand, 
            () => NavigatePlaylistsCommand, 
            () => NavigateRadioCommand, 
            () => NavigateAudiobooksCommand,
            navigateToPlaylist: playlistId =>
            {
                CurrentPage = "Playlists";
                Playlist.SelectById(playlistId);
                Nav.SetPlaylistActive(playlistId);
                LogNav($"PinnedPlaylist:{playlistId}");
            },
            navigateToSmartPlaylist: smartId =>
            {
                CurrentPage = "Playlists";
                Playlist.SelectSmartPlaylist(smartId);
                LogNav($"SmartPlaylist:{smartId}");
            });

        var oldAiPlaylists = _playlists.GetAll().Where(p => p.Name.StartsWith("AI: ", StringComparison.OrdinalIgnoreCase)).ToList();
        if (oldAiPlaylists.Count > 0)
        {
            foreach (var pl in oldAiPlaylists)
            {
                var q = pl.Name.Substring(4).Trim();
                if (string.IsNullOrWhiteSpace(q)) continue;
                var humanName = GenerateHumanPlaylistName(q);
                if (!_playlists.NameExists(humanName) || pl.Name == humanName)
                {
                    _playlists.Rename(pl.Id, humanName);
                }
                else
                {
                    _playlists.Rename(pl.Id, $"{humanName} (Migrated)");
                }
            }
            Playlist.Refresh();
            Nav.Rebuild();
        }

        // Register providers with the new registry - USE THE FIELD
        foreach (var plugin in _plugins.Plugins.OfType<IProvider>())
            _providerRegistry.Register(plugin);
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
            if (t != null && Player != null) Player.PlayTrack(t);
        };

        RadioLibrary.TrackDetailRequested += t => Detail.OpenFor(t);
        AudiobookLibrary.TrackDetailRequested += t => Detail.OpenFor(t);
        Playlist.TrackDetailRequested += track => Detail.OpenFor(track);

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
        Playlist.PlayAllRequested += playlist => { if (playlist?.Tracks.Count > 0 && Player != null) Player.PlayPlaylist(playlist); };
        Playlist.PlaylistsChanged += () => Nav.Rebuild();
        Playlist.AttachNavigation(Nav);
        
        Playlist.BulkQueueRequested += tracks => 
        { 
            if (tracks == null) return;
            int count = 0;
            foreach (var t in tracks) 
            {
                _library.AddToQueue(t.Id); 
                count++;
            }
            if (count > 0) ToastService.Instance.Show($"Added {count} track(s) to queue", ToastType.Info, durationMs: 2500, scope: "queue-add");
        };

        Library.NavigateToLibraryRequested += () => CurrentPage = "Library";
        Library.TrackDetailRequested += track => Detail.OpenFor(track);
        Import.ImportCompleted += () => { Library.Refresh(); Library.RefreshArtistGroups(); };
        Input.TrackMetadataUpdated += Library.Refresh;

        Settings.SeedLibraryRequested += count =>
        {
            for (int i = 0; i < count; i++)
            {
                _library.Add(new Track
                {
                    Id = Guid.NewGuid(),
                    Title = $"Dev Seed {i + 1:D3}",
                    Artist = "DevSeeder",
                    Source = TrackSource.Local,
                    FilePath = null,
                    DateAdded = DateTime.UtcNow,
                    Tags = new List<string> { "dev-seed" },
                    MediaType = MediaType.Music
                });
            }
            ToastService.Instance.Show($"Seeded {count} synthetic tracks (tag: dev-seed).", ToastType.Success);
        };

        Settings.RemoveSeededRequested += () =>
        {
            var ids = _library.GetAll().Where(t => t.Tags != null && t.Tags.Contains("dev-seed")).Select(t => t.Id).ToList();
            foreach (var id in ids) _library.Remove(id);
            ToastService.Instance.Show($"Removed {ids.Count} seeded tracks.", ToastType.Success);
        };
    }

    private void WireIntegrationEvents()
    {
        Input.PlaylistImportRequested += async (playlistUrl) =>
        {
            // Normalize URL for comparison (strip tracking params)
            var normalizedUrl = NormalizePlaylistUrl(playlistUrl);
            
            // Guard: prevent double imports
            lock (_activePlaylistImports)
            {
                if (_activePlaylistImports.Contains(normalizedUrl))
                {
                    Log.Information("[MainViewModel] Playlist import already in progress: {Url}", normalizedUrl);
                    ToastService.Instance.Show("This playlist is already being imported.", ToastType.Info, scope: "import");
                    return;
                }
                _activePlaylistImports.Add(normalizedUrl);
            }

            // NEW: Detect YouTube auto-generated mixes (RD...) and cap them
            if (playlistUrl.Contains("list=RD", StringComparison.OrdinalIgnoreCase))
            {
                var confirmDialog = new Views.ConfirmDialog(
                    "Large Auto-Generated Mix",
                    "This is a YouTube auto-generated mix that may contain 1000+ tracks. Importing it will take a long time and may hit rate limits.\n\nContinue anyway?");
                
                var confirmed = await DialogService.Instance.ShowDialogAsync<bool>(confirmDialog);
                if (!confirmed)
                {
                    lock (_activePlaylistImports) { _activePlaylistImports.Remove(normalizedUrl); }
                    return;
                }
            }

            try
            {
                if (_plugins.Get<YtDlpDownloadProvider>() is not { } ytDlpProvider || !ytDlpProvider.SupportsUrl(playlistUrl))
                {
                    ToastService.Instance.Show("Downloads are disabled. Enable yt-dlp in Settings.", ToastType.Warning);
                    return;
                }

                await _downloadService.DownloadPlaylistAsync(playlistUrl, onTrackReady: (downloadedTrack) =>
                {
                    if (_plugins.Get<LastFmMetadataProvider>() is { }) _enrichment.EnrichAsync(downloadedTrack);
                    Dispatcher.UIThread.Post(() => Library.Refresh());
                });
            }
            finally
            {
                // Always remove from active set
                lock (_activePlaylistImports)
                {
                    _activePlaylistImports.Remove(normalizedUrl);
                }
            }
        };

        Input.RadioSiteRequested += async (stationName, channels) =>
        {
            var chosen = await DialogService.Instance.ShowDialogAsync<List<RadioChannel>>(new Views.Dialogs.RadioMoodDialog(channels));
            if (chosen == null || chosen.Count == 0) return;

            // Snapshot existing radio URLs to skip duplicates
            var existing = new HashSet<string>(
                _library.GetAll()
                    .Where(t => t.MediaType == MediaType.Radio && !string.IsNullOrEmpty(t.Url))
                    .Select(t => t.Url!),
                StringComparer.OrdinalIgnoreCase);

            // Resolve all channels in parallel instead of one-by-one
            var resolved = await Task.WhenAll(chosen.Select(async channel =>
            {
                var s = channel.StreamUrl;
                if (string.IsNullOrWhiteSpace(s))
                    s = await RadioStreamResolver.ResolveAsync($"{stationName} {channel.Name}");
                return (Channel: channel, Stream: s);
            }));

            int added = 0;
            foreach (var (channel, stream) in resolved)
            {
                if (string.IsNullOrWhiteSpace(stream))
                {
                    ToastService.Instance.Show(string.Format(LocalizationService.Instance["Radio_Resolved_Failed"], channel.Name), ToastType.Warning);
                    continue;
                }
                // Skip if this stream URL is already in the library
                if (!existing.Add(stream)) continue;

                _library.Add(new Track { Title = $"{stationName} - {channel.Name}", Artist = stationName, Url = stream, Source = TrackSource.Unknown, MediaType = MediaType.Radio });
                added++;
            }

            Library.Refresh(); RadioLibrary.Refresh();
            if (added > 0) ToastService.Instance.Show(string.Format(LocalizationService.Instance["Radio_StationAdded"], added), ToastType.Success);
        };

        Input.RadioCatalogRequested += async () =>
        {
            var dialog = new Views.Dialogs.RadioCatalogDialog(station =>
            {
                var track = new Track
                {
                    Title = station.Name,
                    Artist = station.Genre, 
                    Url = station.StreamUrl,
                    Source = TrackSource.Unknown, 
                    MediaType = MediaType.Radio
                };
                
                // Check duplicate before adding (LibraryService.Add is void and silently ignores duplicates)
                if (_library.IsDuplicate(track)) return false;
                
                _library.Add(track);
                Dispatcher.UIThread.Post(() => { Library.Refresh(); RadioLibrary.Refresh(); });
                return true;
            });
            
            // We no longer care about the dialog result, as stations are added immediately
            await DialogService.Instance.ShowDialogAsync<object?>(dialog);
        };

        Input.TrackAdded += () =>
        {
            var track = _library.GetAll().LastOrDefault();
            if (track == null) return;
            if (track.MediaType != MediaType.Radio && _plugins.Get<LastFmMetadataProvider>() is { }) _enrichment.EnrichAsync(track);
            Dispatcher.UIThread.Post(() => Library.Refresh());
        };

        _downloadService.DownloadCompleted += OnDownloadCompleted;
        _downloadService.DownloadFailed += OnDownloadFailed;
        _downloadService.PlaylistBatchStarted += OnPlaylistBatchStarted;
        _downloadService.PlaylistBatchProgress += OnPlaylistBatchProgress;
        _downloadService.PlaylistBatchCompleted += OnPlaylistBatchCompleted;
        _downloadService.PlaylistTrackStarted += OnPlaylistTrackStarted;

        Settings.LastFmConnectRequested += async () =>
        {
            try
            {
                var auth = new LastFmAuthService(_config.GetLastFmApiKey(), _config.GetLastFmApiSecret());
                if (!auth.IsConfigured) { Settings.ReportLastFmAuthFailed("Set API key first."); return; }
                var token = await auth.GetRequestTokenAsync();
                if (string.IsNullOrEmpty(token)) { Settings.ReportLastFmAuthFailed("Token request failed."); return; }
                
                ShellHelper.OpenUrl(auth.GetAuthUrl(token));
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
            _lastFm.ReloadCredentials(_config); 
            Settings.ReportLastFmConnected(result.Username);
        };

        Settings.LastFmDisconnectRequested += () =>
        {
            _keyStore.DeleteKey("LastFm:SessionKey"); _keyStore.DeleteKey("LastFm:Username");
            _lastFm.ReloadCredentials(_config); 
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
            var path = await DialogService.Instance.PickFileAsync(
                "Import AI Tag JSON",
                "*.json", "*.txt", "*.md"
            );
            if (string.IsNullOrEmpty(path)) { Settings.ReportImportComplete(0, 0); return null; }
            string jsonContent;
            await using (var stream = System.IO.File.OpenRead(path))
            using (var reader = new System.IO.StreamReader(stream))
                jsonContent = await reader.ReadToEndAsync();
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

    private static string NormalizePlaylistUrl(string url)
    {
        // Strip common tracking parameters for comparison
        var uri = new Uri(url);
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        
        // Remove tracking params
        query.Remove("si");
        query.Remove("pp");
        query.Remove("utm_source");
        query.Remove("utm_medium");
        query.Remove("utm_campaign");
        
        // Rebuild URL
        var builder = new UriBuilder(uri)
        {
            Query = query.ToString()
        };
        
        return builder.Uri.ToString();
    }

    // --- Extracted Download Event Handlers ---
    private void OnDownloadCompleted(string trackId, string filePath, bool isInteractive)
    {
        Dispatcher.UIThread.Post(() =>
        {
            Library.Refresh(); Library.RefreshArtistGroups();
            if (isInteractive) ToastService.Instance.Show("Track download completed.", ToastType.Success, scope: "download");
        });
    }

    private void OnDownloadFailed(string trackId, string error, bool isInteractive)
    {
        Dispatcher.UIThread.Post(() =>
        {
            Library.Refresh();
            if (isInteractive) ToastService.Instance.Show("Track download failed.", ToastType.Error, scope: "download");
        });
    }

    private void OnPlaylistBatchStarted(int totalTracks)
    {
        var activity = ToastService.Instance.StartLiveActivity("Downloading Playlist", totalTracks > 0 ? $"0/{totalTracks}" : "Fetching metadata...", isIndeterminate: totalTracks == 0);
        lock (_playlistBatches) { _playlistBatches.Add((activity, totalTracks)); }
    }

    private void OnPlaylistBatchProgress(int completed, int total, int skipped)
    {
        LiveNotification? activity;
        lock (_playlistBatches)
        {
            var match = _playlistBatches.FirstOrDefault(b => b.Total == total && b.Total > 0);
            activity = match != default ? match.Activity : _playlistBatches.LastOrDefault().Activity;
        }
        if (activity == null) return;
        ToastService.Instance.UpdateLiveActivity(activity, $"Downloading: {completed}/{total} (Skipped: {skipped})", total > 0 ? (completed + skipped) * 100.0 / total : 0, false);
    }

    private void OnPlaylistBatchCompleted(int completed, int failed, int skipped)
    {
        LiveNotification? activity;
        lock (_playlistBatches)
        {
            activity = _playlistBatches.Count > 0 ? _playlistBatches[0].Activity : null;
            if (_playlistBatches.Count > 0) _playlistBatches.RemoveAt(0);
        }
        ToastService.Instance.CompleteLiveActivity(activity, $"Bulk complete: {completed} downloaded, {failed} failed, {skipped} skipped.");
        Dispatcher.UIThread.Post(() => Library.Refresh());
    }

    private void WireAIAndPowerEvents()
    {
        _localAI.FallbackNotice += OnLocalAIFallbackNotice;
        
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
        _powerState.PowerStateChanged += OnPowerStateChanged;
        ApplyBatteryEffectsTier(_powerState.Current);
        Settings.PowerModelsChanged += (b, p, a) => _localAI.ConfigurePowerModels(b, p, a);
        _localAI.ConfigurePowerModels(Settings.BatteryModel, Settings.PerformanceModel, Settings.AutoPowerModelSwitch);
        _powerState.StartPolling();

        Settings.MaxConcurrentDownloadsChanged += limit => _downloadService.UpdateConcurrencyLimit(limit);
        _downloadService.UpdateConcurrencyLimit(Settings.MaxConcurrentDownloads);

        _enrichment.BackfillCompleted += OnBackfillCompleted;

        _ = Task.Run(async () =>
        {
            await Task.Delay(3000);
            if (!_prefsService.Current.AutoGenerateMoodPlaylist) return;
            if (PowerStateService.ReadPowerState() != PowerState.Battery) _enrichment.BackfillAsync();
            else { _initialMoodPlaylistRun = true; _ = RunMoodPlaylistAsync(forceRefresh: false); }
        });
    }

    // --- Extracted AI & Power Event Handlers ---
    private void OnLocalAIFallbackNotice(string message) => Dispatcher.UIThread.Post(() => ToastService.Instance.Show(message, ToastType.Warning));

    private void OnPowerStateChanged(PowerState state)
    {
        _localAI.OnPowerStateChanged(state);
        Dispatcher.UIThread.Post(() => Settings.PowerStateLabel = state == PowerState.AC ? "Plugged In" : "On Battery");
        ApplyBatteryEffectsTier(state);
    }

    private void OnBackfillCompleted()
    {
        if (_initialMoodPlaylistRun) return;
        _initialMoodPlaylistRun = true;
        if (_prefsService.Current.AutoGenerateMoodPlaylist) _ = RunMoodPlaylistAsync(forceRefresh: false);
    }

        private void ApplyBatteryEffectsTier(PowerState state)
    {
        if (state == PowerState.Battery && _prefsService.Current.AutoEffectsTier)
            _effectsTier.SetBatteryOverride(EffectsTier.Minimal);
        else if (state == PowerState.AC)
            _effectsTier.ClearBatteryOverride();
    }

    private async void OnClearYtDlpCacheRequested()
    {
        try
        {
            // FIX: Pass raw name, let ProcessRunner handle the .exe resolution
            var result = await ProcessRunner.RunAsync("yt-dlp", new[] { "--rm-cache-dir" }, TimeSpan.FromSeconds(30));
            
            if (result.ExitCode == 0)
            {
                ToastService.Instance.Show("yt-dlp cache cleared!", ToastType.Success, 5000, scope: "maintenance");
            }
            else if (result.TimedOut)
            {
                ToastService.Instance.Show("Cache clear timed out after 30 seconds.", ToastType.Error, 5000, scope: "maintenance");
            }
            else
            {
                var errorOutput = string.IsNullOrWhiteSpace(result.StandardError) 
                    ? "Unknown error" 
                    : result.StandardError.Split('\n')[0].Trim();
                ToastService.Instance.Show($"Cache clear failed: {errorOutput}", ToastType.Error, 5000, scope: "maintenance");
            }
        }
        catch (Exception ex)
        {
            ToastService.Instance.Show($"Cache clear failed: {ex.Message}", ToastType.Error, 5000, scope: "maintenance");
        }
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
        
        var humanName = GenerateHumanPlaylistName(query);
        var playlistName = _playlists.NameExists(humanName) ? $"{humanName} ({DateTime.UtcNow.ToLocalTime():HH:mm})" : humanName;
        
        var playlist = _playlists.Create(playlistName, $"Generated by AI prompt: {query}", _aiPlaylistsFolder?.Id);
        foreach (var track in tracks) _playlists.AddTrack(playlist.Id, track);
        
        Playlist.Refresh(); Playlist.SelectById(playlist.Id); Nav.RefreshPlaylistLists(); CurrentPage = "Playlists";
        Dispatcher.UIThread.Post(() => GlobalSearchQuery = string.Empty);
    }

    private string GenerateHumanPlaylistName(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return "AI Mix";
        
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                         .Select(w => w.Length > 0 ? char.ToUpper(w[0]) + w.Substring(1).ToLower() : w);
        var cleanQuery = string.Join(" ", words);

        int seed = StableHash(cleanQuery.ToLowerInvariant());
        string[] flavors = { 
            $"{cleanQuery} Mix", 
            $"The {cleanQuery} Experience", 
            $"Essential {cleanQuery}", 
            $"{cleanQuery} Deep Cuts", 
            $"Late Night {cleanQuery}", 
            $"{cleanQuery} Essentials", 
            $"Vibe: {cleanQuery}" 
        };
        return flavors[seed % flavors.Length];
    }

    private static int StableHash(string s)
    {
        unchecked
        {
            uint h = 2166136261;
            foreach (var c in s) { h ^= c; h *= 16777619; }
            return (int)(h & 0x7FFFFFFF);
        }
    }

    private void OnPlaylistTrackStarted(string title, int current, int total)
    {
        LiveNotification? activity;
        lock (_playlistBatches)
        {
            var match = _playlistBatches.FirstOrDefault(b => b.Total == total && b.Total > 0);
            activity = match != default ? match.Activity : _playlistBatches.LastOrDefault().Activity;
        }
        if (activity == null) return;

        // Truncate long titles to prevent UI overflow
        var displayTitle = title.Length > 50 ? title[..47] + "..." : title;
        ToastService.Instance.UpdateLiveActivity(activity, $"Downloading {current}/{total}: {displayTitle}");
    }
}
