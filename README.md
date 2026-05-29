# 🎵 NullWave

A personal music organizer with download and playback capabilities, built with C#/.NET 8 and Avalonia UI on Linux.

## About

NullWave lets you save, organize, download, and play music from YouTube, Spotify, Last.fm, SoundCloud, and local files in one unified library. Organizer-first, player second.

> ⚠️ Please read [DISCLAIMER.md](DISCLAIMER.md) before using NullWave.

## Features

### Organizer
- Add tracks by URL (YouTube, Spotify, Last.fm, SoundCloud) or local file
- Auto-fetch metadata from YouTube Data API v3 and Last.fm
- Read ID3 tags from local audio files (MP3, FLAC, WAV, OGG, M4A, AAC)
- Library with search, sort, favorites, play tracking, and queue
- Bulk folder import with subfolder support and duplicate detection
- Track detail panel with editable title, artist, notes, and tags
- Playlist management
- Export library to JSON or CSV
- Source filters (YouTube, Spotify, Last.fm, SoundCloud, Local)

### Security
- Encrypted local API key storage (AES-256-GCM, machine-bound)
- Secure 3-pass data wipe (keys, logs, everything)
- Log redaction — API keys never appear in log output
- Keys stored at `~/.nullwave/keys.enc`, never in project folder

### Playback & Download
- Local file playback via LibVLCSharp
- YouTube audio download via yt-dlp (downloads to `~/.nullwave/downloads/`)
- Mini player bar with play/pause/stop and progress display

## Tech Stack

- .NET 8, C#, Avalonia UI 12 (MVVM)
- LibVLCSharp + system libVLC (playback)
- yt-dlp (download)
- TagLib# (ID3 tag reading)
- Serilog (logging with redaction)
- xUnit (23 tests passing)
- YouTube Data API v3
- Last.fm API
- SQLite + EF Core (coming — Phase 5)

## API Keys

Keys are stored encrypted at `~/.nullwave/keys.enc` — never in the project folder.
Manage them via **Settings → Open Settings** inside the app, or set environment variables as fallback:

```bash
export NULLWAVE_YOUTUBE_KEY="your_key"
export NULLWAVE_LASTFM_KEY="your_key"
```

## Requirements

- .NET 8 SDK
- libVLC (`sudo dnf install vlc-libs` on Fedora)
- yt-dlp (`pip install yt-dlp`)

## Building

```bash
git clone https://github.com/Alexsandrgardaphadze/NullWave.git
cd NullWave
dotnet build
dotnet run
```

## Roadmap

See [ROADMAP.md](ROADMAP.md) for the full development plan.

## Author

ZenQuant — PackItPro Team

## Version

v0.1.2

## License

MIT — see [LICENSE](LICENSE). See [DISCLAIMER.md](DISCLAIMER.md) for terms of use.