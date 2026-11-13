# Privacy Policy

## Local-First Architecture

NullWave is a local-first application. Your music library, playlists, listening history, and preferences are stored on your device. Release builds use `~/.nullwave/` on Linux/macOS and `%APPDATA%\NullWave\` on Windows; debug builds use `~/NullWaveDev`. Set `NULLWAVE_HOME` to override the data directory. We do not operate servers, collect analytics, or transmit your personal data.

## Third-Party Network Contacts

When you use certain features, NullWave may contact these external services:

### App Updates

- **api.github.com / github.com**: Opening Settings → Updates, or pressing Check, contacts GitHub for NullWave releases (installed builds only) and the latest yt-dlp version.

### Music Metadata & Streaming

- **ws.audioscrobbler.com**: Last.fm scrobbling and metadata (requires your API key)
- **www.googleapis.com**: YouTube Data API for video metadata (requires your API key)
- **img.youtube.com**: YouTube video thumbnails
- **api.deezer.com**: Fallback metadata service
- **open.spotify.com**: Spotify page metadata (public scraping, no API key)

### Weather & Mood (Optional)

- **api.openweathermap.org**: Weather data for mood-based playlists (requires your API key)

### Radio & Streaming

- **radio-browser.info** (and its volunteer-run mirror servers): Community radio station directory. Searches and station lookups are sent to one of these servers. Resolving a station's stream URL for playback registers one "play" with the directory. Radio streams themselves connect directly to each station's own server.
- **somafm.com**: SomaFM internet radio streams

### External Plugins

External plugins run in-process with NullWave's own privileges and may make their own network calls. The statement that NullWave does not transmit your data applies to NullWave itself, not to third-party plugins.

### Downloads (via yt-dlp)

- **Various platforms**: YouTube, SoundCloud, Bandcamp, Audiomack, Archive.org, Mixcloud, and 1000+ other sites supported by yt-dlp
- **bandcamp.com**: Bandcamp audio downloads and metadata
- **audiomack.com**: Audiomack audio downloads and metadata
- **archive.org**: Internet Archive audio downloads
- **mixcloud.com**: Mixcloud audio downloads
- **github.com**: yt-dlp version checks and updates

### Local AI

- **ollama.ai**: Downloads AI models for local mood playlist generation (runs entirely on your device)

All API keys are stored locally in an encrypted keystore on your device. Third-party services have their own privacy policies and see your IP address when contacted.

## AI Features

AI-powered features (mood playlists, smart sorting) run locally on your device via Ollama. No audio, metadata, or listening habits are sent to external AI providers. All processing happens on your machine.

## Data Deletion

You have full control over your local files:

- Use the "Remove" context menu on individual tracks
- Run "Sweep orphaned files" in Settings → Maintenance
- Delete the data folder (`~/.nullwave/` or `%APPDATA%\NullWave\`) to remove everything

## Contact

NullWave is open source. Review the code, report issues, or contribute at:
https://github.com/ZenQuantDev/NullWave
