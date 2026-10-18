using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Api;
using DocumentIDE.Api.Controllers;
using DocumentIDE.Api.Security;
using DocumentIDE.Core.Batch;
using DocumentIDE.Core.History;
using DocumentIDE.Core.SourceControl;
using DocumentIDE.Infrastructure.History;
using DocumentIDE.Infrastructure.Persistence;
using DocumentIDE.Infrastructure.SourceControl;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace DocumentIDE.IntegrationTests.SourceControl;

/// <summary>
/// SCM-09 — Document IDE History ↔ Git Correlation (spec 13 §4.3/§12/§13/§24).
///
/// These tests drive the correlation store and the correlation service against
/// a real Git repository and a real SQLite database:
/// <list type="bullet">
/// <item>a successful commit can be linked to a <c>WorkSession</c> (the
/// mandated first failing behavior),</item>
/// <item>the Git commit remains valid when the IDE correlation data is deleted,
/// because Git commit ids are derived correlation metadata and never a source of
/// truth,</item>
/// <item>batch operation history stays usable with no Git repository at all, so
/// Document IDE History remains independent of Git,</item>
/// <item>no correlation operation ever creates a Git commit, and</item>
/// <item>failures are typed and inspectable instead of silently dropping
/// correlation data.</item>
/// </list>
///
/// Planned-file mapping: the sprint plans this suite at
/// <c>tests/DocumentIDE.IntegrationTests/SourceControl/WorkSessionCorrelationTests.cs</c>
/// (no <c>DocumentIDE.UnitTests</c> project exists in this repository); the
/// frontend half lives colocated at
/// <c>src/DocumentIDE.Web/src/features/source-control/CommitCorrelationPanel.test.tsx</c>
/// (CAN-05 batch-B ruling).
/// </summary>
public sealed class WorkSessionCorrelationTests : IDisposable
{
    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), "scm09", Guid.NewGuid().ToString("N"), "app.db");

    // ---------------------------------------------------------------------
    // Step 3 (mandated first failing test) — a successful commit links to a
    // WorkSession.
    // ---------------------------------------------------------------------

    /// <summary>
    /// Required test 1 (first failing test): "Successful commit can be linked to
    /// a WorkSession." The commit is created by the real SCM-07 flow first; the
    /// session is started and the changed document recorded as Document IDE
    /// History; only then is the created commit id attached as derived
    /// correlation metadata (spec 13 §13/§24).
    /// </summary>
    [Fact]
    public async Task SuccessfulCommitCanBeLinkedToAWorkSession()
    {
        using var repo = CreateRepositoryWithBaselineCommit(new[] { "docs/a.md" });
        repo.WriteFile("docs/a.md", "a v2");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "--", "docs/a.md"));

        (GitCommitService commits, _) = CreateCommitService();
        GitCommitResult commit = await commits.CommitAsync(repo.RootPath, "link this commit");
        Assert.Equal(GitCommitOutcome.Succeeded, commit.Outcome);
        GitCommitInfo info = commit.Commit!;

        WorkSessionService sessions = await CreateSessionServiceAsync();
        WorkSessionResult started = await sessions.StartAsync(
            workspaceId: "ws-1",
            repositoryRoot: repo.RootPath,
            worktreeRoot: repo.RootPath,
            label: "correlate the first commit",
            CancellationToken.None);
        Assert.Equal(WorkSessionOutcome.Succeeded, started.Outcome);
        string sessionId = started.Session!.Id;

        WorkSessionResult recorded = await sessions.RecordChangedDocumentAsync(
            sessionId, "doc-1", "docs/a.md", CancellationToken.None);
        Assert.Equal(WorkSessionOutcome.Succeeded, recorded.Outcome);
        Assert.Equal("doc-1", Assert.Single(recorded.Session!.ChangedDocuments).DocumentId);

        WorkSessionResult linked = await sessions.AttachCommitAsync(
            sessionId, info.CommitId, info.ShortId, info.Summary, CancellationToken.None);
        Assert.Equal(WorkSessionOutcome.Succeeded, linked.Outcome);

        WorkSessionResult read = await sessions.GetAsync(sessionId, CancellationToken.None);
        WorkSessionCommit correlation = Assert.Single(read.Session!.CommitCorrelations);
        Assert.Equal(info.CommitId, correlation.CommitId);
        Assert.Equal(info.ShortId, correlation.ShortId);
        Assert.Equal("link this commit", correlation.Summary);
        Assert.Equal("doc-1", Assert.Single(read.Session.ChangedDocuments).DocumentId);

        // The correlation is derived metadata: Git still holds exactly the commit
        // that was created, and nothing about the link changed Git.
        Assert.Equal(info.CommitId, Head(repo));
    }

    // ---------------------------------------------------------------------
    // Acceptance — the commit stays valid when correlation data is dropped.
    // ---------------------------------------------------------------------

    /// <summary>
    /// Required test 2: "Git commit remains valid if IDE correlation data is
    /// deleted." Deleting the WorkSession drops only derived Document IDE
    /// metadata: the commit object, the message, HEAD and the working tree are
    /// untouched (spec 13 §13/§24).
    /// </summary>
    [Fact]
    public async Task GitCommitRemainsValidWhenTheCorrelationDataIsDeleted()
    {
        using var repo = CreateRepositoryWithBaselineCommit(new[] { "docs/a.md" });
        repo.WriteFile("docs/a.md", "a v2");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "--", "docs/a.md"));

        (GitCommitService commits, _) = CreateCommitService();
        GitCommitResult commit = await commits.CommitAsync(repo.RootPath, "survives the deletion");
        GitCommitInfo info = commit.Commit!;

        WorkSessionService sessions = await CreateSessionServiceAsync();
        WorkSessionResult started = await sessions.StartAsync(
            "ws-1", repo.RootPath, repo.RootPath, "drop me", CancellationToken.None);
        string sessionId = started.Session!.Id;
        await sessions.RecordChangedDocumentAsync(sessionId, "doc-1", "docs/a.md", CancellationToken.None);
        await sessions.AttachCommitAsync(sessionId, info.CommitId, info.ShortId, info.Summary, CancellationToken.None);

        WorkSessionResult deleted = await sessions.DeleteAsync(sessionId, CancellationToken.None);
        Assert.Equal(WorkSessionOutcome.Succeeded, deleted.Outcome);

        // The correlation is gone... (and its child associations were dropped
        // with it rather than being orphaned).
        Assert.Equal(0, await CountRowsAsync("work_session_documents"));
        Assert.Equal(0, await CountRowsAsync("work_session_commits"));
        WorkSessionResult gone = await sessions.GetAsync(sessionId, CancellationToken.None);
        Assert.Equal(WorkSessionOutcome.SessionNotFound, gone.Outcome);
        Assert.Null(gone.Session);

        // ...and the Git commit is still perfectly valid: HEAD, the object, the
        // message and the working tree all survive the deletion.
        Assert.Equal(info.CommitId, Head(repo));
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "cat-file", "-e", info.CommitId + "^{commit}"));
        Assert.Contains(
            "survives the deletion",
            TestGitRepository.RunGitCaptureUtf8(repo.RootPath, "log", "-1", "--format=%s"),
            StringComparison.Ordinal);
        Assert.Equal("a v2", File.ReadAllText(Path.Combine(repo.RootPath, "docs", "a.md")));
    }

    // ---------------------------------------------------------------------
    // Acceptance — Document IDE History is independent of Git.
    // ---------------------------------------------------------------------

    /// <summary>
    /// Required test 3: "Batch operation history remains usable without Git."
    /// The batch apply history is written and read back in a folder Git knows
    /// nothing about; the correlation surface accepts the same non-Git source
    /// and never fabricates a repository context or initialises Git.
    /// </summary>
    [Fact]
    public async Task BatchOperationHistoryRemainsUsableWithoutGit()
    {
        string folder = CreateTempFolder("plain");
        try
        {
            // Git itself reports that this path has no repository, so the test
            // is about a genuinely non-Git source, not a broken one.
            var runner = new GitProcessRunner(GitProcessRunnerOptions.Default, new GitWorktreeMutationQueue());
            GitRepositoryContext context = await new GitRepositoryService(runner).DiscoverAsync(folder);
            Assert.Equal(GitRepositoryStatus.NoRepository, context.Status);

            // Batch operation history is written and read back with no Git.
            var history = new JsonlHistoryStore(Path.Combine(folder, "history"));
            string filePath = Path.Combine(folder, "notes.md");
            await File.WriteAllTextAsync(filePath, "target");
            await history.RecordAsync(new BatchApplyHistoryRecord(
                "plan-1",
                "doc-1",
                filePath,
                BatchApplyFileOutcome.Success,
                null,
                null,
                "snap-1",
                DateTimeOffset.UtcNow));

            BatchApplyHistoryRecord record = Assert.Single(await history.GetApplyRecordsAsync("plan-1"));
            Assert.Equal("doc-1", record.DocumentId);
            Assert.Equal(filePath, record.FilePath);

            // The correlation surface accepts the same non-Git source: the
            // session starts with no repository context instead of inventing one.
            WorkSessionService sessions = await CreateSessionServiceAsync();
            WorkSessionResult started = await sessions.StartAsync(
                workspaceId: null, repositoryRoot: null, worktreeRoot: null, label: "no git", CancellationToken.None);
            Assert.Equal(WorkSessionOutcome.Succeeded, started.Outcome);
            Assert.Null(started.Session!.RepositoryRoot);
            Assert.Null(started.Session.WorktreeRoot);

            // Nothing initialised Git, and the batch history survived the
            // correlation work.
            Assert.False(Directory.Exists(Path.Combine(folder, ".git")));
            Assert.Single(await history.GetApplyRecordsAsync("plan-1"));
        }
        finally
        {
            DeleteDirectoryBestEffort(folder);
        }
    }

    /// <summary>
    /// The correlation store is optional derived metadata (spec 13 §24): when it
    /// cannot be used the outcome is a typed <c>StoreUnavailable</c> with an
    /// inspectable message, and the commit created before the failure is still
    /// valid.
    /// </summary>
    [Fact]
    public async Task CorrelationStoreFailureIsTypedAndNeverInvalidatesTheCommit()
    {
        using var repo = CreateRepositoryWithBaselineCommit(new[] { "docs/a.md" });
        repo.WriteFile("docs/a.md", "a v2");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "--", "docs/a.md"));

        (GitCommitService commits, _) = CreateCommitService();
        GitCommitResult commit = await commits.CommitAsync(repo.RootPath, "unrelated to correlation");
        GitCommitInfo info = commit.Commit!;

        var service = new WorkSessionService(new UnavailableWorkSessionRepository());
        WorkSessionResult attached = await service.AttachCommitAsync(
            "session-1", info.CommitId, info.ShortId, info.Summary, CancellationToken.None);

        Assert.Equal(WorkSessionOutcome.StoreUnavailable, attached.Outcome);
        Assert.False(attached.IsSuccess);
        Assert.Null(attached.Session);
        Assert.Contains("unaffected", attached.Message!, StringComparison.OrdinalIgnoreCase);

        // The commit is untouched by the correlation failure.
        Assert.Equal(info.CommitId, Head(repo));
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "cat-file", "-e", info.CommitId + "^{commit}"));
    }

    // ---------------------------------------------------------------------
    // Typed input rejection and idempotence.
    // ---------------------------------------------------------------------

    /// <summary>
    /// Blank ids are typed input rejections and nothing is written: the session
    /// table stays empty (spec 13 §20 typed outcomes, no silent fallback).
    /// </summary>
    [Fact]
    public async Task BlankIdsAreRejectedTypedWithoutWritingAnything()
    {
        WorkSessionService sessions = await CreateSessionServiceAsync();

        WorkSessionResult noSession = await sessions.RecordChangedDocumentAsync(null, "doc-1", null, CancellationToken.None);
        Assert.Equal(WorkSessionOutcome.SessionRequired, noSession.Outcome);
        Assert.Equal("SessionRequired", noSession.ErrorCode);

        WorkSessionResult noDocument = await sessions.RecordChangedDocumentAsync("session-1", "  ", null, CancellationToken.None);
        Assert.Equal(WorkSessionOutcome.DocumentRequired, noDocument.Outcome);
        Assert.Equal("DocumentRequired", noDocument.ErrorCode);

        WorkSessionResult noCommit = await sessions.AttachCommitAsync("session-1", " ", null, null, CancellationToken.None);
        Assert.Equal(WorkSessionOutcome.CommitRequired, noCommit.Outcome);
        Assert.Equal("CommitRequired", noCommit.ErrorCode);

        WorkSessionResult noGet = await sessions.GetAsync("", CancellationToken.None);
        Assert.Equal(WorkSessionOutcome.SessionRequired, noGet.Outcome);

        WorkSessionResult noDelete = await sessions.DeleteAsync("\t", CancellationToken.None);
        Assert.Equal(WorkSessionOutcome.SessionRequired, noDelete.Outcome);

        Assert.Equal(0, await CountRowsAsync("work_sessions"));
        Assert.Equal(0, await CountRowsAsync("work_session_documents"));
        Assert.Equal(0, await CountRowsAsync("work_session_commits"));
    }

    /// <summary>
    /// An unknown session id is a typed <c>SessionNotFound</c> (never a silent
    /// success) and nothing is stored.
    /// </summary>
    [Fact]
    public async Task UnknownSessionIsTypedSessionNotFound()
    {
        WorkSessionService sessions = await CreateSessionServiceAsync();

        WorkSessionResult recorded = await sessions.RecordChangedDocumentAsync(
            "missing", "doc-1", "docs/a.md", CancellationToken.None);
        Assert.Equal(WorkSessionOutcome.SessionNotFound, recorded.Outcome);

        WorkSessionResult attached = await sessions.AttachCommitAsync(
            "missing", "abc123", null, null, CancellationToken.None);
        Assert.Equal(WorkSessionOutcome.SessionNotFound, attached.Outcome);

        WorkSessionResult deleted = await sessions.DeleteAsync("missing", CancellationToken.None);
        Assert.Equal(WorkSessionOutcome.SessionNotFound, deleted.Outcome);

        Assert.Equal(0, await CountRowsAsync("work_session_documents"));
        Assert.Equal(0, await CountRowsAsync("work_session_commits"));
    }

    /// <summary>
    /// Recording the same document and correlating the same commit twice keep
    /// exactly one association each, so a repeated operation cannot inflate the
    /// correlation history.
    /// </summary>
    [Fact]
    public async Task RecordingTheSameDocumentAndCommitTwiceKeepsOneAssociationEach()
    {
        WorkSessionService sessions = await CreateSessionServiceAsync();
        string sessionId = (await sessions.StartAsync(null, null, null, "idempotent", CancellationToken.None)).Session!.Id;

        await sessions.RecordChangedDocumentAsync(sessionId, "doc-1", "docs/a.md", CancellationToken.None);
        await sessions.RecordChangedDocumentAsync(sessionId, "doc-1", "docs/moved.md", CancellationToken.None);
        await sessions.AttachCommitAsync(sessionId, "abc123", "abc", "first", CancellationToken.None);
        await sessions.AttachCommitAsync(sessionId, "abc123", "abc", "second", CancellationToken.None);

        WorkSessionResult read = await sessions.GetAsync(sessionId, CancellationToken.None);
        WorkSessionDocument document = Assert.Single(read.Session!.ChangedDocuments);
        // The association is refreshed, not appended.
        Assert.Equal("docs/moved.md", document.Path);
        WorkSessionCommit correlation = Assert.Single(read.Session.CommitCorrelations);
        Assert.Equal("second", correlation.Summary);
    }

    /// <summary>
    /// Paths with spaces, Unicode and special characters survive the round trip
    /// verbatim; the store never normalizes or shell-interprets them.
    /// </summary>
    [Fact]
    public async Task PathsWithSpacesUnicodeAndSpecialCharactersSurviveTheRoundTrip()
    {
        WorkSessionService sessions = await CreateSessionServiceAsync();
        string sessionId = (await sessions.StartAsync(null, null, null, "ünicode speshal", CancellationToken.None)).Session!.Id;
        const string path = "docs/Ordner mit Leerzeichen/übersicht & (kapitel) #1.md";

        await sessions.RecordChangedDocumentAsync(sessionId, "doc-ü", path, CancellationToken.None);

        WorkSessionResult read = await sessions.GetAsync(sessionId, CancellationToken.None);
        WorkSessionDocument document = Assert.Single(read.Session!.ChangedDocuments);
        Assert.Equal("doc-ü", document.DocumentId);
        Assert.Equal(path, document.Path);
        Assert.Equal("ünicode speshal", read.Session.Label);
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    /// <summary>A correlation service over a real, isolated SQLite database.</summary>
    private async Task<WorkSessionService> CreateSessionServiceAsync()
    {
        var appDb = new AppDb(_dbPath);
        await appDb.InitializeAsync();
        return new WorkSessionService(new WorkSessionRepository(appDb));
    }

    /// <summary>Reads a raw row count straight from the correlation store.</summary>
    private async Task<long> CountRowsAsync(string table)
    {
        var appDb = new AppDb(_dbPath);
        await appDb.InitializeAsync();
        using var conn = appDb.CreateConnection();
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT count(*) FROM {table};";
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private static string CreateTempFolder(string prefix)
    {
        string folder = Path.Combine(Path.GetTempPath(), "scm09", $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static void DeleteDirectoryBestEffort(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception)
        {
            // Best effort: a locked temp directory must not fail the test run.
        }
    }

    private static (GitCommitService Commits, GitRepositoryService Repositories) CreateCommitService()
    {
        var runner = new GitProcessRunner(GitProcessRunnerOptions.Default, new GitWorktreeMutationQueue());
        var repositories = new GitRepositoryService(runner);
        var stages = new GitStageService(runner, repositories);
        return (new GitCommitService(runner, repositories, stages), repositories);
    }

    private static TestGitRepository CreateRepositoryWithBaselineCommit(string[] baselineFiles)
    {
        var repo = TestGitRepository.Create($"scm09-{Guid.NewGuid():N}");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "checkout", "-b", "main"));
        foreach (string file in baselineFiles)
        {
            repo.WriteFile(file, "v1");
        }

        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "-A"));
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "commit", "-qm", "baseline"));
        return repo;
    }

    private static string Head(TestGitRepository repo) =>
        TestGitRepository.RunGitCaptureUtf8(repo.RootPath, "rev-parse", "HEAD").Trim();

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        string? directory = Path.GetDirectoryName(_dbPath);
        if (directory is not null && Directory.Exists(directory))
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (Exception)
            {
                // Best effort: a locked temp database must not fail the test run.
            }
        }
    }

    /// <summary>A correlation store that cannot be used at all (spec 13 §24).</summary>
    private sealed class UnavailableWorkSessionRepository : IWorkSessionRepository
    {
        public Task CreateAsync(WorkSession session, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("correlation store offline");

        public Task<WorkSession?> GetByIdAsync(string sessionId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("correlation store offline");

        public Task<bool> RecordChangedDocumentAsync(
            WorkSessionDocument document, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("correlation store offline");

        public Task<bool> AttachCommitAsync(
            WorkSessionCommit commit, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("correlation store offline");

        public Task<bool> DeleteAsync(string sessionId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("correlation store offline");
    }
}

/// <summary>
/// SCM-09 — privileged HTTP surface for the correlation (spec 13 §13/§18/§24).
///
/// Every route carries the localhost session-token guard. Input rejections
/// (blank ids, nothing ran) are typed 400s; every execution outcome stays HTTP
/// 200 with a typed body so the pane renders it — the same split SCM-06/SCM-07
/// established. The correlation surface owns no Git mutation and cannot create
/// a commit.
/// </summary>
public sealed class WorkSessionCorrelationApiTests : IDisposable
{
    private readonly WebApplicationFactory<Program> _root = new();
    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), "scm09-api", Guid.NewGuid().ToString("N"), "app.db");

    /// <summary>
    /// The full correlation flow through the API: commit (SCM-07), start a work
    /// session, record the changed document, correlate the created commit id,
    /// and read the correlation back. Document IDE History and Git History stay
    /// separate payloads, and the flow creates no additional commit.
    /// </summary>
    [Fact]
    public async Task WorkSessionRoutesCorrelateASuccessfulCommitWithoutCreatingAnotherCommit()
    {
        using var repo = CreateRepositoryWithBaselineCommit(new[] { "docs/a.md" });
        repo.WriteFile("docs/a.md", "a v2");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "--", "docs/a.md"));
        string headBefore = Head(repo);
        int commitsBefore = CommitCount(repo);

        using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = CreateAuthorizedClient(factory);

        using HttpResponseMessage commitResponse = await client.PostAsJsonAsync(
            "/api/source-control/commit",
            new SourceControlCommitRequest(repo.RootPath, "api commit to correlate"));
        Assert.Equal(HttpStatusCode.OK, commitResponse.StatusCode);
        SourceControlCommitResponse commit = (await commitResponse.Content
            .ReadFromJsonAsync<SourceControlCommitResponse>())!;
        Assert.True(commit.IsSuccess);
        string commitId = commit.Commit!.CommitId;
        Assert.NotEqual(headBefore, commitId);

        using HttpResponseMessage startResponse = await client.PostAsJsonAsync(
            "/api/source-control/work-sessions",
            new SourceControlWorkSessionRequest(repo.RootPath, "ws-1", "correlate"));
        Assert.Equal(HttpStatusCode.OK, startResponse.StatusCode);
        SourceControlCorrelationResponse started = (await startResponse.Content
            .ReadFromJsonAsync<SourceControlCorrelationResponse>())!;
        Assert.Equal("Succeeded", started.Outcome);
        Assert.True(started.IsSuccess);
        Assert.Null(started.ErrorCode);
        string sessionId = started.Session!.Id;
        // Git context is recorded as read-only derived context, exactly as Git reported it.
        Assert.Equal(Path.GetFullPath(repo.RootPath), started.Session.RepositoryRoot, ignoreCase: true);
        Assert.Equal(Path.GetFullPath(repo.RootPath), started.Session.WorktreeRoot, ignoreCase: true);
        Assert.Equal("ws-1", started.Session.WorkspaceId);
        Assert.Equal("correlate", started.Session.Label);
        Assert.Empty(started.Session.ChangedDocuments);
        Assert.Empty(started.Session.CommitCorrelations);

        using HttpResponseMessage documentResponse = await client.PostAsJsonAsync(
            $"/api/source-control/work-sessions/{Uri.EscapeDataString(sessionId)}/documents",
            new SourceControlWorkSessionDocumentRequest("doc-1", "docs/a.md"));
        Assert.Equal(HttpStatusCode.OK, documentResponse.StatusCode);
        SourceControlCorrelationResponse recorded = (await documentResponse.Content
            .ReadFromJsonAsync<SourceControlCorrelationResponse>())!;
        Assert.Equal("Succeeded", recorded.Outcome);
        SourceControlWorkSessionDocumentResponse document = Assert.Single(recorded.Session!.ChangedDocuments);
        Assert.Equal("doc-1", document.DocumentId);
        Assert.Equal("docs/a.md", document.Path);

        using HttpResponseMessage correlationResponse = await client.PostAsJsonAsync(
            $"/api/source-control/work-sessions/{Uri.EscapeDataString(sessionId)}/commit-correlations",
            new SourceControlWorkSessionCommitRequest(commitId, commit.Commit.ShortId, commit.Commit.Summary));
        Assert.Equal(HttpStatusCode.OK, correlationResponse.StatusCode);
        SourceControlCorrelationResponse correlated = (await correlationResponse.Content
            .ReadFromJsonAsync<SourceControlCorrelationResponse>())!;
        Assert.Equal("Succeeded", correlated.Outcome);
        SourceControlWorkSessionCommitResponse correlation = Assert.Single(correlated.Session!.CommitCorrelations);
        Assert.Equal(commitId, correlation.CommitId);
        Assert.Equal(commit.Commit.ShortId, correlation.ShortId);
        Assert.Equal("api commit to correlate", correlation.Summary);

        // Reading the correlation back is its own payload: Document IDE History
        // (session + changed documents) and Git History (commit ids) are kept
        // distinguishable, never merged into a unified timeline.
        using HttpResponseMessage readResponse = await client.GetAsync(
            $"/api/source-control/work-sessions/{Uri.EscapeDataString(sessionId)}");
        Assert.Equal(HttpStatusCode.OK, readResponse.StatusCode);
        SourceControlCorrelationResponse read = (await readResponse.Content
            .ReadFromJsonAsync<SourceControlCorrelationResponse>())!;
        Assert.Equal("Succeeded", read.Outcome);
        Assert.Equal("doc-1", Assert.Single(read.Session!.ChangedDocuments).DocumentId);
        Assert.Equal(commitId, Assert.Single(read.Session.CommitCorrelations).CommitId);

        // The correlation flow created no commit of its own.
        Assert.Equal(commitId, Head(repo));
        Assert.Equal(commitsBefore + 1, CommitCount(repo));
    }

    /// <summary>
    /// The correlation surface works for a path Git knows nothing about: the
    /// session starts with no repository context, Git is never initialised, and
    /// the existing status route still reports the typed non-repository state.
    /// </summary>
    [Fact]
    public async Task CorrelationWorksForANonGitFolderWithoutInitialisingGit()
    {
        string folder = Path.Combine(Path.GetTempPath(), "scm09-api", $"plain-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            using WebApplicationFactory<Program> factory = CreateFactory();
            using HttpClient client = CreateAuthorizedClient(factory);

            using HttpResponseMessage response = await client.PostAsJsonAsync(
                "/api/source-control/work-sessions",
                new SourceControlWorkSessionRequest(folder, null, "no git"));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            SourceControlCorrelationResponse body = (await response.Content
                .ReadFromJsonAsync<SourceControlCorrelationResponse>())!;
            Assert.Equal("Succeeded", body.Outcome);
            Assert.Null(body.Session!.RepositoryRoot);
            Assert.Null(body.Session.WorktreeRoot);

            // Git was neither initialised nor hidden: the status route still
            // reports the typed non-repository state for the same folder.
            SourceControlStatusResponse status = (await client.GetFromJsonAsync<SourceControlStatusResponse>(
                $"/api/source-control/status?path={Uri.EscapeDataString(folder)}"))!;
            Assert.Equal("NoRepository", status.Status);
            Assert.False(status.HasRepository);

            Assert.False(Directory.Exists(Path.Combine(folder, ".git")));
        }
        finally
        {
            DeleteDirectoryBestEffort(folder);
        }
    }

    /// <summary>
    /// Blank ids are typed 400s and nothing runs — the same input-rejection
    /// split as SCM-06/SCM-07.
    /// </summary>
    [Fact]
    public async Task CorrelationRoutesRejectBlankIdsWith400()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = CreateAuthorizedClient(factory);

        using HttpResponseMessage noPath = await client.PostAsJsonAsync(
            "/api/source-control/work-sessions",
            new SourceControlWorkSessionRequest(" ", null, null));
        Assert.Equal(HttpStatusCode.BadRequest, noPath.StatusCode);
        using JsonDocument noPathPayload = JsonDocument.Parse(await noPath.Content.ReadAsStringAsync());
        Assert.Equal("PathRequired", noPathPayload.RootElement.GetProperty("errorCode").GetString());

        using HttpResponseMessage noDocument = await client.PostAsJsonAsync(
            "/api/source-control/work-sessions/session-1/documents",
            new SourceControlWorkSessionDocumentRequest("  ", "docs/a.md"));
        Assert.Equal(HttpStatusCode.BadRequest, noDocument.StatusCode);
        using JsonDocument noDocumentPayload = JsonDocument.Parse(await noDocument.Content.ReadAsStringAsync());
        Assert.Equal("DocumentRequired", noDocumentPayload.RootElement.GetProperty("errorCode").GetString());

        using HttpResponseMessage noCommit = await client.PostAsJsonAsync(
            "/api/source-control/work-sessions/session-1/commit-correlations",
            new SourceControlWorkSessionCommitRequest(null, null, null));
        Assert.Equal(HttpStatusCode.BadRequest, noCommit.StatusCode);
        using JsonDocument noCommitPayload = JsonDocument.Parse(await noCommit.Content.ReadAsStringAsync());
        Assert.Equal("CommitRequired", noCommitPayload.RootElement.GetProperty("errorCode").GetString());

        using HttpResponseMessage noSession = await client.PostAsJsonAsync(
            "/api/source-control/work-sessions/%20/documents",
            new SourceControlWorkSessionDocumentRequest("doc-1", null));
        Assert.Equal(HttpStatusCode.BadRequest, noSession.StatusCode);
        using JsonDocument noSessionPayload = JsonDocument.Parse(await noSession.Content.ReadAsStringAsync());
        Assert.Equal("SessionRequired", noSessionPayload.RootElement.GetProperty("errorCode").GetString());
    }

    /// <summary>
    /// An unknown session is a typed <c>SessionNotFound</c> with HTTP 200 (the
    /// pane renders the body), and deleting correlation data through the API
    /// leaves the Git commit valid.
    /// </summary>
    [Fact]
    public async Task UnknownSessionIsTypedAndDeletingCorrelationLeavesTheCommitValid()
    {
        using var repo = CreateRepositoryWithBaselineCommit(new[] { "docs/a.md" });
        repo.WriteFile("docs/a.md", "a v2");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "--", "docs/a.md"));

        (GitCommitService commitService, _) = CreateCommitService();
        GitCommitResult commit = await commitService.CommitAsync(repo.RootPath, "kept after deletion");
        Assert.Equal(GitCommitOutcome.Succeeded, commit.Outcome);
        string commitId = commit.Commit!.CommitId;

        using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = CreateAuthorizedClient(factory);

        using HttpResponseMessage unknown = await client.PostAsJsonAsync(
            "/api/source-control/work-sessions/does-not-exist/commit-correlations",
            new SourceControlWorkSessionCommitRequest(commitId, null, null));
        Assert.Equal(HttpStatusCode.OK, unknown.StatusCode);
        SourceControlCorrelationResponse unknownBody = (await unknown.Content
            .ReadFromJsonAsync<SourceControlCorrelationResponse>())!;
        Assert.Equal("SessionNotFound", unknownBody.Outcome);
        Assert.False(unknownBody.IsSuccess);
        Assert.Null(unknownBody.Session);

        using HttpResponseMessage startResponse = await client.PostAsJsonAsync(
            "/api/source-control/work-sessions",
            new SourceControlWorkSessionRequest(repo.RootPath, null, "delete me"));
        string sessionId = (await startResponse.Content.ReadFromJsonAsync<SourceControlCorrelationResponse>())!
            .Session!.Id;
        using HttpResponseMessage correlateResponse = await client.PostAsJsonAsync(
            $"/api/source-control/work-sessions/{Uri.EscapeDataString(sessionId)}/commit-correlations",
            new SourceControlWorkSessionCommitRequest(commitId, commit.Commit.ShortId, commit.Commit.Summary));
        Assert.Equal(HttpStatusCode.OK, correlateResponse.StatusCode);

        using HttpResponseMessage deleteResponse = await client.DeleteAsync(
            $"/api/source-control/work-sessions/{Uri.EscapeDataString(sessionId)}");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);
        SourceControlCorrelationResponse deleted = (await deleteResponse.Content
            .ReadFromJsonAsync<SourceControlCorrelationResponse>())!;
        Assert.Equal("Succeeded", deleted.Outcome);

        using HttpResponseMessage goneResponse = await client.GetAsync(
            $"/api/source-control/work-sessions/{Uri.EscapeDataString(sessionId)}");
        SourceControlCorrelationResponse gone = (await goneResponse.Content
            .ReadFromJsonAsync<SourceControlCorrelationResponse>())!;
        Assert.Equal("SessionNotFound", gone.Outcome);
        Assert.Null(gone.Session);

        // The commit survived the correlation deletion.
        Assert.Equal(commitId, Head(repo));
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "cat-file", "-e", commitId + "^{commit}"));
    }

    /// <summary>
    /// Every correlation route is guarded exactly like the other privileged
    /// Source Control routes (spec 13 §18).
    /// </summary>
    [Fact]
    public async Task CorrelationRoutesRejectMissingInvalidTokenAndHostileOrigin()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();

        using (HttpClient anonymous = factory.CreateClient())
        {
            using HttpResponseMessage response = await anonymous.PostAsJsonAsync(
                "/api/source-control/work-sessions",
                new SourceControlWorkSessionRequest(Path.GetTempPath(), null, null));
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        using var scope = factory.Services.CreateScope();
        var tokenProvider = scope.ServiceProvider.GetRequiredService<ISessionTokenProvider>();

        using (HttpClient hostileOrigin = factory.CreateClient())
        {
            hostileOrigin.DefaultRequestHeaders.Add("Origin", "https://attacker.example");
            hostileOrigin.DefaultRequestHeaders.Add("X-DocumentIDE-Session-Token", tokenProvider.SessionToken);
            using HttpResponseMessage response = await hostileOrigin.PostAsJsonAsync(
                "/api/source-control/work-sessions",
                new SourceControlWorkSessionRequest(Path.GetTempPath(), null, null));
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        using (HttpClient wrongToken = factory.CreateClient())
        {
            wrongToken.DefaultRequestHeaders.Add("Origin", "http://localhost:5173");
            wrongToken.DefaultRequestHeaders.Add("X-DocumentIDE-Session-Token", "invalid-token");
            using HttpResponseMessage response = await wrongToken.GetAsync(
                "/api/source-control/work-sessions/session-1");
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        using (HttpClient anonymousRead = factory.CreateClient())
        {
            using HttpResponseMessage response = await anonymousRead.DeleteAsync(
                "/api/source-control/work-sessions/session-1");
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    /// <summary>
    /// A host whose correlation store is an isolated temp database; the shared
    /// local application database is never written by these tests.
    /// </summary>
    private WebApplicationFactory<Program> CreateFactory() =>
        _root.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<AppDb>();
            services.AddSingleton(new AppDb(_dbPath));
        }));

    private static HttpClient CreateAuthorizedClient(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var tokenProvider = scope.ServiceProvider.GetRequiredService<ISessionTokenProvider>();
        HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Origin", "http://localhost:5173");
        client.DefaultRequestHeaders.Add("X-DocumentIDE-Session-Token", tokenProvider.SessionToken);
        return client;
    }

    private static (GitCommitService Commits, GitRepositoryService Repositories) CreateCommitService()
    {
        var runner = new GitProcessRunner(GitProcessRunnerOptions.Default, new GitWorktreeMutationQueue());
        var repositories = new GitRepositoryService(runner);
        var stages = new GitStageService(runner, repositories);
        return (new GitCommitService(runner, repositories, stages), repositories);
    }

    private static TestGitRepository CreateRepositoryWithBaselineCommit(string[] baselineFiles)
    {
        var repo = TestGitRepository.Create($"scm09-api-{Guid.NewGuid():N}");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "checkout", "-b", "main"));
        foreach (string file in baselineFiles)
        {
            repo.WriteFile(file, "v1");
        }

        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "-A"));
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "commit", "-qm", "baseline"));
        return repo;
    }

    private static string Head(TestGitRepository repo) =>
        TestGitRepository.RunGitCaptureUtf8(repo.RootPath, "rev-parse", "HEAD").Trim();

    private static int CommitCount(TestGitRepository repo) =>
        int.Parse(TestGitRepository.RunGitCaptureUtf8(repo.RootPath, "rev-list", "--count", "HEAD").Trim());

    private static void DeleteDirectoryBestEffort(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception)
        {
            // Best effort: a locked temp directory must not fail the test run.
        }
    }

    public void Dispose()
    {
        _root.Dispose();
        SqliteConnection.ClearAllPools();
        DeleteDirectoryBestEffort(Path.GetDirectoryName(_dbPath)!);
    }
}


