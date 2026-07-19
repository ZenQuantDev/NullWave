# NullWave - Roadmap

> Last updated: 19-Jul-2026

---

## Legend

| Symbol | Meaning |
|--------|---------|
| ✅ | Done - merged to main |
| 🔄 | In progress - active branch |
| 🔜 | Up next - ready to start |
| 📋 | Planned - scoped, not started |
| 💡 | Future - idea, not yet scoped |

---

## Phase 1 - Core Foundation ✅

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

## Phase 2 - MVVM + UI Shell ✅

**Goal:** Full MVVM wiring with sidebar, track list, detail panel, and mini player.

- ✅ `MainViewModel` orchestrating all child ViewModels
- ✅ `LibraryViewModel` - sort, filter, search, favorites, PlayTrackRequested event
- ✅ `PlayerViewModel` - PlayTrack, PlayPause, Stop, PlayPauseIcon binding
- ✅ `TrackDetailViewModel` - slide-in panel, editable Title/Artist/tags/notes
- ✅ `ImportViewModel` - progress strip, ImportCompleted → Library.Refresh
- ✅ `BoolToOpacityConverter` for star favorite display
- ✅ Right-click context menu via `ListBox.ItemContainerTheme`
- ✅ ⋮ button `MenuFlyout` with `CommandParameter="{Binding}"`

---

## Phase 3 - Refactor / Split Views ✅

**Goal:** Thin MainWindow shell; each panel is its own UserControl.

- ✅ `MenuBarView.axaml` - extracted top menu bar (hidden by default, Alt-toggle)
- ✅ `SidebarView.axaml` - nav, filters, sources, Discord-style profile bar at bottom
- ✅ `TrackListView.axaml` - search bar, ListBox, ContextMenu, import progress
- ✅ `TrackDetailView.axaml` - 0→320px `DoubleTransition` slide panel
- ✅ `MiniPlayerView.axaml` - Spotify-style 3-column bar (art, controls, volume)
- ✅ `ImportProgressView.axaml` - import progress strip with fraction counter
- ✅ MainWindow reduced to thin DockPanel shell with Alt-key handler
- ✅ Bump Avalonia 12.0.3 → 12.0.4
- ✅ Merge `refactor/split-views` → `main`

**Notes:**
- `TrackSource` enum should be moved to `Models/TrackSource.cs`
- `MetadataService` is a candidate for splitting (see Phase 7)

---

## Phase 4 - Advanced Logging ✅

**Branch:** `feature/4-advanced-logging`
**Goal:** Every action logged, every startup state visible, every error attributed to its source.

- ✅ `NullWavePaths.cs` - single source of truth for all `~/.nullwave/*` paths
- ✅ `NullActionLogger.cs` - static helper: `User()`, typed convenience methods, `Error()`, `StartupLine()`
- ✅ `NullWaveLogConfig.cs` - three Serilog sinks: System, UserActions, Errors (channel-filtered)
- ✅ `StartupDiagnosticsService.cs` - logs version, runtime, OS, library load time, API key status, connectivity, VLC + yt-dlp versions
- ✅ `PlayerViewModel` - all playback events emit structured `NullActionLogger` calls
- ✅ `MainViewModel` - exit, settings, navigation all logged
- ✅ `Program.cs` - `EnsureDirectories()` + log init as first operations; `CloseAndFlush()` on exit
- ✅ Separate log files: `NullWave-*.log`, `UserActions-*.log`, `Errors-*.log`

**Remaining (wire into other ViewModels):**
- 📋 `ImportViewModel` - add `NullActionLogger.ImportStarted/Completed/Failed` calls
- 📋 `LibraryViewModel` - add `TrackAdded`, `TrackRemoved`, `FavoriteToggled`, `SearchPerformed`
- 📋 `TrackDetailViewModel` - add `TrackEdited` on save
- 📋 `PlaylistViewModel` - add `PlaylistCreated`, `PlaylistDeleted`, `PlaylistTrackAdded`
- 📋 `SettingsViewModel` - add `SettingChanged` on key save

---

## Phase 5 - UI Redesign ✅

**Branch:** `feature/5-ui-redesign`
**Goal:** Visual polish - proper color scheme, icon library, larger type, UI depth, Alt menu toggle, local profile bar.

- ✅ `Themes/Colors.axaml` - dark navy/purple palette, all brushes defined
- ✅ `Themes/Typography.axaml` - xs→3xl type scale, semantic aliases
- ✅ `Themes/Shapes.axaml` - radii, spacing tokens, dimension constants
- ✅ `Themes/ControlStyles.axaml` - nav/icon-btn/primary/ghost buttons, ListBoxItem, TextBox, ProgressBar, Slider, Menu, ScrollBar
- ✅ `App.axaml` - thin shell, merges all theme files
- ✅ Alt-key menu bar toggle (Firefox-style, `MainWindow.axaml.cs`)
- ✅ Discord-style local profile bar in `SidebarView` (avatar, username, bio, gear → Settings)
- ✅ `UserProfileViewModel` - loads/saves `~/.nullwave/profile.json`, avatar picker
- ✅ Spotify-style `MiniPlayerView` - 3-column, art thumbnail, controls, volume slider
- ✅ All hardcoded colors/sizes/radii replaced with theme tokens

**Remaining:**
- 📋 Replace remaining emoji in nav buttons with proper SVG icon set (`Assets/Icons/Icons.axaml`)
- 📋 `OpacityTransition` on menu bar show/hide (150ms fade)
- 📋 Profile edit UI (inline in sidebar or dedicated Settings tab)
- ✅ `AppearanceTab` - accent color, row style, font scale, sidebar width, compact mode
- ✅ `PreferencesService` - auto-saves all General + Appearance settings to `~/.nullwave/prefs.json`
- 📋 Wire appearance settings to actual UI (live accent color, row height, font scale)
- ✅ Logo placeholder in `SidebarView` - 32×32 accent square beside wordmark
- 📋 Replace placeholder with real SVG logo (`Assets/Icons/logo.svg`)
- 📋 `TrackListView` - apply theme tokens, larger row height, better typography
- 📋 `TrackDetailView` - apply theme tokens

---

## Phase 6 - Now Playing Redesign 🔄

**Branch:** `feature/6-now-playing`
**Goal:** Spotify-inspired Now Playing panel with album art and blur background.

### 6.1 Album Art Fetching

### 6.1 Album Art Fetching
- ✅ YouTube thumbnail fetching via `img.youtube.com` (no API key required)
- ✅ SoundCloud thumbnail fetching via yt-dlp
- ✅ Startup backfill for existing tracks missing thumbnails
- ✅ `AlbumArtService` - unified art fetching with priority chain (YouTube → SoundCloud → Last.fm → Placeholder)
- ✅ `TrackTitleParser` - shared helper to clean messy YouTube titles for accurate Last.fm queries
- 📋 Cache key: `SHA256(Artist + Album)` truncated to 16 chars (currently uses URL/ID hashing)
- 📋 Fallback: `Assets/placeholder-art.png`

### 6.2 Blur Background Effect

Evaluate in order:
1. `ExperimentalAcrylicBorder` - hardware blur, Avalonia 12+
2. `WriteableBitmap` software Gaussian blur at 1/4 resolution
3. Avalonia `BlurEffect` on a `Canvas`

- 📋 `BlurredArtBackground.axaml` - reusable UserControl: `IBitmap?` in, blurred+darkened surface out

### 6.3 Now Playing Left Panel

- 📋 `NowPlayingPanelView.axaml` - album art (240×240), title, artist, like/more buttons, Last.fm bio, tag chips
- 📋 `NowPlayingPanelViewModel` - binds to `PlayerViewModel.CurrentTrack`, loads art + bio async
- 📋 Replaces or sits alongside `TrackDetailView` (decision pending)

### 6.4 Now Playing Bar

- ✅ "Now Playing" accent bar indicator on track rows via `TrackIdEqualsConverter` (persists across filtered views)
- 📋 Full-width progress bar as accent underline beneath player bar
- ✅ Art thumbnail in `MiniPlayerView` wired to `AlbumArtPath`

---

## Phase 7 - Custom Windows + Full Wiring 💡

**Branch:** `feature/7-custom-windows`
**Goal:** Custom About/Update windows, full backend wiring, code splitting.

### 7.1 Custom Windows

- 💡 `AboutWindow.axaml` - version, build date, GitHub link, license
- 💡 `UpdateWindow.axaml` - GitHub Releases API check, current vs latest, download button
- 💡 `SettingsWindow.axaml` - dedicated window with tabs: General, API Keys, Profile, Advanced

### 7.2 UI Wiring Audit

- 💡 Audit all `Button`, `MenuItem`, `MenuFlyout` items - confirm every Command is wired
- 💡 Implement: Queue track, Add to playlist, Open file location, Copy track info, clipboard support
- 💡 Wire "About NullWave" → `AboutWindow`; "Check for updates" → `UpdateWindow`

### 7.3 Code Splitting

- 💡 `MetadataService` → `YouTubeMetadataFetcher`, `LastFmMetadataFetcher`, `LocalMetadataFetcher`
- 💡 `TrackSource` enum → `Models/TrackSource.cs`
- 💡 `Converters/` folder for all `IValueConverter` implementations
- 💡 File size limit: no file > 400 lines

### 7.4 Additional Features

- 💡 Playlist CRUD - create, rename, delete; drag tracks into playlists
- 💡 Queue system - play next, play later
- 💡 Last.fm scrobble (track > 50% played)
- 💡 Theme switcher - light / dark / accent color picker
- 💡 Global keyboard shortcuts - play/pause, next/prev, search focus
- 💡 Export playlist (M3U format)
- ✅ SQLite database persistence - `DatabaseService` + `TrackRecord`, fully wired into `LibraryService`

---

---

## Phase 8 - Smart Sorting & AI Integration ✅

**Branch:** `feature/8-smart-sorting`
**Goal:** Context-aware playlist generation using local weather and on-device AI.

- ✅ `HardwareDetector` - reads `/proc/meminfo`, `nvidia-smi`, and `rocm-smi` to recommend optimal Ollama model
- ✅ `LocalAIService` - Ollama HTTP API integration for mood-based track ranking
- ✅ `WeatherService` - OpenWeather API integration with 1-hour caching
- ✅ `WeatherMoodMap` - maps weather conditions and time of day to real Last.fm community tags
- ✅ `MoodPlaylistService` - full pipeline orchestrator (weather → tags → filter → AI rank)
- ✅ `LastFmEnrichmentService` - startup backfill for missing tags and album art
- ✅ Smart Sorting settings tab (hardware info, model download, coordinates)
- ✅ Auto-generate first mood playlist after enrichment backfill completes

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
├ refactor/*        ← structural changes, no new features
├ feature/{n}-*     ← new features per phase number
└ fix/*             ← bug fixes, can target any branch
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
---

## Phase 9 - Stability, Feedback & Naming Audit 🔄

**Goal:** Fix confirmed playback/data bugs, make every action give visible feedback,
and replace vague descriptions ("left side bar", "top bar") with the actual
component names below so future planning stays unambiguous.

### 9.1 Confirmed bugs (this session)

- ✅ `LibraryService.ReimportAssets` - fixed substring false-positive matching
  (e.g. "Low" matching "s-low-ed"); rewritten as two-pass exact + word-boundary
  token matching
- ✅ `LibraryService.VerifyLinks` - new: cross-checks stored track titles against
  embedded file tags, flags mis-links `RepairPaths`/`ReimportAssets` can't detect
- ✅ `NullWaveLogConfig` - Default vs Advanced/Verbose logging modes, live-switchable
  via `SettingsViewModel.VerboseLogging`, no restart needed
- ✅ `PlaybackService.CrossfadeToAsync` / `OnPlaying` - fixed the "silent playback"
  bug: the native volume-reapply nudge was gated on `_fadeCts` state that never
  reset after a fade completed, so after the *first* crossfade or fade-pause in a
  session, the nudge silently stopped firing for all future track starts. Also
  fixed event-handler attach order in crossfade (was attaching to the new
  `MediaPlayer` *after* calling `Play()`).
- 📋 SoundCloud import - a track was found with its `Title` field containing a raw
  pasted SoundCloud URL instead of a resolved title. Ruled out
  `SoundCloudMetadataFetcher` (its failure path falls back to literal
  "SoundCloud track", not the raw URL) - likely originates in
  `TrackInputViewModel`'s add-track flow. Needs that file to confirm.
- 📋 `LocalAIService.RankTracksForMoodAsync` - observed a real `HttpIOException`
  mid-ranking that silently falls back to keyword sorting with no user-facing
  indication anything went wrong.
- 💡 `TitleSanitizer.cs` and `TrackTitleParser.cs` - two separate implementations
  doing largely the same job (strip bracket junk, strip ft./feat., split
  "Artist - Title"). Candidate for consolidation under Phase 7.3 Code Splitting.


### 9.1b Confirmed bugs (17-Jul-2026 session)
- ✅ `LibraryService.ForceCleanTitles` - fixed non-idempotency: a track whose
  title still contained a residual separator after a partial clean (e.g.
  "Door - Minecraft Volume Alpha") could get re-split on a later run, since
  the method had no memory of what it had already processed. Added
  `Track.TitleForceCleaned` bool (also added to `TrackRecord.FromTrack`/
  `ToTrack` - the flag wasn't persisted at all initially, so it silently
  reset every restart). Guard now skips any track already marked cleaned.
- ✅ `LibraryService.VerifyLinks` - `NormalizeForCompare` now decomposes
  Unicode diacritics (`NormalizationForm.FormD` + strip `NonSpacingMark`)
  before stripping punctuation, fixing false-positive mismatches on titles
  like "Oxygène" vs a manually-typed "Oxygene".
- ✅ `TrackDetailViewModel.RelinkFileCommand` - new: file picker to manually
  repoint a track's `FilePath` when `VerifyLinks` flags a wrong-file link.
  Paired with `LibraryService.RefreshAlbumArt()`, which re-extracts embedded
  art immediately on relink instead of waiting for the next startup's
  `BackfillAlbumArt` pass.
- ✅ `ImportViewModel.ImportFolderAsync` - fixed duplicate imports: re-importing
  a folder whose files were already in the library (under a raw, uncleaned
  tag like "PASTEL GHOST ~ ETHEREALITY") bypassed `IsDuplicate()`'s exact
  Title+Artist match, since the existing library entry had a cleaned title
  ("Ethereality"). Now runs `TitleSanitizer.Sanitize()` on the raw tag before
  constructing the candidate `Track`, so both copies normalize to the same
  Title/Artist and are correctly recognized as duplicates.
- ✅ `LibraryService.RemoveDuplicates` - new maintenance tool (Preview/Remove,
  same pattern as `SweepOrphanedFiles`): groups tracks by normalized
  (Title, Artist) and removes all but a "best" keeper per group (priority:
  file exists on disk > PlayCount > IsFavorite > earliest DateAdded). Used
  once to clear 22 duplicates left over from repeated test imports.
  ⚠️ Known limitation: keeper selection checks only `File.Exists`, not
  whether the file's embedded tags actually match the track - in one run
  this kept 3 tracks whose `FilePath` pointed at an unrelated song (a
  pre-existing bug, not caused by this tool) and deleted the correctly-linked
  copies instead. Fixed manually via Relink File; a future pass could
  strengthen the keeper check to also require `VerifyLinks`-style tag
  agreement, not just file existence.

  ### 9.1c Search, sort & add-track overhaul (19-Jul-2026 session)

- ✅ `LibraryViewModel.FetchLibraryDataInternal` - fixed the core search bug: the
  `LibraryView.Source` branch (active when a sidebar source filter like "YouTube"
  is selected) never applied the search query at all, only the source filter -
  typing in the search box silently did nothing whenever a source filter was
  active. Rewritten to layer base-set selection → search → sort unconditionally,
  which also fixed a secondary bug where `FilterBySource` results were returned
  completely unsorted regardless of the chosen `SortField`.
- ✅ `LibraryViewModel.ApplySmartSearch` - new smart query syntax: `artist:`/`a:`,
  `title:`/`t:`, `source:`/`s:`, `tag:`/`genre:`, `is:favorite`/`fav`, plus bare
  global terms (OR-matched against Title/Artist). Uses a `pendingKey`/
  `pendingValueParts`/`FlushPending()` accumulator so multi-word values
  (`artist:tame impala`) parse correctly as a single filter instead of splitting
  on whitespace. Also supports negation: `-word` (bare exclusion) and
  `-key:value` (negated filter, e.g. `-artist:eminem`).
- ✅ `ViewModels/Playlists/PlaylistImportViewModel.cs` - removed. Constructed and
  wired into `TrackInputViewModel` but its one real method (`ImportPlaylist`) had
  no call sites anywhere in the app; the actual playlist-download flow has run
  through `MainViewModel`'s `DownloadPlaylistAsync` wiring since Phase 8. Also
  removed `Views/Controls/ImportProgressView.axaml` (and its reference in
  `MainWindow.axaml`) - dead markup left over from before the toast-based live
  activity system replaced inline progress bars.
- ✅ `Views/Controls/TrackListView.axaml` - toolbar redesigned:
  - Search box gets a magnifying-glass icon, an inline "×" clear button
    (`Library.ClearSearchTextCommand`, `Library.HasSearchQuery`), and a "?" help
    flyout documenting the smart search syntax.
  - New sort-direction toggle button (`Library.ToggleSortDirectionCommand`) next
    to the sort `ComboBox` - previously `SortAscending` existed on the ViewModel
    with no UI control to change it.
  - Sort `ComboBox` now shows human-readable labels ("Date Added" not
    "DateAdded") via `SortFieldDisplayConverter`.
  - Column headers (Title/Artist, Source, Plays, Added) are now clickable sort
    triggers with a direction-arrow indicator; clicking the active column's
    header flips direction instead of no-op.
  - New track-count label ("415 tracks") in the header row.
  - "+ Add Track" `SplitButton`'s dropdown replaced with a self-contained flyout
    form (URL input + Add button + local file/folder options), replacing the
    separate always-in-layout "URL INPUT" row. Add button gated on
    `TrackInputViewModel.IsInputUrlValid`.
- ✅ `LibraryService`/`LibraryViewModel` sort now applies a secondary `.ThenBy`
  tie-breaker per `SortField` (mostly by Title) so equal-value groups (e.g. many
  tracks with `PlayCount == 0`) render in a stable, predictable order instead of
  arbitrary/insertion order.
- ✅ `Views/MainWindow.axaml.cs` - fixed a real hotkey bug: `OnKeyDown`'s guard
  against intercepting keystrokes while typing (`e.Source is TextBox`) only
  matched the exact source type, but Avalonia's `TextBox` is templated - the
  actual typing source is an internal `TextPresenter`, so the check silently
  failed and `M`/`N`/`Space` fired as global hotkeys (mute/next-track/play-pause)
  while typing in the search box. Fixed by walking the visual tree
  (`GetVisualAncestors()`) from `e.Source` to check for any ancestor `TextBox`.
- 💡 Clipboard pre-fill for the Add Track flyout - deferred. Avalonia's
  `IClipboard` surface on the current build doesn't expose `GetTextAsync()` or
  the attempted `GetDataAsync(DataFormats.Text)` fallback (`DataFormats` itself
  is obsolete, superseded by `DataFormat`). Code is commented out in
  `TrackListView.axaml.cs` (`OnAddFlyoutOpened`) pending confirmation of the
  correct method signature for this Avalonia version.
- 💡 Autocomplete/hint UI for search keys (inline suggestions while typing
  `a:`/`t:`/etc.) - not started, natural follow-up to the help flyout.

### 9.2 Universal action feedback

- 📋 Audit every user-triggered action for a live/toast notification, not just
  maintenance operations (`RepairPaths`, `SweepOrphanedFiles`, etc. already
  covered). Known gaps: `MoodPlaylistService` AI ranking (silent fallback), track
  edits in `TrackDetailViewModel`, playlist CRUD in `PlaylistViewModel`.

### 9.3 Component naming reference (use these in future roadmap entries, not descriptions)

| Informal description | Actual component |
|---|---|
| "left side bar" | `Views/Controls/SidebarView.axaml` |
| "the profile thing" | `UserProfileViewModel.cs` + `Views/ProfileWindow.axaml` |
| "top bar" / "add button" | The toolbar inside `Views/Controls/TrackListView.axaml` (search box + sort `ComboBox` + `+ Add` `SplitButton`) - **not** `MenuBarView.axaml`, the separate Alt-key-toggled File/Library/Settings/Help bar |
| "search tab" | ❓ Unclear - `LibraryViewModel.SearchQuery`/`Search()` is already wired into `TrackListView`'s search box. Is this about that search box, or a separate dedicated Search page that was never built? |
| "appearance tab is a placeholder" | `AppearanceTab.axaml` is built and saves via `PreferencesService` (Phase 5 ✅) - but "wire appearance settings to actual UI (live accent color, row height, font scale)" is still 📋. The tab works but doesn't re-skin the app live yet, which likely reads as "placeholder" |
| "updates tab is useless" | `UpdatesTab.axaml` + `UpdateService`/`DependencyUpdateService` are real and wired (`CheckForUpdateAsync`, `UpdateYtDlpAsync`, `CheckDependenciesAsync`) - worth re-checking before assuming nothing works |
| "sorting doesn't work as intended" | ❓ Needs repro steps - `LibraryViewModel.SortByTitle/Artist/Date/PlayCount` and `LibraryService.GetSorted()` appear wired |

### 9.4 Deferred (explicitly, not forgotten)

- 💡 Modular/independent feature architecture (e.g. profile removable without
  breaking core) - large architectural change, own future phase
- 💡 Top bar / Add-track flow redesign - frontend, after 9.1-9.2 land per
  "backend first" priority
- 💡 `SidebarView` padding/spacing pass
- 💡 Unique/creative differentiator features - ideas welcome, not yet scoped

