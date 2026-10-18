using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Api;
using DocumentIDE.Api.Controllers;
using DocumentIDE.Api.Security;
using DocumentIDE.Core.SourceControl;
using DocumentIDE.Infrastructure.SourceControl;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DocumentIDE.IntegrationTests.SourceControl;

/// <summary>
/// SCM-13 — Explicit Git Push (spec 13 §14.4/§18/§19/§20/§23).
///
/// These tests drive <see cref="GitPushService"/> against real Git repositories
/// and real <b>local bare remotes</b> (no network access) through the SCM-01
/// controlled process boundary and the SCM-01 per-worktree mutation queue:
/// <list type="bullet">
/// <item>an explicit push updates the configured upstream's branch,</item>
/// <item>a repository with <b>no configured upstream</b> is a first-class typed
/// state and runs no push at all (never a guessed default remote),</item>
/// <item>a rejected (non-fast-forward) push is typed and <b>preserves every
/// local commit</b>,</item>
/// <item>authentication-required and permission/host failures stay typed and
/// never hang on a terminal prompt,</item>
/// <item>no credential material from a remote URL ever reaches an outcome
/// message, error or diagnostic.</item>
/// </list>
/// </summary>
public sealed class GitPushTests
{
    /// <summary>
    /// Required test (mandatory first failing test): "Local bare-remote push
    /// succeeds." A local commit is published to the configured upstream's bare
    /// remote, and the tracking state Git re-reads afterwards reports the branch
    /// as in sync.
    /// </summary>
    [Fact]
    public async Task PushToLocalBareRemoteSucceeds()
    {
        using var repo = GitFetchTests.CreateRepositoryWithUpstream(out string remotePath);
        GitFetchTests.CommitFile(repo.RootPath, "docs/pushed.md", "pushed v1", "local push commit");
        string localHead = GitFetchTests.Head(repo);

        GitPushService service = CreateService();
        GitPushResult result = await service.PushAsync(repo.RootPath);

        Assert.Equal(GitPushOutcome.Succeeded, result.Outcome);
        Assert.True(result.IsSuccess);
        Assert.Null(result.Failure);

        // The bare remote's main branch now points at exactly the local HEAD.
        string remoteHead = TestGitRepository
            .RunGitCaptureUtf8(remotePath, "rev-parse", "main")
            .Trim();
        Assert.Equal(localHead, remoteHead);

        GitRemoteState after = Assert.IsType<GitRemoteState>(result.Remote);
        Assert.True(after.HasUpstream);
        Assert.Equal("origin/main", after.UpstreamReference);
        Assert.Equal(0, after.Ahead);
        Assert.Equal(0, after.Behind);
    }

    // ---------------------------------------------------------------------
    // Rejection preserves local commits (spec 13 §14.4/§20/§23)
    // ---------------------------------------------------------------------

    /// <summary>
    /// Required test: "Rejected push is surfaced." The bare remote moved ahead
    /// (another clone pushed), so the local push is a non-fast-forward and is
    /// rejected. The push is typed (never a silent success) and every local
    /// commit is preserved: HEAD, local history and the working tree are
    /// unchanged, and the remote branch was not rewritten (spec 13 §14.4/§23).
    /// </summary>
    [Fact]
    public async Task RejectedPushIsTypedAndPreservesLocalCommits()
    {
        using var repo = GitFetchTests.CreateRepositoryWithUpstream(out string remotePath);

        // A separate clone advances the bare remote past the local branch.
        string clone = GitFetchTests.CloneRemote(remotePath);
        try
        {
            GitFetchTests.CommitFile(clone, "docs/remote.md", "remote v2", "remote commit");
            Assert.Equal(0, TestGitRepository.RunGit(clone, "push", "-q", "origin", "main"));
        }
        finally
        {
            GitFetchTests.DeleteDirectoryBestEffort(clone);
        }

        // The local branch advances without integrating the remote commit, so
        // the histories diverge and the push cannot fast-forward.
        GitFetchTests.CommitFile(repo.RootPath, "docs/local.md", "local v2", "local commit");
        string localHeadBefore = GitFetchTests.Head(repo);
        string statusBefore = GitFetchTests.WorkingTreeStatus(repo.RootPath);
        IReadOnlyList<string> treeBefore = GitFetchTests.SnapshotWorkingTree(repo.RootPath);

        GitPushResult result = await CreateService().PushAsync(repo.RootPath);

        Assert.Equal(GitPushOutcome.Rejected, result.Outcome);
        Assert.False(result.IsSuccess);
        Assert.Equal("Rejected", result.ErrorCode);
        Assert.NotNull(result.Failure);

        // Local commits are preserved: HEAD, history and the working tree are
        // exactly as they were before the rejected push.
        Assert.Equal(localHeadBefore, GitFetchTests.Head(repo));
        Assert.Contains(
            "local commit",
            TestGitRepository.RunGitCaptureUtf8(repo.RootPath, "log", "--format=%s"));
        Assert.Equal(statusBefore, GitFetchTests.WorkingTreeStatus(repo.RootPath));
        Assert.Equal(treeBefore, GitFetchTests.SnapshotWorkingTree(repo.RootPath));

        // The remote branch was not rewritten by the rejected push.
        string remoteHead = TestGitRepository
            .RunGitCaptureUtf8(remotePath, "rev-parse", "main")
            .Trim();
        Assert.NotEqual(localHeadBefore, remoteHead);
    }

    // ---------------------------------------------------------------------
    // No-upstream is typed and runs no Git (spec 13 §14.2/§14.4)
    // ---------------------------------------------------------------------

    /// <summary>
    /// A branch with no configured upstream is the first-class typed
    /// <c>NoUpstream</c> state and runs <b>no</b> push at all: Document IDE never
    /// guesses a default remote for a branch Git does not track (spec 13
    /// §14.2/§14.4).
    /// </summary>
    [Fact]
    public async Task PushWithNoUpstreamIsTypedAndRunsNoGit()
    {
        using var repo = GitFetchTests.CreateRepositoryWithoutUpstream();

        var recording = new RecordingGitProcessRunner(
            new GitProcessRunner(GitProcessRunnerOptions.Default, new GitWorktreeMutationQueue()));
        GitPushService service = CreateService(recording);

        GitPushResult result = await service.PushAsync(repo.RootPath);

        Assert.Equal(GitPushOutcome.NoUpstream, result.Outcome);
        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Remote);
        Assert.False(result.Remote!.HasUpstream);
        Assert.Null(result.Remote.Ahead);
        Assert.Null(result.Remote.Behind);

        // No push subcommand ever ran.
        Assert.DoesNotContain(
            recording.Invocations,
            arguments => arguments.Count > 0 && arguments[0] == "push");
    }

    // ---------------------------------------------------------------------
    // Typed remote failures (spec 13 §14.4/§20)
    // ---------------------------------------------------------------------

    /// <summary>
    /// Required test: "Authentication-required state does not hang waiting for
    /// terminal input." A remote that asks for credentials is typed
    /// <c>AuthenticationRequired</c> from Git's own <c>LC_ALL=C</c>-pinned
    /// diagnostics (with the runner's <see cref="GitProcessErrorKind"/>
    /// deliberately unset, so the text classification is what is exercised), and
    /// the call returns promptly rather than blocking. Driven with a scripted
    /// runner, so no network is needed.
    /// </summary>
    [Fact]
    public async Task PushAuthenticationFailureIsTypedAndBoundedFromGitDiagnostics()
    {
        using var repo = GitFetchTests.CreateRepositoryWithUpstream(out _);

        var inner = new GitProcessRunner(GitProcessRunnerOptions.Default, new GitWorktreeMutationQueue());
        var scripted = new ScriptedGitProcessRunner(
            inner,
            ("push", new GitProcessResult(
                GitProcessStatus.Failed,
                128,
                string.Empty,
                "fatal: could not read Username for 'https://example.invalid': terminal prompts disabled")));
        GitPushService service = CreateService(scripted);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        GitPushResult result = await service.PushAsync(repo.RootPath);
        stopwatch.Stop();

        Assert.Equal(GitPushOutcome.AuthenticationRequired, result.Outcome);
        Assert.False(result.IsSuccess);
        Assert.Equal("AuthenticationRequired", result.ErrorCode);
        Assert.NotNull(result.Failure);
        Assert.Equal(GitProcessErrorKind.None, result.Failure!.ErrorKind);

        // Bounded: the typed outcome returns immediately; nothing waits for a
        // terminal prompt.
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(30),
            $"the typed authentication outcome must not wait for input (took {stopwatch.Elapsed}).");

        // No credential/secret material is echoed into the surfaced message.
        Assert.DoesNotContain("Username", result.Message ?? string.Empty);
        Assert.DoesNotContain("https://", result.Message ?? string.Empty);
    }

    /// <summary>
    /// A remote that denies the push (permission wording) is typed
    /// <c>PermissionDenied</c> rather than a generic failure (spec 13 §20).
    /// </summary>
    [Fact]
    public async Task PushPermissionDeniedIsTypedFromGitDiagnostics()
    {
        using var repo = GitFetchTests.CreateRepositoryWithUpstream(out _);

        var inner = new GitProcessRunner(GitProcessRunnerOptions.Default, new GitWorktreeMutationQueue());
        var scripted = new ScriptedGitProcessRunner(
            inner,
            ("push", new GitProcessResult(
                GitProcessStatus.Failed,
                128,
                string.Empty,
                "remote: Permission denied to push to this repository.\n" +
                "fatal: unable to access 'https://example.invalid/repo.git/': The requested URL returned error: 403")));
        GitPushService service = CreateService(scripted);

        GitPushResult result = await service.PushAsync(repo.RootPath);

        Assert.Equal(GitPushOutcome.PermissionDenied, result.Outcome);
        Assert.Equal("PermissionDenied", result.ErrorCode);
        Assert.NotNull(result.Failure);
    }

    /// <summary>
    /// A remote that cannot be reached yields the typed <c>HostUnreachable</c>
    /// outcome — never an exception and never a silent success (spec 13 §20).
    /// Uses a local path that is not a repository: no network access.
    /// </summary>
    [Fact]
    public async Task PushHostUnreachableIsTyped()
    {
        using var repo = GitFetchTests.CreateRepositoryWithUpstream(out _);
        string missingRemote = Path.Combine(Path.GetTempPath(), "scm13", $"missing-{Guid.NewGuid():N}.git");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "remote", "set-url", "origin", missingRemote));

        GitPushResult result = await CreateService().PushAsync(repo.RootPath);

        Assert.Equal(GitPushOutcome.HostUnreachable, result.Outcome);
        Assert.False(result.IsSuccess);
        Assert.Equal("HostUnreachable", result.ErrorCode);
        Assert.NotNull(result.Failure);
    }

    /// <summary>
    /// A machine without discoverable Git yields the typed <c>GitUnavailable</c>
    /// outcome, never an exception (spec 13 §14.4/§20).
    /// </summary>
    [Fact]
    public async Task PushWhenGitUnavailableIsTypedGitUnavailable()
    {
        using var repo = GitFetchTests.CreateRepositoryWithUpstream(out _);

        var runner = new UnavailableGitProcessRunner();
        GitPushService service = CreateService(runner, new GitRepositoryService(runner));
        GitPushResult result = await service.PushAsync(repo.RootPath);

        Assert.Equal(GitPushOutcome.GitUnavailable, result.Outcome);
        Assert.False(result.IsSuccess);
        Assert.Null(result.Remote);
    }

    /// <summary>
    /// A workspace outside any Git worktree reports the typed
    /// <c>NoRepository</c> outcome; nothing runs (spec 13 §5).
    /// </summary>
    [Fact]
    public async Task PushOutsideRepositoryIsTypedNoRepository()
    {
        string folder = Path.Combine(Path.GetTempPath(), $"scm13-push-norepo-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            GitPushResult result = await CreateService().PushAsync(folder);

            Assert.Equal(GitPushOutcome.NoRepository, result.Outcome);
            Assert.False(result.IsSuccess);
            Assert.Null(result.Remote);
        }
        finally
        {
            GitFetchTests.DeleteDirectoryBestEffort(folder);
        }
    }

    // ---------------------------------------------------------------------
    // Credential material is never surfaced (spec 13 §14.4/§20/§23)
    // ---------------------------------------------------------------------

    /// <summary>
    /// A remote URL that carries userinfo (<c>https://user:password@host/…</c>)
    /// appears in Git's own push diagnostics. No part of that credential may
    /// reach the typed outcome's message, its failure error message, or the
    /// diagnostic streams carried on the failure.
    /// </summary>
    [Fact]
    public async Task PushFailureNeverSurfacesCredentialMaterialFromRemoteUrl()
    {
        using var repo = GitFetchTests.CreateRepositoryWithUpstream(out _);

        const string secret = "s3cr3t-pw";
        string remoteUrl = $"https://alice:{secret}@example.invalid/repo.git";

        var inner = new GitProcessRunner(GitProcessRunnerOptions.Default, new GitWorktreeMutationQueue());
        var scripted = new ScriptedGitProcessRunner(
            inner,
            ("push", new GitProcessResult(
                GitProcessStatus.Failed,
                128,
                $"To {remoteUrl}",
                $"fatal: unable to access '{remoteUrl}/': The requested URL returned error: 401")));
        GitPushService service = CreateService(scripted);

        GitPushResult result = await service.PushAsync(repo.RootPath);

        Assert.Equal(GitPushOutcome.AuthenticationRequired, result.Outcome);
        Assert.NotNull(result.Failure);

        Assert.DoesNotContain(secret, result.Message ?? string.Empty);
        Assert.DoesNotContain(secret, result.Failure!.ErrorMessage ?? string.Empty);
        Assert.DoesNotContain(secret, result.Failure.StandardOutput);
        Assert.DoesNotContain(secret, result.Failure.StandardError);
        // The userinfo itself is never exposed, even without the password.
        Assert.DoesNotContain("alice:", result.Failure.StandardError);
        Assert.DoesNotContain("alice:", result.Failure.StandardOutput);
    }

    // ---------------------------------------------------------------------
    // Controlled invocation, serialization and environment (spec 13 §18/§19/§23)
    // ---------------------------------------------------------------------

    /// <summary>
    /// The push invocation is exactly <c>["push"]</c>: structured arguments
    /// (never a shell string), no branch/remote/refspec argument, and no option
    /// that force-pushes, deletes or widens the pushed scope (spec 13 §14.4/§23).
    /// </summary>
    [Fact]
    public async Task PushCarriesOnlyStructuredPushArguments()
    {
        using var repo = GitFetchTests.CreateRepositoryWithUpstream(out _);
        GitFetchTests.CommitFile(repo.RootPath, "docs/pushed.md", "pushed", "push me");

        var recording = new RecordingGitProcessRunner(
            new GitProcessRunner(GitProcessRunnerOptions.Default, new GitWorktreeMutationQueue()));
        GitPushService service = CreateService(recording);

        GitPushResult result = await service.PushAsync(repo.RootPath);
        Assert.Equal(GitPushOutcome.Succeeded, result.Outcome);

        GitProcessRequest pushRequest = Assert.Single(
            recording.Requests,
            request => request.Arguments.Count > 0 && request.Arguments[0] == "push");
        Assert.Equal(new[] { "push" }, pushRequest.Arguments.ToArray());

        string[] forbidden =
        {
            "--force", "-f", "--force-with-lease", "--all", "--tags", "--mirror", "--delete",
            "--set-upstream", "--prune", "pull", "merge", "rebase", "commit", "--no-verify",
        };
        foreach (IReadOnlyList<string> invocation in recording.Invocations)
        {
            foreach (string token in invocation)
            {
                Assert.DoesNotContain(token, forbidden);
            }
        }
    }

    /// <summary>
    /// Push runs against the true worktree root even when the caller passes a
    /// subdirectory — a subdirectory would defeat per-worktree serialization if
    /// it were used as the working directory (spec 13 §19).
    /// </summary>
    [Fact]
    public async Task PushUsesTheTrueWorktreeRootEvenFromASubdirectory()
    {
        using var repo = GitFetchTests.CreateRepositoryWithUpstream(out _);
        GitFetchTests.CommitFile(repo.RootPath, "docs/pushed.md", "pushed", "push me");
        string nested = Path.Combine(repo.RootPath, "docs");

        var recording = new RecordingGitProcessRunner(
            new GitProcessRunner(GitProcessRunnerOptions.Default, new GitWorktreeMutationQueue()));
        GitPushService service = CreateService(recording);

        GitPushResult result = await service.PushAsync(nested);

        Assert.Equal(GitPushOutcome.Succeeded, result.Outcome);
        GitProcessRequest pushRequest = Assert.Single(
            recording.Requests,
            request => request.Arguments.Count > 0 && request.Arguments[0] == "push");
        Assert.Equal(
            Path.GetFullPath(repo.RootPath),
            Path.GetFullPath(pushRequest.WorkingDirectory),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Push runs through the SCM-01 per-worktree mutation queue, so concurrent
    /// pushes against one worktree serialize (spec 13 §19), and the resolving
    /// call from a nested subdirectory shares the same slot.
    /// </summary>
    [Fact]
    public async Task PushRunsThroughThePerWorktreeMutationQueue()
    {
        using var repo = GitFetchTests.CreateRepositoryWithUpstream(out _);
        GitFetchTests.CommitFile(repo.RootPath, "docs/pushed.md", "pushed", "push me");
        string nested = Path.Combine(repo.RootPath, "docs");

        var queue = new GitWorktreeMutationQueue();
        var runner = new GitProcessRunner(GitProcessRunnerOptions.Default, queue);
        var repositories = new GitRepositoryService(runner);
        var service = new GitPushService(runner, repositories, new GitRemoteService(runner, repositories));

        GitPushResult[] results = await Task.WhenAll(
            service.PushAsync(repo.RootPath),
            service.PushAsync(nested),
            service.PushAsync(repo.RootPath));

        Assert.All(results, result => Assert.Equal(GitPushOutcome.Succeeded, result.Outcome));
        Assert.Equal(1, queue.MaxObservedConcurrency);
    }

    /// <summary>
    /// The effective environment applied to the push process must neutralize
    /// interactive credential prompting: a configured credential helper can
    /// otherwise block on a GUI prompt for the whole default timeout and be
    /// mis-typed (spec 13 §14.4/§18). The invocation must also carry a bounded
    /// timeout as a backstop.
    /// </summary>
    [Fact]
    public async Task PushProcessIsBoundedAndDisablesInteractiveCredentials()
    {
        using var repo = GitFetchTests.CreateRepositoryWithUpstream(out _);
        GitFetchTests.CommitFile(repo.RootPath, "docs/pushed.md", "pushed", "push me");

        var recording = new RecordingGitProcessRunner(
            new GitProcessRunner(GitProcessRunnerOptions.Default, new GitWorktreeMutationQueue()));
        GitPushService service = CreateService(recording);

        GitPushResult result = await service.PushAsync(repo.RootPath);
        Assert.Equal(GitPushOutcome.Succeeded, result.Outcome);

        GitProcessRequest pushRequest = Assert.Single(
            recording.Requests,
            request => request.Arguments.Count > 0 && request.Arguments[0] == "push");

        IReadOnlyDictionary<string, string> effective =
            GitProcessRunnerOptions.ResolveEnvironmentVariables(pushRequest.EnvironmentOverrides);

        Assert.Equal("0", effective["GIT_TERMINAL_PROMPT"]);
        Assert.Equal(string.Empty, effective["GIT_ASKPASS"]);
        Assert.Equal(string.Empty, effective["SSH_ASKPASS"]);
        Assert.Equal("never", effective["GCM_INTERACTIVE"]);

        // Bounded: the invocation never disables the timeout.
        TimeSpan timeout = pushRequest.Timeout ?? GitProcessRunnerOptions.Default.DefaultTimeout;
        Assert.True(timeout > TimeSpan.Zero, "the push invocation must carry a positive timeout.");
        Assert.NotEqual(Timeout.InfiniteTimeSpan, timeout);
    }

    /// <summary>
    /// Paths containing spaces, Unicode and special characters are handled
    /// verbatim (no shell, structured arguments) and a push still publishes the
    /// local commit to the configured upstream (spec 13 §18/§23).
    /// </summary>
    [Fact]
    public async Task PushHandlesPathsWithSpacesUnicodeAndSpecialCharacters()
    {
        string suffix = Guid.NewGuid().ToString("N");
        using var repo = GitFetchTests.CreateRepositoryWithUpstream(
            out string remotePath,
            directoryName: $"scm13 repo \u00fc \u6771 & (# {suffix}",
            remoteDirectoryName: $"remote \u00fc \u6771 & (# {suffix}.git");

        GitFetchTests.CommitFile(repo.RootPath, "docs/pushed.md", "pushed", "push me");
        string localHead = GitFetchTests.Head(repo);

        GitPushResult result = await CreateService().PushAsync(repo.RootPath);

        Assert.Equal(GitPushOutcome.Succeeded, result.Outcome);
        Assert.Equal(
            localHead,
            TestGitRepository.RunGitCaptureUtf8(remotePath, "rev-parse", "main").Trim());
    }

    /// <summary>
    /// A repository/worktree that disappears before the operation is a typed
    /// non-success — never an exception (spec 13 §20 failure/edge cases).
    /// </summary>
    [Fact]
    public async Task PushOnDisappearedWorktreeIsTyped()
    {
        TestGitRepository repo = GitFetchTests.CreateRepositoryWithUpstream(out _);
        string root = repo.RootPath;
        repo.Dispose();

        GitPushResult result = await CreateService().PushAsync(root);

        Assert.False(result.IsSuccess);
        Assert.Contains(
            result.Outcome,
            new[] { GitPushOutcome.Failed, GitPushOutcome.NoRepository });
    }

    // ---------------------------------------------------------------------
    // Push is never automatic (spec 13 §14.4/§23)
    // ---------------------------------------------------------------------

    /// <summary>
    /// Required regression test: "Commit does not auto-push." A commit performed
    /// through the SCM-07 commit service never invokes <c>git push</c> and never
    /// changes the remote: the local commit exists while the bare remote's
    /// branch is still at its previous commit. Push remains a separate,
    /// user-initiated action (spec 13 §14.4/§23).
    /// </summary>
    [Fact]
    public async Task CommitDoesNotAutoPush()
    {
        using var repo = GitFetchTests.CreateRepositoryWithUpstream(out string remotePath);

        var recording = new RecordingGitProcessRunner(
            new GitProcessRunner(GitProcessRunnerOptions.Default, new GitWorktreeMutationQueue()));
        var repositories = new GitRepositoryService(recording);
        var stages = new GitStageService(recording, repositories);
        var commits = new GitCommitService(recording, repositories, stages);

        string remoteHeadBefore = TestGitRepository
            .RunGitCaptureUtf8(remotePath, "rev-parse", "main")
            .Trim();

        repo.WriteFile("docs/committed.md", "committed");
        GitStageResult staged = await stages.StageAllAsync(repo.RootPath);
        Assert.True(staged.IsSuccess);

        GitCommitResult committed = await commits.CommitAsync(repo.RootPath, "commit only");
        Assert.Equal(GitCommitOutcome.Succeeded, committed.Outcome);

        // The local commit was created …
        string localHead = GitFetchTests.Head(repo);
        Assert.NotEqual(remoteHeadBefore, localHead);

        // … the commit flow really ran …
        Assert.Contains(
            recording.Invocations,
            arguments => arguments.Count > 0 && arguments[0] == "commit");

        // … and no push ever ran: neither invoked nor reflected on the remote.
        Assert.DoesNotContain(
            recording.Invocations,
            arguments => arguments.Count > 0 && arguments[0] == "push");
        Assert.Equal(
            remoteHeadBefore,
            TestGitRepository.RunGitCaptureUtf8(remotePath, "rev-parse", "main").Trim());
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    internal static GitPushService CreateService(
        IGitProcessRunner? runner = null,
        GitRepositoryService? repositories = null,
        GitRemoteService? remotes = null)
    {
        var queue = new GitWorktreeMutationQueue();
        IGitProcessRunner effective = runner ?? new GitProcessRunner(GitProcessRunnerOptions.Default, queue);
        GitRepositoryService repos = repositories ?? new GitRepositoryService(effective);
        GitRemoteService remoteService = remotes ?? new GitRemoteService(effective, repos);
        return new GitPushService(effective, repos, remoteService);
    }
}

/// <summary>
/// SCM-13 — privileged Source Control API surface for the explicit push
/// (spec 13 §14.4/§18/§19/§20).
///
/// The push route runs one explicit <c>git push</c> against the resolved
/// worktree and returns the refreshed tracking state; a branch with no
/// configured upstream is the typed <c>NoUpstream</c> outcome and runs no push;
/// every remote failure stays HTTP 200 with a typed outcome. Every route
/// inherits the localhost session-token guard; a missing path is a typed 400 and
/// nothing runs.
/// </summary>
public sealed class GitPushApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public GitPushApiTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// The push route runs one explicit <c>git push</c> and publishes the local
    /// commit to the configured upstream's bare remote (spec 13 §14.4).
    /// </summary>
    [Fact]
    public async Task PushEndpointPublishesLocalCommit()
    {
        using var repo = GitFetchTests.CreateRepositoryWithUpstream(out string remotePath);
        GitFetchTests.CommitFile(repo.RootPath, "docs/pushed.md", "pushed", "push me");
        string localHead = GitFetchTests.Head(repo);

        using var client = await CreateAuthorizedClientAsync();
        using var response = await client.PostAsJsonAsync(
            "/api/source-control/push",
            new SourceControlPushRequest(repo.RootPath));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        SourceControlPushResponse? body = await response.Content.ReadFromJsonAsync<SourceControlPushResponse>();

        Assert.NotNull(body);
        Assert.Equal("Succeeded", body!.Outcome);
        Assert.True(body.IsSuccess);
        Assert.Null(body.ErrorCode);
        Assert.Null(body.Failure);
        Assert.NotNull(body.Remote);
        Assert.True(body.Remote!.HasUpstream);
        Assert.Equal("origin/main", body.Remote.UpstreamReference);
        Assert.Equal(0, body.Remote.Ahead);
        Assert.Equal(0, body.Remote.Behind);

        Assert.Equal(
            localHead,
            TestGitRepository.RunGitCaptureUtf8(remotePath, "rev-parse", "main").Trim());
    }

    /// <summary>
    /// A repository with no configured upstream is the explicit typed
    /// <c>NoUpstream</c> outcome with a null remote payload — never a silent
    /// push to a guessed default remote (spec 13 §14.2/§14.4).
    /// </summary>
    [Fact]
    public async Task PushEndpointReportsNoUpstreamTypedly()
    {
        using var repo = GitFetchTests.CreateRepositoryWithoutUpstream();

        using var client = await CreateAuthorizedClientAsync();
        using var response = await client.PostAsJsonAsync(
            "/api/source-control/push",
            new SourceControlPushRequest(repo.RootPath));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        SourceControlPushResponse? body = await response.Content.ReadFromJsonAsync<SourceControlPushResponse>();

        Assert.NotNull(body);
        Assert.Equal("NoUpstream", body!.Outcome);
        Assert.False(body.IsSuccess);
        Assert.Equal("NoUpstream", body.ErrorCode);
        Assert.NotNull(body.Remote);
        Assert.False(body.Remote!.HasUpstream);
    }

    /// <summary>A missing request body/path is a typed 400 and no Git runs (spec 13 §18).</summary>
    [Fact]
    public async Task PushEndpointRequiresPath()
    {
        using var client = await CreateAuthorizedClientAsync();
        using var response = await client.PostAsJsonAsync(
            "/api/source-control/push",
            new SourceControlPushRequest(null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using JsonDocument payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("PathRequired", payload.RootElement.GetProperty("errorCode").GetString());
    }

    /// <summary>
    /// The push route is guarded exactly like the other privileged Source Control
    /// routes: a missing token is 401, a hostile origin is 403 and a wrong token
    /// is 401, and no unauthenticated request reaches Git (spec 13 §18).
    /// </summary>
    [Fact]
    public async Task PushRouteRejectsMissingInvalidTokenAndHostileOrigin()
    {
        using var repo = GitFetchTests.CreateRepositoryWithUpstream(out string remotePath);
        string remoteHeadBefore = TestGitRepository
            .RunGitCaptureUtf8(remotePath, "rev-parse", "main")
            .Trim();

        using (var anonymous = _factory.CreateClient())
        {
            using var response = await anonymous.PostAsJsonAsync(
                "/api/source-control/push",
                new SourceControlPushRequest(repo.RootPath));
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var tokenProvider = scope.ServiceProvider.GetRequiredService<ISessionTokenProvider>();

            using var hostileOrigin = _factory.CreateClient();
            hostileOrigin.DefaultRequestHeaders.Add("Origin", "https://attacker.example");
            hostileOrigin.DefaultRequestHeaders.Add("X-DocumentIDE-Session-Token", tokenProvider.SessionToken);
            using var hostileResponse = await hostileOrigin.PostAsJsonAsync(
                "/api/source-control/push",
                new SourceControlPushRequest(repo.RootPath));
            Assert.Equal(HttpStatusCode.Forbidden, hostileResponse.StatusCode);

            using var wrongToken = _factory.CreateClient();
            wrongToken.DefaultRequestHeaders.Add("Origin", "http://localhost:5173");
            wrongToken.DefaultRequestHeaders.Add("X-DocumentIDE-Session-Token", "invalid-token");
            using var wrongResponse = await wrongToken.PostAsJsonAsync(
                "/api/source-control/push",
                new SourceControlPushRequest(repo.RootPath));
            Assert.Equal(HttpStatusCode.Unauthorized, wrongResponse.StatusCode);
        }

        // No unauthenticated request reached Git: the remote branch is unchanged.
        Assert.Equal(
            remoteHeadBefore,
            TestGitRepository.RunGitCaptureUtf8(remotePath, "rev-parse", "main").Trim());
    }

    private Task<HttpClient> CreateAuthorizedClientAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var tokenProvider = scope.ServiceProvider.GetRequiredService<ISessionTokenProvider>();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Origin", "http://localhost:5173");
        client.DefaultRequestHeaders.Add("X-DocumentIDE-Session-Token", tokenProvider.SessionToken);
        return Task.FromResult(client);
    }
}
