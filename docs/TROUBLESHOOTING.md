# Troubleshooting

## A file won't play / shows a lock badge

Encrypted (EFS) or locked audio files show a **lock badge with a clear reason**, and playback
**refuses unreadable local files up front** — one toast, no auto-next cascade. Crossfade no longer
consumes a queued track whose file is unreadable. In the logs this surfaces as
`[FileBrowser] Tag read failed for <path>` with `System.UnauthorizedAccessException: Access to the
path '…' is denied` (common on secondary drives, phone-mounts, and network paths with non-ASCII
names). The fix is permissions/decryption on the file itself, not a NullWave setting.

## A radio station went silent

Imported Radio Browser stations self-heal: on failure the current URL is re-resolved from the
station's `radiobrowser:<uuid>` tag and playback retries, click-free. Use **Maintenance → repair
radio streams** to preview and fix all stations in bulk. Stations gone from the directory are tagged
`radio:dead` and never deleted. Auto-repair is a persisted preference.

## Local `.webm` / `.mka` don't play in a release build

Release builds prune bundled LibVLC to an **audio-only plugin set**, so Matroska/WebM containers
aren't demuxed there. The dev build (`dotnet run`) keeps the full plugin set and plays them. This is
a deliberate release trade-off (smaller installer), documented in the README.

## A provider/mood feature is missing

A missing API key marks that provider inactive (see `SOURCES.md`); it does not break the library.
OpenWeather specifically: no key → no weather-based mood hints, everything else unaffected.

## Getting logs / a support bundle

**Settings → DevTools** exports a support bundle (preferences + recent logs). Run with `--dev` (or
`NULLWAVE_DEV=1`) to enable dev overlays and the full log detail.

### About redaction — read this before trusting "paths are redacted"

Crash reports, copied logs, and support bundles are run through `DiagnosticsRedactor` **before
anything leaves the machine**. The *strong, verifiable* coverage is the **user-profile prefix**
(`C:\Users\<name>`, `/home/<name>`) and **known secret keys**. The implementation we can inspect
does **not** guarantee redaction of secondary-drive / network roots (e.g. `D:\…`) or deeper trailing
path segments — those may remain in some outputs. **Verify your build's actual coverage** before
relying on the broad claim:

```powershell
Select-String -Path Helpers\Diagnostics\DiagnosticsRedactor.cs -Pattern 'Regex|Replace|Users|home|Redact' |
  Select-Object LineNumber, Line