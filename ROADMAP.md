# NullWave - Roadmap

> Last updated: 24-Oct-2026

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

## Release Plan

| Version     | Codename / Focus      | Key Deliverables                                                                                                                                       |
| ----------- | --------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------ |
| **v0.5.1**  | Stability             | Metadata fixes, playlist URL interception, skip-penalty decay, AI playlist padding.                                                                    |
| **v0.5.2**  | QoL & Polish          | Shipped 17-Aug-2026 (toasts, multi-select groundwork, proxy, onboarding).                                                                              |
| **v0.6.0**  | "Oxeye Daisy"         | Shipped 19-Sep-2026: light theme, audiobooks + radio, sleep timer, crossfade, profile badges, RU localization, backups, onboarding polish.             |
| **v0.6.1**  | "Stability & Safety"  | Shipped 23-Sep-2026: xUnit test foundation (196 tests), P0 data safety (atomic writes/quarantine), P0 security (badge pinning), P1 download stability. |
| **v0.6.2**  | Correctness           | Shipped 02-Oct-2026: DownloadService concurrency fixes, Mood AI prompt schema, unified TagTaxonomy, Spotify bridge fixes.                              |
| **v0.6.3**  | Responsiveness        | Shipped 11-Oct-2026: Async hardware detection, LibraryService hot-path optimization, clean-machine first-run onboarding.                               |
| **v0.6.4**  | "Foundations"         | Shipped 24-Oct-2026: Architecture overhaul, O(1) duplicate detection, strict UTC enforcement, badge catalog, radio hardening, download reliability.             |
| **v0.6.5**  | Premium & Performance | "Effects: Reduced" tier, visual audit, performance budgets for low-end hardware.                                                                       |
| **v0.6.6**  | Launch Readiness      | Feature freeze, `/docs` wiki, Velopack installers, Reddit launch prep.                                                                                 |
| **v0.7.0+** | Ecosystem             | Media keys (MPRIS/SMTC), macOS build, Subsonic server, multi-source search plugin.                                                                     |

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
- 📋 `ImportViewModel` - add `NullActionLogger.ImportStarted/Completed/Failed` calls
- ✅ `LibraryViewModel` - add `TrackAdded`, `TrackRemoved`, `FavoriteToggled`, `SearchPerformed`
- ✅ `TrackDetailViewModel` - add `TrackEdited` on save
- 📋 `PlaylistViewModel` - add `PlaylistCreated`, `PlaylistDeleted`, `PlaylistTrackAdded`
- ✅ `SettingsViewModel` - add `SettingChanged` on key save

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
- ✅ Replace remaining emoji in nav buttons with proper SVG icon set (`Assets/Icons/Icons.axaml`)
- 📋 `OpacityTransition` on menu bar show/hide (150ms fade)
- ✅ Profile edit UI (inline in sidebar or dedicated Settings tab)
- ✅ `AppearanceTab` - accent color, row style, font scale, sidebar width, compact mode
- ✅ `PreferencesService` - auto-saves all General + Appearance settings to `~/.nullwave/prefs.json`
- ✅ Wire appearance settings to actual UI (live accent color, row height, font scale, sidebar width, density)
- ✅ Logo placeholder in `SidebarView` - 32×32 accent square beside wordmark
- 📋 Replace placeholder with real SVG logo (`Assets/Icons/logo.svg`)
- 📋 `TrackListView` - apply theme token, larger row height, better typography
- 📋 `TrackDetailView` - apply theme token

---

## Phase 6 - Now Playing Redesign 💡 (Deferred to v0.6.0)

**Branch:** `feature/6-now-playing`
**Goal:** Spotify-inspired Now Playing panel with album art and blur background.

- ✅ YouTube thumbnail fetching via `img.youtube.com` (no API key required)
- ✅ SoundCloud thumbnail fetching via yt-dlp
- ✅ Startup backfill for existing tracks missing thumbnails
- ✅ `AlbumArtService` - unified art fetching with priority chain (YouTube → SoundCloud → Last.fm → Placeholder)
- ✅ `TrackTitleParser` - shared helper to clean messy YouTube titles for accurate Last.fm queries
- ✅ Cache key: `SHA256(Artist + Album)` truncated to 16 chars (currently uses URL/ID hashing)
- ✅ Fallback: `Assets/placeholder-art.png`
- 🔄 `BlurredArtBackground.axaml` - reusable UserControl: `IBitmap?` in, blurred+darkened surface out
- 📋 Letterbox-crop YouTube thumbnails before caching (prototype `LetterboxCropper` drafted, not shipped)
- ✅ `NowPlayingPanelView.axaml` - album art (240×240), title, artist, like/more buttons, Last.fm bio, tag chips
- ✅ `NowPlayingPanelViewModel` - binds to `PlayerViewModel.CurrentTrack`, loads art + bio async
- 📋 Replaces or sits alongside `TrackDetailView` (decision pending)
- ✅ "Now Playing" accent bar indicator on track rows via `TrackIdEqualsConverter` (persists across filtered views)
- 📋 Full-width progress bar as accent underline beneath player bar
- ✅ Tinted/dim source badges + offline indicator restyle
- ✅ Art thumbnail in `MiniPlayerView` wired to `AlbumArtPath`

---

## Phase 7 - Custom Windows + Full Wiring 💡

**Branch:** `feature/7-custom-windows`
**Goal:** Custom About/Update windows, full backend wiring, code splitting.

- ✅ `AboutWindow.axaml` - version, build date, GitHub link, license
- ✅ `UpdateWindow.axaml` - GitHub Releases API check, current vs latest, download button
- ✅ `SettingsWindow.axaml` - dedicated window with tabs: General, API Keys, Profile, Advanced
- 🔄 Audit all `Button`, `MenuItem`, `MenuFlyout` items - confirm every Command is wired
- ✅ Implement: Queue track, Add to playlist, Open file location, Copy track info, clipboard support
- ✅ Wire "About NullWave" → `AboutWindow`; "Check for updates" → `UpdateWindow`
- ✅ `MetadataService` → `YouTubeMetadataFetcher`, `LastFmMetadataFetcher`, `LocalMetadataFetcher`
- 💡 `TrackSource` enum → `Models/TrackSource.cs`
- 💡 `Converters/` folder for all `IValueConverter` implementations
- 🔄 File size limit: no file > 400 lines
- ✅ Playlist CRUD - create, rename, delete; drag tracks into playlists
- ✅ Queue system - play next, play later
- ✅ Last.fm scrobble (track > 50% played)
- ✅ Theme switcher - light / dark / accent color picker
- ✅ Global keyboard shortcuts - play/pause, next/prev, search focus
- 🔄 Export playlist (M3U format)
- ✅ SQLite database persistence - `DatabaseService` + `TrackRecord`, fully wired into `LibraryService`

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
- ✅ "AI returns too few tracks" padding fix implemented
- ✅ **Local AI Model Fallback** - auto-switches to safe installed models (e.g. `gemma3:4b`) if the configured model is missing

---

## Phase 9 - Stability, Feedback & Naming Audit 🔄

**Goal:** Fix confirmed playback/data bugs, make every action give visible feedback, and replace vague descriptions with actual component names.

- ✅ `LibraryService.ReimportAssets` - fixed substring false-positive matching.
- ✅ `LibraryService.VerifyLinks` - new: cross-checks stored track titles against embedded file tags.
- ✅ `NullWaveLogConfig` - Default vs Advanced/Verbose logging modes, live-switchable.
- ✅ `PlaybackService.CrossfadeToAsync` / `OnPlaying` - fixed the "silent playback" bug and event-handler attach order.
- ✅ `LocalAIService.RankTracksForMoodAsync` - fixed silent fallbacks and HTTP IO exceptions.
- ✅ `TitleSanitizer.cs` and `TrackTitleParser.cs` - candidate for consolidation under Phase 7.3 Code Splitting.
- ✅ `LibraryService.ForceCleanTitles` - fixed non-idempotency with `Track.TitleForceCleaned` bool.
- ✅ `LibraryService.VerifyLinks` - `NormalizeForCompare` now decomposes Unicode diacritics.
- ✅ `TrackDetailViewModel.RelinkFileCommand` - new: file picker to manually repoint a track's `FilePath`.
- ✅ `ImportViewModel.ImportFolderAsync` - fixed duplicate imports via `TitleSanitizer.Sanitize()`.
- ✅ `LibraryService.RemoveDuplicates` - new maintenance tool (Preview/Remove).
- ✅ `LibraryViewModel.FetchLibraryDataInternal` - fixed core search bug ignoring source filters.
- ✅ `LibraryViewModel.ApplySmartSearch` - new smart query syntax (`artist:`, `title:`, `-word`, etc.).
- ✅ `Views/Controls/TrackListView.axaml` - toolbar redesigned with clear button, help flyout, sort toggle, and clickable headers.
- ✅ `Views/MainWindow.axaml.cs` - fixed hotkey bug intercepting keystrokes while typing in search box.
- ✅ `LibraryService.ForceCleanTitles` successfully stripping artist names and junk from raw YouTube download titles.
- ✅ `DownloadService` rate-limit throttling implemented between bulk downloads.
- ✅ `LocalAIService.GenerateTagsForTrackAsync` / `GenerateTagsBulkAsync` - fixed `KeyNotFoundException` crashes with `TryGetProperty` guards and markdown stripping.
- ✅ `YouTubeMetadataFetcher.FetchAsync` - replaced `EnsureSuccessStatusCode()` with manual HTTP status check.
- ✅ `LibraryService.VerifyLinks` - drastically reduced false positives (27 → 6) with `FeatureArtistRegex` and `BracketContentRegex`.
- ✅ Single-host toast routing (`SetActiveHost`) - fixes double-toast when Settings dialog is open.
- ✅ Toast cap (max 4, oldest non-live dropped) to prevent stacking walls.
- ✅ Undo pattern extended: playlists and folders.
- ✅ Hover-pause, Dismiss-all, queue-clear Undo, "Added to playlist" Undo, mood-gen dedupe via scope.
- ✅ Core user actions (`TrackEdited`, `FavoriteToggled`, `SearchPerformed`, `TrackAdded`, `TrackRemoved`, `SkipPenalty`, `SettingChanged`) emitting structured `[ACTION]` logs.
- 📋 Audit remaining user-triggered actions for live/toast notifications.
- 💡 Modular/independent feature architecture (e.g. profile removable without breaking core) - large architectural change, own future phase
- 💡 Top bar / Add-track flow redesign - frontend, after 9.1-9.2 land per "backend first" priority
- 💡 `SidebarView` padding/spacing pass
- 💡 Unique/creative differentiator features - ideas welcome, not yet scoped

---

## Phase 10 - Customizable Navigation & Playlist System ✅

**Branch:** `feature/nav-redesign-and-more`
**Goal:** Replace the hardcoded sidebar with a data-driven, user-customizable nav system; consolidate Stats into the Profile Window; move Queue to a slide-in panel.

- ✅ New `NavItem` model: type (`Core`/`PinnedPlaylist`/`SavedSearch`), display order, label, icon, target
- ✅ `SidebarView` rewritten as data-driven `ItemsControl` bound to `ObservableCollection<NavItem>`
- ✅ Core items (Library, Playlists) are reorderable but not hideable/removable
- ✅ Order persisted via `PreferencesService`
- ✅ True drag-and-drop reordering implemented natively via Avalonia `DragDrop.DoDragDropAsync`
- ✅ Pinned playlists fully wired (`PinPlaylist`, `UnpinPlaylist`, `AutoSuggestPinEnabled`)
- ✅ First pinned slot defaults to an auto-suggested playlist (most tracks)
- ✅ "Pin to sidebar" action wired via `PinPlaylistCommand`
- 📋 Saved smart-search queries (`TargetQuery`) model exists but UI wiring pending
- ✅ `QueueViewModel` + `QueueView` slide-in panel mechanics mirror `TrackDetailView` exactly
- ✅ `LibraryService.MoveQueueItem(fromIndex, toIndex)` implemented
- ✅ In-queue drag-and-drop reordering implemented in `QueueView.axaml.cs`
- ✅ Mutually exclusive panels: Queue and Track Detail panels cannot be open simultaneously
- ✅ `QueueEntry` model distinguishes manual vs auto-filled entries
- ✅ Remove the separate `StatsPage`/`PlaceholderPageViewModel` wiring from `MainViewModel`
- 📋 Audit Profile's existing stats section for gaps once Duration exists
- ✅ Brought multi-select (`SelectionMode="Multiple"`), floating bulk action bar (Queue, Remove), and queue integration to the track list inside a selected playlist
- ✅ Reused `SortFieldDisplayConverter`/`BoolToSortIconConverter` for sortable headers.
- 📋 Add `Duration` (TimeSpan or int seconds) to `Track`/`TrackRecord`
- ✅ Adopt flower codenames for major releases alongside semver

---

## Phase 11 - Queue/Shuffle Integration & Polish ✅

**Goal:** Unify the manual Queue with the shuffle/repeat navigation system, and fix known interaction bugs.

- ✅ `LibraryViewModel.AddToQueue` reads SelectedTrack instead of the CommandParameter passed from context menu
- 📋 Window resizing behavior reported as "terrible" - needs a repro
- ✅ Skip penalty tracking active
- ✅ Shuffle deck generation active
- ✅ Pre-fill the queue with ~10 upcoming tracks based on the active navigation mode
- ✅ UI: distinguish auto-filled queue entries from user-added ones

---

## Phase 12 - Navigation Redesign & Detail Polish ✅

**Branch:** `feature/nav-redesign-and-more`
**Goal:** Replace hardcoded sidebar with data-driven nav, consolidate stats, and polish track details.

- ✅ `NavItem` model and data-driven `SidebarView`
- ✅ Pill-based navigation (Playlists/Artists) with drag-and-drop reordering
- ✅ `NavigationViewModel` with pinned playlist support
- ✅ `TrackDetailViewModel` redesign with Last.fm Artist Info integration
- ✅ Artist grouping in library view

---

## Phase 13 - Plugin Architecture & Service Isolation ✅

**Goal:** Make all external dependencies optional, disconnectable, and independently replaceable.

- ✅ 13.1 Core Plugin System (`IPlugin`, `PluginManager`, `PluginState`)
- ✅ 13.2 Extract Existing Services (`YtDlpDownloadProvider`, `LastFmMetadataProvider`, `OllamaAIProvider`, `OpenWeatherProvider`)
- 📋 13.3 Process Isolation (Deprioritized)
- ✅ 13.4 UI & Management (Plugins tab in Settings with live toggles and toast feedback)

---

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

## Phase 15 - v0.5.0 "Blue Orchid" Final Polish ✅

**Branch:** `feature/15-ui-polish`
**Goal:** Final UI refinements, bug fixes, and cross-platform testing before release.

- ✅ Sidebar drag & drop for playlist reordering and folder organization
- ✅ Active playlist context (playback continues through playlist even after queue clear)
- ✅ Instant sidebar refresh on playlist deletion (no reboot required)
- ✅ Live play count and last played updates via `LibraryChanged` event
- ✅ Slim scrollbars with hover reveal
- ✅ Unified context menus (right-click and 3-dot menu parity)
- ✅ Ghost pin purging on sidebar rebuild
- ✅ Windows compatibility testing and `.csproj` adjustments
- ✅ YouTube Playlist bulk download routing
- ✅ Documentation and CHANGELOG finalization
- ✅ `Ctrl+L` keyboard shortcut for search focus
- ✅ Hover preview flyouts on collapsed sidebar icons
- ✅ Deduplicate mood generation double-toast

---

## Phase 16 - v0.5.2 Quality of Life & Polish ✅

**Goal:** Immediate UX improvements, better network handling, and user onboarding.

- ✅ **Notification Rework:** Grouped toasts with action buttons (Cancel download, View error, Undo).
- ✅ **Multi-select & Bulk Actions:** `SelectionMode.Multiple` (Ctrl/Shift) in track list with floating action bar.
- ✅ **Proxy Support:** HTTP/SOCKS5 proxy routing for yt-dlp and metadata calls.
- ✅ **Help Tab & Onboarding:** Dedicated Help tab with API setup guides and a first-run onboarding wizard.

---

## Phase 17 - v0.6.0 "Oxeye Daisy" Major Features ✅

**Goal:** The big feature drop for the next major version, focusing on media types, UI polish, and localization.

- ✅ **Audiobook Mode:** Playback speed, resume-from-position, chapter navigation, and sleep timer.
- ✅ **Light Theme & Oxeye Daisy:** Dark/Light/System modes with theme-aware accent mixing; new signature accent.
- ✅ **Live Radio Page:** Dedicated library page, SomaFM catalog + mood picker, and ICY now-playing metadata.
- ✅ **Profile Window Overhaul:** Signed + computed badges, custom banner images, managed avatar assets, and Install ID.
- ✅ **Crossfade & Sleep Timer:** Dual long-lived MediaPlayer engine with generation-guarded transitions; 15-60 min sleep timer.
- ✅ **Multi-select & Bulk Actions:** Ctrl/Shift selection with floating bulk bar for queue, favorite, playlist, and remove.
- ✅ **Localization:** Full English/Russian string tables with live language switching.
- ✅ **Database Safety:** Rolling auto-backups with retention and restore-from-backup.
- ✅ **Onboarding & Proxy:** First-run wizard for setup; yt-dlp proxy and geo-proxy fields.
- ✅ **Download Manager:** UI for tracking active/failed downloads with retry actions.
- ✅ **Onboarding & Help Polish:** Unified UI styles, hidden API keys, Ollama dependency tracking, and installation guides.
- ✅ **Download Manager Stability:** Fixed fatal XAML resource crash and improved active/failed job tracking.
- ✅ **Library Maintenance:** Force-clean titles, orphaned file sweeping, and duplicate removal tools.
- ✅ **Local AI Fallback:** Auto-switches to safe installed models if configured Ollama model is missing.
- ✅ **Settings ViewModel refactor:** split `SettingsViewModel` into domain partial files.

---

## Phase 18 - v0.6.2 "Correctness" ✅

**Goal:** Fix download concurrency, mood AI reliability, and unify tag vocabularies.

- ✅ **DownloadService (C4)**: Fix stuck URLs, prevent `SemaphoreSlim` drift, fix playlist hang, add 20-min hard timeout.
- ✅ **Mood Ranking (C5)**: Add explicit JSON output schema to Ollama prompt, pre-select candidates, cap `num_ctx`.
- ✅ **Unified Tag Taxonomy (C6)**: Consolidate `WeatherMoodMap`, `ExternalAITagService`, and local AI prompts.
- ✅ **Metadata & Spotify Bridge (C7, C11)**: Route Spotify metadata through `SpotifyPageParser`, fix `SplitArtistCredits`.
- ✅ **Local AI & Library (C8, C10)**: Fix Ollama connection refused detection, remove file paths from prompts.
- ✅ **Unified `ProcessRunner` (P9)**: Replace fragmented `Process.Start` calls with a single robust helper.
- ✅ **In-App Log Viewer (Help Tab)**: Bind `InAppLogSink` to a UI with text/level filters.
- ✅ **"What's New" Screen**: Render the latest `CHANGELOG.md` section as a markdown popup.
- ✅ **UI & Startup Polish**: Fixed VLC console popup, sidebar drag-arming, Settings header clipping.
- ✅ **P2P Sharing Foundation**: Introduced `nullwave://` URI scheme generation.
- ✅ **Custom WaveSeekBar**: Replaced standard MiniPlayer slider with custom animated wave seek bar.

---

## Phase 19 - v0.6.3 "Responsiveness & First Run" ✅

**Goal:** Eliminate UI thread blocking, optimize library hot-paths, and ensure a smooth clean-machine install.

- ✅ **Hardware Detection (P1-P4)**: Move `DetectHardware` to background thread, enforce RAM limits, add AVX/AVX2 gate.
- ✅ **Library Performance (P5, P6)**: Added SQLite FTS5 global search, disk thumbnail sidecars, in-flight decode deduplication.
- ✅ **First-Run Friction (P7, P8)**: Added VLC location probing, cached tool-version checks, post-first-frame audio initialization.

---

## Phase 20 - v0.6.4 "Quick Wins" ✅

**Branch:** `fix/ui-search-quick-wins`
**Goal:** Small, visible, low-risk UI and search improvements that ship immediate value.

- ✅ **1.1 Multi-word search AND logic**: `BuildFtsQuery` and `ApplySmartSearch` fallback both require all terms to match.
- ✅ **1.2 Artist filter persistence**: Guard flag prevents `OnSearchQueryChanged` from clearing the artist filter.
- ✅ **1.3 Valid sorts per view**: `SortFieldCatalog` ensures each page only offers valid sort options.
- ✅ **1.4 Length sort & honest headers**: New `SortField.Length` with zero-duration sinking; Length header is now sortable.
- ✅ **1.5 Column alignment**: Header and row grids now share identical margins and column 0 spacer dimensions.
- ✅ **1.6 Global search routing**: Query reaches Library, Radio, Audiobooks, and Playlist VMs.
- ✅ **1.7 Clipboard pre-fill**: Add Track flyout pre-fills HTTP/HTTPS URLs from clipboard.

---

## Phase 21 - v0.6.4 "Data Safety" ✅

**Branch:** `fix/data-safety`
**Goal:** Protect user data with non-destructive merges, schema versioning, and safe backups.

- ✅ **2.1 Playlist rename safety**: Blocked renames to existing names in UI; made `Deduplicate` merge tracks instead of deleting.
- ✅ **2.2 Migration runner**: Introduced schema version via `user_version`; migrations run once in order.
- ✅ **2.3 Safe manual backup**: Used `VACUUM INTO` for consistent backups; validated with integrity check.
- ✅ **2.4 Playlist persistence**: Tokenized cover paths on save; unique timestamped filenames; old files deleted and cache invalidated.
- ✅ **2.5 Cascading deletes**: Removing a track cleans Queue and Playlists; Undo captures and restores memberships.
- ✅ **2.6 Radio detection**: One-time repair migration for misclassified tracks (e.g., "Radiohead" URLs).
- ✅ **2.7 Small fixes**: WAL flush on dispose; FTS rebuild after vacuum; background migration cancellation prevents test crashes.

---

## Phase 22 - v0.6.4 "Settings Foundation" ✅

**Branch:** `feature/20-settings-foundation`
**Goal:** Adding a setting becomes cheap; every setting can be wired the same way.

- ✅ **One save path**: `Update(...)` is the only way to change a setting; removed all `ScheduleSave()` calls.
- ✅ **Single "changed" signal**: `Changed` event tells subscribers which setting names changed.
- ✅ **Preferences versioning**: Defined version 2; migrate on load with backup.
- ✅ **Setting descriptors**: Generic row templates (toggle, segmented, slider) for new pages.
- ✅ **Experimental Features System**: Added `EnableExperimentalFeatures` toggle in Advanced tab.

---

## Phase 23 - v0.6.4 "Code Cleanup & Standardization" ✅

**Branch:** `fix/data-safety` (combined with Phase 21)
**Goal:** Eliminate duplicate systems and standardize core utilities.

- ✅ **D3+D4 Track Normalization**: Created unified `TrackNormalizer` class consolidating 6+ duplicate implementations.
- ✅ **D9 Time Standardization**: Changed `LibraryService.RecordPlay` to use `DateTime.UtcNow`.
- ✅ **D17 Shell Helper**: Created cross-platform `ShellHelper` with `OpenFolder`, `OpenFile`, `OpenUrl` methods.
- ✅ **Experimental Features System**: Gated in-development features behind visual badges and warning banners.
- ✅ **Crossfade Stability**: Removed experimental banner from AudioTab - feature is now stable.

---

## Phase 24 - v0.6.4 "Architecture & Performance Refactor" ✅

**Branch:** `refactor/architecture-overhaul`
**Goal:** Eliminate technical debt, enforce strict UTC rules, and optimize hot paths for large libraries.

- ✅ **Folder Reorganization**: Moved files into logical subfolders (`Models/Library`, `Helpers/Imaging`, `ViewModels/Shell`, `Services/Database`).
- ✅ **DatabaseService Split**: Split the 800+ line `DatabaseService` into 5 focused partial classes.
- ✅ **O(1) Duplicate Detection**: `LibraryService` now uses an in-memory Identity Index for instant duplicate checks.
- ✅ **ProcessRunner Unification**: Migrated all raw `Process.Start` calls to the unified, deadlock-free `ProcessRunner`.
- ✅ **Memory Leak Fixes**: Implemented strict `Dispose()` patterns in `MainViewModel` and `PlayerViewModel`.
- ✅ **RelayCommand Safety**: Unified `RelayCommand` to inherit from `SafeRelayCommand` for global exception handling.
- ✅ **Banned Symbols Analyzer**: Added `BannedSymbols.txt` and Roslyn analyzers to enforce UTC-only `DateTime` usage.
- ✅ **Shared INPC Base**: Introduced `NotifyBase` to eliminate duplicated `INotifyPropertyChanged` boilerplate.
- ✅ **UTC Enforcement**: Replaced all remaining `DateTime.Now` usages with `DateTime.UtcNow`.
- ✅ **Last.fm Credential Reloading**: `LastFmService` now reloads credentials in-place when connecting/disconnecting.
- ✅ **Sync & Dedupe Fixes**: Fixed dry-run mutations in `SyncOne`, and routed `RemoveDuplicates` through `Remove()`.
- ✅ `RadioBrowserProvider`: DNS server discovery, per-server timeouts, http/https-only streams, URL dedupe.
- ✅ Mini-player buffering indicator; ICY fallback no longer shows the station name as the song.
- ✅ Badge catalog JSON with v2 signing, KeyId rotation support and expired-badge handling.
- ✅ v9/v10 migrations (UTC LastPlayed) run synchronously in one transaction.
- ✅ yt-dlp JS runtime auto-detection; silent duplicate clicks; playback-hijack guard; duplicate import guard with YouTube URL canonicalization; "- Topic" stripping on single imports.
---


## Phase 25 - v0.6.4 "Notifications" 📋

**Branch:** `feature/20-notifications`
**Goal:** Configurable, localized notifications with in-app and native OS support.

- 📋 **Categories and preferences**: Global and per-category settings.
- 📋 **Router in front of ToastService**: `NotificationService` decides which sinks to use.
- 📋 **Native sink**: Windows and Linux native toasts with focus rule.
- 📋 **Settings page**: Control all notification settings with a Test button.

---

## Phase 26 - v0.6.4 "Factory Reset" 📋

**Branch:** `feature/20-factory-reset`
**Goal:** Safe wipe with three tiers (Settings, Library, Factory).

- 📋 **Reset on next start**: Marker file; delete according to allowlist at very start of launch.
- 📋 **Always take backup**: Unless user opts out.

---

## Phase 27 - v0.6.5 "Premium Look & Performance" 📋

**Goal:** High-end aesthetics that respect the performance floor (i3 380M, 8GB RAM).

- ✅ **Wallpaper v1 pulled forward into v0.6.3**: Whole-window wallpaper, class-gated chrome, readability floors, presets.
- 📋 **Effects Tier**: "Full" vs "Reduced" appearance setting (respects OS animation settings).
- 🔄 **Visual Audit**: Display-size, HiDPI-aware async decoding, sidecars, and bitmap prewarming are in place. Blur, per-row shadows, and virtualization audit remain open.
- 🔄 **Performance Budgets**: Define and hit targets for cold start, idle RAM, 5k track scroll smoothness, and zero >150ms UI stalls.

---

## Phase 28 - v0.6.6 "Launch Readiness" 📋

**Goal:** Survive the first impression on Reddit.

- 📋 **Documentation**: `/docs` folder (install, sources, Mood Mix, troubleshooting, architecture, privacy).
- 📋 **Repo Hygiene**: Clean `.gitignore`, issue templates asking for diagnostics, `THIRD-PARTY-NOTICES`.
- 📋 **Release Packaging**: Windows Setup/Portable (Velopack), Linux AppImage/Flatpak.
- 📋 **Feature Freeze**: 1-2 weeks before launch, bug fixes only, clean-machine install verification.
- 📋 Portable-build audio verification: local mp3/flac/m4a, https radio, first-try YouTube import on a clean data dir.
- 📋 Matroska/WebM demuxer decision for the VLC prune whitelist (local .webm/.mka unsupported in release builds).

---

## Phase 29 - Post-Launch / Deferred Features 💡

**Goal:** Highly requested features deferred to ensure a stable v0.6 launch.

- 💡 **Synced Lyrics**: LRCLIB integration with playback-position line highlighting.
- 💡 **Advanced Audio**: 10-band EQ (LibVLC) and ReplayGain/volume normalization.
- 💡 **Smart Playlists**: SQL-driven dynamic playlists (forgotten, most played, recently added).
- 💡 **More Sources**: Bandcamp import, M3U/M3U8 import and export.
- 💡 **Export & Sharing**: Batch export/archiving, P2P/Torrent-like sharing, embedded tag sync.
- 💡 YouTube Data API fallback to yt-dlp embedded metadata when the API returns BadRequest (kills "Unknown Title" imports).
- 💡 Locked badges with progress bars and unlock toasts (persisted seen-set).
- 💡 yt-dlp child-process kill watchdog on cancel (no leaked processes).
- 💡 BadgeIssuer private key from env var or file instead of CLI argument.

---

## Phase 30 - v0.7.0 "Ecosystem" (Name TBD) 💡

**Goal:** Deep OS integration, cross-device sync, and community features.

- 💡 **OS Integration**: Media keys (MPRIS on Linux, SMTC on Windows), Discord Rich Presence, portable mode.
- 💡 **macOS Build**: Test-only via friend's Mac + CI, replace Linux-specific code, map Ctrl to Cmd.
- 💡 **Subsonic Server**: Opt-in, LAN-only, authenticated server for downloaded tracks.
- 💡 **Multi-Source Search**: `IMusicSearchProvider` plugin interface to query YouTube/SoundCloud/Bandcamp in parallel.

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

```text
main                ← always stable, builds clean
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
