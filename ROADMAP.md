# NullWave - Roadmap

> Last updated: 16-Aug-2026

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

| Version | Codename / Focus | Key Deliverables |
| ------- | ---------------- | ---------------- |
| **v0.5.1** | Stability | Metadata fixes, playlist URL interception, skip-penalty decay, AI playlist padding (mostly complete on `fix/metadata-and-playlist-downloads`). |
| **v0.5.2** | QoL & Polish | Notification rework, multi-select, proxy support, onboarding wizard (Phase 16). |
| **v0.6.0** | Major Features | Audiobooks, synced lyrics, dynamic smart playlists, EQ, Bandcamp (Phase 17). |
| **v0.7.0+**| Ecosystem | Installers, i18n, Discord RPC, animations (Phase 18). |

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

**Remaining (wire into other ViewModels):**

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

**Remaining:**

- 📋 Replace remaining emoji in nav buttons with proper SVG icon set (`Assets/Icons/Icons.axaml`)
- 📋 `OpacityTransition` on menu bar show/hide (150ms fade)
- 📋 Profile edit UI (inline in sidebar or dedicated Settings tab)
- ✅ `AppearanceTab` - accent color, row style, font scale, sidebar width, compact mode
- ✅ `PreferencesService` - auto-saves all General + Appearance settings to `~/.nullwave/prefs.json`
- ✅ Wire appearance settings to actual UI (live accent color, row height, font scale, sidebar width, density)
- ✅ Logo placeholder in `SidebarView` - 32×32 accent square beside wordmark
- 📋 Replace placeholder with real SVG logo (`Assets/Icons/logo.svg`)
- 📋 `TrackListView` - apply theme token, larger row height, better typography
- 📋 `TrackDetailView` - apply theme tokens

---

## Phase 6 - Now Playing Redesign 💡 (Deferred to v0.6.0)

**Branch:** `feature/6-now-playing`
**Goal:** Spotify-inspired Now Playing panel with album art and blur background.
*Note: Cosmetic UI tweaks deferred by choice to v0.6.0 to ship v0.5.0 cleanly.*

### 6.1 Album Art Fetching

- ✅ YouTube thumbnail fetching via `img.youtube.com` (no API key required)
- ✅ SoundCloud thumbnail fetching via yt-dlp
- ✅ Startup backfill for existing tracks missing thumbnails
- ✅ `AlbumArtService` - unified art fetching with priority chain (YouTube → SoundCloud → Last.fm → Placeholder)
- ✅ `TrackTitleParser` - shared helper to clean messy YouTube titles for accurate Last.fm queries
- 📋 Cache key: `SHA256(Artist + Album)` truncated to 16 chars (currently uses URL/ID hashing)
- 📋 Fallback: `Assets/placeholder-art.png`

### 6.2 Blur Background Effect

- 📋 `BlurredArtBackground.axaml` - reusable UserControl: `IBitmap?` in, blurred+darkened surface out
- 📋 Letterbox-crop YouTube thumbnails before caching (prototype `LetterboxCropper` drafted, not shipped)

### 6.3 Now Playing Left Panel

- 📋 `NowPlayingPanelView.axaml` - album art (240×240), title, artist, like/more buttons, Last.fm bio, tag chips
- 📋 `NowPlayingPanelViewModel` - binds to `PlayerViewModel.CurrentTrack`, loads art + bio async
- 📋 Replaces or sits alongside `TrackDetailView` (decision pending)

### 6.4 Now Playing Bar

- ✅ "Now Playing" accent bar indicator on track rows via `TrackIdEqualsConverter` (persists across filtered views)
- 📋 Full-width progress bar as accent underline beneath player bar
- 📋 Tinted/dim source badges + offline indicator restyle
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
- ✅ Queue system - play next, play later
- ✅ Last.fm scrobble (track > 50% played)
- 💡 Theme switcher - light / dark / accent color picker
- 💡 Global keyboard shortcuts - play/pause, next/prev, search focus
- 💡 Export playlist (M3U format)
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

---

## Phase 9 - Stability, Feedback & Naming Audit 🔄

**Goal:** Fix confirmed playback/data bugs, make every action give visible feedback, and replace vague descriptions with actual component names.

### 9.1 Confirmed bugs (this session)
- ✅ `LibraryService.ReimportAssets` - fixed substring false-positive matching.
- ✅ `LibraryService.VerifyLinks` - new: cross-checks stored track titles against embedded file tags.
- ✅ `NullWaveLogConfig` - Default vs Advanced/Verbose logging modes, live-switchable.
- ✅ `PlaybackService.CrossfadeToAsync` / `OnPlaying` - fixed the "silent playback" bug and event-handler attach order.
- ✅ `LocalAIService.RankTracksForMoodAsync` - fixed silent fallbacks and HTTP IO exceptions.
- 💡 `TitleSanitizer.cs` and `TrackTitleParser.cs` - candidate for consolidation under Phase 7.3 Code Splitting.

### 9.1b Confirmed bugs (17-Jul-2026 session)
- ✅ `LibraryService.ForceCleanTitles` - fixed non-idempotency with `Track.TitleForceCleaned` bool.
- ✅ `LibraryService.VerifyLinks` - `NormalizeForCompare` now decomposes Unicode diacritics.
- ✅ `TrackDetailViewModel.RelinkFileCommand` - new: file picker to manually repoint a track's `FilePath`.
- ✅ `ImportViewModel.ImportFolderAsync` - fixed duplicate imports via `TitleSanitizer.Sanitize()`.
- ✅ `LibraryService.RemoveDuplicates` - new maintenance tool (Preview/Remove).

### 9.1c Search, sort & add-track overhaul (19-Jul-2026 session)
- ✅ `LibraryViewModel.FetchLibraryDataInternal` - fixed core search bug ignoring source filters.
- ✅ `LibraryViewModel.ApplySmartSearch` - new smart query syntax (`artist:`, `title:`, `-word`, etc.).
- ✅ `Views/Controls/TrackListView.axaml` - toolbar redesigned with clear button, help flyout, sort toggle, and clickable headers.
- ✅ `Views/MainWindow.axaml.cs` - fixed hotkey bug intercepting keystrokes while typing in search box.

### 9.1d Windows Porting & Download Stability (13-Aug-2026 session)
- ✅ `LibraryService.ForceCleanTitles` successfully stripping artist names and junk from raw YouTube download titles.
- ✅ `DownloadService` rate-limit throttling implemented between bulk downloads.

### 9.1e Metadata, AI & Link Verification Stability (15-Aug-2026 session)
- ✅ `LocalAIService.GenerateTagsForTrackAsync` / `GenerateTagsBulkAsync` - fixed `KeyNotFoundException` crashes with `TryGetProperty` guards and markdown stripping.
- ✅ `YouTubeMetadataFetcher.FetchAsync` - replaced `EnsureSuccessStatusCode()` with manual HTTP status check.
- ✅ `LibraryService.VerifyLinks` - drastically reduced false positives (27 → 6) with `FeatureArtistRegex` and `BracketContentRegex`.

### 9.2 Universal action feedback (16-Aug-2026 session)
- ✅ Single-host toast routing (`SetActiveHost`) — fixes double-toast when Settings dialog is open.
- ✅ Toast cap (max 4, oldest non-live dropped) to prevent stacking walls.
- ✅ Undo pattern extended: playlists and folders.
- ✅ Hover-pause, Dismiss-all, queue-clear Undo, "Added to playlist" Undo, mood-gen dedupe via scope.
- ✅ Core user actions (`TrackEdited`, `FavoriteToggled`, `SearchPerformed`, `TrackAdded`, `TrackRemoved`, `SkipPenalty`, `SettingChanged`) emitting structured `[ACTION]` logs.
- 📋 Audit remaining user-triggered actions for live/toast notifications.

### 9.3 Component naming reference (use these in future roadmap entries, not descriptions)

| Informal description | Actual component |
| -------------------- | ---------------- |
| "left side bar" | `Views/Controls/SidebarView.axaml` |
| "the profile thing" | `UserProfileViewModel.cs` + `Views/ProfileWindow.axaml` |
| "top bar" / "add button" | The toolbar inside `Views/Controls/TrackListView.axaml` (search box + sort `ComboBox` + `+ Add` `SplitButton`) - **not** `MenuBarView.axaml`, the separate Alt-key-toggled File/Library/Settings/Help bar |
| "search tab" | ❓ Unclear - `LibraryViewModel.SearchQuery`/`Search()` is already wired into `TrackListView`'s search box. Is this about that search box, or a separate dedicated Search page that was never built? |
| "appearance tab is a placeholder" | `AppearanceTab.axaml` is built and saves via `PreferencesService` (Phase 5 ✅) - but "wire appearance settings to actual UI (live accent color, row height, font scale)" is still 📋. The tab works but doesn't re-skin the app live yet, which likely reads as "placeholder" |
| "updates tab is useless" | `UpdatesTab.axaml` + `UpdateService`/`DependencyUpdateService` are real and wired (`CheckForUpdateAsync`, `UpdateYtDlpAsync`, `CheckDependenciesAsync`) - worth re-checking before assuming nothing works |
| "sorting doesn't work as intended" | ❓ Needs repro steps - `LibraryViewModel.SortByTitle/Artist/Date/PlayCount` and `LibraryService.GetSorted()` appear wired |

### 9.4 Deferred (explicitly, not forgotten)

- 💡 Modular/independent feature architecture (e.g. profile removable without breaking core) - large architectural change, own future phase
- 💡 Top bar / Add-track flow redesign - frontend, after 9.1-9.2 land per "backend first" priority
- 💡 `SidebarView` padding/spacing pass
- 💡 Unique/creative differentiator features - ideas welcome, not yet scoped

---

## Phase 10 - Customizable Navigation & Playlist System ✅

**Branch:** `feature/nav-redesign-and-more`
**Goal:** Replace the hardcoded sidebar with a data-driven, user-customizable nav system; consolidate Stats into the Profile Window; move Queue to a slide-in panel (matching Track Detail's mechanism); redesign the Playlists tab to match the Library tab's search/sort UX; lay groundwork for pinned playlists and saved searches.

### 10.1 Nav data model + Sidebar rewrite (foundation) ✅

- ✅ New `NavItem` model: type (`Core`/`PinnedPlaylist`/`SavedSearch`), display order, label, icon, target (page name / playlist ID / search query)
- ✅ `SidebarView` rewritten as data-driven `ItemsControl` bound to `ObservableCollection<NavItem>`, replacing the current hardcoded named-button + manual `Classes.Add("active")` pattern in `SidebarView.axaml.cs`
- ✅ Core items (Library, Playlists) are reorderable but not hideable/removable
- ✅ Order persisted via `PreferencesService` (existing infra, no new persistence layer)
- ✅ Reference implementation to preserve: the Library tab's Sources filter pattern (`FilterYouTube`/`FilterLastFm`/etc. + active-state styling) is explicitly called out as "already working well" - do not regress this UX when rewriting Sidebar
- ✅ True drag-and-drop reordering implemented natively via Avalonia `DragDrop.DoDragDropAsync` (exceeds v1 up/down controls goal)

### 10.2 Pinned playlists / saved searches ✅

- ✅ Pinned playlists fully wired (`PinPlaylist`, `UnpinPlaylist`, `AutoSuggestPinEnabled`)
- ✅ First pinned slot defaults to an auto-suggested playlist (most tracks)
- ✅ "Pin to sidebar" action wired via `PinPlaylistCommand`
- 📋 Saved smart-search queries (`TargetQuery`) model exists but UI wiring pending

### 10.3 Queue → slide-in panel ✅

- ✅ `QueueViewModel` + `QueueView` slide-in panel mechanics mirror `TrackDetailView` exactly
- ✅ `LibraryService.MoveQueueItem(fromIndex, toIndex)` implemented
- ✅ In-queue drag-and-drop reordering implemented in `QueueView.axaml.cs`
- ✅ Mutually exclusive panels: Queue and Track Detail panels cannot be open simultaneously
- ✅ `QueueEntry` model distinguishes manual vs auto-filled entries

### 10.4 Stats → Profile Window consolidation ✅

- ✅ Most of this already exists: `ProfileWindow.axaml` / `UserProfileViewModel` already show Total Tracks/Favorites/Plays/Skips, Top Track, Top Artist, and a Library Breakdown (YouTube/SoundCloud/Local percentage bars) - this was built in earlier work, confirmed present as of this phase's kickoff
- ✅ Remove the separate `StatsPage`/`PlaceholderPageViewModel` wiring from `MainViewModel` and the `NavigateStatsCommand` page-navigation behavior entirely - "Stats" as a standalone page goes away
- 📋 Audit Profile's existing stats section for gaps once Duration exists (10.6) - e.g. total listening time, most-played by minutes rather than play count
- 💡 Consider whether Local source is the only "storage-based" bucket worth breaking out, or whether Spotify/LastFm sources should get their own breakdown slice too (currently only YouTube/SoundCloud/Local are shown)

### 10.5 Playlists tab redesign (Library-style) 🔄

- 🔄 Bring `TrackListView`'s toolbar polish (search box + clear button, sort direction toggle, clickable sortable headers, result count) to the track list inside a selected playlist in `PlaylistsView.axaml`.
- 📋 Evaluate whether the playlist _list itself_ (left panel) needs its own search box.
- 📋 Reuse `SortFieldDisplayConverter`/`BoolToSortIconConverter` from Phase 9.1c.

### 10.6 Track duration (cross-cutting, opportunistic) 📋

- 📋 Add `Duration` (TimeSpan or int seconds) to `Track`/`TrackRecord`, populated via `TagLib` on local file read and wherever else metadata is fetched (YouTube/SoundCloud fetchers)
- 📋 No dedicated milestone for this - add as touched incidentally while working through 10.1-10.5, particularly useful for 10.4 (listening time stats) and 10.3 (showing track length in the queue panel)

### 10.7 Naming & branding (low-effort, parallel-track)

- 📋 Expand `9.3 Component naming reference` into a living glossary as new components are built in this phase (e.g. `NavItem`, `QueueViewModel`) - keep names canonical from day one instead of a future audit
- 💡 Adopt flower codenames for major releases alongside semver (e.g. `0.5.0 "Blue Orchid"`), similar in spirit to Android's dessert-name convention - cosmetic, no functional dependency on anything else in this phase, can start whenever

---

## Phase 11 - Queue/Shuffle Integration & Polish ✅

**Goal:** Unify the manual Queue with the shuffle/repeat navigation system, and fix known interaction bugs from Phase 10.

### 11.1 Bug fixes (carried over from Phase 10 testing) ✅

- ✅ `LibraryViewModel.AddToQueue` reads SelectedTrack instead of the CommandParameter passed from context menu / "⋮" flyout - "Add to Queue" silently does nothing unless a track happens to already be selected. Fix: accept Track? parameter, fall back to SelectedTrack if null.
- 📋 Window resizing behavior reported as "terrible" - needs a repro (screen recording or specific window sizes) before scoping a fix.

### 11.2 Auto-queue / shuffle integration ✅

- ✅ Skip penalty tracking active (`[Player] Skip penalty recorded for 'X' (skipped after Y.Ys, total skips: Z)`)
- ✅ Shuffle deck generation active (`[PlaybackNavigator] Shuffle deck built: X tracks...`)
- ✅ Pre-fill the queue with ~10 upcoming tracks based on the active navigation mode (Normal Shuffle, Smart Shuffle AI, Repeat, or plain library order), refilling as tracks are consumed - closes the gap where PlaybackNavigator and the manual Queue are currently fully independent systems
- ✅ Decide: does Smart Shuffle's AI ranking (`LocalAIService`) drive the auto-fill order, or is a separate lighter heuristic more appropriate for "what's coming up" previews vs. one-shot mood-playlist generation?
- ✅ UI: distinguish auto-filled queue entries from user-added ones (e.g. a subtle "Up Next" section header vs. manually queued tracks) so removing/reordering feels predictable

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

## Phase 13 - Plugin Architecture & Service Isolation 🔄

**Goal:** Make all external dependencies optional, disconnectable, and independently replaceable.

- ✅ 13.1 Core Plugin System (`IPlugin`, `PluginManager`, `PluginState`)
- ✅ 13.2 Extract Existing Services (`YtDlpDownloadProvider`, `LastFmMetadataProvider`, `OllamaAIProvider`, `OpenWeatherProvider`)
- 📋 13.3 Process Isolation (Deprioritized - lightweight interface approach achieves goals without IPC complexity)
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
- ✅ Windows compatibility testing and `.csproj` adjustments (Confirmed running on Windows 10.0.26100 X64, `AppData\Roaming` paths)
- ✅ YouTube Playlist bulk download routing (`MainViewModel` intercepts playlist URLs, strips dummy track, triggers native yt-dlp bulk download with aria2c/yt-dlp fallback)
- ✅ Documentation and CHANGELOG finalization

### Known Limitations (deferred to v0.6+)

- 📋 Ctrl+L keyboard shortcut for search focus
- 📋 Hover preview flyouts on collapsed sidebar icons
- 📋 Deduplicate mood generation double-toast

---

## Phase 16 - v0.5.2 Quality of Life & Polish 🔜

**Goal:** Immediate UX improvements, better network handling, and user onboarding.

- 📋 **Notification Rework:** Grouped toasts with action buttons (Cancel download, View error, Undo) to replace static text notifications.
- 📋 **Multi-select & Bulk Actions:** `SelectionMode.Multiple` (Ctrl/Shift) in track list with floating action bar (Add to playlist, Remove, Queue).
- 📋 **Proxy Support:** HTTP/SOCKS5 proxy routing for yt-dlp (`--proxy socks5://...`) and `HttpClientHandler` (for metadata calls). SOCKS5 chosen over VPN for simplicity and lack of admin rights requirement.
- 📋 **Help Tab & Onboarding:** Dedicated Help tab with API setup guides and feature documentation, plus a first-run onboarding wizard (Theme → API Keys → Download Dir).

---

## Phase 17 - v0.6.0 Major Features 💡

**Goal:** The big feature drop for the next major version.

- 📋 **Audiobook Mode:** LibVLC `SetRate(float)` for 0.5x-2.0x speed control, `ResumePositionMs` DB column for auto-remembering playback position, simple crop editor (start/end) via FFmpeg/yt-dlp `--download-sections`.
- 📋 **Synced Lyrics:** LRCLIB integration (no API key required, FOSS-friendly) to fetch LRC files, highlight current line synced to playback position via DispatcherTimer.
- 📋 **Advanced Audio Controls:** 10-band EQ (LibVLC equalizer) and ReplayGain/volume normalization.
- 📋 **Dynamic Smart Playlists:** SQL-driven auto-playlists: "Forgotten Gems" (not played in 6 months), "Heavy Rotation" (most played this week), "Fresh Drops" (added in last 7 days).
- 📋 **More Sources:** Bandcamp import support to complement YouTube/SoundCloud/Spotify.

---

## Phase 18 - Platform & Ecosystem 💡

**Goal:** Distribution, localization, and deep OS integration.

- 📋 **Installers & Auto-Update:** Velopack or NSIS via GitHub Actions; wire `UpdateService` to silent/background updates.
- 📋 **i18n:** Localization support (starting with English/Russian).
- 📋 **Discord Rich Presence:** Show currently playing track and artist in Discord status.
- 📋 **Animation Pass:** Page transitions, micro-interactions, menu-bar opacity transition (150ms fade).

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