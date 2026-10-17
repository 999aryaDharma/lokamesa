using System;
using System.Collections.Generic;
using System.Globalization;
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
using DocumentIDE.Core.SourceControl;
using DocumentIDE.Infrastructure.SourceControl;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DocumentIDE.IntegrationTests.SourceControl;

/// <summary>
/// SCM-08 — Git Commit History &amp; File History (spec 13 §9.4/§12/§18/§19/§20/§23).
///
/// These tests drive <see cref="GitHistoryService"/> against real Git
/// repositories through the SCM-01 controlled process boundary:
/// <list type="bullet">
/// <item>the repository history shows the real commits with their metadata
/// (id, short id, author, committer time, message, file count) — spec 13 §12,</item>
/// <item>file history is filtered to the commits that touched one selected
/// document (<c>git log -- &lt;path&gt;</c>) — spec 13 §9.4/§12,</item>
/// <item>large histories load incrementally through skip/limit pagination
/// (spec 13 §25 "large history views should be paginated/lazy-loaded"),</item>
/// <item>a selected commit's document version can be inspected read-only
/// (<c>git show &lt;commit&gt;:&lt;path&gt;</c>) without mutating anything,</item>
/// <item>failures are typed and inspectable (Git missing, worktree gone,
/// rejected paths/queries, Git's own permission/auth/conflict/lock/remote
/// signatures), and</item>
/// <item>Git history stays a separate authority from Document IDE History
/// (spec 13 §4.3/§12): the read path never writes to the repository.</item>
/// </list>
///
/// Planned-file mapping: the sprint plans this suite at
/// <c>tests/DocumentIDE.IntegrationTests/SourceControl/GitHistoryTests.cs</c>;
/// the frontend half lives colocated at
/// <c>src/DocumentIDE.Web/src/features/source-control/GitHistoryPanel.test.tsx</c>
/// (CAN-05 batch-B ruling — the repository's <c>tests/frontend/**</c> path in
/// the sprint template is stale).
/// </summary>
public sealed class GitHistoryTests
{
    // ---------------------------------------------------------------------
    // Step 1 — Commit list query (first failing test)
    // ---------------------------------------------------------------------

    /// <summary>
    /// Required test 1 (the mandatory RED-first test): "known fixture history
    /// renders correctly." A repository with a known three-commit history
    /// reports those commits newest-first with their real metadata and the
    /// number of files each commit changed (spec 13 §12).
    /// </summary>
    [Fact]
    public async Task KnownFixtureHistoryRendersCorrectly()
    {
        using TestGitRepository repo = CreateRepositoryWithKnownHistory();
        (GitHistoryService service, _) = CreateService();

        GitHistoryResult result = await service.GetHistoryAsync(repo.RootPath);

        Assert.Equal(GitHistoryStatus.Available, result.Status);
        GitHistoryPage page = Assert.IsType<GitHistoryPage>(result.Page);
        Assert.Equal(3, page.Commits.Count);
        Assert.Equal(0, page.Skip);
        Assert.Equal(GitHistoryQuery.DefaultLimit, page.Limit);
        Assert.False(page.HasMore);
        Assert.Null(page.NextSkip);

        // Newest first, exactly like `git log`.
        Assert.Equal(
            new[] { "third: adds a unicode file", "second: edits a", "first commit" },
            page.Commits.Select(commit => commit.Summary).ToArray());

        GitHistoryEntry head = page.Commits[0];
        Assert.Equal(Head(repo), head.CommitId);
        Assert.Matches("^[0-9a-f]{7,40}$", head.ShortId);
        Assert.StartsWith(head.ShortId, head.CommitId, StringComparison.Ordinal);
        Assert.Equal("SCM-08 Author", head.AuthorName);
        Assert.Equal("scm08@example.invalid", head.AuthorEmail);
        Assert.False(string.IsNullOrWhiteSpace(head.CommitterTime));
        Assert.Equal(
            DateTimeOffset.Parse(head.CommitterTime, CultureInfo.InvariantCulture).UtcDateTime,
            DateTimeOffset
                .Parse(
                    TestGitRepository.RunGitCaptureUtf8(repo.RootPath, "log", "-1", "--format=%cI").Trim(),
                    CultureInfo.InvariantCulture)
                .UtcDateTime);

        // File counts come from Git's own name list for each commit.
        Assert.Equal(2, page.Commits[0].FileCount);
        Assert.Equal(1, page.Commits[1].FileCount);
        Assert.Equal(2, page.Commits[2].FileCount);

        // The fixture's history matches what a raw `git log` reports, so the
        // reader is a faithful projection of the repository, not a cache.
        string[] raw = TestGitRepository
            .RunGitCaptureUtf8(repo.RootPath, "log", "--format=%H")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .ToArray();
        Assert.Equal(raw, page.Commits.Select(commit => commit.CommitId).ToArray());
    }

    // ---------------------------------------------------------------------
    // Step 2 — File history query (spec 13 §9.4/§12)
    // ---------------------------------------------------------------------

    /// <summary>
    /// Required test 2: "file history filters to commits affecting the file."
    /// The file history of <c>docs/notes.md</c> contains only the commit that
    /// touched it, while <c>docs/a.md</c> (touched by all three commits)
    /// contains all three — the read is a real <c>git log -- &lt;path&gt;</c>,
    /// and the per-commit file count is pathspec-limited, exactly like Git's
    /// own answer for that path (spec 13 §9.4/§12).
    /// </summary>
    [Fact]
    public async Task FileHistoryFiltersToCommitsAffectingTheFile()
    {
        using TestGitRepository repo = CreateRepositoryWithKnownHistory();
        (GitHistoryService service, _) = CreateService();

        GitHistoryResult notes = await service.GetFileHistoryAsync(repo.RootPath, "docs/notes.md");

        Assert.Equal(GitHistoryStatus.Available, notes.Status);
        GitHistoryPage notesPage = Assert.IsType<GitHistoryPage>(notes.Page);
        GitHistoryEntry onlyCommit = Assert.Single(notesPage.Commits);
        Assert.Equal("first commit", onlyCommit.Summary);
        // The path filter narrows the file count too: for this commit only
        // docs/notes.md matches the pathspec, not the two files it changed.
        Assert.Equal(1, onlyCommit.FileCount);
        Assert.Equal(2, await RepositoryFileCountAsync(service, repo, "first commit"));

        GitHistoryResult a = await service.GetFileHistoryAsync(repo.RootPath, "docs/a.md");

        Assert.Equal(GitHistoryStatus.Available, a.Status);
        Assert.Equal(
            new[] { "third: adds a unicode file", "second: edits a", "first commit" },
            Assert.IsType<GitHistoryPage>(a.Page).Commits.Select(commit => commit.Summary).ToArray());

        // A path Git never recorded yields an empty page, not a failure.
        GitHistoryResult never = await service.GetFileHistoryAsync(repo.RootPath, "docs/never-existed.md");
        Assert.Equal(GitHistoryStatus.Available, never.Status);
        Assert.Empty(Assert.IsType<GitHistoryPage>(never.Page).Commits);
    }

    /// <summary>
    /// Failure/edge case "path traversal / repository escape": a file-history
    /// path that is absolute, escaping or unnormalized is rejected typed
    /// <em>before</em> any Git or filesystem access, so no request can read
    /// history outside the selected worktree (spec 13 §23).
    /// </summary>
    [Theory]
    [InlineData("../outside.md")]
    [InlineData("docs/../../outside.md")]
    [InlineData("C:\\Windows\\System32\\drivers\\etc\\hosts")]
    [InlineData("/etc/hosts")]
    [InlineData("docs\\..\\..\\outside.md")]
    public async Task FileHistoryRejectsEscapingOrAbsolutePathsWithoutRunningGit(string relativePath)
    {
        using TestGitRepository repo = CreateRepositoryWithKnownHistory();
        var recording = new RecordingGitProcessRunner(
            new GitProcessRunner(GitProcessRunnerOptions.Default, new GitWorktreeMutationQueue()));
        (GitHistoryService service, _) = CreateService(recording);

        GitHistoryResult result = await service.GetFileHistoryAsync(repo.RootPath, relativePath);

        Assert.Equal(GitHistoryStatus.PathRejected, result.Status);
        Assert.False(result.IsAvailable);
        Assert.Null(result.Page);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorCode));
        Assert.False(string.IsNullOrWhiteSpace(result.Message));

        // A rejected request never reached Git at all.
        Assert.DoesNotContain(recording.Invocations, arguments => arguments.Contains("log"));
    }

    /// <summary>
    /// A missing file-history path is an input rejection with nothing run —
    /// the pane must pass the document it selected (spec 13 §12/§18).
    /// </summary>
    [Fact]
    public async Task FileHistoryWithoutAPathIsRejectedTypedWithoutRunningGit()
    {
        using TestGitRepository repo = CreateRepositoryWithKnownHistory();
        var recording = new RecordingGitProcessRunner(
            new GitProcessRunner(GitProcessRunnerOptions.Default, new GitWorktreeMutationQueue()));
        (GitHistoryService service, _) = CreateService(recording);

        GitHistoryResult result = await service.GetFileHistoryAsync(repo.RootPath, "   ");

        Assert.Equal(GitHistoryStatus.InvalidRequest, result.Status);
        Assert.Equal("PathRequired", result.ErrorCode);
        Assert.Null(result.Page);
        Assert.DoesNotContain(recording.Invocations, arguments => arguments.Contains("log"));
    }

    // ---------------------------------------------------------------------
    // Step 3 — Pagination / lazy loading (spec 13 §25)
    // ---------------------------------------------------------------------

    /// <summary>
    /// Required test 3: "large histories load incrementally." A 25-commit
    /// repository is read in pages of 10 through the skip/limit window; each
    /// page reports <c>HasMore</c>/<c>NextSkip</c> from Git, the pages append
    /// without overlap or gaps, and the concatenation is exactly the
    /// repository's history in <c>git log</c> order (spec 13 §25).
    /// </summary>
    [Fact]
    public async Task LargeHistoryLoadsIncrementally()
    {
        using TestGitRepository repo = CreateRepositoryWithManyCommits(commitCount: 25);
        (GitHistoryService service, _) = CreateService();
        string[] expectedOrder = TestGitRepository
            .RunGitCaptureUtf8(repo.RootPath, "log", "--format=%H")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .ToArray();

        var collected = new List<string>();
        int? skip = 0;
        var pages = new List<GitHistoryPage>();
        while (skip is not null && pages.Count < 10)
        {
            GitHistoryResult result = await service.GetHistoryAsync(
                repo.RootPath,
                new GitHistoryQuery(Skip: skip.Value, Limit: 10));

            Assert.Equal(GitHistoryStatus.Available, result.Status);
            GitHistoryPage page = Assert.IsType<GitHistoryPage>(result.Page);
            Assert.Equal(skip.Value, page.Skip);
            Assert.Equal(10, page.Limit);
            Assert.True(page.Commits.Count <= 10);
            Assert.All(page.Commits, commit => Assert.False(string.IsNullOrWhiteSpace(commit.CommitId)));

            pages.Add(page);
            collected.AddRange(page.Commits.Select(commit => commit.CommitId));
            skip = page.NextSkip;
        }

        // 25 commits in pages of 10: 10 + 10 + 5, then Git says there is no more.
        Assert.Equal(3, pages.Count);
        Assert.Equal(new[] { 10, 10, 5 }, pages.Select(page => page.Commits.Count).ToArray());
        Assert.True(pages[0].HasMore);
        Assert.Equal(10, pages[0].NextSkip);
        Assert.True(pages[1].HasMore);
        Assert.Equal(20, pages[1].NextSkip);
        Assert.False(pages[2].HasMore);
        Assert.Null(pages[2].NextSkip);

        // No overlap, no gap, same order as Git itself.
        Assert.Equal(expectedOrder, collected.ToArray());

        // The same window applies to a file history, so the pane can lazily
        // load the commits of one document too.
        GitHistoryResult filePage = await service.GetFileHistoryAsync(
            repo.RootPath,
            "docs/journal.md",
            new GitHistoryQuery(Skip: 5, Limit: 3));
        GitHistoryPage fileHistory = Assert.IsType<GitHistoryPage>(filePage.Page);
        Assert.Equal(3, fileHistory.Commits.Count);
        Assert.Equal(5, fileHistory.Skip);
        Assert.True(fileHistory.HasMore);
        Assert.Equal(
            expectedOrder.Skip(5).Take(3).ToArray(),
            fileHistory.Commits.Select(commit => commit.CommitId).ToArray());
    }

    /// <summary>
    /// A page window Git cannot serve (negative skip, zero or oversized limit)
    /// is rejected typed before any Git invocation, so a hostile or buggy
    /// client cannot ask for the whole history in one call (spec 13 §25).
    /// </summary>
    [Theory]
    [InlineData(-1, 10)]
    [InlineData(0, 0)]
    [InlineData(0, -5)]
    [InlineData(0, GitHistoryQuery.MaxLimit + 1)]
    public async Task HistoryQueryWindowIsRejectedTypedWithoutRunningGit(int skip, int limit)
    {
        using TestGitRepository repo = CreateRepositoryWithKnownHistory();
        var recording = new RecordingGitProcessRunner(
            new GitProcessRunner(GitProcessRunnerOptions.Default, new GitWorktreeMutationQueue()));
        (GitHistoryService service, _) = CreateService(recording);

        GitHistoryResult result = await service.GetHistoryAsync(repo.RootPath, new GitHistoryQuery(skip, limit));

        Assert.Equal(GitHistoryStatus.InvalidRequest, result.Status);
        Assert.Null(result.Page);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorCode));
        Assert.DoesNotContain(recording.Invocations, arguments => arguments.Contains("log"));
    }

    // ---------------------------------------------------------------------
    // Step 4 — Commit selection wired into document version inspection
    // ---------------------------------------------------------------------

    /// <summary>
    /// Selecting a commit lets the user inspect the selected document at that
    /// commit (<c>git show &lt;commit&gt;:&lt;path&gt;</c>, spec 13 §12). The
    /// read is a pure read: the working-tree file and the repository state are
    /// exactly what they were before the inspection.
    /// </summary>
    [Fact]
    public async Task SelectedCommitExposesTheDocumentVersionAtThatCommit()
    {
        using TestGitRepository repo = CreateRepositoryWithKnownHistory();
        (GitHistoryService service, _) = CreateService();
        string[] history = History(service, repo);
        string firstCommit = history[^1];
        string secondCommit = history[^2];
        string headBefore = Head(repo);
        string statusBefore = Status(repo);

        GitFileVersionResult second = await service.GetFileVersionAsync(repo.RootPath, "docs/a.md", secondCommit);

        Assert.Equal(GitHistoryStatus.Available, second.Status);
        GitFileVersion version = Assert.IsType<GitFileVersion>(second.Version);
        Assert.Equal(secondCommit, version.CommitId);
        Assert.Equal("docs/a.md", version.Path);
        Assert.Equal("a v2", version.Content);
        Assert.False(version.IsBinary);
        Assert.False(version.IsTruncated);
        Assert.Equal(4, version.SizeBytes);

        GitFileVersion first = Assert.IsType<GitFileVersion>(
            (await service.GetFileVersionAsync(repo.RootPath, "docs/a.md", firstCommit)).Version);
        Assert.Equal("a v1", first.Content);

        // Inspecting a version never mutates the working tree or the
        // repository: the file on disk is still the newest save.
        Assert.Equal("a v3", File.ReadAllText(Path.Combine(repo.RootPath, "docs", "a.md")));
        Assert.Equal(headBefore, Head(repo));
        Assert.Equal(statusBefore, Status(repo));
    }

    /// <summary>
    /// A version that does not exist at the selected commit is a typed
    /// <see cref="GitHistoryStatus.NotFound"/> — a real answer the pane can
    /// render ("not in this commit"), never an empty string pretending to be
    /// content (spec 13 §12).
    /// </summary>
    [Fact]
    public async Task DocumentVersionAbsentAtTheSelectedCommitIsTypedNotFound()
    {
        using TestGitRepository repo = CreateRepositoryWithKnownHistory();
        (GitHistoryService service, _) = CreateService();
        string firstCommit = History(service, repo)[^1];

        // docs/b é.md was added by the third commit, so it does not exist in
        // the first one.
        GitFileVersionResult result = await service.GetFileVersionAsync(repo.RootPath, "docs/b \u00e9.md", firstCommit);

        Assert.Equal(GitHistoryStatus.NotFound, result.Status);
        Assert.Null(result.Version);
        Assert.NotNull(result.Failure);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }

    /// <summary>
    /// A binary document (e.g. a DOCX) is reported as a binary change with its
    /// size and no content: the pane must not pretend a byte blob is text
    /// (spec 13 §9.3/§12).
    /// </summary>
    [Fact]
    public async Task BinaryDocumentVersionIsReportedWithoutTextContent()
    {
        using TestGitRepository repo = CreateRepositoryWithKnownHistory();
        byte[] docx = CreateBinaryDocx();
        File.WriteAllBytes(Path.Combine(repo.RootPath, "docs", "treaty.docx"), docx);
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "-A"));
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "commit", "-qm", "adds a binary document"));

        (GitHistoryService service, _) = CreateService();
        GitFileVersionResult result = await service.GetFileVersionAsync(repo.RootPath, "docs/treaty.docx", Head(repo));

        Assert.Equal(GitHistoryStatus.Available, result.Status);
        GitFileVersion version = Assert.IsType<GitFileVersion>(result.Version);
        Assert.True(version.IsBinary);
        Assert.Null(version.Content);
        Assert.False(version.IsTruncated);
        Assert.Equal(docx.Length, version.SizeBytes);
    }

    /// <summary>
    /// A document larger than the inspection bound is truncated with an
    /// explicit flag and its true size, so one click cannot pull an unbounded
    /// payload into the pane (spec 13 §25).
    /// </summary>
    [Fact]
    public async Task LargeDocumentVersionIsTruncatedAndReportsItsTrueSize()
    {
        using TestGitRepository repo = CreateRepositoryWithKnownHistory();
        int size = GitHistoryService.MaxVersionBytes + 4096;
        repo.WriteFile("docs/large.md", new string('a', size));
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "-A"));
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "commit", "-qm", "adds a large document"));

        (GitHistoryService service, _) = CreateService();
        GitFileVersionResult result = await service.GetFileVersionAsync(repo.RootPath, "docs/large.md", Head(repo));

        GitFileVersion version = Assert.IsType<GitFileVersion>(result.Version);
        Assert.True(version.IsTruncated);
        Assert.False(version.IsBinary);
        Assert.Equal(size, version.SizeBytes);
        Assert.Equal(GitHistoryService.MaxVersionBytes, version.Content!.Length);
    }

    /// <summary>
    /// The commit id and the document path are untrusted input: a
    /// revision expression, an option-looking value, an escaping path or a
    /// missing argument is rejected typed before any Git invocation, so a
    /// request can never turn into an arbitrary revision/option lookup
    /// (spec 13 §23).
    /// </summary>
    [Theory]
    [InlineData("docs/a.md", "HEAD")]
    [InlineData("docs/a.md", "HEAD~1")]
    [InlineData("docs/a.md", "main")]
    [InlineData("docs/a.md", "--help")]
    [InlineData("docs/a.md", "")]
    [InlineData("docs/a.md", "   ")]
    [InlineData("docs/a.md", "abc")]
    [InlineData("docs/a.md", "zzzzzzzz")]
    [InlineData("docs/a.md", null)]
    public async Task DocumentVersionRejectsNonCommitIdentifiersWithoutRunningGit(string path, string? commitId)
    {
        using TestGitRepository repo = CreateRepositoryWithKnownHistory();
        var recording = new RecordingGitProcessRunner(
            new GitProcessRunner(GitProcessRunnerOptions.Default, new GitWorktreeMutationQueue()));
        (GitHistoryService service, _) = CreateService(recording);

        GitFileVersionResult result = await service.GetFileVersionAsync(repo.RootPath, path, commitId);

        Assert.Equal(GitHistoryStatus.InvalidRequest, result.Status);
        Assert.Null(result.Version);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorCode));
        Assert.DoesNotContain(recording.Invocations, arguments => arguments.Contains("show"));
    }

    /// <summary>
    /// A well-formed but unknown commit id stays a typed failure with Git's own
    /// failure attached, instead of a silent empty version.
    /// </summary>
    [Fact]
    public async Task DocumentVersionForAnUnknownCommitIsATypedFailure()
    {
        using TestGitRepository repo = CreateRepositoryWithKnownHistory();
        (GitHistoryService service, _) = CreateService();

        GitFileVersionResult result = await service.GetFileVersionAsync(repo.RootPath, "docs/a.md", "abcdef0123");

        Assert.Equal(GitHistoryStatus.Failed, result.Status);
        Assert.Null(result.Version);
        Assert.NotNull(result.Failure);
        Assert.NotEqual(0, result.Failure!.ExitCode);
    }

    // ---------------------------------------------------------------------
    // Step 5 — Repository states: a repository whose HEAD is unborn
    // ---------------------------------------------------------------------

    /// <summary>
    /// A freshly initialized repository has nothing committed yet, so Git's own
    /// <c>log</c> fails on the unborn HEAD. That is not an error the user must
    /// read: it is "this branch has no history yet", so the read reports an
    /// available, empty page (spec 13 §20 — typed outcomes, never a misleading
    /// failure for a normal repository state).
    /// </summary>
    [Fact]
    public async Task UnbornHeadRepositoryReportsAnEmptyHistoryPage()
    {
        using TestGitRepository repo = CreateUnbornRepository();
        (GitHistoryService service, _) = CreateService();

        GitHistoryResult result = await service.GetHistoryAsync(repo.RootPath);

        Assert.Equal(GitHistoryStatus.Available, result.Status);
        GitHistoryPage page = Assert.IsType<GitHistoryPage>(result.Page);
        Assert.Empty(page.Commits);
        Assert.False(page.HasMore);
        Assert.Null(page.NextSkip);
        Assert.Equal(0, page.Skip);
        Assert.Equal(GitHistoryQuery.DefaultLimit, page.Limit);

        // The repository really is a Git worktree whose log fails: the empty
        // page is a translation of Git's answer, not a silent fallback.
        Assert.NotEqual(0, TestGitRepository.RunGit(repo.RootPath, "log", "-1", "--format=%H"));
    }

    /// <summary>
    /// The same holds for a file history on an unborn HEAD: no commits means no
    /// commits touched the document, i.e. an available, empty page — the pane
    /// must not show a failure for a repository that simply has no history yet.
    /// </summary>
    [Fact]
    public async Task UnbornHeadFileHistoryIsAnEmptyPageNotAFailure()
    {
        using TestGitRepository repo = CreateUnbornRepository();
        (GitHistoryService service, _) = CreateService();

        GitHistoryResult result = await service.GetFileHistoryAsync(repo.RootPath, "docs/a.md");

        Assert.Equal(GitHistoryStatus.Available, result.Status);
        GitHistoryPage page = Assert.IsType<GitHistoryPage>(result.Page);
        Assert.Empty(page.Commits);
        Assert.False(page.HasMore);
        Assert.Null(page.NextSkip);
    }

    // ---------------------------------------------------------------------
    // Step 6 — Failure, state and read-only coverage
    // ---------------------------------------------------------------------

    /// <summary>
    /// A workspace outside any Git worktree is a valid Document IDE source with
    /// no Git state: the history read reports the typed
    /// <see cref="GitHistoryStatus.NoRepository"/> and no <c>git log</c> is ever
    /// attempted (spec 13 §5/§11).
    /// </summary>
    [Fact]
    public async Task HistoryOutsideAnyRepositoryIsTypedNoRepositoryWithoutRunningLog()
    {
        string scratch = Path.Combine(Path.GetTempPath(), $"scm08-norepo-{Guid.NewGuid():N}");
        Directory.CreateDirectory(scratch);
        try
        {
            var recording = new RecordingGitProcessRunner(
                new GitProcessRunner(GitProcessRunnerOptions.Default, new GitWorktreeMutationQueue()));
            (GitHistoryService service, _) = CreateService(recording);

            GitHistoryResult history = await service.GetHistoryAsync(scratch);
            GitHistoryResult fileHistory = await service.GetFileHistoryAsync(scratch, "docs/a.md");
            GitFileVersionResult version = await service.GetFileVersionAsync(scratch, "docs/a.md", "abcdef0123");

            Assert.Equal(GitHistoryStatus.NoRepository, history.Status);
            Assert.Equal(GitHistoryStatus.NoRepository, fileHistory.Status);
            Assert.Equal(GitHistoryStatus.NoRepository, version.Status);
            Assert.Null(history.Page);
            Assert.DoesNotContain(recording.Invocations, arguments => arguments.Contains("log"));
            Assert.DoesNotContain(recording.Invocations, arguments => arguments.Contains("show"));
        }
        finally
        {
            TryDeleteDirectory(scratch);
        }
    }

    /// <summary>
    /// Git being missing/unusable is a typed
    /// <see cref="GitHistoryStatus.GitUnavailable"/> for history, file history
    /// and version inspection — never an exception and never a silent empty
    /// page (spec 13 §20).
    /// </summary>
    [Fact]
    public async Task HistoryWhenGitIsUnavailableIsTypedGitUnavailable()
    {
        using TestGitRepository repo = CreateRepositoryWithKnownHistory();
        var unavailable = new GitProcessRunner(
            new GitProcessRunnerOptions { ExecutablePath = "scm08-definitely-missing-git-executable" });
        (GitHistoryService service, _) = CreateService(unavailable);

        Assert.Equal(GitHistoryStatus.GitUnavailable, (await service.GetHistoryAsync(repo.RootPath)).Status);
        Assert.Equal(
            GitHistoryStatus.GitUnavailable,
            (await service.GetFileHistoryAsync(repo.RootPath, "docs/a.md")).Status);
        Assert.Equal(
            GitHistoryStatus.GitUnavailable,
            (await service.GetFileVersionAsync(repo.RootPath, "docs/a.md", "abcdef0123")).Status);
    }

    /// <summary>
    /// The read path is strictly read-only (spec 13 §19/§4.2): every invocation
    /// it makes is in SCM-01's read-only vocabulary (so it never takes the
    /// per-worktree mutation queue), the repository state is byte-identical
    /// afterwards, and the invocations are structured — a real <c>log</c> with
    /// the pagination window, <c>--</c> before the pathspec, and a
    /// <c>show &lt;commit&gt;:&lt;path&gt;</c> version read.
    /// </summary>
    [Fact]
    public async Task HistoryAndVersionReadsAreStrictlyReadOnly()
    {
        using TestGitRepository repo = CreateRepositoryWithKnownHistory();
        var recording = new RecordingGitProcessRunner(
            new GitProcessRunner(GitProcessRunnerOptions.Default, new GitWorktreeMutationQueue()));
        (GitHistoryService service, _) = CreateService(recording);
        string headBefore = Head(repo);
        string statusBefore = Status(repo);

        await service.GetHistoryAsync(repo.RootPath);
        await service.GetFileHistoryAsync(repo.RootPath, "docs/a.md");
        await service.GetFileVersionAsync(repo.RootPath, "docs/a.md", headBefore);

        // 1. Nothing the read path ran can mutate repository state.
        Assert.NotEmpty(recording.Requests);
        Assert.All(
            recording.Requests,
            request => Assert.False(GitCommandClassifier.MutatesRepositoryState(request.Arguments)));

        // 2. The repository (HEAD and the index/worktree status) is untouched.
        Assert.Equal(headBefore, Head(repo));
        Assert.Equal(statusBefore, Status(repo));

        // 3. The repository history read is one structured `log` with the window.
        GitProcessRequest repositoryLog = Assert.Single(
            recording.Requests.Where(request => request.Arguments.Contains("log") && !request.Arguments.Contains("--")));
        Assert.Contains("--name-only", repositoryLog.Arguments);
        Assert.Contains("--skip=0", repositoryLog.Arguments);
        Assert.Contains($"--max-count={GitHistoryQuery.DefaultLimit + 1}", repositoryLog.Arguments);
        // `--no-color` must follow the subcommand; before `log` Git rejects it.
        Assert.Contains("--no-color", repositoryLog.Arguments);
        Assert.True(
            repositoryLog.Arguments.ToList().IndexOf("log") < repositoryLog.Arguments.ToList().IndexOf("--no-color"));

        // 4. The file history read keeps the pathspec behind `--` and forces a
        //    literal pathspec (spec 13 §23).
        GitProcessRequest fileLog = Assert.Single(
            recording.Requests.Where(request => request.Arguments.Contains("log") && request.Arguments.Contains("--")));
        Assert.Equal("docs/a.md", fileLog.Arguments[^1]);
        Assert.Equal("--", fileLog.Arguments[^2]);
        Assert.NotNull(fileLog.EnvironmentOverrides);
        Assert.Equal("1", fileLog.EnvironmentOverrides!["GIT_LITERAL_PATHSPECS"]);

        // 5. The version read is one byte-capturing `show` of an object name,
        //    never a shell command string (spec 13 §18/§9.3).
        GitProcessRequest show = Assert.Single(
            recording.Requests.Where(request => request.Arguments.Contains("show")));
        Assert.Equal($"{headBefore}:docs/a.md", show.Arguments[^1]);
        Assert.True(show.CaptureBinaryOutput);

        // No invocation ever passed a concatenated shell command.
        Assert.All(
            recording.Requests,
            request => Assert.All(
                request.Arguments,
                argument => Assert.DoesNotContain("&&", argument, StringComparison.Ordinal)));
    }

    /// <summary>
    /// Git's own failure classifications (permission, authentication, index
    /// lock, hook, conflict, remote rejection, dirty worktree, invalid worktree)
    /// are passed through inspectably instead of being flattened into a silent
    /// empty page (spec 13 §20/§21).
    /// </summary>
    [Theory]
    [InlineData(GitProcessErrorKind.GitPermissionDenied)]
    [InlineData(GitProcessErrorKind.GitAuthRequired)]
    [InlineData(GitProcessErrorKind.GitLockContention)]
    [InlineData(GitProcessErrorKind.GitCommitHookFailed)]
    [InlineData(GitProcessErrorKind.GitPushRejected)]
    [InlineData(GitProcessErrorKind.GitConflict)]
    [InlineData(GitProcessErrorKind.GitUntrackedConflict)]
    [InlineData(GitProcessErrorKind.GitDirtyWorktree)]
    [InlineData(GitProcessErrorKind.GitWorktreeInvalid)]
    [InlineData(GitProcessErrorKind.Unknown)]
    public async Task GitFailureClassificationsStayTypedAndInspectable(GitProcessErrorKind kind)
    {
        using TestGitRepository repo = CreateRepositoryWithKnownHistory();
        var synthetic = new GitProcessResult(
            GitProcessStatus.Failed,
            128,
            string.Empty,
            "synthetic failure signature",
            kind,
            "synthetic failure signature");

        var logFaulting = new FaultingGitProcessRunner(
            new GitProcessRunner(GitProcessRunnerOptions.Default, new GitWorktreeMutationQueue()),
            arguments => arguments.Contains("log") ? synthetic : null);
        (GitHistoryService logService, _) = CreateService(logFaulting);

        GitHistoryResult history = await logService.GetHistoryAsync(repo.RootPath);
        Assert.Equal(GitHistoryStatus.Failed, history.Status);
        Assert.Null(history.Page);
        Assert.NotNull(history.Failure);
        Assert.Equal(kind, history.Failure!.ErrorKind);
        Assert.Equal(128, history.Failure.ExitCode);

        var showFaulting = new FaultingGitProcessRunner(
            new GitProcessRunner(GitProcessRunnerOptions.Default, new GitWorktreeMutationQueue()),
            arguments => arguments.Contains("show") ? synthetic : null);
        (GitHistoryService showService, _) = CreateService(showFaulting);

        GitFileVersionResult version = await showService.GetFileVersionAsync(repo.RootPath, "docs/a.md", Head(repo));
        Assert.Equal(GitHistoryStatus.Failed, version.Status);
        Assert.Null(version.Version);
        Assert.NotNull(version.Failure);
        Assert.Equal(kind, version.Failure!.ErrorKind);
    }

    /// <summary>
    /// The two classifications that map onto a distinct typed outcome keep
    /// doing so — a missing Git executable is
    /// <see cref="GitHistoryStatus.GitUnavailable"/> and a missing repository is
    /// <see cref="GitHistoryStatus.NoRepository"/> (spec 13 §5/§20).
    /// </summary>
    [Theory]
    [InlineData(GitProcessErrorKind.GitNotInstalled, GitHistoryStatus.GitUnavailable)]
    [InlineData(GitProcessErrorKind.GitRepositoryNotFound, GitHistoryStatus.NoRepository)]
    public async Task GitDiscoveryClassificationsMapOntoTheirTypedHistoryOutcome(
        GitProcessErrorKind kind,
        GitHistoryStatus expected)
    {
        using TestGitRepository repo = CreateRepositoryWithKnownHistory();
        var synthetic = new GitProcessResult(
            GitProcessStatus.Failed,
            128,
            string.Empty,
            "synthetic discovery failure",
            kind,
            "synthetic discovery failure");

        var faulting = new FaultingGitProcessRunner(
            new GitProcessRunner(GitProcessRunnerOptions.Default, new GitWorktreeMutationQueue()),
            arguments => arguments.Contains("rev-parse") ? synthetic : null);
        (GitHistoryService service, _) = CreateService(faulting);

        Assert.Equal(expected, (await service.GetHistoryAsync(repo.RootPath)).Status);
        Assert.Equal(expected, (await service.GetFileVersionAsync(repo.RootPath, "docs/a.md", "abcdef0123")).Status);
    }

    /// <summary>
    /// A worktree that disappears between discovery and the read is a typed,
    /// inspectable failure — never an empty page and never an exception
    /// (spec 13 §20).
    /// </summary>
    [Fact]
    public async Task WorktreeDisappearingBeforeTheReadIsATypedFailure()
    {
        using TestGitRepository repo = CreateRepositoryWithKnownHistory();
        var vanishing = new VanishingWorktreeGitProcessRunner(
            new GitProcessRunner(GitProcessRunnerOptions.Default, new GitWorktreeMutationQueue()),
            deleteBefore: arguments => arguments.Contains("log"),
            directory: repo.RootPath);
        (GitHistoryService service, _) = CreateService(vanishing);

        GitHistoryResult result = await service.GetHistoryAsync(repo.RootPath);

        Assert.Equal(GitHistoryStatus.Failed, result.Status);
        Assert.Null(result.Page);
        Assert.NotNull(result.Failure);
        Assert.Equal(GitProcessErrorKind.GitWorktreeInvalid, result.Failure!.ErrorKind);
        Assert.False(Directory.Exists(repo.RootPath));
    }

    /// <summary>
    /// Pathspecs are matched literally (spec 13 §23): in a repository where a
    /// bracketed name and its glob-equivalent both exist, the file history of
    /// <c>docs/notes[1].md</c> reports only the commit that touched that exact
    /// file — without the literal pathspec the glob would leak the other
    /// document's commit into this document's history. A path with a space and
    /// a non-ASCII character is reported verbatim for the same reason.
    /// </summary>
    [Fact]
    public async Task FileHistoryMatchesPathspecsLiterally()
    {
        var repo = TestGitRepository.Create($"scm08-literal-{Guid.NewGuid():N}");
        using (repo)
        {
            Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "checkout", "-b", "main"));
            Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "config", "user.name", "SCM-08 Author"));
            Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "config", "user.email", "scm08@example.invalid"));

            repo.WriteFile("docs/notes1.md", "plain notes");
            Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "-A"));
            Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "commit", "-qm", "adds plain notes"));

            repo.WriteFile("docs/notes[1].md", "bracketed notes");
            repo.WriteFile("docs/caf\u00e9 r\u00e9sum\u00e9.md", "accented notes");
            Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "-A"));
            Assert.Equal(
                0,
                TestGitRepository.RunGit(repo.RootPath, "commit", "-qm", "adds bracketed and accented notes"));

            (GitHistoryService service, _) = CreateService();

            GitHistoryResult bracketed = await service.GetFileHistoryAsync(repo.RootPath, "docs/notes[1].md");
            GitHistoryEntry bracketedCommit = Assert.Single(Assert.IsType<GitHistoryPage>(bracketed.Page).Commits);
            Assert.Equal("adds bracketed and accented notes", bracketedCommit.Summary);
            Assert.Equal(1, bracketedCommit.FileCount);

            // The glob-equivalent file keeps its own, separate history.
            GitHistoryResult plain = await service.GetFileHistoryAsync(repo.RootPath, "docs/notes1.md");
            Assert.Equal(
                new[] { "adds plain notes" },
                Assert.IsType<GitHistoryPage>(plain.Page).Commits.Select(commit => commit.Summary).ToArray());

            GitHistoryResult accented =
                await service.GetFileHistoryAsync(repo.RootPath, "docs/caf\u00e9 r\u00e9sum\u00e9.md");
            GitHistoryEntry accentedCommit = Assert.Single(Assert.IsType<GitHistoryPage>(accented.Page).Commits);
            Assert.Equal("adds bracketed and accented notes", accentedCommit.Summary);

            // And a version can be inspected through such a path too.
            GitFileVersionResult version = await service.GetFileVersionAsync(
                repo.RootPath,
                "docs/caf\u00e9 r\u00e9sum\u00e9.md",
                accentedCommit.CommitId);
            Assert.Equal("accented notes", Assert.IsType<GitFileVersion>(version.Version).Content);
        }
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    private static string[] History(GitHistoryService service, TestGitRepository repo) =>
        service
            .GetHistoryAsync(repo.RootPath)
            .GetAwaiter()
            .GetResult()
            .Page!
            .Commits
            .Select(commit => commit.CommitId)
            .ToArray();

    private static string Status(TestGitRepository repo) =>
        TestGitRepository.RunGitCaptureUtf8(repo.RootPath, "status", "--porcelain=v2", "-z");

    /// <summary>A minimal binary document: ZIP magic, a NUL byte and a payload.</summary>
    private static byte[] CreateBinaryDocx()
    {
        var bytes = new byte[512];
        bytes[0] = (byte)'P';
        bytes[1] = (byte)'K';
        bytes[2] = 0x03;
        bytes[3] = 0x04;
        for (int index = 10; index < bytes.Length; index++)
        {
            bytes[index] = (byte)(index % 251);
        }

        return bytes;
    }

    /// <summary>The repository-wide file count for one commit, for comparison with the path-filtered count.</summary>
    private static async Task<int> RepositoryFileCountAsync(
        GitHistoryService service,
        TestGitRepository repo,
        string summary)
    {
        GitHistoryResult history = await service.GetHistoryAsync(repo.RootPath);
        return history.Page!.Commits.Single(commit => commit.Summary == summary).FileCount;
    }

    /// <summary>
    /// Records every Git invocation while delegating to a real runner, so tests
    /// can prove which arguments the read path used (structured <c>log</c>
    /// invocation, <c>--</c> before the pathspec) and that rejected input never
    /// reached Git.
    /// </summary>
    private sealed class RecordingGitProcessRunner : IGitProcessRunner
    {
        private readonly IGitProcessRunner _inner;

        public RecordingGitProcessRunner(IGitProcessRunner inner)
        {
            _inner = inner;
        }

        public List<IReadOnlyList<string>> Invocations { get; } = new();

        public List<GitProcessRequest> Requests { get; } = new();

        public Task<GitCapabilityInfo> GetCapabilityAsync(CancellationToken cancellationToken = default) =>
            _inner.GetCapabilityAsync(cancellationToken);

        public async Task<GitProcessResult> RunAsync(
            GitProcessRequest request,
            CancellationToken cancellationToken = default)
        {
            lock (Invocations)
            {
                Invocations.Add(request.Arguments);
                Requests.Add(request);
            }

            return await _inner.RunAsync(request, cancellationToken);
        }
    }

    /// <summary>
    /// Delegates to a real runner but substitutes a canned typed failure for
    /// every invocation the <paramref name="fault"/> predicate selects, so the
    /// service's classification passthrough can be exercised deterministically
    /// (spec 13 §20).
    /// </summary>
    private sealed class FaultingGitProcessRunner : IGitProcessRunner
    {
        private readonly IGitProcessRunner _inner;
        private readonly Func<IReadOnlyList<string>, GitProcessResult?> _fault;

        public FaultingGitProcessRunner(IGitProcessRunner inner, Func<IReadOnlyList<string>, GitProcessResult?> fault)
        {
            _inner = inner;
            _fault = fault;
        }

        public Task<GitCapabilityInfo> GetCapabilityAsync(CancellationToken cancellationToken = default) =>
            _inner.GetCapabilityAsync(cancellationToken);

        public Task<GitProcessResult> RunAsync(
            GitProcessRequest request,
            CancellationToken cancellationToken = default) =>
            _fault(request.Arguments) is { } synthetic
                ? Task.FromResult(synthetic)
                : _inner.RunAsync(request, cancellationToken);
    }

    /// <summary>
    /// Deletes the worktree from disk immediately before the selected
    /// invocation, simulating a repository that disappears mid-operation
    /// (spec 13 §20 failure/edge cases).
    /// </summary>
    private sealed class VanishingWorktreeGitProcessRunner : IGitProcessRunner
    {
        private readonly IGitProcessRunner _inner;
        private readonly Func<IReadOnlyList<string>, bool> _deleteBefore;
        private readonly string _directory;

        public VanishingWorktreeGitProcessRunner(
            IGitProcessRunner inner,
            Func<IReadOnlyList<string>, bool> deleteBefore,
            string directory)
        {
            _inner = inner;
            _deleteBefore = deleteBefore;
            _directory = directory;
        }

        public Task<GitCapabilityInfo> GetCapabilityAsync(CancellationToken cancellationToken = default) =>
            _inner.GetCapabilityAsync(cancellationToken);

        public Task<GitProcessResult> RunAsync(
            GitProcessRequest request,
            CancellationToken cancellationToken = default)
        {
            if (_deleteBefore(request.Arguments))
            {
                DeleteDirectoryOrThrow(_directory);
            }

            return _inner.RunAsync(request, cancellationToken);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            DeleteDirectory(path);
        }
        catch (Exception)
        {
            // Best effort: a locked file must not fail the assertion that follows.
        }
    }

    /// <summary>
    /// Removes a test worktree for real. Git marks loose object files read-only
    /// on Windows, so the attributes are cleared first; a worktree that cannot
    /// be removed would make the "worktree disappeared" scenario a lie, so it
    /// throws instead of silently continuing.
    /// </summary>
    private static void DeleteDirectoryOrThrow(string path)
    {
        try
        {
            DeleteDirectory(path);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"The test could not remove the worktree '{path}' to simulate it disappearing.",
                exception);
        }
    }

    private static void DeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(path, recursive: true);
    }

    /// <summary>
    /// Builds the real SCM-01 runner, the SCM-02 repository service and the
    /// SCM-08 service under test — the same composition the API host registers.
    /// </summary>
    private static (GitHistoryService Service, GitRepositoryService Repositories) CreateService(
        IGitProcessRunner? runner = null)
    {
        IGitProcessRunner effective = runner
            ?? new GitProcessRunner(GitProcessRunnerOptions.Default, new GitWorktreeMutationQueue());
        var repositories = new GitRepositoryService(effective);
        return (new GitHistoryService(effective, repositories), repositories);
    }

    /// <summary>
    /// Deterministic three-commit history on branch <c>main</c>:
    /// <list type="number">
    /// <item><c>first commit</c> — adds <c>docs/a.md</c> and <c>docs/notes.md</c>,</item>
    /// <item><c>second: edits a</c> — changes <c>docs/a.md</c> only,</item>
    /// <item><c>third: adds a unicode file</c> — changes <c>docs/a.md</c> and
    /// adds <c>docs/b é.md</c> (paths with a space and a non-ASCII character).</item>
    /// </list>
    /// </summary>
    private static TestGitRepository CreateRepositoryWithKnownHistory()
    {
        var repo = TestGitRepository.Create($"scm08-repo-{Guid.NewGuid():N}");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "checkout", "-b", "main"));
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "config", "user.name", "SCM-08 Author"));
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "config", "user.email", "scm08@example.invalid"));

        repo.WriteFile("docs/a.md", "a v1");
        repo.WriteFile("docs/notes.md", "notes v1");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "-A"));
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "commit", "-qm", "first commit"));

        repo.WriteFile("docs/a.md", "a v2");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "-A"));
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "commit", "-qm", "second: edits a"));

        repo.WriteFile("docs/a.md", "a v3");
        repo.WriteFile("docs/b \u00e9.md", "b v1");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "-A"));
        Assert.Equal(
            0,
            TestGitRepository.RunGit(repo.RootPath, "commit", "-qm", "third: adds a unicode file"));

        return repo;
    }

    private static string Head(TestGitRepository repo) =>
        TestGitRepository.RunGitCaptureUtf8(repo.RootPath, "rev-parse", "HEAD").Trim();

    /// <summary>
    /// A repository that exists and has a staged document but no commit yet, so
    /// HEAD is unborn (Git's own <c>log</c> fails with "does not have any
    /// commits yet").
    /// </summary>
    private static TestGitRepository CreateUnbornRepository()
    {
        var repo = TestGitRepository.Create($"scm08-unborn-{Guid.NewGuid():N}");
        repo.WriteFile("docs/a.md", "staged but never committed");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "-A"));
        return repo;
    }

    /// <summary>
    /// Builds a repository with <paramref name="commitCount"/> commits, each
    /// touching the same document, so both the repository history and the file
    /// history of that document have exactly that many entries.
    /// </summary>
    private static TestGitRepository CreateRepositoryWithManyCommits(int commitCount)
    {
        var repo = TestGitRepository.Create($"scm08-large-{Guid.NewGuid():N}");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "checkout", "-b", "main"));
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "config", "user.name", "SCM-08 Author"));
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "config", "user.email", "scm08@example.invalid"));

        for (int index = 1; index <= commitCount; index++)
        {
            repo.WriteFile("docs/journal.md", $"entry {index:D2}");
            Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "-A"));
            Assert.Equal(
                0,
                TestGitRepository.RunGit(repo.RootPath, "commit", "-qm", $"commit {index:D2}"));
        }

        return repo;
    }
}
