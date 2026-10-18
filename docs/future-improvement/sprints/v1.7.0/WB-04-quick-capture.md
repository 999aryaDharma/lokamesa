# WB-04 — Quick Capture

**Label:** `future-improvement`
**Roadmap version:** `v1.7.0`
**Status:** SPECIFIED
**Priority:** `P0`
**Kind:** feature
**Recommended branch:** `feat/wb-04-quick-capture`

## Goal

Capture a thought in one keystroke: an in-app shortcut and an OS-global hotkey open a one-field panel that immediately writes a sticky note to the current (or a default) wildboard, with no folder or file decision.

## Depends On

- `WB-03` — Wildboard Canvas Surface
- `FND-07` — Desktop/WebView Production Host — **DONE**
- `CAN-05` — Spatially Indexed Assisted Placement — **DONE**

Hard rule: do not start unless `WB-03` is `DONE` and the V1 dependencies are `DONE`.

## Required Reading

- `AGENTS.md`
- `docs/specs/03-workspace-canvas-spec.md` §placement
- `docs/future-improvement/specs/1.7.0-wildboard-core.md` §8
- `docs/future-improvement/specs/00-shared-future-contracts.md` §6 (accessibility)
- `src/DocumentIDE.Web/src/features/canvas/layout/AssistedPlacement.ts`
- `src/DocumentIDE.Desktop/MainWindow.xaml.cs`
- `src/DocumentIDE.Desktop/WebView/LocalSessionBridge.cs`

## In Scope

- `captureController` (target resolution, placement, submission).
- `QuickCapturePanel` (keyboard-first, accessible).
- In-app shortcut `Ctrl+Shift+Space`.
- OS-global hotkey `Alt+Space` in the WPF host, delivered to the web UI as a WebView2 message.
- Auto-create an `Inbox` wildboard when none exists.

## Out of Scope

- Media or clipboard-image capture (`1.9.0`).
- Rich formatting in the capture field (`1.8.0`).
- User-configurable hotkeys.
- Cloud sync.

## Planned Files / Artifacts

**Create**

- `src/DocumentIDE.Web/src/features/wildboard/captureController.ts`
- `src/DocumentIDE.Web/src/features/wildboard/captureController.test.ts`
- `src/DocumentIDE.Web/src/features/wildboard/QuickCapturePanel.tsx`
- `src/DocumentIDE.Web/src/features/wildboard/QuickCapturePanel.test.tsx`
- `src/DocumentIDE.Web/src/features/wildboard/desktopCaptureBridge.ts`
- `src/DocumentIDE.Web/src/features/wildboard/desktopCaptureBridge.test.ts`
- `src/DocumentIDE.Desktop/Hotkeys/GlobalHotkey.cs`
- `tests/DocumentIDE.IntegrationTests/Desktop/GlobalHotkeyTests.cs`

**Modify**

- `src/DocumentIDE.Desktop/MainWindow.xaml.cs` — register/unregister, forward `WM_HOTKEY`.
- `src/DocumentIDE.Web/src/App.tsx` — mount the panel, bind the shortcut, subscribe to the desktop bridge.

## Interfaces & Contracts

**Consumes**

- `WorkspaceDto`, `createWorkspace` (`WB-01`).
- `createWildboardObject`, `fetchWildboardObjects` (`WB-03`).
- `placeDocumentsNearOrigin`, `PlaceableObject` (`AssistedPlacement`).
- `ViewportState` (`viewport.ts`).

**Produces**

```ts
// captureController.ts
export class EmptyCaptureError extends Error {}
export const CAPTURE_TARGET_NAME = 'Inbox';

export function resolveCaptureTarget(
  workspaces: WorkspaceDto[],
  activeWorkspaceId: string | null,
  lastWildboardId: string | null,
): string | null; // wildboard id, or null when none exist

export function placeCapture(
  existing: WildboardObjectView[],
  viewport: ViewportState,
): { x: number; y: number };

export function submitCapture(input: {
  text: string;
  workspaces: WorkspaceDto[];
  activeWorkspaceId: string | null;
  lastWildboardId: string | null;
  existing: WildboardObjectView[];
  viewport: ViewportState;
  createWorkspace: (name: string, kind: 'wildboard') => Promise<WorkspaceDto>;
  createObject: (
    workspaceId: string,
    body: { kind: 'text'; body: string; x: number; y: number },
  ) => Promise<unknown>;
}): Promise<{ workspaceId: string; createdWorkspace: boolean }>;
```

```ts
// desktopCaptureBridge.ts
export const DESKTOP_CAPTURE_MESSAGE = 'wildboard:capture';
export function subscribeToDesktopCapture(handler: () => void): () => void;
```

```csharp
// GlobalHotkey.cs
public sealed class GlobalHotkey : IDisposable
{
    public const int WmHotkey = 0x0312;
    public const int CaptureHotkeyId = 0xB10;
    public GlobalHotkey(IntPtr windowHandle);
    public bool TryRegister(uint modifiers, uint virtualKey);
    public void Dispose();
    public static bool IsCaptureMessage(int msg);
}
```

Target resolution order:

1. `activeWorkspaceId` when that workspace is `wildboard`;
2. `lastWildboardId` when it still exists;
3. the most recently updated `wildboard`;
4. otherwise create `Inbox` and use it.

`placeCapture` maps existing objects to `{ ...object, positionMode: 'manual' }` (so nothing is reflowed), uses the viewport centre as origin, and falls back to `origin + 24px offset` if placement returns `blocked`. A capture must never be lost.

## Implementation Steps

### Step group A — capture controller

- [ ] **A1.** Write failing `captureController.test.ts`:
  - `Empty_Text_Throws_EmptyCaptureError` (also whitespace-only);
  - `Uses_Active_Wildboard_When_Active`;
  - `Falls_Back_To_Last_Wildboard`;
  - `Falls_Back_To_Most_Recently_Updated_Wildboard`;
  - `Creates_Inbox_When_No_Wildboard_Exists` (asserts `createdWorkspace: true`);
  - `Never_Selects_A_Documents_Workspace`;
  - `Placement_Does_Not_Overlap_Existing_Objects`;
  - `Blocked_Placement_Still_Returns_Coordinates`;
  - `Preserves_Multi_Line_Text`.
- [ ] **A2.** Run `npx vitest run src/features/wildboard/captureController.test.ts`. Expected: FAIL.
- [ ] **A3.** Implement `captureController`. Trim outer whitespace but preserve internal newlines. Use `placeDocumentsNearOrigin` with `columns: 4`, `gap: 24`, origin at the viewport centre.
- [ ] **A4.** Re-run A2. Expected: PASS.
- [ ] **A5.** Commit: `feat(wildboard): add capture target and placement controller`.

### Step group B — panel

- [ ] **B1.** Write failing `QuickCapturePanel.test.tsx`:
  - renders a single textarea with `aria-label="Capture a thought"` and `role="dialog"`;
  - `Enter_Submits_Once`;
  - `ShiftEnter_Inserts_Newline_And_Does_Not_Submit`;
  - `Escape_Closes_Without_Submitting`;
  - `Autofocuses_On_Open`;
  - `Shows_Target_Workspace_Name`.
- [ ] **B2.** Run → FAIL → implement `QuickCapturePanel` using `SheetShell` for chrome, `Escape` handled explicitly, submit disabled while saving, and an inline error on failure that keeps the text.
- [ ] **B3.** Re-run B2. Expected: PASS.
- [ ] **B4.** Commit: `feat(wildboard): add quick capture panel`.

### Step group C — in-app binding and app wiring

- [ ] **C1.** Write failing `App.wildboard.test.tsx` cases: `CtrlShiftSpace_Opens_Capture`, `Submitting_Capture_Creates_Text_Object`, `Capture_Errors_Keep_Panel_Open_With_Text`.
- [ ] **C2.** Run → FAIL → wire `App.tsx` (shortcut listener, panel mount, submit through `submitCapture`, refresh store).
- [ ] **C3.** Re-run → PASS. Confirm `Ctrl+Space` and `Alt+Space` are **not** bound in-app.
- [ ] **C4.** Commit: `feat(web): wire quick capture into the shell`.

### Step group D — desktop global hotkey

- [ ] **D1.** Write failing `desktopCaptureBridge.test.ts`: `Handler_Called_On_Capture_Message`, `Ignores_Other_Message_Types`, `Unsubscribe_Removes_Listener`, `No_Op_Without_WebView2`.
- [ ] **D2.** Run → FAIL → implement `desktopCaptureBridge.ts`.
- [ ] **D3.** Write failing `GlobalHotkeyTests.cs` (pure parts): `IsCaptureMessage_Recognizes_WmHotkey`, `IsCaptureMessage_Rejects_Other`, `CaptureHotkeyId_Is_Stable`.
- [ ] **D4.** Run `dotnet test tests\DocumentIDE.IntegrationTests\DocumentIDE.IntegrationTests.csproj --filter "FullyQualifiedName~GlobalHotkeyTests"` → FAIL → implement `GlobalHotkey` (`RegisterHotKey`/`UnregisterHotKey` via P/Invoke) → PASS.
- [ ] **D5.** Wire `MainWindow.xaml.cs`: create the hotkey after `Loaded`, add an `HwndSource` hook, on `WM_HOTKEY` with `CaptureHotkeyId` call `WebView.CoreWebView2.PostWebMessageAsJson("{\"type\":\"wildboard:capture\"}")`, unregister on `Closed`. If registration fails, log at warning level and keep the app usable.
- [ ] **D6.** Record in the handoff that OS-level registration cannot be asserted in the automated suite; provide a manual check (`Alt+Space` with the app minimised opens the panel) with the observed result.
- [ ] **D7.** Commit: `feat(desktop): add global capture hotkey`.

## Required Tests

- [ ] `captureController.test.ts` — nine cases in A1.
- [ ] `QuickCapturePanel.test.tsx` — six cases in B1.
- [ ] `App.wildboard.test.tsx` — three capture cases.
- [ ] `desktopCaptureBridge.test.ts` — four cases.
- [ ] `GlobalHotkeyTests.cs` — three pure cases.
- [ ] A loopback-only assertion: `Capture_Only_Calls_Local_Api` (mock `fetch`, assert every URL is the injected API base).

At minimum, the first behavior must be demonstrated by a failing test before implementation:

> `captureController.test.ts > Empty_Text_Throws_EmptyCaptureError`

## Failure / Edge Cases

- No wildboard exists → `Inbox` is created once; two concurrent captures must not create two inboxes (guard with a module-level in-flight promise).
- Capture submitted while the API is down → panel stays open with the user's text and an error.
- Very large text (> 1 MiB) → API `413` surfaces as an inline error; nothing is created.
- Hotkey already registered by another application → registration fails, warning logged, in-app shortcut still works.
- App not focused → global hotkey still opens the panel and focuses it.
- Multi-line paste → preserved as a single sticky note.
- `Escape` while saving → panel closes; the in-flight save completes and the object appears.
- Text containing only whitespace → rejected, nothing written.

## Security & File-Safety Gate

- [ ] Capture performs no network I/O beyond the loopback API.
- [ ] No clipboard contents are read unless the user pastes them.
- [ ] No shell or command invocation.
- [ ] Captured text is written only through `WB-02`'s verified vault path.
- [ ] The global hotkey does not expose or bypass the session token.

## Acceptance Criteria

- [ ] `Ctrl+Shift+Space` opens the capture panel in-app.
- [ ] `Alt+Space` opens it while the app is minimised (manual check recorded).
- [ ] `Enter` submits, `Shift+Enter` adds a newline, `Escape` cancels.
- [ ] A capture writes a real `.md` sticky and places it without overlapping.
- [ ] Target resolution follows the documented order and never chooses a `documents` workspace.
- [ ] Captures are never silently lost.

## Definition of Done

- [ ] implementation steps complete
- [ ] required tests pass
- [ ] `powershell -ExecutionPolicy Bypass -File scripts/verify.ps1` passes
- [ ] no required test skipped; the manual hotkey check is recorded, not claimed as automated
- [ ] no unresolved placeholder markers
- [ ] `git diff` self-reviewed
- [ ] acceptance criteria mapped to evidence
- [ ] handoff written to `docs/future-improvement/handoffs/WB-04-HANDOFF.md`
- [ ] coherent implementation commit

## Commit Guidance

```text
feat(wildboard): quick capture
```

## Ready-to-Run Agent Prompt

```text
Implement WB-04 — Quick Capture only.

Read AGENTS.md, docs/specs/03, docs/future-improvement/specs/1.7.0-wildboard-core.md,
docs/future-improvement/specs/00-shared-future-contracts.md, and ADR-003 first.
This sprint is part of roadmap v1.7.0; do not implement WB-05 or later.

Use strict TDD. Capture performs no network I/O beyond the loopback API.
The in-app shortcut is Ctrl+Shift+Space; Alt+Space is the OS-global hotkey.
If the global hotkey cannot be registered, log a warning and keep the app usable —
do not fail startup.
Do not read the clipboard without an explicit user paste.

At completion:
1. run the required tests and scripts/verify.ps1,
2. self-review git diff,
3. map acceptance criteria to evidence,
4. record the manual Alt+Space check with its observed result,
5. write docs/future-improvement/handoffs/WB-04-HANDOFF.md,
6. report changed files, exact commands/results, deviations/risks,
7. commit using the commit guidance.
```
