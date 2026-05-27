# 🎵 NullWave

A personal music collector and organizer desktop app built with C# and Avalonia UI on Linux.

## About

NullWave lets you save, organize, and manage music from YouTube, Spotify, SoundCloud, and local
files in one unified library. Organizer-first, player second.

> ⚠️ Please read [DISCLAIMER.md](DISCLAIMER.md) before using NullWave.

## Features

- Add tracks by URL (YouTube, Spotify, SoundCloud) or local file
- Auto-fetch metadata from YouTube Data API v3
- Library with search, sort, favorites, play tracking, and queue
- Playlist management
- Export library to JSON or CSV
- Encrypted local API key storage (AES-256-GCM, machine-bound)
- Secure data wipe (keys, logs, all data)
- Serilog logging with API key redaction
- Settings window with full key management

## Tech Stack

- .NET 8, C#
- Avalonia UI 12 (MVVM)
- Serilog (logging)
- xUnit (23 tests passing)
- YouTube Data API v3
- Spotify Web API (coming soon)
- SoundCloud API (coming soon)
- SQLite + EF Core (coming soon)
- yt-dlp (coming soon)
- NAudio / LibVLCSharp (coming soon)

## API Keys

NullWave stores API keys encrypted on your machine at `~/.nullwave/keys.enc`.
Keys never touch the project folder or git history.

You can manage keys via **Settings → Open Settings** inside the app,
or set them as environment variables as a fallback:

```bashexport NULLWAVE_YOUTUBE_KEY="your_key_here"
export NULLWAVE_SPOTIFY_CLIENT_ID="your_key_here"
export NULLWAVE_SPOTIFY_CLIENT_SECRET="your_key_here"
export NULLWAVE_SOUNDCLOUD_CLIENT_ID="your_key_here"

## Building

```bashgit clone https://github.com/Alexsandrgardaphadze/NullWave.git
cd NullWave
dotnet build
dotnet run

Requires .NET 8 SDK.

## Roadmap

See [ROADMAP.md](ROADMAP.md) for the full development plan.

## Author

ZenQuant — PackItPro Team

## Version

v0.1.1

## License

MIT — see [LICENSE](LICENSE). See [DISCLAIMER.md](DISCLAIMER.md) for terms of use.