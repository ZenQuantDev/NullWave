# Changelog

All notable changes to NullWave will be documented in this file.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

---

## [0.1.3] - 30-05-2026

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
- `PlayerViewModel` — `AlbumArtPath` / `HasAlbumArt` properties (Phase 6 placeholder); all playback actions now emit structured log entries via `NullActionLogger`
- `MainWindow.axaml.cs` — `OnKeyDown` handler toggles menu bar on `Alt` press (Firefox-style)
- `SidebarView.axaml` — Discord-style local profile bar at bottom (avatar circle, username, bio, gear → Settings); section labels use design tokens; emoji replaced with text labels
- `MiniPlayerView.axaml` — Spotify-style 3-column layout: track info + art thumbnail left, controls + progress center, volume slider right
- `MenuBarView.axaml` — hidden by default, shown/hidden via Alt key; all emoji removed from menu items

### Changed
- All hardcoded hex colors replaced with `{StaticResource Brush*}` tokens
- All hardcoded font sizes replaced with `{StaticResource FontSize*}` tokens
- All hardcoded corner radii replaced with `{StaticResource Radius*}` tokens
- `ImportProgressView` shows track count fraction (x / total) alongside status text

### Fixed
- `MenuBarView.axaml` XAML parse error — missing space between `Header="Sort by Play Count"` and `Command=` attribute
- `MenuBarView.axaml` same issue on `Header="Open Data Folder"` and `Command=`
- `PlayerViewModel.AlbumArtPath` no longer reads from `Track` model (Phase 6 concern) — stored on ViewModel directly, cleared on each new track

### Logging output after this version
```
~/.nullwave/logs/
  NullWave-YYYYMMDD.log         ← all events
  UserActions-YYYYMMDD.log      ← [ACTION] entries only
  Errors-YYYYMMDD.log           ← errors with source attribution
```

---

## [0.1.2] - 29-05-2026

### Added
- LibVLCSharp local file playback (play/pause/stop/seek/volume)
- yt-dlp download integration (audio download to ~/.nullwave/downloads/)
- PlayerViewModel — mini player bar with track display, status, download progress
- DownloadService — wraps yt-dlp as process, parses progress, fires completion events
- PlaybackService — wraps LibVLCSharp MediaPlayer with event-driven state
- TrackDetailViewModel — sliding right panel (0→320px animated) with editable fields
- ImportViewModel — bulk folder import with progress bar and subfolder dialog
- ConfirmDialog — reusable Yes/No dialog
- BoolToOpacityConverter — favorite star opacity (full/dim)
- Play command in ⋮ context menu per track row
- Mini player bar wired to PlayerViewModel
- FilterLastFmCommand in sidebar

### Changed
- MainViewModel wired: Library.PlayTrackRequested → Player.PlayTrack
- MainWindow.axaml fully rewritten with sidebar, detail panel, floating ＋ button
- Track rows now show stacked Title+Artist, play count, inline ⭐, ⋮ menu
- Material.Avalonia removed (incompatible with Avalonia 12.0.3), pure custom styles used

### Fixed
- libVLC not found on Fedora (/usr/lib64) — symlinks + ldconfig config added
- BoolConverters.ToObject unavailable in Avalonia 12 — replaced with custom converter
- FilterLastFmCommand declared twice — duplicate removed
- Mini player bar had no Player bindings — fully wired

### Security
- API key redaction in logs verified working
- KeyStore encryption confirmed operational

---

## [0.1.1] - 28-05-2026

### Added
- YouTube Data API v3 real metadata fetching (title + channel name)
- Encrypted local API key storage (AES-256-GCM, machine-bound via /etc/machine-id)
- KeyStoreService — secure read/write/delete of API keys to ~/.nullwave/keys.enc
- SecureDeleteService — 3-pass secure wipe for keys, logs, and full data
- ConfigService — reads from KeyStore, falls back to environment variables
- SettingsViewModel wired to KeyStore and SecureDelete
- Log redaction enricher — masks API key patterns in all log output
- Menu bar (File, Library, Settings, Help)
- Left sidebar navigation (Library, Playlists, Queue, Stats)
- Sidebar source filters (YouTube, Spotify, SoundCloud, Local)
- Sidebar quick filters (Favorites, Recent)
- Sort commands (Title, Artist, Date Added, Play Count)
- Mini player bar (placeholder, playback coming in Phase 3)
- Open Data Folder and Open Logs shortcuts in Help menu
- Nuclear wipe option in Settings menu
- DISCLAIMER.md — terms of use and liability protection
- ROADMAP.md — full phased development plan
- SoundCloud added to TrackSource enum and SourceDetector

### Changed
- Removed Instagram from TrackSource (replaced with SoundCloud)
- MainViewModel refactored into focused child ViewModels
- ViewModelBase moved to ViewModels/Base/ namespace
- RelayCommand updated with generic RelayCommand<T>
- API keys moved from config files to environment variables, then to encrypted KeyStore
- All TextBox bindings set to TwoWay mode
- AppLogger upgraded with redaction enricher and improved output templates

### Fixed
- Duplicate variable declaration in TrackInputViewModel.AddLocalFileAsync
- Missing using directives in SettingsViewModel and TrackViewModel
- appsettings.json removed from .csproj to prevent build errors after file deletion
- obj/bin removed from git tracking

### Security
- API keys never stored in project folder or git history
- Keystore encrypted with AES-256-GCM, key derived from machine-id + username
- Log output redacts strings matching API key patterns
- Secure 3-pass file wipe before deletion

---

## [0.1.0] - 26-05-2026

### Added
- Initial project setup with Avalonia UI (.NET 8)
- Core track model: Title, Artist, URL, FilePath, Source, DateAdded
- TrackSource enum (YouTube, Spotify, Local, Instagram, Unknown)
- LibraryService with full in-memory track management
- PlaylistService (Create, Remove, Rename, Add/Remove/Reorder tracks)
- MetadataService (placeholder, API-ready)
- UrlParserService (YouTube ID, Spotify ID, local file support)
- ExportService (JSON and CSV export)
- SourceDetector, RelayCommand, AppLogger (Serilog)
- Basic Avalonia UI window
- 23 unit tests (xUnit) — all passing