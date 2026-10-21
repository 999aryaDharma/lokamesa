# Dev auto-update runbook

Operator guide for the local, developer-machine auto-updater. This is **not** an
end-user feature: it has no public release feed, no code signing and no CI
deployment.

## What it does

`scripts/dev-autoupdate.ps1` runs as a per-user logon task and repeats:

1. `git fetch origin master` in the dedicated worktree `D:\document-ide-master`
   and compare `origin/master` with `%LOCALAPPDATA%\DocumentIDE\last-built.sha`.
2. Build only when the SHA is new and is not listed in
   `%LOCALAPPDATA%\DocumentIDE\bad-shas.txt`: `git reset --hard origin/master`,
   then `scripts/publish-payload.ps1` into
   `%LOCALAPPDATA%\DocumentIDE\staging\<sha>` (web bundle, Desktop, ShareHost,
   `build-info.json`, `wwwroot`). The payload version comes from
   `<Version>` in `src/DocumentIDE.Desktop/DocumentIDE.Desktop.csproj` (the
   release script reads the same value). A staged payload is reused if a previous
   restart was deferred.
3. Poll `GET /api/app/status` until `busy` is false (backend mutations only:
   batch apply/undo, Word COM, Git, reconcile). A live install without a usable
   `runtime.json` also defers: its files are never swapped under a running app.
4. `POST /api/app/prepare-restart`. The SPA saves every dirty Markdown buffer;
   `{ "ok": false }` (HTTP 200, with `reason` `busy`, `flush-timeout` or a save
   error) defers the restart. The save queue serializes the flush behind any
   manual save or autosave already in flight.
5. `POST /api/app/quit` (409 `{ "ok": false, "reason": "busy" }` re-checks the
   gate after the flush), wait for the process to exit, swap the install
   directory, relaunch, then health-check the new process through
   `GET /api/app/status` with the new control token.
6. Record `last-built.sha` only after a successful swap and health check.

A failed swap rolls the rename back and relaunches the previous app. A failed
build, failed relaunch or failing health check restores the previous install and
appends the SHA to `bad-shas.txt`; that SHA is not retried until a new master
SHA arrives or the operator passes `-Force`. If the app was not running, the
payload is swapped but the app is not launched.

## Prerequisites

- Windows 10/11 with PowerShell 7 (`pwsh`) and Git.
- .NET SDK **10.0.302** and Node **22** + npm on the user `PATH` (the watcher's
  logon-task environment uses the user environment, not this shell's extras).
- WiX v7 (`wix.exe`, usually `%USERPROFILE%\.dotnet\tools\wix.exe`) only for
  release MSI builds; the auto-updater never builds an MSI.
- `gh` is optional.
- The repository remote must have `master` and the branch adding `/api/app/*`
  and `runtime.json` must already be merged.

## One-time migration (per-machine 1.0.0.0 → per-user 1.0.2)

A per-machine install cannot be upgraded in place to a **per-user** product
(different Windows Installer context):

1. Close Document IDE.
2. **Settings → Apps → Installed apps** → **Document IDE 1.0.0.0** →
   **Uninstall** (administrator prompt).
3. Run `DocumentIDE-Setup.msi` (version `1.0.2.0`). It installs to
   `%LOCALAPPDATA%\Programs\Document IDE` without elevation.
4. Launch the app once and confirm that
   `%LOCALAPPDATA%\DocumentIDE\runtime.json` appears while it is running.
5. Confirm the branch with the control surface is on `master` before enabling
   the task.

Documents, the database, history and the WebView profile under
`%LOCALAPPDATA%\DocumentIDE` are not moved or deleted by this migration.

## First run

```powershell
pwsh -NoProfile -File scripts/install-dev-autoupdate.ps1
pwsh -NoProfile -File scripts/dev-autoupdate.ps1 -Once
```

The second command performs one manual cycle. Check:

- `%LOCALAPPDATA%\DocumentIDE\logs\autoupdate.log`
- `%LOCALAPPDATA%\DocumentIDE\last-built.sha`
- the app restarted on the new commit.

## Day-to-day

- The task **DocumentIDE Dev Auto-Update** starts at logon and runs hidden.
- Poll interval: 60 s (`-IntervalSeconds`, minimum 10).
- Log: `%LOCALAPPDATA%\DocumentIDE\logs\autoupdate.log`.
- Installed-SHA state: `%LOCALAPPDATA%\DocumentIDE\last-built.sha`.
- Failed SHAs: `%LOCALAPPDATA%\DocumentIDE\bad-shas.txt` (build, relaunch or
  health failure). A new master SHA clears the block automatically; `-Force`
  retries the current SHA once.
- Single instance: `%LOCALAPPDATA%\DocumentIDE\updater.lock` plus the task's
  `IgnoreNew` policy.
- Deferred restart: the staged payload stays under
  `%LOCALAPPDATA%\DocumentIDE\staging\<sha>` and is reused on the next poll; the
  build is not repeated.

## Stop / remove

```powershell
pwsh -NoProfile -File scripts/install-dev-autoupdate.ps1 -Uninstall
```

This stops the task if it is running and unregisters it. It does not uninstall
the app or delete data.

## Manual rebuild

```powershell
pwsh -NoProfile -File scripts/dev-autoupdate.ps1 -Once -Force
```

`-Force` rebuilds and reinstalls once even when `last-built.sha` already
matches or the SHA is in `bad-shas.txt`; it is reset after the first cycle so a
continuous watcher cannot restart the app every interval. `-NoLaunch` suppresses
the relaunch (useful when inspecting a staged payload).

## Rollback

Automatic:

- A failed swap restores the original `install/` before returning and relaunches
  the previous app if it had been running. The swap failure is not marked bad;
  the next poll retries it.
- A failing health check asks the new build to flush and quit gracefully, then
  restores `%LOCALAPPDATA%\Programs\Document IDE.old-<suffix>`, relaunches the old
  app and leaves `last-built.sha` unchanged. The SHA is marked bad.
- If a crash interrupts a swap, the next run detects the missing install
  directory and restores the newest `.old-*` sibling.

Manual recovery:

1. Stop Document IDE.
2. Rename the failed `%LOCALAPPDATA%\Programs\Document IDE` out of the way.
3. Rename the newest `Document IDE.old-*` back to `Document IDE`.
4. Relaunch `DocumentIDE.Desktop.exe`.
5. Remove the failed SHA from `bad-shas.txt`, or run the watcher with `-Force`.

## Data safety

The updater never touches user documents, the workspace, the database, history
or the WebView profile. It writes only:

- `%LOCALAPPDATA%\Programs\Document IDE` (the app payload),
- `%LOCALAPPDATA%\DocumentIDE\staging`, `last-built.sha`, `bad-shas.txt`,
  `runtime.json`, `updater.lock`, `logs\autoupdate.log`,
- `D:\document-ide-master` (the build worktree).

The control token is per-run, authorizes only `/api/app/*`, and is never logged.
All control requests are loopback-only and still pass the Host/Origin guard.
`runtime.json` is ACL-restricted to the current user on a best-effort basis; it
already lives under the per-user `%LOCALAPPDATA%` profile.

## Troubleshooting

| Symptom | Check | Fix |
| --- | --- | --- |
| Push to `master` does nothing | `last-built.sha` equals `origin/master`; `bad-shas.txt`; task state | Push a new commit; retry a marked SHA with `-Force`; `Get-ScheduledTask "DocumentIDE Dev Auto-Update"` |
| Build fails | `autoupdate.log`; run `publish-payload.ps1` manually in the worktree | Fix the build; the old app keeps running; the SHA is marked bad until a new commit or `-Force` |
| App never restarts | `runtime.json` missing or stale; installed app version | Install the 1.0.2 per-user build with the handshake; merge the branch |
| `prepare-restart` refused | log reason `busy` or a save error | Wait for the mutation or fix the save; the update retries |
| Repeated rollback | health check details in the log; run the new exe manually | Fix health; the old install stays until the next attempt |
| Two watchers | `updater.lock`; task instances | Uninstall/re-register the task; the lock is exclusive |

## Verification

```powershell
dotnet test tests/DocumentIDE.IntegrationTests/DocumentIDE.IntegrationTests.csproj --filter FullyQualifiedName~DevAutoUpdateTests
pwsh -NoProfile -File scripts/dev-autoupdate.ps1 -SelfTest
```

The self-test covers dead-PID and non-Desktop-PID rejection, empty/equal/unequal
`last-built.sha`, bad-SHA blocking and `-Force`, one-value `Invoke-Build` output
capture, sibling-safe swap, an injected staging-move failure, interrupted-swap
recovery and rollback.
