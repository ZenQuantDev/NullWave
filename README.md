# 🎵 NullWave

A personal music organizer with download and playback capabilities, built with C#/.NET 8 and Avalonia UI on Linux.

## About

NullWave lets you save, organize, download, and play music from YouTube, Last.fm, SoundCloud, and local files in one unified library. Organizer-first, player second.

> ⚠️ Please read [DISCLAIMER.md](DISCLAIMER.md) before using NullWave.

---

## Features

### Organizer
- Add tracks by URL (YouTube, Last.fm, SoundCloud) or local file
- Auto-fetch metadata from YouTube Data API v3 and Last.fm
- Read ID3 tags from local audio files (MP3, FLAC, WAV, OGG, M4A, AAC)
- Library with search, sort, favorites, play tracking, and queue
- Bulk folder import with subfolder support and duplicate detection
- Track detail panel with editable title, artist, notes, and tags
- Playlist management
- Export library to JSON or CSV
- Source filters (YouTube, Last.fm, SoundCloud, Local)

### Player
- Local file playback via LibVLCSharp
- YouTube audio download via yt-dlp (to `~/.nullwave/downloads/`)
- Spotify-style now-playing bar — album art, controls, volume slider
- Play/pause/stop with position display

### UI
- Dark theme — deep navy + purple accent color system
- Spotify-inspired now-playing bar (3-column layout)
- Discord-style local profile bar in sidebar (username, bio, avatar, settings gear)
- Alt-key menu bar toggle (hidden by default, Firefox-style)
- Design system in `Themes/` — colors, typography, shapes, and control styles all in one place
- Local user profile — username, bio, avatar — no account or login required

### Security
- Encrypted local API key storage (AES-256-GCM, machine-bound)
- Secure 3-pass data wipe (keys, logs, everything)
- Log redaction — API keys never appear in log output
- Keys stored at `~/.nullwave/keys.enc`, never in project folder

### Logging
- Structured startup diagnostics on every launch (version, OS, API key status, connectivity, VLC/yt-dlp versions)
- Three separate log files: system events, user actions, errors with source attribution
- Every error tagged with the ViewModel or Service that caused it

---

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Language / Framework | C# 12, .NET 8, Avalonia UI 12 (MVVM) |
| Playback | LibVLCSharp + system libVLC |
| Download | yt-dlp |
| Metadata | TagLib# (ID3), YouTube Data API v3, Last.fm API |
| Logging | Serilog (3 sinks: system, user-actions, errors) |
| Security | AES-256-GCM encrypted KeyStore |
| Testing | xUnit (23 tests) |
| Persistence | SQLite (coming — Phase 7) |

---

## API Keys

Keys are stored encrypted at `~/.nullwave/keys.enc` — never in the project folder or git history.

Manage them via **Settings → Open Settings** inside the app, or set environment variables as fallback:

```bash
export NULLWAVE_YOUTUBE_KEY="your_key"
export NULLWAVE_LASTFM_KEY="your_key"
```

---

## Requirements

- .NET 8 SDK
- libVLC — `sudo dnf install vlc-libs` (Fedora) or `sudo apt install libvlc-dev` (Debian/Ubuntu)
- yt-dlp — `pip install yt-dlp`

**Fedora note:** libVLC installs to `/usr/lib64`. NullWave expects `/usr/lib`. Create symlinks:
```bash
sudo ln -s /usr/lib64/libvlc.so /usr/lib/libvlc.so
sudo ln -s /usr/lib64/libvlccore.so /usr/lib/libvlccore.so
```

---

## Building

```bash
git clone https://github.com/Alexsandrgardaphadze/NullWave.git
cd NullWave
dotnet build
dotnet run
```

---

## Data Directory

Everything NullWave writes lives under `~/.nullwave/`:

```
~/.nullwave/
  keys.enc          ← encrypted API keys
  profile.json      ← local user profile (username, bio, avatar path)
  avatar.png        ← profile picture
  library.db        ← music library (coming — Phase 7)
  downloads/        ← yt-dlp audio downloads
  art/              ← cached album art (coming — Phase 6)
  logs/
    NullWave-YYYYMMDD.log       ← all events
    UserActions-YYYYMMDD.log    ← user action log
    Errors-YYYYMMDD.log         ← errors with source attribution
```

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
  Helpers/
    Logging/            ← NullActionLogger, NullWaveLogConfig
```

---

## Roadmap

See [ROADMAP.md](ROADMAP.md) for the full phased development plan.

---

## Author

ZenQuant — PackItPro Team

## Version

v0.1.3

## License

MIT — see [LICENSE](LICENSE). See [DISCLAIMER.md](DISCLAIMER.md) for terms of use.