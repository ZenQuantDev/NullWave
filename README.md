# 🎵 NullWave

<p align="center">
  <img alt="NullWave - Oxeye Daisy" src="Assets/Art/banner.png" width="100%">
</p>

<p align="center">
  <a href="https://github.com/ZenQuantDev/NullWave/actions/workflows/ci.yml"><img alt="CI" src="https://github.com/ZenQuantDev/NullWave/actions/workflows/ci.yml/badge.svg"></a>
  <a href="LICENSE"><img alt="license" src="https://img.shields.io/badge/license-MIT%20%C2%B7%20attribution-green?style=flat-square"></a>
  <a href="https://github.com/ZenQuantDev/NullWave/releases"><img alt="release" src="https://img.shields.io/github/v/release/ZenQuantDev/NullWave?include_prereleases&label=release&color=blue&style=flat-square"></a>
  <a href="https://deepwiki.com/ZenQuantDev/NullWave"><img alt="Ask DeepWiki" src="https://deepwiki.com/badge.svg"></a>
</p>
<p align="center">
  <img alt="Windows" src="https://img.shields.io/badge/Windows-0078D4?logo=windows&logoColor=white&style=flat-square">
  <img alt="Linux" src="https://img.shields.io/badge/Linux-FCC624?logo=linux&logoColor=black&style=flat-square">
  <a href="https://github.com/ZenQuantDev/NullWave/discussions"><img alt="discussions" src="https://img.shields.io/github/discussions/ZenQuantDev/NullWave?label=discussions&logo=github&style=flat-square"></a>
  <a href="https://github.com/ZenQuantDev/NullWave/discussions/categories/ideas"><img alt="feature requests" src="https://img.shields.io/badge/feature%20requests-open-brightgreen?style=flat-square"></a>
</p>

A personal music organizer with download, playback, AI-powered smart sorting, and an extensible plugin system, built with C#/.NET 10 and Avalonia UI.

## Version

v0.6.5 "Oxeye Daisy"

## About

NullWave lets you save, organize, download, and play music from YouTube, Last.fm, SoundCloud, radio streams, and local files in one unified library. It features context-aware playlist generation using local weather and on-device AI. Organizer-first, player second.

> ⚠️ Please read [DISCLAIMER.md](DISCLAIMER.md) before using NullWave.

---

## Features

### 🧠 Smart Sorting (AI + Weather)

- **Auto-Mood Playlists**: Generates playlists based on your local weather (via OpenWeather API) and time of day.
- **Local AI Ranking**: Uses a local Ollama instance to rank tracks by suitability for the current mood.
- **Hardware Detection**: Automatically scans your CPU, RAM, and GPU VRAM to recommend and download the best AI model for your machine.
- **Last.fm Enrichment**: Automatically backfills missing genre tags and album art for your entire library on startup.

### 📚 Organizer

- Add tracks by URL (YouTube, Last.fm, SoundCloud) or local file
- Auto-fetch metadata from YouTube Data API v3 and Last.fm
- Read ID3 tags from local audio files (MP3, FLAC, WAV, OGG, M4A, AAC)
- Library with search, sort, favorites, play tracking, and queue
- **Multi-select bulk actions (Ctrl/Shift) with floating action bar**
- Bulk folder import with subfolder support and duplicate detection
- **Local File Browser**: a Files page to browse, play and queue local audio without importing it - breadcrumbs, path editor, pinned folders, depth-limited recursive search, multi-select bulk actions, and lock badges for encrypted (EFS) or unreadable files.
- **Duplicate Import Guard**: YouTube tracking params (`&pp=`, `&si=`) are stripped on import; re-pasting an existing link shows an "already in your library" toast instead of downloading twice
- Track detail panel with editable title, artist, notes, and tags
- Playlist management and source filters (YouTube, Last.fm, SoundCloud, Local)
- Plugin system with bundled OpenWeather and Radio Browser providers; user-installed plugins take precedence over bundled copies with the same ID. Plugins execute in-process, so only install plugins you trust.
- Searchable Radio Browser directory, curated offline stations, and repairable radio stream references

### 🎧 Player

- Local file playback via LibVLCSharp
- YouTube audio download via yt-dlp (to `<data directory>/downloads/`)
- YouTube and SoundCloud playlist import
- Audiobook mode: playback speed, resume-from-position, chapter navigation, sleep timer
- Live radio page with ICY metadata and station picker
- Dual-player crossfade (experimental, opt-in)
- **Sleep timer (15/30/45/60 min) with live mini-player countdown**
- Light / Dark / System theme modes
- Spotify-style now-playing bar with "Now Playing" accent indicators
- Shuffle, Repeat One, Repeat All, and Seek ±5 seconds
- **Length Sort**: Sort your library by track duration, with radio streams intelligently sorted last.
- Autoplay next track on finish
- **Network Resilience**: Circuit breakers bypass unavailable Last.fm, OpenWeather, and YouTube Data API services during cooldowns.

### 🎨 UI & Customization

- **Spotify-style Sidebar**: Collapsible rail mode (Ctrl+B), pinned playlists, playlist folders, and drag-and-drop reordering.
- **Live Theme Engine**: Light/Dark/System modes, 9 accent colors, 10 duotone pairs, 3 density styles, compact mode, and font scaling (zero restarts).
- **Profile Window**: Signed & computed badges, custom banner images, consolidated stats, and Install ID.
- **Localization**: English/Russian localization for primary app flows, including settings and radio catalog, with live language switching.
- **Onboarding Wizard**: First-run flow for theme, API keys, and download directory.
- **Smart Search**: FTS5-powered prefix search with AND logic for multi-word queries, operators (`artist:`, `title:`, `tag:`, `is:favorite`, `-exclude`), and global filtering across Library, Radio, and Audiobooks pages.
- **Clipboard Pre-fill**: Automatically paste YouTube/SoundCloud/Spotify URLs from your clipboard when opening the Add Track dialog.
- **Mood Mix**: Weather-driven playlists (AI or tag-based) with auto-regeneration and pin preservation.

### 🛠️ Maintenance Suite

- Orphaned file sweep (dry-run preview; orphans move to an `_orphans` folder and are never deleted)
- Duplicate detection with smart keeper selection
- Link verification (stored metadata vs embedded tags)
- Path repair, asset reimport, and database vacuum optimization
- **Rolling auto-backups with retention and one-click restore from backup**

---

## Tech Stack

| Layer                | Technology                                                                                                           |
| -------------------- | -------------------------------------------------------------------------------------------------------------------- |
| Language / Framework | C# 12, .NET 10 LTS, Avalonia UI 12 (MVVM)                                                                            |
| Playback             | LibVLCSharp + system libVLC                                                                                          |
| Download             | yt-dlp                                                                                                               |
| Metadata             | TagLib# (ID3), YouTube Data API v3, Last.fm API                                                                      |
| Smart Sorting        | Ollama (Local AI), OpenWeather API                                                                                   |
| Logging              | Serilog (3 sinks: system, user-actions, errors)                                                                      |
| Security             | DPAPI on Windows; Secret Service (`secret-tool`) on Linux; falls back to a weaker machine-derived key if unavailable |
| Persistence          | SQLite-net-pcl                                                                                                       |

---

## API Keys

Keys are stored encrypted in the NullWave data directory - never in the project folder or git history. Manage them via **Settings → API Keys** inside the app, or set environment variables as fallback:

```bash
export NULLWAVE_YOUTUBE_KEY="your_key"
export NULLWAVE_LASTFM_KEY="your_key"
export NULLWAVE_OPENWEATHER_KEY="your_key"
```

---

## Requirements

- **.NET 10 Runtime** (LTS, supported until Nov 2028)
- **libVLC** (Bundled in Windows installer; Linux requires `sudo dnf install vlc-libs` or `sudo apt install libvlc-dev`)
- **yt-dlp** (`winget install yt-dlp.yt-dlp` or `pip install yt-dlp`)
- **JavaScript Runtime** (Deno ≥2.3.0 **recommended**, or Node.js ≥22.0.0, or QuickJS on PATH - required for YouTube signature challenges)
- **FFmpeg** (Optional, but highly recommended for audio transcoding and embedding metadata/album art)

**Windows note:** Requires .NET 10 Runtime. LibVLC native libraries auto-installed via NuGet. yt-dlp installed via winget.

**Linux note:** libVLC installs to `/usr/lib64` on Fedora. NullWave expects `/usr/lib`. Create symlinks if needed:

```bash
sudo ln -s /usr/lib64/libvlc.so /usr/lib/libvlc.so
sudo ln -s /usr/lib64/libvlccore.so /usr/lib/libvlccore.so
```

Optional secure key storage requires `libsecret-tools` on Debian/Ubuntu or `libsecret` on Fedora.

---

## Building

```bash
git clone https://github.com/ZenQuantDev/NullWave.git
cd NullWave
dotnet build
dotnet run
```

### Release packaging (Windows)

```powershell
.\build-release.ps1                         # win-x64, v0.6.5, full + fx
.\measure-startup.ps1 -Rid win-x64 -Iterations 7
```

Upload everything in `publish-artifacts/` to the GitHub Release. The `fx` installer fetches the .NET 10 Desktop Runtime; `full` bundles it. Bundled LibVLC is pruned to an audio-only plugin set, so local `.webm`/`.mka` files are not supported in release builds.

---

## Data Directory

In release builds, the data directory defaults to `%APPDATA%\NullWave` on Windows and `~/.nullwave/` on Linux/macOS. Debug builds use `~/NullWaveDev` to avoid touching release data. Set `NULLWAVE_HOME` to override the data directory on any platform.

```
<data directory>/
  keys.enc          ← encrypted API keys
  keys.enc.v1.bak   ← one-time legacy key backup during migration
  file-ops.json     ← file-operation audit journal
  profile.json      ← local user profile (username, bio, avatar path)
  avatar.png        ← profile picture
  library.db        ← SQLite music library
  prefs.json        ← preferences
  plugins/          ← user-installed plugins (override bundled plugins by ID)
  downloads/        ← yt-dlp audio downloads
  art/              ← cached album art (YouTube thumbnails, SoundCloud covers)
  logs/
    NullWave-DDMMYYYY.log       ← all events
    UserActions-DDMMYYYY.log    ← user action log
    Errors-DDMMYYYY.log         ← errors with source attribution
```

Bundled plugins are shipped beside the application executable under `plugins/`. User-installed plugins live in `<data directory>/plugins/` and are checked first.

## File Safety

Export and organize operations work on copies, leaving originals unchanged. Tag writes follow
the selected `TagWriteMode`; `Always` is an explicit opt-in that permits writes to any existing
file. Explicit removal sends managed audio to the OS Recycle Bin or Trash, while unmanaged
files are left untouched. The file-operation journal is for auditing only; undo is not
implemented.

---

## Project Structure

```
NullWave/
  Themes/               ← design system (Colors, Typography, Shapes, ControlStyles)
  Models/               ← Track, Playlist, UserProfile
  ViewModels/           ← MVVM ViewModels
  Views/
    Controls/           ← UserControls (Sidebar, MiniPlayer, TrackList, etc.)
  Services/             ← business logic (Library, Playback, Download, Metadata, etc.)
  plugins/
    NullWave.Plugins.Contracts/     ← plugin contracts shared with the host
    NullWave.Plugins.OpenWeather/   ← bundled weather provider
    NullWave.Plugins.RadioBrowser/ ← bundled radio directory provider
  Helpers/
    Logging/            ← NullActionLogger, NullWaveLogConfig
```

---

## Roadmap

See [ROADMAP.md](ROADMAP.md) for the full phased development plan.

---

## Author

ZenQuant

## License

MIT with attribution - see [LICENSE](LICENSE). Third-party notices are in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). See [DISCLAIMER.md](DISCLAIMER.md) for terms of use.
