# Changelog

All notable changes to NullWave will be documented in this file.

## [0.1.0] - 2026-05-26

### Added
- Initial project setup with Avalonia UI (.NET 8)
- Core track model with Title, Artist, URL, FilePath, Source, DateAdded
- TrackSource enum (YouTube, Spotify, Local, Instagram, Unknown)
- LibraryService with full in-memory track management
  - Add, Remove, Search, Filter by source
  - Duplicate detection (by URL, FilePath, or Title+Artist)
  - Favorites system (ToggleFavorite)
  - Play count and LastPlayed tracking (RecordPlay)
  - Recently added and recently played views
  - Sorting by Title, Artist, DateAdded, Source, PlayCount, LastPlayed
  - Queue system (Add, Remove, Clear, DequeueNext)
  - History capped at 200 entries
- PlaylistService with full playlist management
  - Create, Remove, Rename playlists
  - Add/Remove tracks, prevent duplicates
  - Reorder tracks (MoveTrack)
- MetadataService (placeholder, API-ready)
- UrlParserService (YouTube ID, Spotify ID, local file support)
- ExportService (JSON and CSV export/import)
- SourceDetector helper (auto-detects source from URL)
- RelayCommand helper (sync and async support)
- AppLogger (Serilog: console + rolling file logs)
- MainViewModel wired to all services
- SettingsViewModel (API keys, export path, preferences)
- TrackViewModel (UI display wrapper)
- Basic Avalonia UI window
- 23 unit tests (xUnit) — all passing

### Project Structure
- Models: Track, Playlist
- Services: LibraryService, PlaylistService, MetadataService, UrlParserService, ExportService
- ViewModels: MainViewModel, SettingsViewModel, TrackViewModel
- Helpers: SourceDetector, RelayCommand, AppLogger
- Views: MainWindow, SettingsWindow
- Tests: LibraryServiceTests, PlaylistServiceTests
