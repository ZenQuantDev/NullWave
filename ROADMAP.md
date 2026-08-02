# NullWave - Roadmap

> Last updated: 02-Aug-2026

---

## Legend

| Symbol | Meaning                       |
| ------ | ----------------------------- |
| ✅     | Done - merged to main         |
| 🔄     | In progress - active branch   |
| 🔜     | Up next - ready to start      |
| 📋     | Planned - scoped, not started |
| 💡     | Future - idea, not yet scoped |

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

## Phase 12 - Navigation Redesign & Detail Polish ✅

**Branch:** `feature/nav-redesign-and-more`
**Goal:** Replace hardcoded sidebar with data-driven nav, consolidate stats, and polish track details.

- ✅ `NavItem` model and data-driven `SidebarView`
- ✅ Pill-based navigation (Playlists/Artists) with drag-and-drop reordering
- ✅ `NavigationViewModel` with pinned playlist support
- ✅ `TrackDetailViewModel` redesign with Last.fm Artist Info integration
- ✅ Artist grouping in library view

## Phase 13 - Plugin Architecture & Service Isolation 🔄

**Goal:** Make all external dependencies optional, disconnectable, and independently replaceable.

- ✅ 13.1 Core Plugin System (`IPlugin`, `PluginManager`, `PluginState`)
- ✅ 13.2 Extract Existing Services (`YtDlpDownloadProvider`, `LastFmMetadataProvider`, `OllamaAIProvider`, `OpenWeatherProvider`)
- 📋 13.3 Process Isolation (Deprioritized - lightweight interface approach achieves goals without IPC complexity)
- ✅ 13.4 UI & Management (Plugins tab in Settings with live toggles and toast feedback)

## Phase 14 - Stability, Persistence & UI Polish ✅

**Goal:** Fix critical playback bugs, persist AI playlists, and improve user feedback.

- ✅ Queue auto-fill hybrid system (`QueueEntry` model with manual/auto distinction)
- ✅ History-based "Previous" button to prevent track oscillation
- ✅ Fixed crossfade queue desync bug
- ✅ Fixed crossfade PipeWire segfault on Linux (delayed native disposal)
- ✅ Smart Shuffle candidate pool floor (prevents 2-track loop under skip pressure)
- ✅ AI prompt playlists (`ai:`) now always persist as real `Playlist` entities in "AI Playlists" folder
- ✅ Offline-ready indicator added to track list
- ✅ Playlist folders support (`CreateFolderDialog`, `PlaylistFolderRecord`)
- ✅ Plugin toggle now shows connect/fail toast feedback
- 📋 Window resizing behavior audit (carried over from Phase 11.1)

---

## Architecture Guidelines

### File size limits

| File type        | Soft limit | Hard limit |
| ---------------- | ---------- | ---------- |
| ViewModel        | 300 lines  | 400 lines  |
| Service          | 300 lines  | 500 lines  |
| View (.axaml)    | 200 lines  | 300 lines  |
| View code-behind | 50 lines   | 100 lines  |

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

| Package                   | Version       | Notes                                   |
| ------------------------- | ------------- | --------------------------------------- |
| .NET                      | 8.0           | LTS                                     |
| Avalonia                  | 12.0.4        |                                         |
| LibVLCSharp               | 3.9.7.1       | Fedora: symlinks to /usr/lib required   |
| TagLib#                   | 2.3.0         |                                         |
| Serilog                   | latest stable | + Serilog.Filters.Expressions           |
| sqlite-net-pcl            | 1.9.172       | Packages installed, implementation next |
| SQLitePCLRaw.bundle_green | 2.1.11        | Native SQLite provider for Linux        |
| CommunityToolkit.Mvvm     | latest stable | RelayCommand, ObservableProperty        |

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
  reset after a fade completed, so after the _first_ crossfade or fade-pause in a
  session, the nudge silently stopped firing for all future track starts. Also
  fixed event-handler attach order in crossfade (was attaching to the new
  `MediaPlayer` _after_ calling `Play()`).
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

| Informal description               | Actual component                                                                                                                                                                                                                                                              |
| ---------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| "left side bar"                    | `Views/Controls/SidebarView.axaml`                                                                                                                                                                                                                                            |
| "the profile thing"                | `UserProfileViewModel.cs` + `Views/ProfileWindow.axaml`                                                                                                                                                                                                                       |
| "top bar" / "add button"           | The toolbar inside `Views/Controls/TrackListView.axaml` (search box + sort `ComboBox` + `+ Add` `SplitButton`) - **not** `MenuBarView.axaml`, the separate Alt-key-toggled File/Library/Settings/Help bar                                                                     |
| "search tab"                       | ❓ Unclear - `LibraryViewModel.SearchQuery`/`Search()` is already wired into `TrackListView`'s search box. Is this about that search box, or a separate dedicated Search page that was never built?                                                                           |
| "appearance tab is a placeholder"  | `AppearanceTab.axaml` is built and saves via `PreferencesService` (Phase 5 ✅) - but "wire appearance settings to actual UI (live accent color, row height, font scale)" is still 📋. The tab works but doesn't re-skin the app live yet, which likely reads as "placeholder" |
| "updates tab is useless"           | `UpdatesTab.axaml` + `UpdateService`/`DependencyUpdateService` are real and wired (`CheckForUpdateAsync`, `UpdateYtDlpAsync`, `CheckDependenciesAsync`) - worth re-checking before assuming nothing works                                                                     |
| "sorting doesn't work as intended" | ❓ Needs repro steps - `LibraryViewModel.SortByTitle/Artist/Date/PlayCount` and `LibraryService.GetSorted()` appear wired                                                                                                                                                     |

### 9.4 Deferred (explicitly, not forgotten)

- 💡 Modular/independent feature architecture (e.g. profile removable without
  breaking core) - large architectural change, own future phase
- 💡 Top bar / Add-track flow redesign - frontend, after 9.1-9.2 land per
  "backend first" priority
- 💡 `SidebarView` padding/spacing pass
- 💡 Unique/creative differentiator features - ideas welcome, not yet scoped

---

## Phase 10 - Customizable Navigation & Playlist System 🔜

**Branch:** `feature/nav-redesign-and-more`
**Goal:** Replace the hardcoded sidebar with a data-driven, user-customizable nav
system; consolidate Stats into the Profile Window; move Queue to a slide-in panel
(matching Track Detail's mechanism); redesign the Playlists tab to match the
Library tab's search/sort UX; lay groundwork for pinned playlists and saved
searches.

### 10.1 Nav data model + Sidebar rewrite (foundation)

- 📋 New `NavItem` model: type (`Core`/`PinnedPlaylist`/`SavedSearch`), display
  order, label, icon, target (page name / playlist ID / search query)
- 📋 `SidebarView` rewritten as data-driven `ItemsControl` bound to
  `ObservableCollection<NavItem>`, replacing the current hardcoded named-button
    - manual `Classes.Add("active")` pattern in `SidebarView.axaml.cs`
- 📋 Core items (Library, Playlists) are reorderable but not hideable/removable
- 📋 Order persisted via `PreferencesService` (existing infra, no new
  persistence layer)
- 📋 Reference implementation to preserve: the Library tab's Sources
  filter pattern (`FilterYouTube`/`FilterLastFm`/etc. + active-state styling)
  is explicitly called out as "already working well" - do not regress this
  UX when rewriting Sidebar
- 📋 v1 reorder UI: up/down controls in a "Customize Sidebar" panel (true
  drag-and-drop reordering is a v2 stretch goal - Avalonia has no built-in
  reorderable-list control, so this needs custom drag-handling logic)

### 10.2 Pinned playlists / saved searches

- 📋 Depends on 10.1. A pinned nav item references either a `Playlist.Id` or
  a saved smart-search query (reusing `LibraryViewModel.ApplySmartSearch`
  syntax from Phase 9.1c - e.g. `is:favorite`, `artist:x`)
- 📋 First pinned slot defaults to an auto-suggested playlist (e.g. most-played)
  until the user manually pins/unpins or disables the default via Settings
  (Settings toggle is a 💡 future item, not required for v1)
- 📋 Additional slots are user-pinned only, via a "Pin to sidebar" action on
  each playlist row / saved search
- 📋 Unpin control lives in the same customize-sidebar surface as reordering

### 10.3 Queue → slide-in panel

- 📋 New `QueueViewModel`, wired to existing `LibraryService.GetQueue()`/
  `AddToQueue`/`RemoveFromQueue`/`ClearQueue` (all already implemented)
- 📋 New `LibraryService.MoveTrackInQueue(fromIndex, toIndex)` - does not
  exist yet, needed for in-queue reordering
- 📋 Panel mechanics mirror `TrackDetailViewModel` exactly (`IsOpen`,
  `PanelWidth`, `PanelOpacity`, same `DoubleTransition` pattern in XAML)
- 📋 Decision needed before implementation: can Queue and Track Detail panels
  be open simultaneously, or are they mutually exclusive (one right-panel
  slot)? Recommend mutually exclusive for v1 - simpler state management,
  revisit if it feels cramped in practice
- 📋 Remove `QueuePage`/`PlaceholderPageViewModel` wiring for Queue once the
  panel replaces it; `NavigateQueueCommand` becomes "open queue panel"
  instead of a page navigation

### 10.4 Stats → Profile Window consolidation

- ✅ Most of this already exists: `ProfileWindow.axaml` / `UserProfileViewModel`
  already show Total Tracks/Favorites/Plays/Skips, Top Track, Top Artist, and
  a Library Breakdown (YouTube/SoundCloud/Local percentage bars) - this was
  built in earlier work, confirmed present as of this phase's kickoff
- 📋 Remove the separate `StatsPage`/`PlaceholderPageViewModel` wiring from
  `MainViewModel` and the `NavigateStatsCommand` page-navigation behavior
  entirely - "Stats" as a standalone page goes away
- 📋 Audit Profile's existing stats section for gaps once Duration exists
  (10.6) - e.g. total listening time, most-played by minutes rather than
  play count
- 💡 Consider whether Local source is the only "storage-based" bucket worth
  breaking out, or whether Spotify/LastFm sources should get their own
  breakdown slice too (currently only YouTube/SoundCloud/Local are shown)

### 10.5 Playlists tab redesign (Library-style)

- 📋 Bring `TrackListView`'s toolbar polish (search box + clear button, sort
    - direction toggle, clickable sortable headers, result count) to the
      track list inside a selected playlist in `PlaylistsView.axaml`
- 📋 Evaluate whether the playlist _list itself_ (left panel) needs its own
  search box - likely only worth it once a user has many playlists; not
  blocking for v1
- 📋 Reuse `SortFieldDisplayConverter`/`BoolToSortIconConverter` from Phase
  9.1c rather than duplicating

### 10.6 Track duration (cross-cutting, opportunistic)

- 📋 Add `Duration` (TimeSpan or int seconds) to `Track`/`TrackRecord`,
  populated via `TagLib` on local file read and wherever else metadata is
  fetched (YouTube/SoundCloud fetchers)
- 📋 No dedicated milestone for this - add as touched incidentally while
  working through 10.1-10.5, particularly useful for 10.4 (listening time
  stats) and 10.3 (showing track length in the queue panel)

### 10.7 Naming & branding (low-effort, parallel-track)

- 📋 Expand `9.3 Component naming reference` into a living glossary as new
  components are built in this phase (e.g. `NavItem`, `QueueViewModel`) -
  keep names canonical from day one instead of a future audit
- 💡 Adopt flower codenames for major releases alongside semver (e.g.
  `0.5.0 "Blue Orchid"`), similar in spirit to Android's dessert-name
  convention - cosmetic, no functional dependency on anything else in this
  phase, can start whenever

## Phase 11 - Queue/Shuffle Integration & Polish 🔜

**Goal:** Unify the manual Queue with the shuffle/repeat navigation system,
and fix known interaction bugs from Phase 10.

### 11.1 Bug fixes (carried over from Phase 10 testing)

- 📋 `LibraryViewModel.AddToQueue` reads SelectedTrack instead of the
  CommandParameter passed from context menu / "⋮" flyout - "Add to Queue"
  silently does nothing unless a track happens to already be selected.
  Fix: accept Track? parameter, fall back to SelectedTrack if null.
- 📋 Window resizing behavior reported as "terrible" - needs a repro
  (screen recording or specific window sizes) before scoping a fix.

### 11.2 Auto-queue / shuffle integration

- 📋 Pre-fill the queue with ~10 upcoming tracks based on the active
  navigation mode (Normal Shuffle, Smart Shuffle AI, Repeat, or plain
  library order), refilling as tracks are consumed - closes the gap
  where PlaybackNavigator and the manual Queue are currently fully
  independent systems
- 📋 Decide: does Smart Shuffle's AI ranking (LocalAIService) drive the
  auto-fill order, or is a separate lighter heuristic more appropriate for
  "what's coming up" previews vs. one-shot mood-playlist generation?
- 📋 UI: distinguish auto-filled queue entries from user-added ones (e.g.
  a subtle "Up Next" section header vs. manually queued tracks) so
  removing/reordering feels predictable

## Phase 13 - Plugin Architecture & Service Isolation 💡

**Goal:** Make all external dependencies (yt-dlp, Ollama, OpenWeather, SoundCloud, etc.)
optional, disconnectable, and independently replaceable. Enable community plugins.

### 13.1 Core Plugin System

- Define `IMusicSourcePlugin`, `IDownloadProvider`, `IMetadataProvider`, `IAIProvider` interfaces
- 📋 Implement `PluginManager` with dependency injection registration
- Add plugin configuration to `PreferencesService` (`~/.nullwave/plugins.json`)
- Create `PluginState` enum (Available/Unavailable/Disabled/Error)
- 📋 Health check system with periodic pings

### 13.2 Extract Existing Services

- 📋 Wrap `DownloadService` → `YtDlpDownloadProvider` (optional, graceful degradation)
- 📋 Wrap `LastFmService` → `LastFmMetadataProvider`
- Wrap `LocalAIService` → `OllamaAIProvider`
- 📋 Wrap `WeatherService` → `OpenWeatherProvider`
- Each provider implements interface + fallback behavior when disabled

### 13.3 Process Isolation

- Create `PluginHost` separate process for CPU/memory-heavy operations
- 📋 Implement named pipe/gRPC communication protocol
- 📋 Add circuit breaker pattern for failing plugins
- Auto-restart crashed plugins with backoff
- Resource monitoring per-plugin (CPU, memory limits)

### 13.4 UI & Management

- 📋 Plugins management tab in Settings
- 📋 Enable/disable toggles with live status indicators
- Plugin installation workflow (future: community plugins)
- 📋 Dependency graph visualization (which features need which plugins)

### 13.5 Documentation

- 📋 Plugin development guide for community contributors
- API reference for plugin interfaces
- 📋 Migration guide from monolithic to plugin-based services

**Success Criteria:**

- NullWave runs with zero external dependencies (local files only mode)
- Disabling yt-dlp doesn't break the app, just hides download features
- AI features degrade gracefully when Ollama is unavailable
- Plugin load time < 200ms per plugin
- No feature regression from current functionality
