# Privacy Policy

## Local-First Architecture
NullWave is a local-first application. Your music library, playlists, listening history, and preferences are stored exclusively on your device in `~/.nullwave/`. We do not operate servers, collect analytics, or transmit your personal data.

## Third-Party Integrations
When you enable optional plugins, the following data may be shared with third-party services:

- **YouTube Data API** (Google): Video IDs, search queries, and metadata requests.
- **Last.fm**: Track titles, artists, and scrobble timestamps (opt-in, requires your API key).
- **OpenWeather**: Latitude/longitude coordinates for weather-based mood playlists (requires your API key).
- **yt-dlp**: Contacts various platforms to download audio. No data is sent to NullWave's servers (we have none).

All API keys are stored locally in an encrypted keystore on your device.

## AI Features
AI-powered features (mood playlists, smart sorting) run locally on your device via Ollama. No audio, metadata, or listening habits are sent to external AI providers. AI-generated content is based on your local library; NullWave is not responsible for user-provided prompts.

## Data Deletion
You have full control over your local files. Use the "Remove" context menu, "Sweep orphaned files" maintenance tool, or delete the `~/.nullwave/` directory to permanently remove all data.

## Contact
NullWave is open source. Review the code, report issues, or contribute at [GitHub repo URL].