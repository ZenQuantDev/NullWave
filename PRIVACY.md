# Privacy Policy

## Local-First Architecture
NullWave is a local-first application. Your music library, playlists, listening history, and preferences are stored exclusively on your device in `~/.nullwave/` (Linux/macOS) or `%APPDATA%\NullWave\` (Windows). We do not operate servers, collect analytics, or transmit your personal data.

## Third-Party Network Contacts

When you use certain features, NullWave may contact these external services:

### Music Metadata & Streaming
- **api.openweathermap.org**: Weather data for mood-based playlists (requires your API key)
- **ws.audioscrobbler.com**: Last.fm scrobbling and metadata (requires your API key)
- **www.googleapis.com**: YouTube Data API for video metadata (requires your API key)
- **img.youtube.com**: YouTube video thumbnails
- **api.deezer.com**: Fallback metadata service
- **open.spotify.com**: Spotify page metadata (public scraping, no API key)

### Radio & Streaming
- **radio-browser.info**: Community radio station directory
- **somafm.com**: SomaFM internet radio streams

### Downloads (via yt-dlp)
- **Various platforms**: YouTube, SoundCloud, Bandcamp, and 1000+ other sites supported by yt-dlp
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