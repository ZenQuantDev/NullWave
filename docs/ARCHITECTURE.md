# Architecture

NullWave is a single desktop application (`net10.0`, Avalonia 12.1.4) split into a core app,
shared plugin contracts, bundled plugins, and a test suite. This page maps the real tree, not an
ideal one; every claim cites the file it comes from.

## Repository layout

| Path | Responsibility |
| ---- | -------------- |
| `Program.cs` | Entry point: single-instance lock, platform render options, `StartWithClassicDesktopLifetime`. |
| `App.axaml.cs` | Avalonia lifecycle bootstrap: merges theme dictionaries (incl. `Themes/Motion.axaml`), localization init, anti-crash handlers. |
| `NullWave.csproj` | Main project: NuGet refs (Central Package Management via `Directory.Packages.props`), bundled-plugin copy targets, banned-API analyzer gate. |
| `Services/` | Business logic: `Library`, `Database`, `Audio` (PlaybackService/LibVLC), `FileSystem` (FileBrowserService), `Plugins`, `Security` (KeyStoreService), `Localization`, `SmartSorting`, `Download`, `Identity`. |
| `ViewModels/` | MVVM state: `Shell` (Main/Navigation/FileBrowser/Playlist), `Playback`, `Library`, `Settings`, `Tools`, `Base` (NotifyBase/ViewModelBase). |
| `Views/` | Avalonia XAML + code-behind: `Controls/` (Sidebar, TrackList, MiniPlayer, …), `Settings/` tabs, `FileBrowserView`, `MainWindow`, `ProfileWindow`, `SettingsWindow`. |
| `Models/` | Data shapes: `Library` (Track, Playlist, …), `FileSystem` (FileSystemNode), enums (`TrackSource`, `MediaType`, `SortField`). |
| `Helpers/` | Cross-cutting: `Localization` (Locales.cs/.ru.cs, LocExtension), `Diagnostics` (DiagnosticsRedactor, CrashHandler), `Converters`, `Attached` (AsyncImage, PixelShift, SmoothScroll, PseudoClass), `Logging` (NullActionLogger), `Imaging`. |
| `Themes/` | Design system: `Colors`, `Typography`, `Shapes`, `ControlStyles`, `Motion` (timing tokens). |
| `plugins/NullWave.Plugins.Contracts/` | The public plugin API (`IPlugin`, `IPluginHost`, `PluginManifest`, `ProviderKind`, `PluginState`) — packageable, no host dependency. |
| `plugins/NullWave.Plugins.{RadioBrowser,OpenWeather}/` | Bundled external plugins, referenced only against Contracts. |
| `tests/` | `NullWave.Tests` (xUnit, 793 tests) + `FakeYtDlp` harness. |
| `tools/BadgeIssuer/` | Offline badge-signing utility (v2 catalog, KeyId rotation). |

## Lifecycle: synchronous core, deferred heavy work

Critical services build **synchronously** so the window has data on first paint; hardware detection,
plugin discovery, and playback warm-up run **after the first-frame callback** (startup logs:
*First frame ~2.2 s*, *Plugins initialized ~3.2 s*). This is why a slow Ollama model or a cold
radio catalog never blocks the UI.

1. `Program.Main` → single-instance lock → render options → `App.Initialize` (theme dictionaries,
   localization, global exception boundaries) → `MainWindow` constructed → first frame.
2. Post-first-frame: `HardwareDetector` (background thread, RAM/AVX gates) → `PluginDiscovery`
   (loads `<data>/plugins/<name>/plugin.json` in **isolated `AssemblyLoadContext`** instances, with
   manifest validation, a path-traversal guard, an 8 s per-plugin timeout, and a `minHostVersion`
   gate) → `PlaybackService` LibVLC init → `BitmapPrewarm`.
3. Providers register (`yt-dlp`, `lastfm`, `openweather`, `ollama`, `radiobrowser`); a missing API
   key marks that provider *inactive* rather than crashing (logs: `OpenWeather API key not
   configured` → `state=Unavailable`).
4. Shutdown: flush Serilog sinks, stop plugins once, unload the AI model.

> **Honest caveat:** plugin isolation is an `AssemblyLoadContext`, **not** an OS process sandbox.
> A misbehaving plugin can still affect the host process; true process isolation is deferred.

## Data layer

`DatabaseService` (split into focused partials) owns SQLite in **WAL mode** with a `user_version`
schema and ordered, run-once migrations (current schema v11). `LibraryService` keeps an in-memory
**O(1) identity index** for duplicate detection, normalizes tracks through a single
`TrackNormalizer`, and enforces **strict UTC** everywhere (a Roslyn `BannedSymbols` analyzer rejects
`DateTime.Now` at build time). Playlist/folder/queue cascades are reversible via an undo capture.

## Motion contract

`Themes/Motion.axaml` is the single source for transition durations; consumers reference tokens via
`{DynamicResource …}` (never literals, never `StaticResource`, so `ThemeService` can rewrite them at
runtime for reduce-motion / effects-tier). Easings are uniformly `CubicEaseOut`. `EffectsTierResolver.Shared`
gates the `effects-minimal` tier (instant swaps, no fades). The Library filter-switch uses a
seq-guarded dip (`DurationDip`, floored opacity) so it reads like the page crossfade without a blank
frame; search and sort stay instant by design.

## Privacy & security surface

`KeyStoreService` stores API credentials AES-256-GCM (v2 keystore format; Linux also clears the
Secret Service entry on delete). `DiagnosticsRedactor` strips local paths and secrets from crash
reports, copied logs, and support bundles **before anything leaves the machine**. Profile badges are
v2-signed with KeyId rotation. See `PRIVACY.md` / `SECURITY.md` at the repo root.

---
*Sources: `Program.cs`, `App.axaml.cs`, `NullWave.csproj`, `Directory.Packages.props`,
`Services/Database/*`, `Services/Library/LibraryService.cs`, `Services/FileSystem/FileBrowserService.cs`,
`Services/Security/KeyStoreService.cs`, `Helpers/Diagnostics/DiagnosticsRedactor.cs`,
`Themes/Motion.axaml`, `plugins/NullWave.Plugins.Contracts/*`, startup logs.*