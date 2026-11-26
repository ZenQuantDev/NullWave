# Installation

NullWave ships as a Windows installer (Velopack) plus cross-platform builds from the release matrix.
It needs **VLC** for playback and **yt-dlp** for downloads; both are auto-probed at first run.

## Requirements

- **.NET 10 Desktop Runtime** — required only for the `fx` (framework-dependent) installer; the
  `full` installer bundles it.
- **VLC** — `PlaybackService` initializes LibVLC from the registry location (Windows) or
  `/usr/lib` symlinks (Linux/Fedora). Without it, local playback and radio are unavailable.
- **yt-dlp** — downloads from YouTube/SoundCloud/Bandcamp/Audiomack/Archive.org/Mixcloud. The app
  probes the version and can stage an update; on Windows it will not silently fall back to winget.
- **ffmpeg** — optional; improves transcode/demux coverage.
- **OS** — Windows 10/11 (x64), Linux (x64), macOS (arm64 + x64, unsigned).

## Windows

Run the installer:
- **`full`** — self-contained, bundles the .NET runtime. Largest, zero prerequisites.
- **`fx`** — framework-dependent, fetches the .NET 10 Desktop Runtime. Smaller.

Both channels come from `build-release.ps1` (`.\build-release.ps1 -Rid win-x64`). Bundled LibVLC is
pruned to an audio-only plugin set, so **local `.webm`/`.mka` files are not supported in release
builds** (use the dev `dotnet run` build, which keeps the full plugin set).

First run shows an onboarding wizard (data directory, API keys, theme). API keys are **optional** —
a missing key disables that provider (e.g. OpenWeather mood hints) without affecting the library.

## Linux

The release matrix publishes a self-contained `nullwave-linux-x64.tar.gz`. Extract and run the
binary; set `NULLWAVE_HOME` to relocate the data directory (default is the platform data dir via
`NullWavePaths`). On Fedora/SELinux systems, LibVLC may need symlinks into `/usr/lib`. An AppImage
is planned as a separate matrix step (needs `appimagetool` + a runtime).

## macOS

The matrix publishes unsigned `nullwave-osx-arm64.tar.gz` and `nullwave-osx-x64.tar.gz`. A signed,
notarized `.dmg` is a separate certified build step (cannot be produced from a non-Mac host).
Keyboard shortcuts map Ctrl→Cmd.

## Data directory

All state (library DB, art cache, plugins, prefs, logs, wallpapers) lives under the platform data
directory, overridable with `NULLWAVE_HOME`. The support bundle (Settings → DevTools) redacts local
paths and secrets before export.

## Troubleshooting

Encrypted (EFS) or locked files show a lock badge and refuse playback up front (one toast, no
auto-next cascade). Radio streams self-heal on failure via their `radiobrowser:<uuid>` tag. See
`docs/TROUBLESHOOTING.md` (landing next) for the full matrix.