# Sources & API keys

NullWave downloads from many sites through a single **yt-dlp** provider and enriches/scrobbles
through **Last.fm**. External radio comes from the bundled **Radio Browser** plugin. The source
chips in the sidebar are driven by `SupportedSites`, so what you see reflects what's registered.

## What needs a key, and what happens without one

A missing key **disables that provider; it never crashes the app.** The startup log proves it:
`Key: YouTube → missing`, `Key: LastFm → missing`, `Key: SoundCloud → missing`,
`Key: Spotify:ClientId → missing`, and `[com.nullwave.openweather] OpenWeather API key not
configured` → the plugin reports `state=Unavailable` and is simply inactive. Your local library,
keyless radio browsing, and already-downloaded tracks keep working. Configure keys in
**Settings → Integrations**.

| Provider | Needs a key | Covers |
| -------- | ----------- | ------ |
| yt-dlp (download) | no (the binary is probed, not a key) | YouTube, SoundCloud, Bandcamp, Audiomack, Archive.org, Mixcloud |
| Last.fm (metadata + scrobble) | yes | tag/art backfill; scrobbles a track once it's >50% played |
| YouTube Data API | optional | richer metadata; falls back to yt-dlp embedded metadata on a BadRequest |
| Spotify | optional (`Spotify:ClientId`) | metadata bridge via `SpotifyPageParser` |
| OpenWeather (mood hints) | yes | powers the Smart-Sorting weather→mood pipeline |
| Radio Browser (radio) | **no** | live station search + top stations, no account |

## Radio Browser (the first external plugin)

Ships as a separate assembly referenced only against `NullWave.Plugins.Contracts`. Imported
stations carry a `radiobrowser:<uuid>` tag. On playback failure the current URL is re-resolved
**click-free** and playback retries; a **Maintenance** action previews and repairs all stations in
bulk. Stations missing from the directory are tagged `radio:dead` and **never deleted**. Auto-repair
is a persisted preference.

## How plugins load (and why they appear late)

External plugins load from `<data>/plugins/<name>/plugin.json` in **isolated `AssemblyLoadContext`**
instances, after the first frame, with manifest validation, a path-traversal guard, an 8 s
per-plugin timeout, and a `minHostVersion` gate. They appear in Settings → Integrations with their
real version/author/kind once loading finishes, and your on/off choice persists. (Isolation is an
`AssemblyLoadContext`, **not** an OS process sandbox — see `ARCHITECTURE.md`.)

---
*Sources: startup logs (`Registered provider: yt-dlp (…)`, `OpenWeather … state=Unavailable`),
`Services/FileSystem`/`SupportedSites`, the v0.6.5-beta.2 changelog (Radio Browser, plugin
discovery guards), `PRIVACY.md`/`SECURITY.md`.*