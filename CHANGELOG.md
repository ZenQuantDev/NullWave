# Changelog

All notable changes to NullWave will be documented in this file.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

---

## [0.3.0] - 13-Jun-2026

### Added
- `Views/Settings/AppearanceTab.axaml` — new Appearance settings tab with:
  - Accent color picker (Purple / Blue / Amber / Green / Red) — saved, live theming in v0.4
  - Track row style selector (Comfortable / Compact / Cozy) with visual previews
  - Font scale selector (Small / Medium / Large)
  - Sidebar width preset (Narrow / Normal / Wide)
  - Compact mode toggle (hides album art thumbnails)
- `Services/PreferencesService.cs` — JSON persistence for all General and Appearance settings; auto-saves on every change
- `Models/Preferences.cs` — `AccentColor`, `TrackRowStyle`, `FontScale`, `CompactMode`, `SidebarWidth` fields
- `Services/PlaybackNavigator.cs` — extracted shuffle/repeat/queue navigation logic from `PlayerViewModel`
- `Services/Metadata/YouTubeMetadataFetcher.cs` — split from `MetadataService`
- `Services/Metadata/SoundCloudMetadataFetcher.cs` — split from `MetadataService`
- `Services/Metadata/LocalMetadataFetcher.cs` — split from `MetadataService`
- `ViewModels/PlaylistImportViewModel.cs` — extracted playlist import state from `TrackInputViewModel`
- `Themes/ControlStyles.axaml` — `TabItem` styles extracted from `SettingsWindow` inline block
- `Themes/ControlStyles.axaml` — `.secondary` button class added

### Changed
- `SettingsWindow.axaml` — removed clipping footer, switched to `DockPanel`, tab order updated (General → Appearance → API Keys → Audio → Updates → Advanced → About)
- `ApiKeysTab.axaml` — Save button moved from global footer into tab, per-field ✓ saved indicator
- `ImportProgressView.axaml` — hardcoded hex colors replaced with theme brushes, added `x / total` counter
- `ConfirmDialog.axaml` — hardcoded hex colors replaced with theme brushes, buttons use `secondary`/`danger` classes
- `TrackListView.axaml` — playlist import bindings updated to `Input.PlaylistImport.*`
- `SidebarView.axaml` — indentation normalized
- `MetadataService.cs` — refactored to thin orchestrator, delegates to fetcher classes
- `PlayerViewModel.cs` — shuffle/repeat/navigation delegated to `PlaybackNavigator`
- `SettingsViewModel.cs` — appearance properties wired to `PreferencesService`

### Fixed
- Settings window content clipping at bottom — `Grid` with fixed `RowDefinitions` replaced with `DockPanel`
- Playlist import progress bar bindings broken after `PlaylistImportViewModel` extraction

## [0.2.1] - 07-Jun-2026

### Added
- `ControlStyles.axaml` — `.player-btn` style: Spotify-inspired borderless player buttons, no background box, subtle hover only
- `.player-btn.play` — filled white circle for play/pause, dark icon inside
- `PlayerViewModel` — `ShuffleForeground` property: accent color when shuffle is on, muted when off
- `PlayerViewModel` — `RepeatForeground` property: accent color when repeat is active, muted when off
- `PlayerViewModel` — `ToggleMuteCommand` with volume memory (`_volumeBeforeMute`)
- `PlayerViewModel` — `ToggleCurrentFavoriteCommand` — favorite toggle for currently playing track
- `PlayerViewModel` — `VolumeIcon` property — switches between muted/unmuted glyph
- `PlayerViewModel` — `IsCurrentFavorite` property bound to current track
- `MiniPlayerView` — favorite star button for current track in left section
- `MiniPlayerView` — mute toggle button replaces static "vol" label
- `DatabaseService` — SQLite-net backed storage: `LoadAll`, `Insert`, `Update`, `Delete`
- `TrackRecord` — flat SQLite-mapped model, tags serialized as pipe-separated string
- `LibraryService` — DB-backed: persists on `Add`, `Remove`, `Update`, `ToggleFavorite`, `RecordPlay`
- `LibraryService` — `BackfillAlbumArt()` on startup: extracts embedded art for existing tracks
- `MetadataService` — `ExtractAlbumArt()`: extracts embedded ID3 art, caches to `~/.nullwave/art/`
- `Helpers/Converters/FilePathToBitmapConverter.cs` — converts file path string to `Bitmap` for Avalonia `Image`
- `TrackDetailViewModel` — `CurrentTrackArtPath` property, refreshed via `RefreshDisplayProperties()`
- `TrackDetailViewModel` — `Save()` now calls `_library.Update()` to persist edits to SQLite
- `PlaybackService` — re-applies volume on `Playing` event to fix silent-start bug on LibVLC pipeline init
- `MainWindow` — keyboard shortcuts: Space (play/pause), ←/→ (seek ±5s), M (mute), N (next), P (previous)

### Fixed
- Track list thumbnails not showing — `Image.Source` now uses `FilePathToBitmapConverter` instead of raw string binding
- Track detail panel art not showing — same converter applied to `Detail.CurrentTrackArtPath`
- Miniplayer album art not showing — converter applied to `Player.AlbumArtPath`
- Tracks loaded from DB had no album art — `BackfillAlbumArt()` extracts and saves on startup
- Remove track not working — `RemoveTrackCommand` now accepts `Track` parameter, context menu passes `CommandParameter="{Binding}"`
- Silent start on playback — volume re-applied after LibVLC pipeline initializes
- Shuffle/repeat buttons gave no visual feedback — now use accent color when active

### Changed
- Miniplayer buttons fully redesigned — Spotify-style `.player-btn` class replaces `.icon-btn`
- Miniplayer layout switched from `StackPanel` to two-row `Grid` center section — eliminates vertical clipping
- Miniplayer fixed `Height="90"`, fixed center `Width="440"` — consistent layout at all window sizes
- `LibraryService` constructor now accepts optional `MetadataService` for art extraction
- `MainViewModel` passes `_metadata` to `LibraryService` constructor

## [0.2.0] - 06-Jun-2026

### Added
- `Themes/Shapes.axaml` — `RadiusArt` (6px) corner radius for track art thumbnails
- `Themes/ControlStyles.axaml` — `.danger` button class (red border, fills on hover)
- `Themes/ControlStyles.axaml` — `ListBoxItem` opacity transition (150ms fade-in on add)
- `MiniPlayerView` — shuffle toggle (`IsShuffle`, `ShuffleIcon`, `ToggleShuffleCommand`)
- `MiniPlayerView` — repeat mode cycle: None → All → One (`CycleRepeatCommand`, `RepeatIcon`)
- `MiniPlayerView` — seek slider now fires only on `PointerReleased` (no feedback loop)
- `MiniPlayerView` — `-5s` / `+5s` seek buttons replacing broken Unicode glyphs
- `MiniPlayerView` — volume slider restored (`Mode=TwoWay` on `Player.Volume`)
- `PlayerViewModel` — `RepeatMode` enum (None/One/All), shuffle with random non-repeat pick
- `PlayerViewModel` — `OnTrackFinished()` wired: autoplay next, repeat-one replay, repeat-all wrap
- `PlayerViewModel` — `SeekTo(float)` public method called from code-behind on seek release
- `PlayerViewModel` — `PlaySelectedTrackRequested` event — play button starts selected track when nothing loaded
- `TrackDetailViewModel` — subscribes to `Track.PropertyChanged` so `PlayCount` and `LastPlayed` update live
- `TrackListView` — unified toolbar: search + sort `ComboBox` + `SplitButton` (`+ Add` with dropdown)
- `TrackListView` — collapsible URL input row toggled by `Input.ShowUrlInputCommand`
- `TrackListView` — URL input row has `Opacity` transition (200ms fade)
- `TrackDetailView` — `Opacity` + `Margin` transitions on slide-in panel
- `TrackInputViewModel` — `IsUrlInputVisible` + `ShowUrlInputCommand` for toggling URL row
- `TrackInputViewModel` — `AddTrack()` auto-detects local file path, folder path, or HTTP URL
- `TrackInputViewModel` — `AddFolderPathAsync()` recursively imports audio files from a folder
- `TrackInputViewModel` — `StatusMessage` property for live import feedback
- `TrackInputViewModel` — playlist URL auto-detection (`IsPlaylistUrl`) triggers `ImportPlaylistAsync`
- `DownloadService` — `IsPlaylistUrl()` static helper (detects `list=`, `/sets/`, `/playlist/`)
- `DownloadService` — `DownloadPlaylistAsync()` — fetches flat metadata via `--flat-playlist --dump-json`, then downloads each track individually with per-track progress callbacks
- `SidebarView` — 32×32 logo placeholder ("N" in accent square) beside NullWave wordmark
- `MainViewModel` — wires `Player.PlaySelectedTrackRequested` → play selected or first track
- SQLite-net-pcl + SQLitePCLRaw.bundle_green packages added (persistence implementation coming next)

### Fixed
- Seek slider feedback loop — `Mode=TwoWay` → `Mode=OneWay`, seek fires on pointer release only
- Volume slider broken after previous refactor — binding and converter references cleaned up
- Play button on miniplayer did nothing when no track was loaded — now starts selected track
- `PlayCount` and `LastPlayed` in detail panel never updated after playback — fixed via INPC subscription
- `AddTrack()` fired with empty inputs showing no feedback — now toggles URL input row instead
- `AddFolderPathAsync` spurious `async` warning (CS1998) removed
- `ImportPlaylistAsync` fire-and-forget warning (CS4014) suppressed with explicit discard

### Changed
- `+ Add Track` + `Import ▼` two-button toolbar replaced with single `SplitButton`
- URL / Title / Artist input row collapsed by default, shown on demand
- `PlayPrevious` / `PlayNext` now operate on full library (`GetAll()`) not queue only
- Seek `-5s`/`+5s` buttons replace `⏪`/`⏩` Unicode glyphs (missing on Fedora)
- Stop button removed from miniplayer bar (still accessible via context menu)

---

## [0.1.3] - 30-May-2026

### Added
- `Themes/` folder — design system split into four files:
  - `Colors.axaml` — full color palette (dark blue, purple accent, surface layers) + all brushes
  - `Typography.axaml` — type scale xs→3xl with semantic aliases (Label, Body, Title, Heading)
  - `Shapes.axaml` — corner radii, spacing tokens, dimension constants (sidebar width, player height, avatar sizes)
  - `ControlStyles.axaml` — all reusable styles: nav/icon-btn/primary/ghost buttons, ListBoxItem, TextBox, ProgressBar, Slider, Menu, ScrollBar
- `App.axaml` reduced to thin shell — merges theme files via `ResourceInclude` and `StyleInclude`
- `Helpers/NullWavePaths.cs` — single source of truth for all `~/.nullwave/*` paths; `EnsureDirectories()` called at startup
- `Helpers/Logging/NullActionLogger.cs` — static structured logger for user actions and attributed errors
- `Helpers/Logging/NullWaveLogConfig.cs` — Serilog configuration with three separate file sinks (System, UserActions, Errors)
- `Services/StartupDiagnosticsService.cs` — logs structured startup block: version, runtime, OS, library load time, API key status, connectivity check, VLC + yt-dlp versions
- `ViewModels/UserProfileViewModel.cs` — local profile (username, bio, avatar), persisted to `~/.nullwave/profile.json`, no auth required
- `Program.cs` updated — `EnsureDirectories()` + `NullWaveLogConfig.Initialize()` called before anything else; top-level exception catch + `CloseAndFlush()` on exit
- `MainViewModel` — `IsMenuBarVisible` + `ToggleMenuBar()` for Alt-key toggle; `Profile` child ViewModel; startup diagnostics wired
- `PlayerViewModel` — `AlbumArtPath` / `HasAlbumArt` properties (Phase 6 placeholder); all playback actions emit structured log entries
- `MainWindow.axaml.cs` — `OnKeyDown` handler toggles menu bar on `Alt` press (Firefox-style)
- `SidebarView.axaml` — Discord-style local profile bar at bottom (avatar circle, username, bio, gear → Settings)
- `MiniPlayerView.axaml` — Spotify-style 3-column layout: track info + art thumbnail left, controls + progress center, volume slider right
- `MenuBarView.axaml` — hidden by default, shown/hidden via Alt key

### Changed
- All hardcoded hex colors replaced with `{StaticResource Brush*}` tokens
- All hardcoded font sizes replaced with `{StaticResource FontSize*}` tokens
- All hardcoded corner radii replaced with `{StaticResource Radius*}` tokens
- `ImportProgressView` shows track count fraction (x / total) alongside status text

### Fixed
- `MenuBarView.axaml` XAML parse errors — missing spaces between attributes
- `PlayerViewModel.AlbumArtPath` stored on ViewModel directly, not read from Track model

---

## [0.1.2] - 29-May-2026

### Added
- LibVLCSharp local file playback (play/pause/stop/seek/volume)
- yt-dlp download integration (audio download to `~/.nullwave/downloads/`)
- `PlayerViewModel` — mini player bar with track display, status, download progress
- `DownloadService` — wraps yt-dlp as process, parses progress, fires completion events
- `PlaybackService` — wraps LibVLCSharp MediaPlayer with event-driven state
- `TrackDetailViewModel` — sliding right panel (0→320px animated) with editable fields
- `ImportViewModel` — bulk folder import with progress bar and subfolder dialog
- `ConfirmDialog` — reusable Yes/No dialog
- `BoolToOpacityConverter` — favorite star opacity (full/dim)
- Play command in ⋮ context menu per track row
- Mini player bar wired to `PlayerViewModel`

### Changed
- `MainViewModel` wired: `Library.PlayTrackRequested` → `Player.PlayTrack`
- `MainWindow.axaml` fully rewritten with sidebar, detail panel, floating + button
- Track rows now show stacked Title+Artist, play count, inline ⭐, ⋮ menu
- Material.Avalonia removed (incompatible with Avalonia 12.0.3), pure custom styles used

### Fixed
- libVLC not found on Fedora (`/usr/lib64`) — symlinks + ldconfig config added
- `BoolConverters.ToObject` unavailable in Avalonia 12 — replaced with custom converter
- `FilterLastFmCommand` declared twice — duplicate removed

### Security
- API key redaction in logs verified working
- KeyStore encryption confirmed operational

---

## [0.1.1] - 28-May-2026

### Added
- YouTube Data API v3 real metadata fetching (title + channel name)
- Encrypted local API key storage (AES-256-GCM, machine-bound via `/etc/machine-id`)
- `KeyStoreService` — secure read/write/delete of API keys to `~/.nullwave/keys.enc`
- `SecureDeleteService` — 3-pass secure wipe for keys, logs, and full data
- `ConfigService` — reads from KeyStore, falls back to environment variables
- `SettingsViewModel` wired to KeyStore and SecureDelete
- Log redaction enricher — masks API key patterns in all log output
- Menu bar (File, Library, Settings, Help)
- Left sidebar navigation (Library, Playlists, Queue, Stats)
- Sidebar source filters (YouTube, Spotify, SoundCloud, Local)
- Sort commands (Title, Artist, Date Added, Play Count)
- DISCLAIMER.md, ROADMAP.md, SECURITY.md, CONTRIBUTING.md added

### Changed
- Removed Instagram from `TrackSource` (replaced with SoundCloud)
- `MainViewModel` refactored into focused child ViewModels
- API keys moved from config files to encrypted KeyStore

### Fixed
- Duplicate variable declaration in `TrackInputViewModel.AddLocalFileAsync`
- Missing using directives in `SettingsViewModel`
- `obj/bin` removed from git tracking

### Security
- API keys never stored in project folder or git history
- Keystore encrypted with AES-256-GCM, key derived from machine-id + username
- Log output redacts strings matching API key patterns
- Secure 3-pass file wipe before deletion

---

## [0.1.0] - 26-May-2026

### Added
- Initial project setup with Avalonia UI (.NET 8)
- Core track model: Title, Artist, URL, FilePath, Source, DateAdded
- `TrackSource` enum (YouTube, Spotify, Local, SoundCloud, Unknown)
- `LibraryService` with full in-memory track management
- `PlaylistService` (Create, Remove, Rename, Add/Remove/Reorder tracks)
- `MetadataService` (placeholder, API-ready)
- `UrlParserService` (YouTube ID, Spotify ID, local file support)
- `ExportService` (JSON and CSV export)
- SourceDetector, RelayCommand, AppLogger (Serilog)
- Basic Avalonia UI window
- 23 unit tests (xUnit) — all passing