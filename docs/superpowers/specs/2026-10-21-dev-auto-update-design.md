# Dev Auto-Update: Local Build → Installed App

**Status:** Proposed
**Date:** 2026-10-21
**Owner:** implementation sprint (to be created from this spec)
**Related:** `AGENTS.md`, `docs/sprints/release/REL-01-windows-installer-packaging.md`,
`.github/workflows/release.yml`, `scripts/build-release.ps1`, `scripts/verify-release.ps1`

## 1. Problem

The Windows app is installed per-machine (`C:\Program Files\Document IDE`) and only
changes when a developer manually rebuilds and reinstalls the MSI. On the development
machine, commits that land on `master` never reach the installed app. The goal is to make
the development machine behave like a web deployment: when `master` advances, the
installed app is rebuilt and restarted automatically, with no manual step and no UAC.

This is a **developer-machine** feature. It is not an end-user update channel and it does
not replace the release verification pipeline.

## 2. Decisions

These were agreed during design dialogue; they are binding for this spec.

| Decision | Choice |
|---|---|
| Audience | The developer's own machine only |
| Trigger | Local build when `origin/master` advances (poll every 60 s) |
| Scope | Whole app: React UI **and** C# binaries |
| Install location | Per-user: `%LOCALAPPDATA%\Programs\Document IDE` |
| Elevation | None; the app owns its install directory |
| Reaction | Auto-apply, auto-restart |
| Unsaved editor content | **Auto-save before restart**; if saving fails, defer and log |
| Build source | Dedicated git worktree for `master`; the developer's branch is untouched |
| Approach | External PowerShell watcher + minimal app-side status/control surface (Approach A) |
| Version | Bump installer/app version to `1.0.2.0` |

## 3. Goals and Non-Goals

### Goals

1. After `origin/master` changes, the installed app is rebuilt and restarted within a few
   minutes with no manual action.
2. No administrator rights or UAC prompt are required for updates.
3. Unsaved editor content is persisted before the app is terminated.
4. Updates never run while a backend mutation is in flight.
5. A failed build, failed swap, or failing new build leaves a working app installed.
6. User documents, the database, history, and WebView profile are never touched by an
   update.

### Non-goals (YAGNI)

- Public release feed, end-user auto-update, delta patches, code signing.
- CI-triggered deployment (GitHub Actions on `master`).
- Updating Microsoft Word or any external dependency.
- A rollback UI; safe automatic rollback plus logs is sufficient.
- Replacing `verify-release.ps1`. Dev auto-update is **not** a release gate.
- Multi-user or multi-machine orchestration.

## 4. Architecture

```
origin/master
     │  git fetch (poll 60 s)
     ▼
┌──────────────────────────────┐        ┌─────────────────────────────┐
│ dev-autoupdate.ps1           │        │ DocumentIDE.Desktop.exe     │
│ (per-user logon task)        │        │                             │
│                              │        │  WebAppHost (loopback API)  │
│ 1. detect new master SHA     │        │   /api/app/status           │
│ 2. build in master worktree  │◄──────►│   /api/app/prepare-restart  │
│ 3. stage payload             │ token  │   /api/app/quit             │
│ 4. wait until not busy       │        │                             │
│ 5. prepare-restart + flush   │        │  ActivityGate (in-flight)   │
│ 6. quit, swap, relaunch      │        │  RestartCoordinator         │
└──────────────────────────────┘        │  runtime.json (pid/port)    │
             │                          └──────────────┬──────────────┘
             │                                         │ ExecuteScriptAsync
             ▼                                         ▼
  %LOCALAPPDATA%\DocumentIDE\staging\<sha>      WebView2 SPA
  %LOCALAPPDATA%\Programs\Document IDE          window.__DOCUMENTIDE_PREPARE_RESTART__
```

### 4.1 Per-user installation and migration

- `installer/DocumentIDE.wxs`:
  - `Scope="perMachine"` → **`Scope="perUser"`**.
  - Install directory: `LocalAppDataFolder\Programs\Document IDE`.
  - `UpgradeCode` and `MajorUpgrade` are retained; the Start Menu shortcut resolves to the
    per-user Start Menu.
- `Version` → **`1.0.2.0`**.
- **One-time migration:** a per-machine 1.0.0.0 installation cannot be upgraded in place to
  a per-user product (different installation context). The release note for 1.0.2 must
  instruct the developer to uninstall "Document IDE" once from *Apps & features*, then
  install the per-user MSI. After that, all updates are automatic.

### 4.2 Build identity

Each build embeds:

- `version` — a single shared version property, used for both the MSI
  (`installer/DocumentIDE.wxs` `Version`) and the assemblies
  (`AssemblyInformationalVersion`), so the installed app and the installer can never
  disagree. The initial value is `1.0.2.0`.
- `sha` — the exact `master` commit it was built from, embedded as
  `AssemblyMetadata("CommitSha", ...)` (or an equivalent generated `BuildInfo` source).

The app exposes both through `/api/app/status`. The watcher records the last successfully
installed SHA in `%LOCALAPPDATA%\DocumentIDE\last-built.sha` to avoid rebuilding the same
commit.

### 4.3 Master worktree

The watcher builds a dedicated worktree so the developer's working tree is never checked
out or modified:

```
git worktree add D:\document-ide-master master
```

The worktree is created on first run if absent, then updated with
`git fetch origin master` + `git reset --hard origin/master` (inside the worktree only).

### 4.4 Watcher

`scripts/dev-autoupdate.ps1`:

- Runs as a per-user logon Scheduled Task, hidden, registered by
  `scripts/install-dev-autoupdate.ps1`.
- Polls every 60 seconds by default (`-IntervalSeconds`, minimum 10).
- Detects change by comparing `origin/master` SHA with `last-built.sha`.
- Builds only when the SHA differs (or `-Force`), preventing build loops.
- Supports `-Once` for manual runs and `-RepoRoot` / `-WorktreeRoot` overrides.
- Logs to `%LOCALAPPDATA%\DocumentIDE\logs\autoupdate.log`.
- Holds a single-instance lock (`updater.lock`) so two watchers cannot race.

### 4.5 Payload build and staging

- A shared helper `scripts/publish-payload.ps1` (extracted from `build-release.ps1`, which
  then calls it) performs:
  - `npm run build` in `src/DocumentIDE.Web`,
  - `dotnet publish` for Desktop and ShareHost, self-contained `win-x64`, `-m:1`,
  - copy the production bundle into `wwwroot`,
  - verify `DocumentIDE.Desktop.exe`, `DocumentIDE.ShareHost.exe`, `wwwroot/index.html`.
- The watcher builds into
  `%LOCALAPPDATA%\DocumentIDE\staging\<sha>\` and writes `update-manifest.json`
  `{ sha, version, builtAtUtc }`.
- The MSI is **not** built for dev updates; the payload layout is identical to the
  installer payload, so it can be swapped directly.

### 4.6 Runtime handshake and control surface

On startup the app writes `%LOCALAPPDATA%\DocumentIDE\runtime.json`:

```json
{ "pid": 1234, "port": 51234, "version": "1.0.2.0", "sha": "abc123...", "controlToken": "..." }
```

- The file is ACL-restricted to the current user and is deleted on clean exit.
- The watcher treats `runtime.json` as valid only when the recorded PID is alive **and**
  the file was written by that PID for the current run; stale files left by a crash are
  ignored and cleaned up.
- `controlToken` is a fresh random value per run and is **distinct** from the UI session
  token. It authorizes only the `/api/app/*` control endpoints; it can never call
  document-mutation routes.
- The existing `PrivilegedRequestGuard` continues to enforce Host/Origin checks. It is
  extended so that `/api/app/*` accepts either the control token or the UI session token;
  every other route still requires the UI session token.
- Tokens are never logged.

Endpoints:

| Method | Path | Purpose |
|---|---|---|
| `GET` | `/api/app/status` | `{ version, sha, busy, reasons[], startedAtUtc }` |
| `POST` | `/api/app/prepare-restart` | Ask the UI to flush unsaved editors. Returns `{ ok, reason? }` |
| `POST` | `/api/app/quit` | Graceful shutdown after a successful prepare |

`quit` uses `IHostApplicationLifetime.StopApplication()`; `WebAppHost` raises a
`ShutdownRequested` event that `MainWindow` handles by calling
`Application.Current.Shutdown()`. `MainWindow.Closed` already disposes `WebAppHost`.

### 4.7 Busy signal

`ActivityGate` (Core, singleton) tracks in-flight **backend mutations**:

- batch apply / undo,
- Word COM operations,
- git operations,
- reconcile/index.

It is a thread-safe counter with an optional reason per entry, exception-safe disposal
(`using var lease = gate.Enter("batch-apply")`). `/api/app/status` reports `busy = count > 0`
and the distinct reasons.

Unsaved editor state is **not** part of `busy`; it is handled by the auto-save handshake in
§4.8.

### 4.8 Auto-save before restart

1. Watcher waits until `busy == false`.
2. Watcher calls `POST /api/app/prepare-restart`.
3. The Desktop `RestartCoordinator` calls
   `WebView.CoreWebView2.ExecuteScriptAsync("window.__DOCUMENTIDE_PREPARE_RESTART__()")`
   and awaits the result (timeout 15 s).
4. The SPA implements
   `window.__DOCUMENTIDE_PREPARE_RESTART__ = async () => { ... }`, which:
   - saves every editor buffer whose content differs from the file on disk, using the
     existing Markdown save endpoint,
   - returns `{ ok: true }` on success,
   - returns `{ ok: false, reason }` if any save fails (read-only file, lock, conflict).
5. If `ok`, the watcher calls `/api/app/quit`. If not, the watcher defers, logs the reason,
   and retries on the next cycle. No data is discarded.
6. If the bridge is absent (e.g. running against the Vite dev server), the app treats the
   flush as a successful no-op and logs a warning.

### 4.9 Swap and relaunch

Once the app has exited (or was not running):

1. Rename `%LOCALAPPDATA%\Programs\Document IDE` → `Document IDE.old-<random>`.
2. Move `staging\<sha>` → `%LOCALAPPDATA%\Programs\Document IDE`.
3. If any step fails, rename `.old-<random>` back and abort.
4. If the app was running, relaunch `DocumentIDE.Desktop.exe`.
5. Wait up to 60 s for `/health`; on success, record `last-built.sha` and delete `.old-*`.
6. On health failure, stop the new process, restore `.old-<random>`, relaunch the old
   version, mark the SHA as bad, and wait for a new SHA.

If the app was **not** running when the update was staged, the watcher still swaps the
directory (so the next launch uses the new build) but does **not** launch the app.

## 5. Safety and Security

- Updates only touch the install directory and the staging directory. The database,
  history, WebView profile, logs, and user documents live under
  `%LOCALAPPDATA%\DocumentIDE\...` or in user-chosen source folders and are never moved,
  deleted, or rewritten by the updater.
- The control surface is loopback-only and guarded by Host/Origin checks plus a per-run
  control token that cannot invoke mutation routes.
- `runtime.json` is ACL-restricted to the current user; no token is ever written to logs.
- The watcher never accepts an arbitrary install path from an untrusted source; paths are
  fixed defaults or explicit operator parameters.
- Swap is performed with directory renames on the same volume, never by deleting the live
  build first.
- Existing source-mutation safety rules (`snapshot → temp → validate → atomic replace`)
  are unaffected; the updater writes application binaries, never user documents.

## 6. Error Handling

| Failure | Behavior |
|---|---|
| `git fetch` fails | Log and retry next cycle; app untouched |
| Build fails | Log; keep app; record SHA as attempted; retry only on a new SHA or `-Force` |
| Disk space insufficient before staging | Abort with a clear log message |
| Auto-save fails | Do not restart; log reason; retry next cycle |
| App does not exit within 30 s | Abort swap; app keeps running |
| Directory swap fails | Roll back the rename; relaunch the old app |
| New build fails `/health` within 60 s | Restore `.old`, relaunch old version, mark SHA bad |
| Two watchers start | Second exits because `updater.lock` is held |

## 7. Testing Strategy (strict TDD)

Every behavioral change follows Red → Green → Refactor.

### Backend / unit

- `ActivityGateTests`: enter/exit nesting, concurrency, disposal on exception, reason
  aggregation.
- `AppStatusEndpointTests`: reports version, SHA, `busy`, and reasons correctly.
- `ControlTokenTests`: the control token is accepted on `/api/app/*` and rejected on every
  other route; the UI session token still works on `/api/app/*`.
- `PrepareRestartTests`: returns `ok=false` with a reason when the flush bridge reports
  failure; returns `ok=true` when it succeeds; times out safely.
- `AppQuitTests`: quit requires authorization and raises `ShutdownRequested`.

### Script tests (existing `ScriptTests` pattern)

- `dev-autoupdate.ps1` detects a changed `origin/master` SHA and skips an unchanged one.
- `publish-payload.ps1` produces `DocumentIDE.Desktop.exe`, `DocumentIDE.ShareHost.exe`,
  and `wwwroot/index.html`.
- Swap helper: injected failure rolls back to the original directory.
- Manifest and `last-built.sha` are written only after a healthy restart.

### Integration / host

- A staged fake payload with a stub process: status polling → prepare → quit → swap
  (using temporary directories, never the real install path).
- Verify the control endpoints are unreachable without the control token.
- Verify the app does not restart while `ActivityGate` is held.

### Manual acceptance (developer machine)

1. Push a trivial change to `master`.
2. Observe the watcher build and restart the app within a few minutes.
3. Open a Markdown file, type without saving, wait for an update; confirm the text was
   saved before restart.
4. Start a long batch operation and confirm the restart waits for it.
5. Push a deliberately broken `master`; confirm the old app is restored and the machine
   stops retrying that SHA.

## 8. Acceptance Criteria

- [ ] The MSI installs per-user under `%LOCALAPPDATA%\Programs\Document IDE` with no UAC.
- [ ] A change to `origin/master` rebuilds and restarts the installed app without manual
      steps.
- [ ] Unsaved editor content is saved before restart; a failed save defers the restart.
- [ ] Restart never occurs while a backend mutation is in flight.
- [ ] A failed build leaves the installed app untouched.
- [ ] A failed swap or failing new build restores the previous working app.
- [ ] User documents, database, history, and WebView profile are unchanged by an update.
- [ ] The control endpoints require the control token and are loopback-only.
- [ ] The full existing test suite remains green.

## 9. Planned Files and Artifacts

- `installer/DocumentIDE.wxs` — per-user scope, install path, version.
- `scripts/publish-payload.ps1` — shared publish helper (new).
- `scripts/build-release.ps1` — call the helper.
- `scripts/dev-autoupdate.ps1` — watcher (new).
- `scripts/install-dev-autoupdate.ps1` — register the logon task (new).
- `src/DocumentIDE.Core/Activity/ActivityGate.cs` — in-flight mutation tracking (new).
- `src/DocumentIDE.Api/Controllers/AppLifecycleController.cs` — status/prepare/quit (new).
- `src/DocumentIDE.Api/Security/` — control-token validation.
- `src/DocumentIDE.Desktop/WebView/RestartCoordinator.cs` — WebView flush bridge (new).
- `src/DocumentIDE.Desktop/WebView/RuntimeHandshake.cs` — runtime.json lifecycle (new).
- `src/DocumentIDE.Web/src/` — `window.__DOCUMENTIDE_PREPARE_RESTART__` flush hook.
- Tests: `tests/DocumentIDE.Core.Tests/Activity/ActivityGateTests.cs`,
  `tests/DocumentIDE.IntegrationTests/Api/AppLifecycleTests.cs`,
  `tests/DocumentIDE.IntegrationTests/Scripts/DevAutoUpdateTests.cs`,
  `tests/DocumentIDE.IntegrationTests/Desktop/RestartCoordinatorTests.cs`.

## 10. Risks and Open Items

- **Build environment coupling.** `scripts/build-release.ps1` and `verify-release.ps1`
  hardcode `DOTNET_ROOT` and a Node path. The watcher will reuse the same conventions;
  unifying them is out of scope but noted.
- **Frontend flush hook.** The SPA must expose a reliable global that saves all dirty
  buffers. If a future editor surface does not register with it, its unsaved state could be
  lost; the hook must be covered by frontend tests.
- **Install-scope migration.** The first per-user install requires a one-time manual
  uninstall of the per-machine product. This is documented, not automated.
- **Long builds.** A cold build may take several minutes; the app keeps running until the
  payload is fully staged.
