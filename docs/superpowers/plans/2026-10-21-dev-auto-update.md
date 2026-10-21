# Dev Auto-Update Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** When `origin/master` advances, rebuild the app on the developer's machine and restart the installed per-user app automatically, saving unsaved editor content first and deferring while mutations are in flight.

**Architecture:** A per-user PowerShell watcher polls `origin/master`, builds the payload in a dedicated `master` worktree, stages it under `%LOCALAPPDATA%\DocumentIDE\staging\<sha>`, waits for the app to report idle, asks the app to flush unsaved editor buffers through a WebView bridge, then quits, atomically swaps the install directory, and relaunches. The app exposes a loopback-only `/api/app/*` control surface guarded by a per-run control token and an in-memory `ActivityGate`.

**Tech Stack:** .NET 10 / ASP.NET Core minimal API + controllers, WPF + WebView2, React 19 + Vitest, PowerShell 7, WiX v4 (MSI), xUnit.

**Spec:** `docs/superpowers/specs/2026-10-21-dev-auto-update-design.md`

## Global Constraints

- Installer `Scope` MUST be `perUser`; install root MUST be `%LOCALAPPDATA%\Programs\Document IDE`. No UAC.
- App + MSI version MUST be `1.0.2.0`.
- Watcher poll interval default MUST be 60 seconds; `-IntervalSeconds` minimum is 10.
- The control surface MUST be loopback-only and MUST require the per-run control token on `/api/app/*`; all other routes keep the existing session-token rule.
- `busy` MUST reflect only in-flight backend mutations (batch apply/undo, Word, git, reconcile). Unsaved editor state is handled by auto-save, never by blocking `busy`.
- Restart MUST NOT proceed while `busy` is true or when auto-save fails.
- Updates MUST never read, move, delete, or rewrite user documents, the database, history, logs, or the WebView profile.
- Every behavioral change follows Red → Green → Refactor, and each task ends with a commit.
- Test commands run from the repository root on Windows/PowerShell.

## Review Focus

1. **Stale `runtime.json` after a crash** — the watcher must validate the PID and ignore/clean a stale handshake file instead of targeting a dead process. Test in Task 8.
2. **Auto-save of a read-only, locked, or conflicting document** — `/api/app/prepare-restart` must return `ok=false` with a reason, and the watcher must not restart. Test in Tasks 3 and 6.
3. **Swap interrupted by a locked file or antivirus scan** — the swap helper must restore the previous install directory and relaunch the old app. Test in Task 8.
4. **New build starts but never becomes healthy** — the watcher must stop it, restore the previous directory, and mark the SHA bad. Test in Task 8.
5. **`origin/master` force-push or history rewrite** — SHA comparison must rebuild and must not remain stuck on `last-built.sha`. Test in Task 8.

---

### Task 1: ActivityGate

**Files:**
- Create: `src/DocumentIDE.Core/Activity/ActivityGate.cs`
- Test: `tests/DocumentIDE.Core.Tests/Activity/ActivityGateTests.cs`

**Interfaces:**
- Produces: `DocumentIDE.Core.Activity.IActivityObserver` with `void Entered(string reason)`.
- Produces: `DocumentIDE.Core.Activity.ActivityGate` with `ActivityGate(IActivityObserver? observer = null)`, `int ActiveCount`, `IReadOnlyList<string> ActiveReasons`, `bool IsBusy`, `IDisposable Enter(string reason)`.

- [ ] **Step 1: Write the failing tests**

```csharp
using System;
using System.Linq;
using System.Threading.Tasks;
using DocumentIDE.Core.Activity;
using Xunit;

namespace DocumentIDE.Core.Tests.Activity;

public class ActivityGateTests
{
    [Fact]
    public void Idle_gate_is_not_busy()
    {
        var gate = new ActivityGate();
        Assert.False(gate.IsBusy);
        Assert.Equal(0, gate.ActiveCount);
        Assert.Empty(gate.ActiveReasons);
    }

    [Fact]
    public void Enter_reports_reason_and_dispose_clears_it()
    {
        var gate = new ActivityGate();
        using (gate.Enter("batch-apply"))
        {
            Assert.True(gate.IsBusy);
            Assert.Contains("batch-apply", gate.ActiveReasons);
        }
        Assert.False(gate.IsBusy);
    }

    [Fact]
    public void Nested_leases_keep_the_gate_busy_until_the_last_dispose()
    {
        var gate = new ActivityGate();
        var outer = gate.Enter("batch-apply");
        var inner = gate.Enter("word-open");
        inner.Dispose();
        Assert.True(gate.IsBusy);
        Assert.DoesNotContain("word-open", gate.ActiveReasons);
        outer.Dispose();
        Assert.False(gate.IsBusy);
    }

    [Fact]
    public void Double_dispose_is_idempotent()
    {
        var gate = new ActivityGate();
        var lease = gate.Enter("undo");
        lease.Dispose();
        lease.Dispose();
        Assert.False(gate.IsBusy);
    }

    [Fact]
    public async Task Concurrent_entries_are_counted_exactly()
    {
        var gate = new ActivityGate();
        var leases = Enumerable.Range(0, 32).Select(_ => gate.Enter("git")).ToArray();
        Assert.Equal(32, gate.ActiveCount);
        await Task.WhenAll(leases.Select(l => Task.Run(l.Dispose)));
        Assert.False(gate.IsBusy);
    }

    [Fact]
    public void Observer_is_notified_on_entry()
    {
        var observer = new RecordingObserver();
        var gate = new ActivityGate(observer);
        using (gate.Enter("reconcile"))
        {
            Assert.Equal(new[] { "reconcile" }, observer.Reasons);
        }
    }

    private sealed class RecordingObserver : IActivityObserver
    {
        public List<string> Reasons { get; } = new();
        public void Entered(string reason) => Reasons.Add(reason);
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test tests/DocumentIDE.Core.Tests/DocumentIDE.Core.Tests.csproj --filter "FullyQualifiedName~ActivityGateTests"`
Expected: FAIL to compile — `ActivityGate` does not exist.

- [ ] **Step 3: Implement `ActivityGate`**

Use a `lock`-protected `Dictionary<string, int>` and a thread-safe `Lease` that decrements exactly once (Interlocked flag). `ActiveReasons` returns the non-zero keys. `Enter` calls `observer?.Entered(reason)` after incrementing, outside the lock.

- [ ] **Step 4: Run the tests and confirm they pass**

Run the Step 2 command. Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git add src/DocumentIDE.Core/Activity/ActivityGate.cs tests/DocumentIDE.Core.Tests/Activity/ActivityGateTests.cs
git commit -m "feat(core): add activity gate for in-flight mutation tracking"
```

---

### Task 2: BuildInfo and control-token authorization

**Files:**
- Create: `src/DocumentIDE.Core/Diagnostics/BuildInfo.cs`
- Modify: `src/DocumentIDE.Api/Security/ISessionTokenProvider.cs`
- Modify: `src/DocumentIDE.Api/Security/SessionTokenProvider.cs`
- Modify: `src/DocumentIDE.Api/Security/PrivilegedRequestGuard.cs`
- Test: `tests/DocumentIDE.Core.Tests/Diagnostics/BuildInfoTests.cs`
- Test: `tests/DocumentIDE.SecurityTests/ControlTokenTests.cs`

**Interfaces:**
- Produces: `BuildInfo(string Version, string Sha)` with `static BuildInfo Load(string baseDirectory)`.
- Produces: `ISessionTokenProvider.ControlToken` and `bool ValidateControlToken(string? token)`.
- Consumes: nothing from Task 1.

- [ ] **Step 1: Write the failing BuildInfo test**

`build-info.json` is `{ "version": "1.0.2.0", "sha": "abc123" }` next to the executable.

```csharp
[Fact]
public void Load_reads_build_info_json()
{
    string dir = Directory.CreateTempSubdirectory().FullName;
    File.WriteAllText(Path.Combine(dir, "build-info.json"),
        """{"version":"1.0.2.0","sha":"abc123"}""");
    BuildInfo info = BuildInfo.Load(dir);
    Assert.Equal("1.0.2.0", info.Version);
    Assert.Equal("abc123", info.Sha);
}

[Fact]
public void Load_falls_back_to_assembly_version_when_the_file_is_missing()
{
    string dir = Directory.CreateTempSubdirectory().FullName;
    BuildInfo info = BuildInfo.Load(dir);
    Assert.False(string.IsNullOrWhiteSpace(info.Version));
    Assert.Equal("unknown", info.Sha);
}
```

- [ ] **Step 2: Write the failing control-token tests**

```csharp
[Fact]
public void Control_token_is_valid_and_distinct_from_the_session_token()
{
    var provider = new SessionTokenProvider();
    Assert.NotEqual(provider.SessionToken, provider.ControlToken);
    Assert.True(provider.ValidateControlToken(provider.ControlToken));
    Assert.False(provider.ValidateControlToken(provider.SessionToken));
    Assert.False(provider.ValidateToken(provider.ControlToken));
}
```

The end-to-end guard test (control token accepted on `/api/app/*` and rejected elsewhere) is added in Task 3, after the controller exists.

- [ ] **Step 3: Run and confirm failure**

Run: `dotnet test tests/DocumentIDE.Core.Tests/DocumentIDE.Core.Tests.csproj --filter "FullyQualifiedName~BuildInfoTests"`
Run: `dotnet test tests/DocumentIDE.SecurityTests/DocumentIDE.SecurityTests.csproj --filter "FullyQualifiedName~ControlTokenTests"`
Expected: FAIL to compile (missing members/header handling).

- [ ] **Step 4: Implement**

- `BuildInfo.Load` reads `build-info.json`; on any failure returns `new BuildInfo(entryAssemblyVersion, "unknown")`.
- `SessionTokenProvider` generates a second 32-byte random token; `ValidateControlToken` uses fixed-time comparison against it.
- `PrivilegedRequestGuard`: read `X-DocumentIDE-Control-Token` as a fallback **only** when `context.Request.Path.StartsWithSegments("/api/app")`. Otherwise keep the existing `X-DocumentIDE-Session-Token`/Bearer rule unchanged.

- [ ] **Step 5: Run and confirm pass**

Run both Step 3 commands. Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/DocumentIDE.Core/Diagnostics/BuildInfo.cs src/DocumentIDE.Api/Security tests/DocumentIDE.Core.Tests/Diagnostics/BuildInfoTests.cs tests/DocumentIDE.SecurityTests/ControlTokenTests.cs
git commit -m "feat(api): add build identity and scoped control-token authorization"
```

---

### Task 3: App lifecycle control surface

**Files:**
- Create: `src/DocumentIDE.Api/Lifecycle/IRestartFlushBridge.cs`
- Create: `src/DocumentIDE.Api/Lifecycle/AppLifecycleState.cs`
- Create: `src/DocumentIDE.Api/Controllers/AppLifecycleController.cs`
- Modify: `src/DocumentIDE.Api/PrivilegedSurfaceRegistration.cs`
- Modify: `src/DocumentIDE.Desktop/WebView/WebAppHost.cs` (add controller to the allow-list)
- Test: `tests/DocumentIDE.IntegrationTests/Api/AppLifecycleTests.cs`

**Interfaces:**
- Consumes: `ActivityGate`, `BuildInfo`.
- Produces:
  - `IRestartFlushBridge.PrepareRestartAsync(CancellationToken) -> Task<RestartFlushResult>`
  - `record RestartFlushResult(bool Ok, string? Reason)`
  - `NoopRestartFlushBridge : IRestartFlushBridge` (returns `Ok=true`)
  - `AppLifecycleState.StartedAtUtc`
  - `GET /api/app/status`, `POST /api/app/prepare-restart`, `POST /api/app/quit`.

- [ ] **Step 1: Write the failing tests**

Use the existing `WebAppHost.StartAsync(database: db, configureServices: ...)` pattern.

```csharp
private static HttpClient Authed(WebAppHost host)
{
    var client = new HttpClient { BaseAddress = new Uri(host.ApiUrl) };
    client.DefaultRequestHeaders.Add("X-DocumentIDE-Session-Token", host.SessionToken);
    return client;
}

[Fact]
public async Task Status_reports_build_identity_and_is_idle()
{
    await using var host = await WebAppHost.StartAsync(configureServices: s =>
        s.AddSingleton(new BuildInfo("1.0.2.0", "abc123")));
    using var client = Authed(host);
    var json = await client.GetFromJsonAsync<JsonElement>("/api/app/status");
    Assert.Equal("1.0.2.0", json.GetProperty("version").GetString());
    Assert.Equal("abc123", json.GetProperty("sha").GetString());
    Assert.False(json.GetProperty("busy").GetBoolean());
}

[Fact]
public async Task Status_is_busy_while_the_gate_is_held()
{
    var gate = new ActivityGate();
    await using var host = await WebAppHost.StartAsync(configureServices: s => s.AddSingleton(gate));
    using (gate.Enter("batch-apply"))
    {
        using var client = Authed(host);
        var json = await client.GetFromJsonAsync<JsonElement>("/api/app/status");
        Assert.True(json.GetProperty("busy").GetBoolean());
        Assert.Contains("batch-apply",
            json.GetProperty("reasons").EnumerateArray().Select(e => e.GetString()));
    }
}

[Fact]
public async Task Prepare_restart_propagates_a_flush_failure()
{
    var bridge = new FakeBridge(new RestartFlushResult(false, "read-only file"));
    await using var host = await WebAppHost.StartAsync(configureServices: s =>
        s.AddSingleton<IRestartFlushBridge>(bridge));
    using var client = Authed(host);
    var response = await client.PostAsync("/api/app/prepare-restart", null);
    var json = await response.Content.ReadFromJsonAsync<JsonElement>();
    Assert.False(json.GetProperty("ok").GetBoolean());
    Assert.Equal("read-only file", json.GetProperty("reason").GetString());
}

[Fact]
public async Task Quit_requires_authorization()
{
    await using var host = await WebAppHost.StartAsync();
    using var anonymous = new HttpClient { BaseAddress = new Uri(host.ApiUrl) };
    Assert.Equal(HttpStatusCode.Unauthorized,
        (await anonymous.PostAsync("/api/app/quit", null)).StatusCode);
}

[Fact]
public async Task Control_token_works_on_the_control_surface_only()
{
    await using var host = await WebAppHost.StartAsync();
    using var client = new HttpClient { BaseAddress = new Uri(host.ApiUrl) };

    var status = new HttpRequestMessage(HttpMethod.Get, "/api/app/status");
    status.Headers.Add("X-DocumentIDE-Control-Token", host.ControlToken);
    Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(status)).StatusCode);

    var documents = new HttpRequestMessage(HttpMethod.Get, "/api/documents/x/markdown");
    documents.Headers.Add("X-DocumentIDE-Control-Token", host.ControlToken);
    Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(documents)).StatusCode);
}
```

`FakeBridge` is a one-line test class implementing `IRestartFlushBridge` and returning its constructor result.

- [ ] **Step 2: Run and confirm failure**

Run: `dotnet test tests/DocumentIDE.IntegrationTests/DocumentIDE.IntegrationTests.csproj --filter "FullyQualifiedName~AppLifecycleTests"`
Expected: FAIL — routes return 404, types missing.

- [ ] **Step 3: Implement**

- `AppLifecycleController` takes `ActivityGate`, `IRestartFlushBridge`, `IHostApplicationLifetime`, `BuildInfo`, `AppLifecycleState`.
- `status` returns `{ version, sha, busy, reasons, startedAtUtc }`.
- `prepare-restart` awaits the bridge with a 15 s linked CTS; timeout returns `{ ok = false, reason = "flush-timeout" }`.
- `quit` returns `202 Accepted`, then schedules `lifetime.StopApplication()` after 100 ms (`Task.Run`) so the response is flushed.
- Register singletons in `AddPrivilegedSurfaces` with `TryAddSingleton` so tests and the Desktop host can override them:
  `TryAddSingleton<ActivityGate>()`, `TryAddSingleton<AppLifecycleState>()`,
  `TryAddSingleton(BuildInfo.Load(AppContext.BaseDirectory))`, and
  `TryAddSingleton<IRestartFlushBridge, NoopRestartFlushBridge>()`.
- Add `typeof(AppLifecycleController)` to `WebAppHost.AllowedControllers`.
- Add `public string ControlToken { get; private set; } = string.Empty;` to `WebAppHost` and assign it from
  `ISessionTokenProvider.ControlToken` at the end of `InitializeAndStartAsync`.

- [ ] **Step 4: Run and confirm pass**

Run the Step 2 command. Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/DocumentIDE.Api/Lifecycle src/DocumentIDE.Api/Controllers/AppLifecycleController.cs src/DocumentIDE.Api/PrivilegedSurfaceRegistration.cs src/DocumentIDE.Desktop/WebView/WebAppHost.cs tests/DocumentIDE.IntegrationTests/Api/AppLifecycleTests.cs
git commit -m "feat(api): expose guarded app lifecycle status and restart control"
```

---

### Task 4: Wire ActivityGate into mutating actions

**Files:**
- Modify: `src/DocumentIDE.Api/Controllers/BatchPreviewController.cs`
- Modify: `src/DocumentIDE.Api/Controllers/WordController.cs`
- Modify: `src/DocumentIDE.Api/Controllers/SourceControlController.cs`
- Modify: `src/DocumentIDE.Api/Controllers/ReconciliationController.cs`
- Test: `tests/DocumentIDE.IntegrationTests/Api/ActivityGateWiringTests.cs`

**Interfaces:**
- Consumes: `ActivityGate.Enter(string)`.
- Produces: no new public types; `busy` becomes true during the listed actions.

- [ ] **Step 1: Write the failing test**

Use the `IActivityObserver` seam from Task 1 to block the action inside `Enter`, which makes the assertion deterministic:

```csharp
private sealed class BlockingObserver : IActivityObserver
{
    public string? Reason { get; private set; }
    public ManualResetEventSlim Entered { get; } = new();
    public ManualResetEventSlim Release { get; } = new();
    public void Entered(string reason)
    {
        Reason = reason;
        Entered.Set();
        Release.Wait(TimeSpan.FromSeconds(30));
    }
}

[Fact]
public async Task Apply_marks_the_gate_busy_while_it_runs()
{
    var observer = new BlockingObserver();
    var gate = new ActivityGate(observer);
    await using var host = await WebAppHost.StartAsync(configureServices: s => s.AddSingleton(gate));
    using var client = Authed(host);

    Task<HttpResponseMessage> apply = client.PostAsync($"/api/batch/preview/{planId}/apply", null);
    Assert.True(observer.Entered.Wait(TimeSpan.FromSeconds(10)));

    var busy = await client.GetFromJsonAsync<JsonElement>("/api/app/status");
    Assert.True(busy.GetProperty("busy").GetBoolean());
    Assert.Equal("batch-apply", observer.Reason);

    observer.Release.Set();
    await apply;
    Assert.False((await client.GetFromJsonAsync<JsonElement>("/api/app/status"))
        .GetProperty("busy").GetBoolean());
}
```

The test creates `planId` with the existing batch-preview fixtures in `tests/DocumentIDE.IntegrationTests/Batch`.

- [ ] **Step 2: Run and confirm failure**

Run: `dotnet test tests/DocumentIDE.IntegrationTests/DocumentIDE.IntegrationTests.csproj --filter "FullyQualifiedName~ActivityGateWiringTests"`
Expected: FAIL — busy stays false.

- [ ] **Step 3: Implement**

Inject `ActivityGate gate` and wrap the mutation body:

- `BatchPreviewController.Apply` → `using var lease = gate.Enter("batch-apply");`
- `BatchPreviewController.Undo` → `"batch-undo"`
- `WordController.OpenWordDocument` / `OpenDocumentInWord` → `"word-open"`
- `SourceControlController`: `Stage`, `Unstage`, `StageAll`, `UnstageAll`, `Commit`, `CommitAll`, `Fetch`, `Push`, `Pull`, `ReviewDiscard`, `Discard`, `Restore`, `OpenWorktree`, `CreateWorktree`, `RemoveWorktree`, `RefreshWorktrees` → `"git"`
- `ReconciliationController.StartReconciliation` → `"reconcile"`

Place the lease at the top of each action so it is released on every return/exception.

- [ ] **Step 4: Run and confirm pass**

Run the Step 2 command. Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/DocumentIDE.Api/Controllers tests/DocumentIDE.IntegrationTests/Api/ActivityGateWiringTests.cs
git commit -m "feat(api): report in-flight mutations as busy"
```

---

### Task 5: Desktop restart coordinator and runtime handshake

**Files:**
- Create: `src/DocumentIDE.Desktop/WebView/RestartCoordinator.cs`
- Create: `src/DocumentIDE.Desktop/WebView/RuntimeHandshake.cs`
- Modify: `src/DocumentIDE.Desktop/WebView/WebAppHost.cs` (ControlToken property + ShutdownRequested event)
- Modify: `src/DocumentIDE.Desktop/MainWindow.xaml.cs`
- Test: `tests/DocumentIDE.IntegrationTests/Desktop/RestartCoordinatorTests.cs`
- Test: `tests/DocumentIDE.IntegrationTests/Desktop/RuntimeHandshakeTests.cs`

**Interfaces:**
- Consumes: `IRestartFlushBridge`, `RestartFlushResult`.
- Produces:
  - `RestartCoordinator : IRestartFlushBridge` with `Func<CancellationToken, Task<RestartFlushResult>>? FlushAsync`.
  - `RuntimeHandshake.Write(string path, RuntimeHandshakePayload payload)` / `Dispose()` deletes the file.
  - `RuntimeHandshakePayload(int Pid, int Port, string Version, string Sha, string ControlToken)`.
  - `event Action? ShutdownRequested` on `WebAppHost`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public async Task Coordinator_returns_ok_when_no_bridge_is_attached()
{
    var coordinator = new RestartCoordinator();
    var result = await coordinator.PrepareRestartAsync(CancellationToken.None);
    Assert.True(result.Ok);
}

[Fact]
public async Task Coordinator_propagates_the_bridge_failure()
{
    var coordinator = new RestartCoordinator
    {
        FlushAsync = _ => Task.FromResult(new RestartFlushResult(false, "locked"))
    };
    var result = await coordinator.PrepareRestartAsync(CancellationToken.None);
    Assert.False(result.Ok);
    Assert.Equal("locked", result.Reason);
}

[Fact]
public void Runtime_handshake_writes_and_deletes_the_file()
{
    string dir = Directory.CreateTempSubdirectory().FullName;
    string path = Path.Combine(dir, "runtime.json");
    using (RuntimeHandshake.Write(path, new RuntimeHandshakePayload(1, 2, "1.0.2.0", "abc", "tok")))
    {
        Assert.True(File.Exists(path));
        Assert.Contains("\"controlToken\":\"tok\"", File.ReadAllText(path));
    }
    Assert.False(File.Exists(path));
}
```

- [ ] **Step 2: Run and confirm failure**

Run: `dotnet test tests/DocumentIDE.IntegrationTests/DocumentIDE.IntegrationTests.csproj --filter "FullyQualifiedName~RestartCoordinatorTests|FullyQualifiedName~RuntimeHandshakeTests"`
Expected: FAIL to compile.

- [ ] **Step 3: Implement**

- `RestartCoordinator.PrepareRestartAsync`: if `FlushAsync` is null return `Ok=true`; otherwise await it.
- `RuntimeHandshake`: serialize camelCase JSON, write with `File.WriteAllText`, delete in `Dispose` swallowing `IOException`.
- `WebAppHost`: subscribe `app.Lifetime.ApplicationStopping` and raise `ShutdownRequested` (the `ControlToken` property was added in Task 3).
- `MainWindow_Loaded`: create `var coordinator = new RestartCoordinator();`, pass it via
  `configureServices: s => s.AddSingleton<IRestartFlushBridge>(coordinator)`, then after WebView init set
  `coordinator.FlushAsync = ct => FlushViaWebViewAsync(ct)`. Write `runtime.json` with `pid`, `port`, `BuildInfo`, and `host.ControlToken`.
- `FlushViaWebViewAsync`: 15 s timeout; `ExecuteScriptAsync("window.__DOCUMENTIDE_PREPARE_RESTART__ ? window.__DOCUMENTIDE_PREPARE_RESTART__() : Promise.resolve({ok:true})")`; parse `{ok, reason}`; on missing bridge return `Ok=true` and log a warning.
- Subscribe `WebAppHost.ShutdownRequested` → `Dispatcher.Invoke(() => Application.Current.Shutdown())`.
- `MainWindow_Closed`: dispose the handshake after `_appHost`.

- [ ] **Step 4: Run and confirm pass**

Run the Step 2 command. Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/DocumentIDE.Desktop/WebView/RestartCoordinator.cs src/DocumentIDE.Desktop/WebView/RuntimeHandshake.cs src/DocumentIDE.Desktop/WebView/WebAppHost.cs src/DocumentIDE.Desktop/MainWindow.xaml.cs tests/DocumentIDE.IntegrationTests/Desktop/RestartCoordinatorTests.cs tests/DocumentIDE.IntegrationTests/Desktop/RuntimeHandshakeTests.cs
git commit -m "feat(desktop): add restart coordinator and runtime handshake"
```

---

### Task 6: Frontend auto-save flush hook

**Files:**
- Create: `src/DocumentIDE.Web/src/features/markdown/markdownRestartFlush.ts`
- Create: `src/DocumentIDE.Web/src/features/markdown/markdownRestartFlush.test.ts`
- Modify: `src/DocumentIDE.Web/src/features/markdown/MarkdownEditor.tsx`
- Modify: `src/DocumentIDE.Web/src/main.tsx`

**Interfaces:**
- Produces:
  - `registerRestartFlush(handler: () => Promise<RestartFlushResult>): () => void`
  - `flushForRestart(): Promise<RestartFlushResult>`
  - `installRestartFlushBridge(target?: Window): void`
  - `interface RestartFlushResult { ok: boolean; reason?: string }`

- [ ] **Step 1: Write the failing Vitest test**

```ts
import { afterEach, describe, expect, it, vi } from 'vitest';
import { flushForRestart, installRestartFlushBridge, registerRestartFlush } from './markdownRestartFlush';

describe('markdown restart flush', () => {
  afterEach(() => vi.restoreAllMocks());

  it('returns ok when no editor is dirty', async () => {
    await expect(flushForRestart()).resolves.toEqual({ ok: true });
  });

  it('runs every registered handler and reports the first failure', async () => {
    registerRestartFlush(async () => ({ ok: false, reason: 'locked' }));
    await expect(flushForRestart()).resolves.toEqual({ ok: false, reason: 'locked' });
  });

  it('installs the window bridge', async () => {
    const target = { } as Window;
    installRestartFlushBridge(target);
    await expect(target.__DOCUMENTIDE_PREPARE_RESTART__()).resolves.toEqual({ ok: true });
  });
});
```

- [ ] **Step 2: Run and confirm failure**

Run: `npx vitest run src/features/markdown/markdownRestartFlush.test.ts` (from `src/DocumentIDE.Web`).
Expected: FAIL — module not found.

- [ ] **Step 3: Implement**

- Maintain a module-level `Set<Handler>`; `flushForRestart` awaits all handlers and returns the first `{ok:false}` or `{ok:true}`.
- `installRestartFlushBridge` assigns `window.__DOCUMENTIDE_PREPARE_RESTART__ = flushForRestart`.
- `MarkdownEditor`: keep `sourceRef`/`savedSourceRef`/`documentIdRef`; register a handler on mount when `documentId` is set that returns `{ok:true}` when clean, otherwise calls `saveMarkdown(documentId, source)` and returns `{ok:true}` on success or `{ok:false, reason}` on error. Unregister on unmount.
- Call `installRestartFlushBridge()` once from `main.tsx` before rendering.

- [ ] **Step 4: Add the editor test**

Extend `MarkdownEditor.test.tsx`: render a dirty editor, call `flushForRestart()`, assert `saveMarkdown` was called and the status becomes "Saved". Add a failure case where `saveMarkdown` rejects and the result carries `ok:false`.

- [ ] **Step 5: Run and confirm pass**

Run: `npx vitest run src/features/markdown` from `src/DocumentIDE.Web`. Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/DocumentIDE.Web/src/features/markdown src/DocumentIDE.Web/src/main.tsx
git commit -m "feat(web): auto-save dirty editors before an app restart"
```

---

### Task 7: Shared payload publish and build identity

**Files:**
- Create: `scripts/publish-payload.ps1`
- Modify: `scripts/build-release.ps1`
- Test: `tests/DocumentIDE.IntegrationTests/Scripts/PublishPayloadTests.cs`

**Interfaces:**
- Produces: `scripts/publish-payload.ps1 -RepoRoot <path> -OutputDir <path> -Version <semver> [-Sha <sha>]` writes the full payload plus `build-info.json`.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public void Publish_payload_script_defines_the_shared_payload_contract()
{
    string script = File.ReadAllText(Path.Combine(GetRepoRoot(), "scripts", "publish-payload.ps1"));
    Assert.Contains("DocumentIDE.Desktop.csproj", script);
    Assert.Contains("DocumentIDE.ShareHost.csproj", script);
    Assert.Contains("build-info.json", script);
    Assert.Contains("wwwroot", script);
}
```

- [ ] **Step 2: Run and confirm failure**

Run: `dotnet test ... --filter "FullyQualifiedName~PublishPayloadTests"`.
Expected: FAIL — file missing.

- [ ] **Step 3: Implement**

Move the publish/copy block from `build-release.ps1` into `publish-payload.ps1` (same `dotnet publish` args, `npm run build`, `wwwroot` copy). After publish, write:

```json
{ "version": "<Version>", "sha": "<Sha or git rev-parse HEAD>" }
```

to `<OutputDir>/build-info.json`. `build-release.ps1` calls the helper with `-OutputDir artifacts/desktop -Version "1.0.2.0"`, then continues with the WiX step unchanged.

- [ ] **Step 4: Run and confirm pass**

Run the Step 2 command. Then run a dry publish in a temp dir and assert the three required files exist.

- [ ] **Step 5: Commit**

```bash
git add scripts/publish-payload.ps1 scripts/build-release.ps1 tests/DocumentIDE.IntegrationTests/Scripts/PublishPayloadTests.cs
git commit -m "feat(release): extract shared payload publish with build identity"
```

---

### Task 8: Auto-update watcher

**Files:**
- Create: `scripts/dev-autoupdate.ps1`
- Test: `tests/DocumentIDE.IntegrationTests/Scripts/DevAutoUpdateTests.cs`

**Interfaces:**
- Consumes: `scripts/publish-payload.ps1`, `%LOCALAPPDATA%\DocumentIDE\runtime.json`, `/api/app/*`.
- Produces: `scripts/dev-autoupdate.ps1 [-RepoRoot] [-WorktreeRoot] [-IntervalSeconds 60] [-Once] [-Force] [-SelfTest] [-SelfTestRoot <path>] [-NoLaunch]`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public void Watcher_script_has_the_required_contract()
{
    string script = File.ReadAllText(Path.Combine(GetRepoRoot(), "scripts", "dev-autoupdate.ps1"));
    foreach (string token in new[]
             {
                 "origin/master", "last-built.sha", "runtime.json",
                 "prepare-restart", "/quit", "update-manifest.json", "updater.lock",
             })
    {
        Assert.Contains(token, script);
    }
}

[Fact]
public void Watcher_self_test_swaps_and_rolls_back()
{
    var psi = new ProcessStartInfo("pwsh", "-NoProfile -File scripts/dev-autoupdate.ps1 -SelfTest")
    {
        WorkingDirectory = GetRepoRoot(),
        RedirectStandardOutput = true,
        RedirectStandardError = true,
    };
    using var process = Process.Start(psi)!;
    string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
    process.WaitForExit(120_000);
    Assert.True(process.ExitCode == 0, output);
}
```

- [ ] **Step 2: Run and confirm failure**

Run: `dotnet test ... --filter "FullyQualifiedName~DevAutoUpdateTests"`.
Expected: FAIL — file missing.

- [ ] **Step 3: Implement the watcher**

Structure the script as functions so `-SelfTest` can call them with temp directories:

- `Get-MasterSha` — `git -C $WorktreeRoot fetch origin master`; `git rev-parse origin/master`.
- `Test-NewBuild` — compare with `last-built.sha`; `$false` unless `-Force`.
- `Invoke-Build` — `git -C $WorktreeRoot reset --hard origin/master`; call `publish-payload.ps1 -OutputDir $staging -Sha $sha -Version "1.0.2.0"`; write `update-manifest.json`.
- `Read-Runtime` — parse `runtime.json`; return `$null` if the PID is dead or the file is stale (> 5 min older than the process start).
- `Wait-UntilIdle` — poll `/api/app/status` every 5 s until `busy` is false.
- `Request-PrepareRestart` — `POST /api/app/prepare-restart`; return `ok`/`reason`.
- `Request-Quit` — `POST /api/app/quit`; wait up to 30 s for the PID to exit.
- `Invoke-Swap` — rename install → `install.old-<rand>`, staging → install; on failure restore.
- `Start-App` / `Test-Health` — launch `DocumentIDE.Desktop.exe`, poll `/health` for 60 s.
- `Invoke-Rollback` — stop the new process, restore `.old-*`, relaunch.
- `-SelfTest` — use `-SelfTestRoot` (or a fresh temp dir) and assert, exiting nonzero on any mismatch:
  1. `Read-Runtime` ignores a `runtime.json` whose PID is dead (Review Focus 1).
  2. `Test-NewBuild` returns true for a different SHA and false for the same SHA (Review Focus 5).
  3. `Invoke-Swap` replaces `install/` with `staging/` and leaves sibling `DocumentIDE/` (data) and `documents/` untouched.
  4. `Invoke-Swap` with an injected rename failure leaves the original `install/` intact (Review Focus 3).
  5. `Invoke-Rollback` after a simulated health failure restores the `.old-*` directory (Review Focus 4).

Main loop: acquire `updater.lock` (exclusive file), poll `origin/master`, build when the SHA is new, wait for idle, prepare-restart, quit, swap, relaunch if the app was running, verify health, record `last-built.sha`, delete `.old-*`. If the app was not running, swap without launching.

All steps append to `%LOCALAPPDATA%\DocumentIDE\logs\autoupdate.log`. Never log the control token.

- [ ] **Step 4: Run and confirm pass**

Run the Step 2 command. Expected: PASS (both tests).

- [ ] **Step 5: Commit**

```bash
git add scripts/dev-autoupdate.ps1 tests/DocumentIDE.IntegrationTests/Scripts/DevAutoUpdateTests.cs
git commit -m "feat(dev): add local auto-update watcher with safe swap and rollback"
```

---

### Task 9: Logon task installer

**Files:**
- Create: `scripts/install-dev-autoupdate.ps1`
- Test: `tests/DocumentIDE.IntegrationTests/Scripts/DevAutoUpdateTests.cs` (extend)

**Interfaces:**
- Produces: `scripts/install-dev-autoupdate.ps1 [-RepoRoot] [-Uninstall]` registers/removes a per-user logon Scheduled Task named `DocumentIDE Dev Auto-Update`.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public void Task_installer_registers_a_per_user_logon_task()
{
    string script = File.ReadAllText(Path.Combine(GetRepoRoot(), "scripts", "install-dev-autoupdate.ps1"));
    Assert.Contains("Register-ScheduledTask", script);
    Assert.Contains("AtLogOn", script);
    Assert.Contains("DocumentIDE Dev Auto-Update", script);
    Assert.Contains("-Uninstall", script);
}
```

- [ ] **Step 2: Run and confirm failure**

Run the filter. Expected: FAIL.

- [ ] **Step 3: Implement**

`Register-ScheduledTask -TaskName "DocumentIDE Dev Auto-Update" -Trigger (New-ScheduledTaskTrigger -AtLogOn) -Action (New-ScheduledTaskAction -Execute "pwsh" -Argument "-NoProfile -WindowStyle Hidden -File `"<path>`" -WorkingDirectory <repo>) -RunLevel Limited`. `-Uninstall` calls `Unregister-ScheduledTask -Confirm:$false`.

- [ ] **Step 4: Run and confirm pass**

Run the filter. Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add scripts/install-dev-autoupdate.ps1 tests/DocumentIDE.IntegrationTests/Scripts/DevAutoUpdateTests.cs
git commit -m "feat(dev): register the auto-update watcher at logon"
```

---

### Task 10: Per-user installer and version bump

**Files:**
- Modify: `installer/DocumentIDE.wxs`
- Modify: `tests/DocumentIDE.IntegrationTests/Release/InstallerTests.cs`
- Modify: `src/DocumentIDE.Desktop/DocumentIDE.Desktop.csproj` (Version)
- Create: `docs/releases/v1.0.2.md`

**Interfaces:**
- Consumes: nothing.
- Produces: a per-user MSI at `%LOCALAPPDATA%\Programs\Document IDE`.

- [ ] **Step 1: Update the failing tests first**

Change `InstallerTests.Installer_Manifest_DocumentIDE_Wxs_Exists_And_Valid`:

```csharp
Assert.Contains("Scope=\"perUser\"", wxsContent);
Assert.Contains("LocalAppDataFolder", wxsContent);
Assert.DoesNotContain("ProgramFiles64Folder", wxsContent);
Assert.Contains("Version=\"1.0.2.0\"", wxsContent);
```

Add a test that the app and MSI versions agree by reading the csproj `<Version>`.

- [ ] **Step 2: Run and confirm failure**

Run: `dotnet test ... --filter "FullyQualifiedName~InstallerTests"`.
Expected: FAIL — still `perMachine` / `1.0.0.0`.

- [ ] **Step 3: Implement**

```xml
<Package Name="Document IDE"
         Manufacturer="Document IDE"
         Version="1.0.2.0"
         UpgradeCode="C3B24F81-99A1-4E12-B8E5-77981A6498B1"
         Scope="perUser">

  <StandardDirectory Id="LocalAppDataFolder">
    <Directory Id="ProgramsFolder" Name="Programs">
      <Directory Id="INSTALLFOLDER" Name="Document IDE" />
    </Directory>
  </StandardDirectory>
  ...
```

Keep `MajorUpgrade`, the WebView2 search, the shortcut component, and `PayloadFiles`. Add `<Version>1.0.2.0</Version>` to `DocumentIDE.Desktop.csproj`. Write `docs/releases/v1.0.2.md` documenting the one-time uninstall of the per-machine 1.0.0.0 build.

- [ ] **Step 4: Run and confirm pass**

Run the filter. Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add installer/DocumentIDE.wxs src/DocumentIDE.Desktop/DocumentIDE.Desktop.csproj tests/DocumentIDE.IntegrationTests/Release/InstallerTests.cs docs/releases/v1.0.2.md
git commit -m "feat(release): install per-user and bump to 1.0.2"
```

---

### Task 11: Operator runbook and full verification

**Files:**
- Create: `docs/runbooks/dev-auto-update.md`
- Create: `tests/DocumentIDE.IntegrationTests/Release/DevAutoUpdateRunbookTests.cs`

**Interfaces:**
- Consumes: the script names and paths produced by Tasks 7–10.
- Produces: an operator runbook that pins the migration, task control, logging, and data-safety rules.

- [ ] **Step 1: Write the failing doc-contract test**

```csharp
[Fact]
public void Runbook_documents_migration_control_and_data_safety()
{
    string runbook = File.ReadAllText(
        Path.Combine(GetRepoRoot(), "docs", "runbooks", "dev-auto-update.md"));
    Assert.Contains("per-user", runbook, StringComparison.OrdinalIgnoreCase);
    Assert.Contains("install-dev-autoupdate.ps1", runbook);
    Assert.Contains("last-built.sha", runbook);
    Assert.Contains("autoupdate.log", runbook);
    Assert.Contains("user documents", runbook, StringComparison.OrdinalIgnoreCase);
    Assert.Contains("rollback", runbook, StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 2: Run and confirm failure**

Run: `dotnet test ... --filter "FullyQualifiedName~DevAutoUpdateRunbookTests"`.
Expected: FAIL — runbook missing.

- [ ] **Step 3: Write the runbook**

Cover:

- Prerequisites: `pwsh`, .NET 10.0.302, Node 22, WiX (only for release builds), `gh` optional.
- One-time migration: uninstall the per-machine 1.0.0.0 product, install the per-user 1.0.2 MSI.
- First run: `scripts/install-dev-autoupdate.ps1`, then `scripts/dev-autoupdate.ps1 -Once` for a manual check.
- Day-to-day: watcher runs at logon; logs at `%LOCALAPPDATA%\DocumentIDE\logs\autoupdate.log`; state at `last-built.sha`.
- Stop/remove: `scripts/install-dev-autoupdate.ps1 -Uninstall`.
- Manual rebuild: `scripts/dev-autoupdate.ps1 -Once -Force`.
- Rollback: the watcher restores `.old-*` automatically; for manual recovery, stop the app, rename the install directory back, relaunch.
- Data safety: the updater never touches user documents, `%LOCALAPPDATA%\DocumentIDE` data, history, or the WebView profile.

- [ ] **Step 4: Run the full verification**

```powershell
dotnet build DocumentIDE.sln
dotnet test DocumentIDE.sln --no-build
npx vitest run          # from src/DocumentIDE.Web
npm run build           # from src/DocumentIDE.Web
```

Expected: all green; no new warnings that fail the build.

- [ ] **Step 5: Commit**

```bash
git add docs/runbooks/dev-auto-update.md tests/DocumentIDE.IntegrationTests/Release/DevAutoUpdateRunbookTests.cs
git commit -m "docs(runbook): document the dev auto-update operator flow"
```

---

## Self-Review

**Spec coverage**

| Spec section | Task |
|---|---|
| 4.1 per-user install | Task 10 |
| 4.2 build identity | Tasks 2, 7 |
| 4.3 master worktree | Task 8 |
| 4.4 watcher | Task 8 |
| 4.5 publish/staging | Tasks 7, 8 |
| 4.6 runtime handshake + control surface | Tasks 2, 3, 5 |
| 4.7 busy signal | Tasks 1, 4 |
| 4.8 auto-save before restart | Tasks 3, 5, 6 |
| 4.9 swap/relaunch | Task 8 |
| 5 safety/security | Tasks 2, 8, 10, 11 |
| 6 error handling | Task 8 (self-test scenarios 3–5) |
| 7 testing | every task |

**Step scan.** Every step names one action and one checkable result. No "handle edge cases" or "write tests for the above" placeholders.

**Type consistency.** `ActivityGate.Enter` → used by Task 4. `RestartFlushResult(Ok, Reason)` → used by Tasks 3, 5. `BuildInfo(Version, Sha)` → used by Tasks 2, 3, 5. `RuntimeHandshakePayload(Pid, Port, Version, Sha, ControlToken)` → used by Task 5 and read by Task 8. `IRestartFlushBridge.PrepareRestartAsync` → implemented in Tasks 3 and 5.

**Review Focus.** Each of the five failure modes has a named test in the owning task (Tasks 8, 3/6, 8, 8, 8).

**Proportion.** The plan is longer than the spec because it pins interfaces and test commands; it does not transcribe controller bodies or script internals.
