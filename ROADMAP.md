# NullWave — Roadmap

> Last updated: 07-Jun-2026

---

## Legend

| Symbol | Meaning |
|--------|---------|
| ✅ | Done — merged to main |
| 🔄 | In progress — active branch |
| 🔜 | Up next — ready to start |
| 📋 | Planned — scoped, not started |
| 💡 | Future — idea, not yet scoped |

---

## Phase 1 — Core Foundation ✅

**Goal:** Working music library with local file playback and metadata reading.

- ✅ Track model + `LibraryService` (SQLite-backed via `DatabaseService`)
- ✅ TagLib# ID3 tag reading on import
- ✅ LibVLC playback via `PlaybackService` (Fedora: symlinks in `/usr/lib`)
- ✅ yt-dlp URL download to `~/.nullwave/downloads/` as MP3 (`DownloadService`)
- ✅ AES-256-GCM encrypted `KeyStore` for API credentials
- ✅ Last.fm API integration (`LastFmService`)
- ✅ YouTube Data API v3 integration (`YouTubeService`)
- ✅ Serilog with redaction enricher (file + console sinks)
- ✅ 23 unit tests passing

---

## Phase 2 — MVVM + UI Shell ✅

**Goal:** Full MVVM wiring with sidebar, track list, detail panel, and mini player.

- ✅ `MainViewModel` orchestrating all child ViewModels
- ✅ `LibraryViewModel` — sort, filter, search, favorites, PlayTrackRequested event
- ✅ `PlayerViewModel` — PlayTrack, PlayPause, Stop, PlayPauseIcon binding
- ✅ `TrackDetailViewModel` — slide-in panel, editable Title/Artist/tags/notes
- ✅ `ImportViewModel` — progress strip, ImportCompleted → Library.Refresh
- ✅ `BoolToOpacityConverter` for star favorite display
- ✅ Right-click context menu via `ListBox.ItemContainerTheme`
- ✅ ⋮ button `MenuFlyout` with `CommandParameter="{Binding}"`

---

## Phase 3 — Refactor / Split Views ✅

**Goal:** Thin MainWindow shell; each panel is its own UserControl.

- ✅ `MenuBarView.axaml` — extracted top menu bar (hidden by default, Alt-toggle)
- ✅ `SidebarView.axaml` — nav, filters, sources, Discord-style profile bar at bottom
- ✅ `TrackListView.axaml` — search bar, ListBox, ContextMenu, import progress
- ✅ `TrackDetailView.axaml` — 0→320px `DoubleTransition` slide panel
- ✅ `MiniPlayerView.axaml` — Spotify-style 3-column bar (art, controls, volume)
- ✅ `ImportProgressView.axaml` — import progress strip with fraction counter
- ✅ MainWindow reduced to thin DockPanel shell with Alt-key handler
- ✅ Bump Avalonia 12.0.3 → 12.0.4
- 🔜 Merge `refactor/split-views` → `main`

**Notes:**
- `TrackSource` enum should be moved to `Models/TrackSource.cs`
- `MetadataService` is a candidate for splitting (see Phase 7)

---

## Phase 4 — Advanced Logging ✅

**Branch:** `feature/4-advanced-logging`
**Goal:** Every action logged, every startup state visible, every error attributed to its source.

- ✅ `NullWavePaths.cs` — single source of truth for all `~/.nullwave/*` paths
- ✅ `NullActionLogger.cs` — static helper: `User()`, typed convenience methods, `Error()`, `StartupLine()`
- ✅ `NullWaveLogConfig.cs` — three Serilog sinks: System, UserActions, Errors (channel-filtered)
- ✅ `StartupDiagnosticsService.cs` — logs version, runtime, OS, library load time, API key status, connectivity, VLC + yt-dlp versions
- ✅ `PlayerViewModel` — all playback events emit structured `NullActionLogger` calls
- ✅ `MainViewModel` — exit, settings, navigation all logged
- ✅ `Program.cs` — `EnsureDirectories()` + log init as first operations; `CloseAndFlush()` on exit
- ✅ Separate log files: `NullWave-*.log`, `UserActions-*.log`, `Errors-*.log`

**Remaining (wire into other ViewModels):**
- 📋 `ImportViewModel` — add `NullActionLogger.ImportStarted/Completed/Failed` calls
- 📋 `LibraryViewModel` — add `TrackAdded`, `TrackRemoved`, `FavoriteToggled`, `SearchPerformed`
- 📋 `TrackDetailViewModel` — add `TrackEdited` on save
- 📋 `PlaylistViewModel` — add `PlaylistCreated`, `PlaylistDeleted`, `PlaylistTrackAdded`
- 📋 `SettingsViewModel` — add `SettingChanged` on key save

---

## Phase 5 — UI Redesign ✅

**Branch:** `feature/5-ui-redesign`
**Goal:** Visual polish — proper color scheme, icon library, larger type, UI depth, Alt menu toggle, local profile bar.

- ✅ `Themes/Colors.axaml` — dark navy/purple palette, all brushes defined
- ✅ `Themes/Typography.axaml` — xs→3xl type scale, semantic aliases
- ✅ `Themes/Shapes.axaml` — radii, spacing tokens, dimension constants
- ✅ `Themes/ControlStyles.axaml` — nav/icon-btn/primary/ghost buttons, ListBoxItem, TextBox, ProgressBar, Slider, Menu, ScrollBar
- ✅ `App.axaml` — thin shell, merges all theme files
- ✅ Alt-key menu bar toggle (Firefox-style, `MainWindow.axaml.cs`)
- ✅ Discord-style local profile bar in `SidebarView` (avatar, username, bio, gear → Settings)
- ✅ `UserProfileViewModel` — loads/saves `~/.nullwave/profile.json`, avatar picker
- ✅ Spotify-style `MiniPlayerView` — 3-column, art thumbnail, controls, volume slider
- ✅ All hardcoded colors/sizes/radii replaced with theme tokens

**Remaining:**
- 📋 Replace remaining emoji in nav buttons with proper SVG icon set (`Assets/Icons/Icons.axaml`)
- 📋 `OpacityTransition` on menu bar show/hide (150ms fade)
- 📋 Profile edit UI (inline in sidebar or dedicated Settings tab)
- ✅ Logo placeholder in `SidebarView` — 32×32 accent square beside wordmark
- 📋 Replace placeholder with real SVG logo (`Assets/Icons/logo.svg`)
- 📋 `TrackListView` — apply theme tokens, larger row height, better typography
- 📋 `TrackDetailView` — apply theme tokens

---

## Phase 6 — Now Playing Redesign 📋

**Branch:** `feature/6-now-playing`
**Goal:** Spotify-inspired Now Playing panel with album art and blur background.

### 6.1 Album Art Fetching

- 📋 `AlbumArtService` — fetches cover art, caches to `~/.nullwave/art/{hash}.jpg`
- 📋 Sources (priority): embedded ID3 tag → Last.fm cover API → YouTube thumbnail
- 📋 Cache key: `SHA256(Artist + Album)` truncated to 16 chars
- 📋 `AlbumArtService.GetArtAsync(track)` → sets `PlayerViewModel.AlbumArtPath`
- 📋 Fallback: `Assets/placeholder-art.png`

### 6.2 Blur Background Effect

Evaluate in order:
1. `ExperimentalAcrylicBorder` — hardware blur, Avalonia 12+
2. `WriteableBitmap` software Gaussian blur at 1/4 resolution
3. Avalonia `BlurEffect` on a `Canvas`

- 📋 `BlurredArtBackground.axaml` — reusable UserControl: `IBitmap?` in, blurred+darkened surface out

### 6.3 Now Playing Left Panel

- 📋 `NowPlayingPanelView.axaml` — album art (240×240), title, artist, like/more buttons, Last.fm bio, tag chips
- 📋 `NowPlayingPanelViewModel` — binds to `PlayerViewModel.CurrentTrack`, loads art + bio async
- 📋 Replaces or sits alongside `TrackDetailView` (decision pending)

### 6.4 Now Playing Bar

- 📋 Full-width progress bar as accent underline beneath player bar
- 📋 Art thumbnail in `MiniPlayerView` wired to `AlbumArtPath` (binding already in place)

---

## Phase 7 — Custom Windows + Full Wiring 💡

**Branch:** `feature/7-custom-windows`
**Goal:** Custom About/Update windows, full backend wiring, code splitting.

### 7.1 Custom Windows

- 💡 `AboutWindow.axaml` — version, build date, GitHub link, license
- 💡 `UpdateWindow.axaml` — GitHub Releases API check, current vs latest, download button
- 💡 `SettingsWindow.axaml` — dedicated window with tabs: General, API Keys, Profile, Advanced

### 7.2 UI Wiring Audit

- 💡 Audit all `Button`, `MenuItem`, `MenuFlyout` items — confirm every Command is wired
- 💡 Implement: Queue track, Add to playlist, Open file location, Copy track info, clipboard support
- 💡 Wire "About NullWave" → `AboutWindow`; "Check for updates" → `UpdateWindow`

### 7.3 Code Splitting

- 💡 `MetadataService` → `YouTubeMetadataFetcher`, `LastFmMetadataFetcher`, `LocalMetadataFetcher`
- 💡 `TrackSource` enum → `Models/TrackSource.cs`
- 💡 `Converters/` folder for all `IValueConverter` implementations
- 💡 File size limit: no file > 400 lines

### 7.4 Additional Features

- 💡 Playlist CRUD — create, rename, delete; drag tracks into playlists
- 💡 Queue system — play next, play later
- 💡 Last.fm scrobble (track > 50% played)
- 💡 Theme switcher — light / dark / accent color picker
- 💡 Global keyboard shortcuts — play/pause, next/prev, search focus
- 💡 Export playlist (M3U format)
- ✅ SQLite database persistence — `DatabaseService` + `TrackRecord`, fully wired into `LibraryService`

---

## Architecture Guidelines

### File size limits

| File type | Soft limit | Hard limit |
|-----------|-----------|-----------|
| ViewModel | 300 lines | 400 lines |
| Service | 300 lines | 500 lines |
| View (.axaml) | 200 lines | 300 lines |
| View code-behind | 50 lines | 100 lines |

### Naming conventions

- ViewModels: `{Name}ViewModel.cs` in `ViewModels/`
- UserControls: `{Name}View.axaml` + `{Name}View.axaml.cs` in `Views/Controls/`
- Services: `{Name}Service.cs` in `Services/`
- Models: `{Name}.cs` in `Models/`
- Converters: `{Name}Converter.cs` in `Helpers/Converters/`
- Theme files: `{Category}.axaml` in `Themes/`


### Branch strategy

```
main                  ← always stable, builds clean
├── refactor/*        ← structural changes, no new features
├── feature/{n}-*     ← new features per phase number
└── fix/*             ← bug fixes, can target any branch
```

---

## Dependency Versions

| Package | Version | Notes |
|---------|---------|-------|
| .NET | 8.0 | LTS |
| Avalonia | 12.0.4 | |
| LibVLCSharp | 3.9.7.1 | Fedora: symlinks to /usr/lib required |
| TagLib# | 2.3.0 | |
| Serilog | latest stable | + Serilog.Filters.Expressions |
| sqlite-net-pcl | 1.9.172 | Packages installed, implementation next |
| SQLitePCLRaw.bundle_green | 2.1.11 | Native SQLite provider for Linux |
| CommunityToolkit.Mvvm | latest stable | RelayCommand, ObservableProperty |

---