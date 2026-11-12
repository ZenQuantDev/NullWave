# NullWave Master Guide (living document)

> Single source of truth for everything planned, in flight, and shipped-but-unverified.
> Supersedes and replaces: all earlier MASTER-GUIDE editions, all addenda, the post-0.6.0 plan,
> the wallpaper-scenes guide, and every per-feature implementation guide. Superseded files move
> to `/docs/archive` and are never edited again.
> Public mirrors: `CHANGELOG.md` (shipped history only) and `ROADMAP.md` (phase overview).
> They are updated FROM this guide, never the reverse.
> Lifecycle rule: an item lives in this guide until it is shipped, verified, and documented.
> The commit that closes an item updates this guide in the same commit.

---

## 0. Legend, rules, and definitions

### 0.1 Legend

| Mark | Meaning                                                           |
| ---- | ----------------------------------------------------------------- |
| ✅   | Shipped: code + tests + evidence recorded                         |
| 🟡   | Implemented, awaiting verification (manual check or missing test) |
| 📋   | Scoped, not started                                               |
| 💡   | Parked idea; not scoped                                           |
| [?]  | Claim not yet verified in running code; treat as unproven         |

### 0.2 Working rules

1. One change at a time: failing test → fix → full test run → commit.
2. No `FIX:`/`SAFETY:` comment without a test that fails without it.
3. Explain-back: every changed line can be explained with its failure mode and its thread.
4. No destructive test unless the data folder is provably temporary (sandbox-home guard).
5. Database tests share one collection so they never run in parallel against one file.
6. Comments and docs never claim behaviour the code lacks.
7. Branches: `fix/*`, `feature/*`, `refactor/*`; merge at checkpoints, never half-done work.
8. Main stays green: merge only when CI is green on both OSes; the merge gate is enforced by
   branch protection, not by discipline.
9. Docs are code: the docs-truth tests check version agreement and the network-host list.
10. Verify, then claim: anything not run and observed is marked `[?]` here until it is.
11. This guide is part of the repo. Closing an item without updating this guide is an
    incomplete commit.

### 0.3 Definition of done (per checkpoint)

- CI green on Windows and Ubuntu; zero unexplained skipped tests.
- Every item contributed at least one test or an explicit reason it cannot have one.
- Manual checks listed for the item were run and their result recorded here.
- CHANGELOG, README, ROADMAP and this guide agree with the code.
- Version bumps happen only in release commits. Published tags are never moved.

---

## 1. Shipped history (read-only; do not reopen)

| Version                   | Date        | Summary                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                           |
| ------------------------- | ----------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| v0.5.0 "Blue Orchid"      | Jul 2026    | Sidebar drag-drop and folders, active-playlist context, instant refresh, live play counts, unified context menus, search shortcut, hover flyouts, Windows compatibility.                                                                                                                                                                                                                                                                                                                                                                                                                                          |
| v0.5.1                    | Aug 2026    | Metadata fixes, playlist URL interception, skip-penalty decay, AI playlist padding.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                               |
| v0.5.2                    | 17-Aug-2026 | Grouped action toasts, multi-select with bulk bar, proxy support, help tab and onboarding.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                        |
| v0.6.0 "Oxeye Daisy"      | 19-Sep-2026 | Audiobook mode, live radio with ICY metadata, crossfade, profile window with badges, RU localization, rolling backups and restore, light/system themes, download manager.                                                                                                                                                                                                                                                                                                                                                                                                                                         |
| v0.6.1                    | 23-Sep-2026 | Test foundation, atomic writes and quarantine, badge pinning, download stability.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                 |
| v0.6.2                    | 02-Oct-2026 | Download concurrency fixes, mood-AI output schema, unified tag taxonomy, Spotify bridge fixes, unified process runner, in-app log viewer, What's-New screen, wave seek bar, URI-scheme foundation.                                                                                                                                                                                                                                                                                                                                                                                                                |
| v0.6.3                    | 11-Oct-2026 | Async hardware detection with CPU gates, FTS5 global search, thumbnail sidecars and decode dedupe, VLC probing, cached tool checks, wallpaper scenes with a low-power effects tier, presets carrying scenes.                                                                                                                                                                                                                                                                                                                                                                                                      |
| v0.6.4 "Foundations"      | 24-Oct-2026 | AND-search and global search routing, sort catalog and length sort, honest headers, column alignment, clipboard pre-fill; playlist rename block and safe merge, migration runner, consistent backups, cover lifecycle, cascading deletes with undo, radio repair migration, WAL flush and FTS rebuild; settings single-save path with change events and versioning; unified track normalizer; strict UTC with banned-API analyzer; folder reorganization, database split, O(1) duplicate index, safe commands, dispose patterns; radio discovery and buffering UI; yt-dlp JS-runtime detection; badge catalog v2. |
| v0.6.5-beta.1 "Hardening" | 31-Oct-2026 | .NET 10 LTS, keystore v2 (OS-native secret storage with auto-migration), first-run download consent, three smart playlists, M3U import/export with preview, provider registry foundation (UI only), four new audio sources, culture-invariant parsing, accent-insensitive search, font fallback chains, download recovery tag, format and rate-limit fixes, YouTube API fallback. Tag is immutable; its notes carry known inaccuracies, see R1.                                                                                                                                                                   |

### 1.1 On the release branch, not yet published (beta.2 candidate)

- Mix/playlist front door with remembered choice; failed-add quarantine; dev sandbox home;
  culture matrix tests. ✅
- Playback-stop: native stop off the UI thread under a generation lock; consent checked before
  database insert (no ghost rows); UI hang watchdog; queue coalescing; navigator caching;
  relink fallback; duration caching. 🟡 (implemented, verification pending: V5)
- Updates-honesty: dev/installed/pre-release detection, exact exit-code handling for package
  managers, parallel dependency checks. 🟡 (implemented, verification pending: V8)
- Settings reorganization: Integrations and Downloads tabs, Maintenance absorbs the danger
  zone and diagnostics; consent revoke UI. ✅
- File safety N1–N6: unified file namer with byte limit and recovery suffix; tag-write mode
  with one central write rule; import Keep/Copy with duplicate check before copy and an
  audit journal; OS-native Recycle Bin removal; export pipeline that copies, renames and
  retags copies only (legacy ID3, relative playlist). 🟡 (implemented, verification pending)
- Keystore K1–K5: Linux data-loss path closed, locked-store fallback, unknown/newer formats
  open read-only and are never quarantined, secure temp files, fallback notice and Secret
  Service cleanup. 🟡 (implemented, verification pending)
- CI: Windows + Ubuntu matrix with banned-API build gate; both checks required to merge. ✅
- Test suite: 752 passing locally on Windows; Ubuntu re-run pending on the close-out commits. 🟡
- Plugin system: Contracts SDK (including `IRadioProvider`), isolated discovery with manifest
  validation, path-traversal guard, 8-second initialization timeout and `minHostVersion` gate;
  permission-checked PluginHost, adapter-to-ProviderRegistry routing, persisted enable state
  and dynamic Settings registration. ✅
- Radio Browser plugin + catalog dialog: debounced search, curated offline fallback, duplicate
  detection, click-free UUID lookup. ✅
- Self-healing radio streams: click-free UUID repair, auto-reconnect on failure, bulk
  preview/repair, `radio:dead` tagging. 🟡 (manual UI checks pending)
- Plugin packaging: build and publish outputs include Radio Browser and OpenWeather under
  app-local `plugins/`; dual-root discovery checks user plugins first. 🟡 (fresh-data-dir app
  startup still needs manual verification; same-Id precedence has automated coverage)

---

## 2. Current state snapshot

- App version on the release branch: 0.6.5-beta.2 (unpublished). Published tag: 0.6.5-beta.1.
- Release branch is ahead of origin; close-out commits not yet pushed.
- CI: matrix configured and both jobs required by branch protection; not yet run on the
  beta.2 close-out changes. Ubuntu green is unproven for this cycle.
- Tests: 752 passing locally on Windows. Ubuntu pending.
- Merge gate proven earlier by a deliberate-break run (banned API fails both jobs).
- Open hard gates before tagging: §3.12 plugin packaging, §3.8 R1 release truth,
  §3.2 reduced smoke matrix.

---

## 3. In flight: close out v0.6.5-beta.2

### 3.1 Process gates 🟡 (evidence recorded)

- For a release candidate, open a draft PR from the release branch into main and merge only
  after both required checks are green.
- Branch protection on main: PR required, both CI jobs required, force pushes blocked,
  deletions restricted. Decide merge style once (squash vs merge commit) and keep it.
- Deliberate-break check: evidence recorded from PR #12. Both required jobs failed at build
  on RS0030; merge was blocked and the PR was closed unmerged.
  **Evidence:** PR #12 head `138e063` (commit `965d0dd`) injected `BreakCheck` with `DateTime.Now`. Both required jobs failed:
    - `build-and-test (windows-latest)`: `Services/Library/LibraryService.cs(460): error RS0030: Do not use 'DateTime.Now'`
    - `build-and-test (ubuntu-latest)`: same error
      Merge button stayed blocked. PR closed unmerged; branch deleted (`git ls-remote --heads origin test/rs0030-gate-check` returns nothing).

### 3.2 Manual verification matrix (each row blocks the beta.2 tag)

| #   | Check                    | How                                                         | Expected                                                                  | Status  |
| --- | ------------------------ | ----------------------------------------------------------- | ------------------------------------------------------------------------- | ------- |
| V2  | Windows migration        | Start on real data after upgrade                            | Key file shows v2 magic or log says already v2; one-time backup is cleaned | Pending |
| V5  | Rapid playback           | Click through 5–6 tracks quickly                            | No multi-second freeze, slow-playback log lines or hang dumps              | Pending |
| V6  | Consent decline          | Decline consent for a single URL and a playlist URL         | Library count unchanged; no file-less rows                                | Pending |
| V8  | Updates tab on dev build | Check updates and dependencies                              | Dev-build message shown; dependency versions populate in parallel         | Pending |

Not run; do not report as verified: V1 Linux keyring path, V3 double import in Copy mode,
V4 export safety, V7 sweep honesty, and V9 TagWriteMode persistence. V10's English/Russian
playlist-result key parity is covered by `SettingsLocalizationTests`; no manual UI result is
claimed.

### 3.3 C8 — orphan sweep caller fixes 🟡

Evidence: CHANGELOG lists sweep safety under Fixed; MaintenanceTab exposes separate
`PreviewOrphanedFilesCommand` and `SweepOrphanedFilesCommand` buttons.
Unverified: (a) guard-refusal explanation text, (b) confirm dialog showing preview counts
before a real run, (c) "move to orphans folder" wording everywhere, (d) missing/unwritable
download-folder fallback. V7 not run.

### 3.4 S7 — consent before silent installs ✅

- `RunPackageInstallAsync` returns `Approved=false` with a null process result before any
  process starts; `InstallVlcAsync` surfaces `InstallCancelledStatus`; remembered choice
  honored via `PackageManagerInstallConsent`.
- yt-dlp winget/pip fallbacks removed so they cannot bypass the gate.
- Tests: `PackageInstallConsentTests` verifies declined consent starts no process and that
  the prompt receives the exact command string.

### 3.5 T1 — regression coverage 🟡

1. Tag-write rule: ✅ `TagWriteModeTests` covers Never/DownloadsOnly/Always, default and
   configured folders, the `[nw-` recovery marker (case-insensitive), sibling-directory
   prefix boundaries, missing files, null/empty paths, and the default-directory regression.
   The previous `Enum.IsDefined` assertion on an already-typed enum was a tautology.
2. Relink with recovery suffix: ✅ `LibraryFileSafetyTests.TryRelink_finds_FileNamer_name_with_new_nw_id_suffix`.
3. Removing an unmanaged row leaves the file untouched: ✅ `LibraryFileSafetyTests.Removing_unmanaged_track_deletes_only_the_library_row`.
4. Export leaves originals byte-identical: ✅ `LibraryFileSafetyTests.Folder_export_keeps_original_file_bytes_unchanged`.

### 3.6 K5 — keystore honesty and cleanup 🟡

CHANGELOG lists the fallback notice and Linux secret-store deletion under Added.
Unverified: the notice text names the fix; injectable command-runner tests for
timeout / not-found / locked. Fedora proof still pending (V1).

### 3.7 L1 — localization and wording debt 🟡

Test-gated surface is complete: `SettingsLocalizationTests` enforces EN+RU parity for
Downloads, Integrations, Maintenance, General and the consent/confirm dialogs; the radio
catalog and Radio Streams card are localized.
Remaining: hardcoded English in view-model toast strings (radio heal, catalog add/resolve
failures) is not covered by that test. Experimental banner wording audited per CHANGELOG.

### 3.8 R1 - release truth (gate: before publishing beta.2) 📋

- Identify the exact commit the published tag and each installer artifact were built from.
- Review release-note statements against shipped behavior without publishing test counts.
- Correct the published release notes to shipped truth (soften or remove unshipped claims);
  add a keys-migration and downgrade note.
- The published tag and artifacts are never moved or re-uploaded.

### 3.9 Docs sync pack ✅

- CHANGELOG beta.2, README file-safety and platform notes, ROADMAP release/phase state, and
  this guide were synchronized. Published beta.1 remains untouched; release notes omit local
  test counts, which are tracked in this guide.

### 3.10 Plugin system close-out 🟡

- Shipped: discovery + host + adapter + registry routing for Radio, catalog dialog,
  self-healing streams, persisted toggles, EN/RU strings for the new UI, and bundled plugin
  build/publish output with user-first dual-root discovery.
- Tests: enabled-state round-trip, manager manifest plumbing, adapter delegation,
  heal classification, plugin-root ordering, and user-over-bundled same-ID precedence.
- Pending manual: toggle off/on survives restart; external row shows real version and
  Radio tag; Store entry disappears once installed; fresh-data-dir load and same-Id user
  override; Preview/Repair on the real library; auto-reconnect on a genuinely dead stream.
- Deferred by decision: plugin uninstall, discovery-failure rows in the Installed tab,
  signed manifests + hash verification, sandboxed permission enforcement.

### 3.11 Tag-write policy correctness ✅

`IsManagedDownload` returned `true` for every path when `DownloadDirectory` equalled the
default `DownloadsDir`, making unrelated files "managed" under DownloadsOnly. Fixed by
extracting `TagWritePolicy` (pure, no DB/preferences dependency) and correcting the
same-directory case. The regression test
`DownloadsOnly_refuses_external_file_when_user_dir_equals_default_dir` failed against the
old implementation and passes with the fix.

### 3.12 Plugin packaging — HARD GATE 🟡

`NullWavePaths.PluginRoots` probes `<DataDir>/plugins` before app-local `plugins/`.
`NullWave.csproj` builds Radio Browser and OpenWeather as non-reference projects and copies
their payloads to build and publish output, excluding `NullWave.Plugins.Contracts.*`.
Discovery claims manifest Ids in root order so a user copy takes precedence over a bundled
copy. Both clean build and publish output contain the two plugin assemblies and manifests,
with no Contracts assembly in either plugin directory. `PluginRootOrderTests` verifies that
a same-Id user copy wins. Fresh-data-dir app startup still needs a manual check before beta.2.

### 3.13 Journal integrity ✅

`FileOperationJournal.PopLast()` had no callers and removed an audit entry without reversing
the associated file operation. The unused method was removed. `FileOpType` remains
`Copy/Move/Delete`; append `TagWrite` only when its first consumer is implemented so existing
numeric journal values are not changed.

---

## 4. Next: v0.6.6 "Launch Readiness"

### 4.1 Provider registry routing 📋

- Active-selection semantics wired for real: downloads and metadata route through the
  selected provider; single-source kinds use one provider, multi-source kinds may run several.
- Prove: switching the active provider changes which fake provider receives the call.

### 4.2 M3U importer hardening 📋

- Replace quadratic path normalization with an indexed lookup; strict matching (identity key
  plus path equality) instead of substring matching; guard path APIs against foreign or
  invalid paths so a hostile playlist cannot kill the preview.
- Manual: export → import round-trip preserves the playlist; a foreign playlist with missing
  files previews honestly with per-row status.

### 4.3 Docs wiki 📋

- Pages: install per OS, first run, sources and their limits, Mood Mix and local AI,
  troubleshooting, architecture (with diagram), testing, privacy.

### 4.4 Repo hygiene 📋

- Ignore build output and sync-conflict files; issue templates that ask for diagnostics;
  third-party notices file.

### 4.5 Docs-truth tests 📋

- No test counts are published in docs. Docs-truth tests cover only version agreement
  (csproj = CHANGELOG top = README) and the network-host list.

### 4.6 Packaging and platform decisions 📋

- Linux package format chosen and built (AppImage or Flatpak).
- Portable build audio verification: local formats, https radio, first-try import on a clean
  data directory.
- Decide and document the container/demuxer whitelist for the pruned playback engine
  (support local webm/matroska or state the refusal in install docs).

### 4.7 Performance budgets and visual audit remainder 📋

- Record Release-build baselines on the floor machine (old low-end laptop): cold start, idle
  memory, 5k-track scroll, stall histogram. Set targets; a checkpoint fails on >10%
  regression.
- Audit image prewarm and synchronous decode paths for UI-thread work; chunk or move
  off-thread.
- Cost audit: blur, per-row shadows, virtualization, HiDPI async decode.
- Window resizing: write a repro or close the item forever.
- Playlist URL processed twice at startup: verify whether still present [?]; dedupe or close.

### 4.8 UI and settings polish 📋

- Reusable settings row control; data-driven settings nav.
- Saved smart-search queries: model exists, wire the UI.
- Fix clipped detail-panel buttons; add the playing-row marker in filtered views.
- Menu bar show/hide fade; real SVG logo.
- Audit that every menu item and button has a live command.
- Updates tab v2 — design agreed, not started: app card as a state machine
  (unknown → checking → up-to-date → available → downloading → staged → error, plus
  terminal not-updatable for dev/system-managed builds); dependency table with
  Installed / Available / Source columns so the update action matches install provenance;
  last-checked stamp; release-notes expander; auto-check and pre-release toggles;
  plugin-compatibility rows driven by discovery failures. Update Plan (dry-run,
  consent-first, provenance-aware) and update journal deferred to v3. No plugin auto-update
  toggle until a store exists.

### 4.9 Freeze and clean-machine protocol 📋

- Feature freeze 1–2 weeks before launch; bug fixes only.
- Clean-machine pass on a VM, the old laptop, and Fedora: onboarding, first import, first
  consented download, first export, radio, audiobook resume, update check.
- Diagnostics copy includes schema version, SQLite version, notification and audio engine
  info.

---

## 5. Later: slipped items and v0.7

### 5.1 Notifications (slipped from 0.6.4) 📋

- Categories with global and per-category preferences; router in front of the toast service;
- native sink per OS with focus rule (errors always visible); settings page with a test
  button; audit of remaining user actions that deserve feedback.

### 5.2 Factory reset (slipped from 0.6.4) 📋

- Three tiers (settings, library, factory); reset-on-next-start marker file processed before
  anything else; always take a backup unless the user opts out.

### 5.3 Library reorganization and undo (v0.7) 📋

- Dry-run preview with template paths; the audit journal gains its first consumer: undo last
  import/organize.
- Move-import mode ships only after undo exists; until then it stays unimplemented by
  decision, not by accident.

### 5.4 Ecosystem (v0.7) 📋

- Media keys (Linux MPRIS, Windows SMTC); portable mode; macOS test-only build; opt-in
  LAN-only Subsonic server with a written threat model; multi-source search provider
  interface.

### 5.5 Files browser (external folders and removable media) 📋

v1 (read-only, no invariant change): browse drives / Music / pinned folders, play, queue,
add-to-library as reference. Reuses TrackListView over a filesystem source.
v2 (in-place editing, requires the §7.1 precondition): write-ahead backup retained until
confirmed, atomic replace with FAT/exFAT fallback and free-space check, volume-serial +
relative-path identity so a re-lettered stick is recognised and a different stick at the
same letter is never written to, read-only media detection, OneDrive placeholder warning,
`TagWrite` journal entries and "revert last tag edit".
Gate: `TagWritePolicy` is the shared decision point for both versions.

---

## 6. Parking lot (💡)

> Entry rule: nothing here enters a drop before 0.6.6 is done. Every idea keeps its smallest
> first version so it can be picked up without re-scoping.

| Idea                             | Smallest first version                                                                                                                             | Fit       |
| -------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------- | --------- |
| Library Doctor                   | One screen listing dead paths, duplicates, zero durations, inconsistent names, garbled tags; dry-run, fix, undo; reuses existing maintenance tools | Very high |
| Tag mojibake rescue              | Detect mis-decoded ID3 text and offer a re-decode preview                                                                                          | High      |
| Phone sync profile               | Saved export target with rules; copies only the difference                                                                                         | High      |
| Clipboard "Add this link?" toast | Resurface deferred clipboard pre-fill as a toast                                                                                                   | High      |
| Local yearly recap               | Generated from play history; shareable image; no telemetry                                                                                         | High      |
| Album-art accent extraction      | Sample dominant color of playing track; temporary UI tint                                                                                          | Med-High  |
| Waveform seek bar                | Precompute peaks offline; render inside the seek bar                                                                                               | Med-High  |
| Harmonic queue from tags         | Read existing key/BPM tags and queue compatible tracks; no analysis engine; avoid AGPL libraries                                                   | Med       |
| Audio fingerprint duplicates     | External fingerprint tool plus opt-in lookup                                                                                                       | Med       |
| Campfire (LAN shared queue)      | Needs pairing code and threat model first                                                                                                          | Med       |
| Stem separation                  | Offline, batch, experimental, off by default; not real-time                                                                                        | Low       |
| Now-playing facts card           | Text-only year/origin from public metadata                                                                                                         | Low-Med   |
| Synced lyrics                    | Line-highlight from a public lyrics source                                                                                                         | Med       |
| Equalizer and normalization      | 10-band EQ plus replay-gain toggle                                                                                                                 | Med       |
| Locked badges with progress      | Persisted seen-set, unlock toasts                                                                                                                  | Low       |
| Download process kill watchdog   | Kill child processes on cancel; no leaks                                                                                                           | Med       |
| Badge issuer key from env/file   | Stop passing secrets via CLI arguments                                                                                                             | Med       |
| Wallpaper parked items           | Album-art background, auto mode by time/weather, audio-reactive effects, live panel blur, saved/shared looks                                       | Med       |

---

## 7. Architecture constants (change only by explicit decision)

### 7.1 Invariants

- UTC everywhere in code; local-time APIs are banned at compile time.
- User-owned files are never hard-deleted: recycle/trash for explicit removal, orphans folder
  for sweeps.
- **Originals are immutable under export and organize**: those operations work on copies
  only. Tag writes are governed separately by `TagWriteMode`; `Always` is an explicit
  opt-in permitting tag writes to any existing file, including files outside the managed
  library. In-place *structural* changes to external originals (rename, move, overwrite)
  remain forbidden until the Files-browser write path ships with consent, a retained
  backup, and a journal entry.
- Consent gates every download and every package install; consent is single-flight and
  checked before any database insert.
- Keys never live in the repo or git history; OS-protected store with a disclosed weaker
  fallback; unknown or newer key formats open read-only and are never quarantined.
- Tests run only against sandbox data folders.
- Imports default to Keep in place; Copy is opt-in; Move is unimplemented.
- Mix/list links are decided by the user once, with an option to remember the choice; they
  are never guessed.
- Tag-write default is Downloads only.
- The file-operation journal is an audit log until undo ships; no comment may claim undo.

### 7.2 Size limits

| File type   | Soft      | Hard |
| ----------- | --------- | ---- |
| ViewModel   | 300 lines | 400  |
| Service     | 300 lines | 500  |
| View        | 200 lines | 300  |
| Code-behind | 50 lines  | 100  |

### 7.3 Branches

- main always stable; `fix/*`, `feature/*`, `refactor/*`; merges only at green checkpoints.

### 7.4 Dependencies and platform notes

- .NET LTS (supported to Nov 2028); Avalonia 12.x; LibVLCSharp with system libVLC (Fedora may
  need library symlinks); TagLib for tags; Serilog three-sink logging; SQLite ORM in active
  use for all persistence; MVVM toolkit for commands and properties.
- Optional tooling per platform: JS runtime for signature challenges, FFmpeg, secret-store
  tooling for secure keys; the app degrades with honest notices when optional tooling is
  missing.

### 7.5 Data layout

- Everything under the user data dir: encrypted keys plus one-time migration backup, audit
  journal, library database, managed downloads (with orphans subfolder), art cache, three
  log channels. A sandbox env var relocates the whole dir for dev runs and tests.

---

## 8. Release and verification protocol

1. Checkpoints over monoliths: publish to yourself/friends early (beta.1, beta.2, …).
2. Every checkpoint meets §0.3 before tagging.
3. Clean-machine pass on at least one machine without your data.
4. Release notes describe only what shipped; unverified claims are cut, not softened into
   silence-breaking promises.
5. Published tags and artifacts are immutable; fixes ship as the next beta or patch.
6. After merge to main: re-run the manual matrix rows that the merge touched.

---

## 9. Archive index (read-only, never edited)

- `IDEAS.md` archived to `docs/archive/IDEAS.md` (07-Nov-2026)
- `PLAN-26-OCT.md` and `ADDENDUM-31-OCT.md` live in the claude.ai Project, not in the repo; they are not archived here
- Earlier MASTER-GUIDE editions and per-feature fix guides remain in the Project for provenance only
- This file is the only editable guide in the repo
