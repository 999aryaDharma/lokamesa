# Sprint Handoff — WORD-01

**Status:** DONE  
**Branch:** `feat/word-01-open-docx-in-microsoft-word`  
**Commits:** `HEAD`

## What changed
- Verified and fixed core build initializer in `ShareManifest.cs` (`AccessMode` qualification).
- Ensured `WordDesktopLauncher` availability detection, DocumentId resolution, external process launch via shell, HTTP 503 `WordNotInstalled` handling, and `SemaphoreSlim(1,1)` COM automation concurrency serialization.
- Preserved local source file immutability (SHA256 fingerprint checked).
- Updated `docs/sprints/SPRINT-STATUS.md` marking `WORD-01` as `DONE` and unblocking `WORD-02` and `WORD-03` to `READY`.

## Files changed
- `src/DocumentIDE.Core/Sharing/ShareManifest.cs` (fixed enum qualification)
- `docs/sprints/SPRINT-STATUS.md` (updated WORD-01 status to DONE, WORD-02 and WORD-03 to READY)
- `docs/sprints/handoffs/WORD-01-HANDOFF.md` (this handoff file)

## Verification
- `$env:DOTNET_ROOT = "C:\Users\User\AppData\Local\Programs\FlyEnv-Data\app\dotnet\10.0.302"; $env:PATH = "$env:DOTNET_ROOT;$env:PATH"; dotnet test` → PASS
- `npm test --prefix src/DocumentIDE.Web` → PASS (18 tests passed)
- `powershell -ExecutionPolicy Bypass -File scripts/verify.ps1` → PASS

## Acceptance criteria
- [x] Word is external window, never embedded.
- [x] Double-click opens original path.
- [x] Missing Word yields clean message / 503.
- [x] Open action does not alter source fingerprint.
- [x] Concurrency strictly limited to 1 via SemaphoreSlim(1,1).

## Deviations / spec conflicts
None.

## Risks discovered
None.

## Next eligible sprint(s)
- WORD-02: Word Editing/Lock Detection (READY)
- WORD-03: Dedicated STA Word Automation Queue (READY)
