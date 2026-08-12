# Changelog

All notable changes to NullWave will be documented in this file.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

---

## [0.5.0] - 09-Aug-2026 — "Blue Orchid"

### Added
- **Active Playlist Context**: Playback now continues through the active playlist even if the queue is cleared.
- **Slim Scrollbars**: Quiet, minimal scrollbars that reveal on hover.
- **About tab redesign** - gradient hero, version chip (tap 7x for a surprise), "What's new", clickable tech credits, GitHub/issue/license links.
- **Staged self-updater** - in-app download + restart-to-install on both OSes.
- **Live Theme Engine ("Blue Orchid")** - new singleton `ThemeService` applies accent,
  font scale, row density and sidebar width at runtime with zero restarts.
  `Themes/Colors.axaml` brushes now reference colors via `DynamicResource`, so the
  whole app re-skins instantly.
- **Signature edition + Accent Duos** - Appearance tab redesigned: "Blue Orchid"
  signature card (azure + orchid), 9 named base colors, and 10 named complementary
  **Accent Duo** pairs rendered as 50/50 split swatches. Segmented chips replace
  dropdowns for font scale and sidebar width.
- **Density system** - new tokens `TrackRowHeight`, `RowArtSize`, `RowTitleSize`,
  `RowSubSize`, `MiniArtSize`, `RowMargin`, `NavPadding`; Comfortable / Compact /
  Cozy row-style cards plus a global **Compact mode** that shrinks rows, art, nav
  padding and the mini-player bar together.
- **Gradient rollout** - `BrushAccentGradient` (dynamic stops) now drives the seek
  and volume sliders, progress bars and the now-playing accent bar.
- **Artist panel upgrade** - Track Detail shows the Last.fm artist photo with
  placeholder-hash detection (`2a96cbd8…`) and album-art fallback, plus a
  "Fans Also Like" similar-artists row.
- **Windows Support** - Conditional `VideoLAN.LibVLC.Windows` package and
  `&lt;RollForward&gt;Major&lt;/RollForward&gt;` for .NET 10 compatibility; `NullWavePaths`
  stores data under `%APPDATA%\NullWave`; `DependencyUpdateService` installs/updates
  yt-dlp and VLC via `winget`; `HardwareDetector` falls back to PowerShell/CIM on
  Windows; native libVLC discovery for Windows install locations.
- **Config hygiene** - `cspell.json` and `.markdownlint.json` updated for the new
  vocabulary (duotone, swatch, orchid, extralarge, …).
- **AI Playlist Persistence**: `ai:` search prompts now create real, persisted playlists in a dedicated "AI Playlists" folder.
- **Playlist Folders**: Added `PlaylistFolderRecord` and `CreateFolderDialog` for organizing playlists.
- **Offline Indicator**: Track list now shows an offline-ready checkmark for locally available tracks.
- **Plugin Feedback**: Toggling plugins in Settings now triggers immediate success/warning toasts.

### Fixed
- **Instant Sidebar Refresh**: Deleted playlists and folders now disappear from the sidebar immediately (no reboot required).
- **Live Play Counts**: `PlayCount` and `LastPlayed` now update live in the UI via `LibraryChanged` events.
- **Ghost Pin Purging**: Pinned playlists that are deleted are now automatically removed from preferences during sidebar rebuilds.
- **Unified Context Menus**: The 3-dot menu and right-click context menus now have feature parity (both include "Add to Playlist").
- **Dynamic version** display from `.csproj` (About tab, startup log, updater).
- **Track Detail/Queue right panel locked** to 320px, mutually exclusive, non-resizable.
- **Mini-player marquee** - long track titles now scroll: the title is measured at
  full width inside a clipped horizontal `StackPanel`, so overflow detection works.
- **Seek bar duration label** - `PlayerViewModel.Position` now also notifies
  `DurationDisplay`, fixing the right-hand label frozen at `00:00`.
- **Queue Oscillation**: Fixed critical bug where tracks would bounce back and forth due to history stack corruption in `PlaybackNavigator`.
- **Crossfade Stability**: 
  - Fixed queue desync causing track oscillation during crossfade.
  - Added safety delay in `PlaybackService` to prevent native PipeWire segfaults on Linux during crossfade teardown.
- **Smart Shuffle Loop**: Added candidate pool floor to prevent 2-track loops under heavy skip pressure.
- **Add to Queue**: Fixed bug where command ignored the passed track parameter and used `SelectedTrack` instead.
- **Right-panel layout collapse** - `MainViewModel.ActiveRightPanelWidth` now returns
  a `GridLength` (320 open / 0 closed). The previous broken binding silently fell
  back to `1*`, leaving a huge black void and an oversized Track Detail/Queue panel.
  Panels stay mutually exclusive and non-resizable by design.
- **VerifyLinks precision** - matcher normalizes artist prefixes
  ("Eminem - X" vs "X"); decoration-aware false-positive tuning tracked in ROADMAP 9.1d.

### Changed
- **Queue System**: Refactored to use `QueueEntry` model, distinguishing between manually added and auto-filled tracks.
- **Roadmap**: Retroactively added Phase 12 (Navigation Redesign) and Phase 14 (Stability & Polish) to reflect shipped work.
- Appearance tab is now a **live** control surface (previously save-only).
- `Preferences.AccentColor` default is now `Blue Orchid`.
- Docs: README bumped to v0.5.0 "Blue Orchid"; ROADMAP gained Phase 15.


## [0.4.2] - 19-Jul-2026

### Added
- **Smart Search Syntax**
  - `LibraryViewModel.ApplySmartSearch` - key:value query syntax: `artist:`/`a:`,
    `title:`/`t:`, `source:`/`s:`, `tag:`/`genre:`, `is:favorite`/`fav`, combined
    with bare global terms (OR-matched against Title/Artist).
  - Multi-word value support via a `pendingKey`/`pendingValueParts` accumulator,
    so `artist:tame impala` parses as a single filter instead of splitting on
    whitespace.
  - Negation support: `-word` (bare exclusion) and `-key:value` (negated filter,
    e.g. `-artist:eminem`).
- **Toolbar Redesign** (`Views/Controls/TrackListView.axaml`)
  - Search box: magnifying-glass icon, inline "×" clear button, and a "?" help
    flyout documenting the smart search syntax.
  - Sort-direction toggle button next to the sort dropdown.
  - Sort dropdown now shows human-readable labels ("Date Added" instead of
    "DateAdded") via new `SortFieldDisplayConverter`.
  - Column headers (Title/Artist, Source, Plays, Added) are now clickable sort
    triggers with a direction-arrow indicator; clicking the active column
    flips sort direction.
  - Track-count label ("415 tracks") added to the header row.
  - "+ Add Track" redesigned as a self-contained flyout (URL input + Add button
    - local file/folder options), replacing the old always-visible "URL INPUT"
    row. Add button gated on new `TrackInputViewModel.IsInputUrlValid`.
- `Helpers/Converters/SortFieldDisplayConverter.cs` and `BoolToSortIconConverter.cs`

### Changed
- `LibraryService`/`LibraryViewModel` sorting now applies a secondary `.ThenBy`
  tie-breaker per `SortField` (mostly by Title), so equal-value groups (e.g.
  many tracks with `PlayCount == 0`) render in a stable, predictable order.
- `TrackInputViewModel.InputUrl` setter now also notifies `IsInputUrlValid`.

### Fixed
- `LibraryViewModel.FetchLibraryDataInternal` - search was silently ignored
  whenever a sidebar source filter (e.g. "YouTube") was active, because the
  `LibraryView.Source` branch never applied the query at all. Also fixed a
  secondary bug where `FilterBySource` results were returned completely
  unsorted regardless of the chosen `SortField`.
- `Views/MainWindow.axaml.cs` - `OnKeyDown`'s typing guard (`e.Source is
  TextBox`) only matched the exact source type, but Avalonia's `TextBox` is
  templated - the real typing source is an internal `TextPresenter` - so the
  check silently failed and `M`/`N`/`Space` fired as global hotkeys (mute,
  next track, play/pause) while typing in the search box. Fixed by walking
  the visual tree from `e.Source` to check for any ancestor `TextBox`.
- `Views/Controls/TrackListView.axaml` - toolbar `Grid.ColumnDefinitions`
  didn't declare enough columns for the controls actually placed in it
  (Avalonia silently tolerates out-of-range `Grid.Column` values rather than
  erroring), leaving the layout fragile against future changes.

### Removed
- `ViewModels/Playlists/PlaylistImportViewModel.cs` - constructed and wired
  into `TrackInputViewModel`, but its one real method (`ImportPlaylist`) had
  no call sites anywhere in the app; the actual playlist-download flow has
  run through `MainViewModel`'s `DownloadPlaylistAsync` wiring since v0.4.0.
- `Views/Controls/ImportProgressView.axaml` (+ code-behind) - dead markup
  left over from before the toast-based live activity system replaced inline
  progress bars; also removed its stale reference from `MainWindow.axaml`.


## [0.4.1] - 30-Jun-2026

### Added
- **Robust Playlist Import Engine**
  - `DownloadService.DownloadPlaylistAsync` now features advanced metadata parsing, extracting `artist`, `creator`, `uploader`, and `channel` from `yt-dlp` `--flat-playlist` JSON with strict priority fallback.
  - Automatic stripping of YouTube's " - Topic" suffix from official channel names during playlist parsing.
  - Fallback to `TrackTitleParser` to split messy "Artist - Title" video names into clean, separate metadata fields.
  - Real-time enrichment via `onTrackReady` callback: tracks are linked to their local `FilePath` and passed to `LastFmEnrichmentService` immediately upon individual download completion.
  - Rate limit protection: randomized 3-8 second throttling between playlist track downloads to prevent YouTube 403 rate-limit errors.
- **Database & Library Synchronization**
  - `DatabaseService` now manages `PlaylistRecord` and `PlaylistTrackRecord` tables for full playlist persistence.
  - `PlaylistService` handles full CRUD operations, including track addition, removal, and reordering.
  - `LibraryService` now fires `LibraryChanged` events on `Add`, `Remove`, and `Update`, ensuring the UI reflects database changes instantly.
  - Startup backfillers for missing YouTube and SoundCloud thumbnails.
  - Maintenance utilities: `RepairPaths`, `ReimportAssets`, `ClearTagsForReSync`, and `ClearAllArt`.
- **UI/UX & Theming (`ControlStyles.axaml`)**
  - Comprehensive Avalonia styling system using centralized static resources.
  - New button variants: `.danger`, `.nav`, `.icon-btn`, `.player-btn` (Spotify-style borderless), `.primary`, `.secondary`, and `.ghost` with smooth hover/press transitions.
  - Custom `ToggleSwitch` template with smooth thickness and brush transitions.
  - Layout components: `.settings-card`, `.help-btn`, `.section-label`, and `.BottomSpacer`.
  - Polished `ListBox`, `TextBox`, `ProgressBar`, `Slider`, `ComboBox`, and `TabControl` styles.

### Changed
- `DownloadService` now blocks playlist URLs in the single-track `DownloadAsync` pipeline to prevent duplicate/rogue download processes.
- `DownloadCompleted` and `DownloadFailed` events now include an `isInteractive` boolean flag to distinguish between user-initiated single downloads and background playlist imports.
- `PlayerViewModel` safely ignores non-interactive download events, preventing background playlist imports from hijacking the active player UI, triggering false error toasts, or interrupting currently playing music.
- Accurate error logging: fixed misleading error logs by correctly mapping the first event argument to `trackId` instead of `url` in failure handlers.

### Fixed
- Concurrency race condition where the single-track pipeline hijacked playlist URLs, causing duplicate downloads and rate-limit throttling.
- Background playlist downloads interrupting active playback and flooding the UI with error toasts for unavailable videos.
- Playlist tracks appearing in the library with "Unknown Artist" and missing album art due to incomplete flat-playlist JSON metadata.
- Tracks not being playable immediately after playlist download due to `FilePath` not being written back to the database.


---

## [0.4.0] - 21-Jun-2026

### Added
- **Smart Sorting (Local AI + Weather)**
  - `Services/SmartSorting/HardwareDetector.cs` - detects CPU cores, RAM, and GPU VRAM (Nvidia/AMD) to recommend the optimal local Ollama model.
  - `Services/SmartSorting/LocalAIService.cs` - integrates with local Ollama instance to rank tracks based on mood and weather.
  - `Services/SmartSorting/MoodPlaylistService.cs` - orchestrates weather fetching, tag mapping, library filtering, and AI ranking.
  - `Services/SmartSorting/WeatherService.cs` - OpenWeather API integration with 1-hour caching.
  - `Services/SmartSorting/WeatherMoodMap.cs` - maps weather conditions and time of day to real Last.fm community tags.
  - Settings UI for Smart Sorting: hardware detection, model download progress, and location coordinates.
- **Last.fm Enrichment & Album Art**
  - `Services/Integration/LastFmEnrichmentService.cs` - automatically backfills missing tags and album art for untagged tracks on startup.
  - `Services/Integration/AlbumArtService.cs` - unified fallback chain for album art (YouTube → SoundCloud → Last.fm → Placeholder).
  - `Services/Metadata/TrackTitleParser.cs` - cleans messy YouTube titles (strips "Official Video", "ft.", etc.) for accurate Last.fm queries.
- **UI & Navigation**
  - `Helpers/Converters/StringEqualsConverter.cs` & `TrackIdEqualsConverter.cs` - new converters for robust page routing and now-playing indicators.
  - "Now Playing" accent bar added to track rows in `TrackListView` and `PlaylistsView`.
  - OpenWeather API key support added to the encrypted `KeyStore`.

### Changed
- `MainWindow.axaml` - replaced `ContentControl` page routing with direct `IsVisible` bindings to fix `DataContext` inheritance bugs.
- `MainViewModel` - removed `CurrentPageViewModel`; navigation now relies solely on the `CurrentPage` string property.
- `App.axaml` - removed `Application.DataTemplates` section as views are now declared directly in `MainWindow`.
- API keys in Settings now auto-save to the encrypted keystore immediately upon typing (no manual "Save" button required).
- `MetadataService` refactored to use `TrackTitleParser` for cleaner Last.fm fallback searches.

### Fixed
- Library filter tabs (YouTube, SoundCloud, etc.) not displaying filtered content due to `ContentControl` `DataContext` reset.
- Search and URL input bar bindings breaking when switching between filtered library views.
- Sidebar filter buttons not visually highlighting when active (Favorites, Recent, Sources).
- "Now playing" row indicator getting lost when switching between filtered library views.

## [0.3.1] - 14-Jun-2026

### Added
- `Services/Metadata/ThumbnailDownloader.cs` - shared static helper for
  downloading and caching remote thumbnails to `~/.nullwave/art/`
- `Helpers/Converters/SourceToBackgroundConverter.cs` - maps `TrackSource`
  to per-source badge colors (YouTube red, SoundCloud orange, etc.)
- `LibraryService.BackfillYouTubeThumbnails()` - on startup, fetches missing
  YouTube thumbnails for existing tracks via `img.youtube.com`
- `LibraryService.BackfillSoundCloudThumbnails()` - on startup, fetches
  missing thumbnails and corrects stale metadata for SoundCloud tracks

### Fixed
- YouTube thumbnails not showing - `_lastFetchedThumbnail` now cached in
  `TrackInputViewModel` and applied when track is added
- SoundCloud thumbnails not showing - fetched via `yt-dlp --print thumbnail`
  and saved to art cache on add and on startup backfill
- `TrackDetailViewModel` field name mismatch (`_track` vs `_currentTrack`)
  caused `IsFavorite` to always return false
- Favorite star always gold in miniplayer and track detail panel -
  `Opacity` now bound to `IsFavorite` via `BoolToOpacityConverter`
- Track detail panel resizing with song title length - fixed `Width="320"`
- `LibraryService.Update()` now syncs in-memory list after DB write
- `TrackDetailView` clipboard copy now uses `IClipboard.SetTextAsync()`
  correctly with `using Avalonia.Input.Platform`

### Changed
- `MetadataService.FetchFromUrlAsync` return type extended to include
  `ThumbnailPath` - all call sites updated
- Source badges in `TrackListView` and `TrackDetailView` now colored
  per source instead of generic accent color
- Services reorganized into subfolders: `Audio/`, `Download/`, `Library/`,
  `Security/`, `System/`, `Integration/`, `Metadata/`


## [0.3.0] - 13-Jun-2026

### Added
- `Views/Settings/AppearanceTab.axaml` - new Appearance settings tab with:
  - Accent color picker (Purple / Blue / Amber / Green / Red) - saved, live theming in v0.4
  - Track row style selector (Comfortable / Compact / Cozy) with visual previews
  - Font scale selector (Small / Medium / Large)
  - Sidebar width preset (Narrow / Normal / Wide)
  - Compact mode toggle (hides album art thumbnails)
- `Services/PreferencesService.cs` - JSON persistence for all General and Appearance settings; auto-saves on every change
- `Models/Preferences.cs` - `AccentColor`, `TrackRowStyle`, `FontScale`, `CompactMode`, `SidebarWidth` fields
- `Services/PlaybackNavigator.cs` - extracted shuffle/repeat/queue navigation logic from `PlayerViewModel`
- `Services/Metadata/YouTubeMetadataFetcher.cs` - split from `MetadataService`
- `Services/Metadata/SoundCloudMetadataFetcher.cs` - split from `MetadataService`
- `Services/Metadata/LocalMetadataFetcher.cs` - split from `MetadataService`
- `ViewModels/PlaylistImportViewModel.cs` - extracted playlist import state from `TrackInputViewModel`
- `Themes/ControlStyles.axaml` - `TabItem` styles extracted from `SettingsWindow` inline block
- `Themes/ControlStyles.axaml` - `.secondary` button class added

### Changed
- `SettingsWindow.axaml` - removed clipping footer, switched to `DockPanel`, tab order updated (General → Appearance → API Keys → Audio → Updates → Advanced → About)
- `ApiKeysTab.axaml` - Save button moved from global footer into tab, per-field ✓ saved indicator
- `ImportProgressView.axaml` - hardcoded hex colors replaced with theme brushes, added `x / total` counter
- `ConfirmDialog.axaml` - hardcoded hex colors replaced with theme brushes, buttons use `secondary`/`danger` classes
- `TrackListView.axaml` - playlist import bindings updated to `Input.PlaylistImport.*`
- `SidebarView.axaml` - indentation normalized
- `MetadataService.cs` - refactored to thin orchestrator, delegates to fetcher classes
- `PlayerViewModel.cs` - shuffle/repeat/navigation delegated to `PlaybackNavigator`
- `SettingsViewModel.cs` - appearance properties wired to `PreferencesService`

### Fixed
- Settings window content clipping at bottom - `Grid` with fixed `RowDefinitions` replaced with `DockPanel`
- Playlist import progress bar bindings broken after `PlaylistImportViewModel` extraction

## [0.2.1] - 07-Jun-2026

### Added
- `ControlStyles.axaml` - `.player-btn` style: Spotify-inspired borderless player buttons, no background box, subtle hover only
- `.player-btn.play` - filled white circle for play/pause, dark icon inside
- `PlayerViewModel` - `ShuffleForeground` property: accent color when shuffle is on, muted when off
- `PlayerViewModel` - `RepeatForeground` property: accent color when repeat is active, muted when off
- `PlayerViewModel` - `ToggleMuteCommand` with volume memory (`_volumeBeforeMute`)
- `PlayerViewModel` - `ToggleCurrentFavoriteCommand` - favorite toggle for currently playing track
- `PlayerViewModel` - `VolumeIcon` property - switches between muted/unmuted glyph
- `PlayerViewModel` - `IsCurrentFavorite` property bound to current track
- `MiniPlayerView` - favorite star button for current track in left section
- `MiniPlayerView` - mute toggle button replaces static "vol" label
- `DatabaseService` - SQLite-net backed storage: `LoadAll`, `Insert`, `Update`, `Delete`
- `TrackRecord` - flat SQLite-mapped model, tags serialized as pipe-separated string
- `LibraryService` - DB-backed: persists on `Add`, `Remove`, `Update`, `ToggleFavorite`, `RecordPlay`
- `LibraryService` - `BackfillAlbumArt()` on startup: extracts embedded art for existing tracks
- `MetadataService` - `ExtractAlbumArt()`: extracts embedded ID3 art, caches to `~/.nullwave/art/`
- `Helpers/Converters/FilePathToBitmapConverter.cs` - converts file path string to `Bitmap` for Avalonia `Image`
- `TrackDetailViewModel` - `CurrentTrackArtPath` property, refreshed via `RefreshDisplayProperties()`
- `TrackDetailViewModel` - `Save()` now calls `_library.Update()` to persist edits to SQLite
- `PlaybackService` - re-applies volume on `Playing` event to fix silent-start bug on LibVLC pipeline init
- `MainWindow` - keyboard shortcuts: Space (play/pause), ←/→ (seek ±5s), M (mute), N (next), P (previous)

### Fixed
- Track list thumbnails not showing - `Image.Source` now uses `FilePathToBitmapConverter` instead of raw string binding
- Track detail panel art not showing - same converter applied to `Detail.CurrentTrackArtPath`
- Miniplayer album art not showing - converter applied to `Player.AlbumArtPath`
- Tracks loaded from DB had no album art - `BackfillAlbumArt()` extracts and saves on startup
- Remove track not working - `RemoveTrackCommand` now accepts `Track` parameter, context menu passes `CommandParameter="{Binding}"`
- Silent start on playback - volume re-applied after LibVLC pipeline initializes
- Shuffle/repeat buttons gave no visual feedback - now use accent color when active

### Changed
- Miniplayer buttons fully redesigned - Spotify-style `.player-btn` class replaces `.icon-btn`
- Miniplayer layout switched from `StackPanel` to two-row `Grid` center section - eliminates vertical clipping
- Miniplayer fixed `Height="90"`, fixed center `Width="440"` - consistent layout at all window sizes
- `LibraryService` constructor now accepts optional `MetadataService` for art extraction
- `MainViewModel` passes `_metadata` to `LibraryService` constructor

## [0.2.0] - 06-Jun-2026

### Added
- `Themes/Shapes.axaml` - `RadiusArt` (6px) corner radius for track art thumbnails
- `Themes/ControlStyles.axaml` - `.danger` button class (red border, fills on hover)
- `Themes/ControlStyles.axaml` - `ListBoxItem` opacity transition (150ms fade-in on add)
- `MiniPlayerView` - shuffle toggle (`IsShuffle`, `ShuffleIcon`, `ToggleShuffleCommand`)
- `MiniPlayerView` - repeat mode cycle: None → All → One (`CycleRepeatCommand`, `RepeatIcon`)
- `MiniPlayerView` - seek slider now fires only on `PointerReleased` (no feedback loop)
- `MiniPlayerView` - `-5s` / `+5s` seek buttons replacing broken Unicode glyphs
- `MiniPlayerView` - volume slider restored (`Mode=TwoWay` on `Player.Volume`)
- `PlayerViewModel` - `RepeatMode` enum (None/One/All), shuffle with random non-repeat pick
- `PlayerViewModel` - `OnTrackFinished()` wired: autoplay next, repeat-one replay, repeat-all wrap
- `PlayerViewModel` - `SeekTo(float)` public method called from code-behind on seek release
- `PlayerViewModel` - `PlaySelectedTrackRequested` event - play button starts selected track when nothing loaded
- `TrackDetailViewModel` - subscribes to `Track.PropertyChanged` so `PlayCount` and `LastPlayed` update live
- `TrackListView` - unified toolbar: search + sort `ComboBox` + `SplitButton` (`+ Add` with dropdown)
- `TrackListView` - collapsible URL input row toggled by `Input.ShowUrlInputCommand`
- `TrackListView` - URL input row has `Opacity` transition (200ms fade)
- `TrackDetailView` - `Opacity` + `Margin` transitions on slide-in panel
- `TrackInputViewModel` - `IsUrlInputVisible` + `ShowUrlInputCommand` for toggling URL row
- `TrackInputViewModel` - `AddTrack()` auto-detects local file path, folder path, or HTTP URL
- `TrackInputViewModel` - `AddFolderPathAsync()` recursively imports audio files from a folder
- `TrackInputViewModel` - `StatusMessage` property for live import feedback
- `TrackInputViewModel` - playlist URL auto-detection (`IsPlaylistUrl`) triggers `ImportPlaylistAsync`
- `DownloadService` - `IsPlaylistUrl()` static helper (detects `list=`, `/sets/`, `/playlist/`)
- `DownloadService` - `DownloadPlaylistAsync()` - fetches flat metadata via `--flat-playlist --dump-json`, then downloads each track individually with per-track progress callbacks
- `SidebarView` - 32×32 logo placeholder ("N" in accent square) beside NullWave wordmark
- `MainViewModel` - wires `Player.PlaySelectedTrackRequested` → play selected or first track
- SQLite-net-pcl + SQLitePCLRaw.bundle_green packages added (persistence implementation coming next)

### Fixed
- Seek slider feedback loop - `Mode=TwoWay` → `Mode=OneWay`, seek fires on pointer release only
- Volume slider broken after previous refactor - binding and converter references cleaned up
- Play button on miniplayer did nothing when no track was loaded - now starts selected track
- `PlayCount` and `LastPlayed` in detail panel never updated after playback - fixed via INPC subscription
- `AddTrack()` fired with empty inputs showing no feedback - now toggles URL input row instead
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
- `Themes/` folder - design system split into four files:
  - `Colors.axaml` - full color palette (dark blue, purple accent, surface layers) + all brushes
  - `Typography.axaml` - type scale xs→3xl with semantic aliases (Label, Body, Title, Heading)
  - `Shapes.axaml` - corner radii, spacing tokens, dimension constants (sidebar width, player height, avatar sizes)
  - `ControlStyles.axaml` - all reusable styles: nav/icon-btn/primary/ghost buttons, ListBoxItem, TextBox, ProgressBar, Slider, Menu, ScrollBar
- `App.axaml` reduced to thin shell - merges theme files via `ResourceInclude` and `StyleInclude`
- `Helpers/NullWavePaths.cs` - single source of truth for all `~/.nullwave/*` paths; `EnsureDirectories()` called at startup
- `Helpers/Logging/NullActionLogger.cs` - static structured logger for user actions and attributed errors
- `Helpers/Logging/NullWaveLogConfig.cs` - Serilog configuration with three separate file sinks (System, UserActions, Errors)
- `Services/StartupDiagnosticsService.cs` - logs structured startup block: version, runtime, OS, library load time, API key status, connectivity check, VLC + yt-dlp versions
- `ViewModels/UserProfileViewModel.cs` - local profile (username, bio, avatar), persisted to `~/.nullwave/profile.json`, no auth required
- `Program.cs` updated - `EnsureDirectories()` + `NullWaveLogConfig.Initialize()` called before anything else; top-level exception catch + `CloseAndFlush()` on exit
- `MainViewModel` - `IsMenuBarVisible` + `ToggleMenuBar()` for Alt-key toggle; `Profile` child ViewModel; startup diagnostics wired
- `PlayerViewModel` - `AlbumArtPath` / `HasAlbumArt` properties (Phase 6 placeholder); all playback actions emit structured log entries
- `MainWindow.axaml.cs` - `OnKeyDown` handler toggles menu bar on `Alt` press (Firefox-style)
- `SidebarView.axaml` - Discord-style local profile bar at bottom (avatar circle, username, bio, gear → Settings)
- `MiniPlayerView.axaml` - Spotify-style 3-column layout: track info + art thumbnail left, controls + progress center, volume slider right
- `MenuBarView.axaml` - hidden by default, shown/hidden via Alt key

### Changed
- All hardcoded hex colors replaced with `{StaticResource Brush*}` tokens
- All hardcoded font sizes replaced with `{StaticResource FontSize*}` tokens
- All hardcoded corner radii replaced with `{StaticResource Radius*}` tokens
- `ImportProgressView` shows track count fraction (x / total) alongside status text

### Fixed
- `MenuBarView.axaml` XAML parse errors - missing spaces between attributes
- `PlayerViewModel.AlbumArtPath` stored on ViewModel directly, not read from Track model

---

## [0.1.2] - 29-May-2026

### Added
- LibVLCSharp local file playback (play/pause/stop/seek/volume)
- yt-dlp download integration (audio download to `~/.nullwave/downloads/`)
- `PlayerViewModel` - mini player bar with track display, status, download progress
- `DownloadService` - wraps yt-dlp as process, parses progress, fires completion events
- `PlaybackService` - wraps LibVLCSharp MediaPlayer with event-driven state
- `TrackDetailViewModel` - sliding right panel (0→320px animated) with editable fields
- `ImportViewModel` - bulk folder import with progress bar and subfolder dialog
- `ConfirmDialog` - reusable Yes/No dialog
- `BoolToOpacityConverter` - favorite star opacity (full/dim)
- Play command in ⋮ context menu per track row
- Mini player bar wired to `PlayerViewModel`

### Changed
- `MainViewModel` wired: `Library.PlayTrackRequested` → `Player.PlayTrack`
- `MainWindow.axaml` fully rewritten with sidebar, detail panel, floating + button
- Track rows now show stacked Title+Artist, play count, inline ⭐, ⋮ menu
- Material.Avalonia removed (incompatible with Avalonia 12.0.3), pure custom styles used

### Fixed
- libVLC not found on Fedora (`/usr/lib64`) - symlinks + ldconfig config added
- `BoolConverters.ToObject` unavailable in Avalonia 12 - replaced with custom converter
- `FilterLastFmCommand` declared twice - duplicate removed

### Security
- API key redaction in logs verified working
- KeyStore encryption confirmed operational

---

## [0.1.1] - 28-May-2026

### Added
- YouTube Data API v3 real metadata fetching (title + channel name)
- Encrypted local API key storage (AES-256-GCM, machine-bound via `/etc/machine-id`)
- `KeyStoreService` - secure read/write/delete of API keys to `~/.nullwave/keys.enc`
- `SecureDeleteService` - 3-pass secure wipe for keys, logs, and full data
- `ConfigService` - reads from KeyStore, falls back to environment variables
- `SettingsViewModel` wired to KeyStore and SecureDelete
- Log redaction enricher - masks API key patterns in all log output
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
- 23 unit tests (xUnit) - all passing