using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Api;
using DocumentIDE.Api.Controllers;
using DocumentIDE.Api.Security;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Domain.Repositories;
using DocumentIDE.Core.SourceControl;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace DocumentIDE.IntegrationTests.SourceControl;

/// <summary>
/// SCM-03 — Source Control Changes Pane.
///
/// These tests drive the privileged Source Control API surface consumed by the
/// changes pane (spec 13 §7/§8/§18) through the real application host and real
/// Git repositories: the pane must show exactly the current Git state, keep
/// staged/unstaged/untracked/conflict groups separate, surface read-only
/// branch/ahead-behind context, and never expose an unauthenticated route.
///
/// Planned-file mapping: the sprint planned this suite at
/// <c>tests/DocumentIDE.IntegrationTests/SourceControl/SourceControlPanelTests.cs</c>
/// (no <c>DocumentIDE.UnitTests</c> project exists in this repository), so it
/// lives here. The frontend half of this sprint is covered by the colocated
/// <c>src/DocumentIDE.Web/src/features/source-control/SourceControlPanel.test.tsx</c>
/// (CAN-05 batch-B ruling: frontend tests live next to the implementation).
/// </summary>
public sealed class SourceControlPanelTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public SourceControlPanelTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    // ---------------------------------------------------------------------
    // Required tests
    // ---------------------------------------------------------------------

    /// <summary>
    /// Required test 1 (first failing test): "Three modified documents appear
    /// as three changes."
    /// </summary>
    [Fact]
    public async Task ThreeModifiedDocumentsAppearAsThreeChanges()
    {
        string[] files = { "docs/agreement.docx", "docs/contract.docx", "docs/proposal.docx" };
        using var repo = CreateRepositoryWithBaselineCommit(files);
        repo.WriteFile("docs/agreement.docx", "agreement v2");
        repo.WriteFile("docs/contract.docx", "contract v2");
        repo.WriteFile("docs/proposal.docx", "proposal v2");

        var (code, status) = await GetStatusAsync(repo.RootPath);

        Assert.Equal(HttpStatusCode.OK, code);
        Assert.NotNull(status);
        Assert.Equal("Available", status!.Status);
        Assert.True(status.HasRepository);
        Assert.Equal(Path.GetFullPath(repo.RootPath), status.WorktreeRoot, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("main", status.BranchName);
        Assert.False(status.IsDetached);
        Assert.False(status.HasUpstream);
        Assert.Null(status.Ahead);
        Assert.Null(status.Behind);

        Assert.Empty(status.Sections.Staged);
        Assert.Empty(status.Sections.Untracked);
        Assert.Empty(status.Sections.Conflicts);

        Assert.Equal(3, status.Sections.Unstaged.Count);
        Assert.Equal(
            new[] { "docs/agreement.docx", "docs/contract.docx", "docs/proposal.docx" },
            status.Sections.Unstaged.Select(entry => entry.Path).OrderBy(path => path).ToArray());
        Assert.All(status.Sections.Unstaged, entry =>
        {
            Assert.Equal("Modified", entry.State);
            Assert.Equal("Modified", entry.WorkingTreeChange);
            Assert.Equal("Unmodified", entry.IndexChange);
        });
    }

    /// <summary>Required test 2: "Staged and unstaged groups remain separate."</summary>
    [Fact]
    public async Task StagedAndUnstagedGroupsRemainSeparate()
    {
        string[] files = { "docs/a.md", "docs/b.md", "docs/c.md" };
        using var repo = CreateRepositoryWithBaselineCommit(files);
        repo.WriteFile("docs/a.md", "a v2");
        repo.WriteFile("docs/b.md", "b v2");
        repo.WriteFile("docs/c.md", "c v2");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "--", "docs/a.md"));
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "--", "docs/c.md"));

        var (code, status) = await GetStatusAsync(repo.RootPath);

        Assert.Equal(HttpStatusCode.OK, code);
        Assert.Equal(
            new[] { "docs/a.md", "docs/c.md" },
            status!.Sections.Staged.Select(entry => entry.Path).OrderBy(path => path).ToArray());
        Assert.Equal(new[] { "docs/b.md" }, status.Sections.Unstaged.Select(entry => entry.Path).ToArray());
        Assert.All(status.Sections.Staged, entry =>
        {
            Assert.Equal("Modified", entry.IndexChange);
            Assert.Equal("Unmodified", entry.WorkingTreeChange);
            Assert.Equal("M.", entry.StatusCharacters);
        });
    }

    /// <summary>
    /// Spec 13 §10: after a file changes again post-staging, Git reports the
    /// staged and working-tree states separately — the pane keeps both.
    /// </summary>
    [Fact]
    public async Task FileWithBothStagedAndWorkingTreeChangesAppearsInBothGroups()
    {
        string[] files = { "docs/a.md" };
        using var repo = CreateRepositoryWithBaselineCommit(files);
        repo.WriteFile("docs/a.md", "a staged version");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "--", "docs/a.md"));
        repo.WriteFile("docs/a.md", "a further unstaged edit");

        var (code, status) = await GetStatusAsync(repo.RootPath);

        Assert.Equal(HttpStatusCode.OK, code);
        var staged = Assert.Single(status!.Sections.Staged);
        var unstaged = Assert.Single(status.Sections.Unstaged);
        Assert.Equal("docs/a.md", staged.Path);
        Assert.Equal("docs/a.md", unstaged.Path);
        Assert.Equal("MM", staged.StatusCharacters);
        Assert.Equal("MM", unstaged.StatusCharacters);
        Assert.Equal("Modified", staged.IndexChange);
        Assert.Equal("Modified", unstaged.WorkingTreeChange);
    }

    /// <summary>
    /// Required test 3 (backend half): "Status refresh updates the pane without
    /// losing selection." Every status call re-derives Git state — a refresh
    /// is never served from a cached snapshot.
    /// </summary>
    [Fact]
    public async Task StatusRefreshReflectsCurrentGitStateWithoutCache()
    {
        string[] files = { "docs/a.md", "docs/b.md", "docs/c.md" };
        using var repo = CreateRepositoryWithBaselineCommit(files);
        repo.WriteFile("docs/a.md", "a v2");
        repo.WriteFile("docs/b.md", "b v2");
        repo.WriteFile("docs/c.md", "c v2");

        var (_, before) = await GetStatusAsync(repo.RootPath);
        Assert.Empty(before!.Sections.Staged);
        Assert.Equal(3, before.Sections.Unstaged.Count);

        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "--", "docs/b.md"));

        var (_, after) = await GetStatusAsync(repo.RootPath);
        Assert.Equal(new[] { "docs/b.md" }, after!.Sections.Staged.Select(entry => entry.Path).ToArray());
        Assert.Equal(
            new[] { "docs/a.md", "docs/c.md" },
            after.Sections.Unstaged.Select(entry => entry.Path).OrderBy(path => path).ToArray());
    }

    // ---------------------------------------------------------------------
    // Exactness: untracked, conflicts, spaces/Unicode
    // ---------------------------------------------------------------------

    [Fact]
    public async Task UntrackedFilesAppearOnlyInTheUntrackedSection()
    {
        using var repo = CreateRepositoryWithBaselineCommit(Array.Empty<string>());
        repo.WriteFile("docs/new notes \u00e9.md", "new");

        var (_, status) = await GetStatusAsync(repo.RootPath);

        var untracked = Assert.Single(status!.Sections.Untracked);
        Assert.Equal("docs/new notes \u00e9.md", untracked.Path);
        Assert.Equal("Untracked", untracked.State);
        Assert.Equal("??", untracked.StatusCharacters);
        Assert.Empty(status.Sections.Staged);
        Assert.Empty(status.Sections.Unstaged);
        Assert.Empty(status.Sections.Conflicts);
    }

    [Fact]
    public async Task ConflictedFilesAppearOnlyInTheConflictSection()
    {
        string[] files = { "docs/conflict.md" };
        using var repo = CreateRepositoryWithBaselineCommit(files);
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "checkout", "-qb", "other"));
        repo.WriteFile("docs/conflict.md", "their side");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "--", "docs/conflict.md"));
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "commit", "-qm", "their change"));
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "checkout", "-q", "main"));
        repo.WriteFile("docs/conflict.md", "our side");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "--", "docs/conflict.md"));
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "commit", "-qm", "our change"));
        Assert.NotEqual(0, TestGitRepository.RunGit(repo.RootPath, "merge", "other"));

        var (code, status) = await GetStatusAsync(repo.RootPath);

        Assert.Equal(HttpStatusCode.OK, code);
        var conflict = Assert.Single(status!.Sections.Conflicts);
        Assert.Equal("docs/conflict.md", conflict.Path);
        Assert.Equal("Conflicted", conflict.State);
        Assert.Equal("BothModified", conflict.ConflictKind);
        Assert.Equal("UU", conflict.StatusCharacters);
        Assert.Empty(status.Sections.Staged);
        Assert.Empty(status.Sections.Unstaged);
    }

    [Fact]
    public async Task PathsWithSpacesAndUnicodeAreReportedExactly()
    {
        string[] files = { "docs/my notes \u00e9\u00e0\u00fc.md" };
        using var repo = CreateRepositoryWithBaselineCommit(
            files, $"scm03 repo \u00fc (v1) {Guid.NewGuid():N}");
        repo.WriteFile("docs/my notes \u00e9\u00e0\u00fc.md", "edited");

        var (_, status) = await GetStatusAsync(repo.RootPath);

        var entry = Assert.Single(status!.Sections.Unstaged);
        Assert.Equal("docs/my notes \u00e9\u00e0\u00fc.md", entry.Path);
        Assert.Equal(
            Path.Combine(Path.GetFullPath(repo.RootPath), "docs", "my notes \u00e9\u00e0\u00fc.md"),
            entry.AbsolutePath,
            StringComparer.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------------
    // Read-only repository/worktree context and ahead/behind placeholders
    // ---------------------------------------------------------------------

    [Fact]
    public async Task ContextReportsReadOnlyBranchAndDetachedState()
    {
        using var repo = CreateRepositoryWithBaselineCommit(Array.Empty<string>());
        string headCommit = TestGitRepository.RunGitCapture(repo.RootPath, "rev-parse", "HEAD").Trim();

        var (code, context) = await GetContextAsync(repo.RootPath);

        Assert.Equal(HttpStatusCode.OK, code);
        Assert.NotNull(context);
        Assert.Equal("Available", context!.Status);
        Assert.True(context.HasRepository);
        Assert.Equal(Path.GetFullPath(repo.RootPath), context.WorktreeRoot, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(Path.GetFullPath(repo.RootPath), context.RepositoryRoot, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("main", context.BranchName);
        Assert.False(context.IsDetached);
        Assert.Equal(headCommit, context.HeadCommit);

        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "checkout", "-q", "--detach"));

        var (_, detached) = await GetContextAsync(repo.RootPath);
        Assert.True(detached!.IsDetached);
        Assert.Null(detached.BranchName);
    }

    [Fact]
    public async Task AheadBehindPlaceholdersComeFromConfiguredUpstreamState()
    {
        using var repo = CreateRepositoryWithBaselineCommit(Array.Empty<string>());
        string remotePath = Path.Combine(Path.GetTempPath(), "scm03", $"remote-{Guid.NewGuid():N}");
        Directory.CreateDirectory(remotePath);

        try
        {
            Assert.Equal(0, TestGitRepository.RunGit(remotePath, "init", "--bare", "-q", "."));
            Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "remote", "add", "origin", remotePath));
            Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "push", "-q", "-u", "origin", "main"));

            // One local commit ahead of the upstream.
            repo.WriteFile("docs/ahead.md", "ahead");
            Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "--", "docs/ahead.md"));
            Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "commit", "-qm", "ahead"));

            var (_, status) = await GetStatusAsync(repo.RootPath);

            Assert.True(status!.HasUpstream);
            Assert.Equal("origin/main", status.UpstreamReference);
            Assert.Equal(1, status.Ahead);
            Assert.Equal(0, status.Behind);
        }
        finally
        {
            DeleteDirectoryBestEffort(remotePath);
        }
    }

    // ---------------------------------------------------------------------
    // Failure / edge cases
    // ---------------------------------------------------------------------

    [Fact]
    public async Task NonGitFolderYieldsTypedNoRepositoryState()
    {
        string folder = Path.Combine(Path.GetTempPath(), "scm03", $"plain-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);

        try
        {
            var (code, status) = await GetStatusAsync(folder);
            Assert.Equal(HttpStatusCode.OK, code);
            Assert.Equal("NoRepository", status!.Status);
            Assert.False(status.HasRepository);
            Assert.Empty(status.Sections.Staged);
            Assert.Empty(status.Sections.Unstaged);
            Assert.Empty(status.Sections.Untracked);
            Assert.Empty(status.Sections.Conflicts);
            Assert.NotNull(status.Failure);
            Assert.Equal("GitRepositoryNotFound", status.Failure!.ErrorKind);

            var (contextCode, context) = await GetContextAsync(folder);
            Assert.Equal(HttpStatusCode.OK, contextCode);
            Assert.Equal("NoRepository", context!.Status);
            Assert.False(context.HasRepository);
        }
        finally
        {
            DeleteDirectoryBestEffort(folder);
        }
    }

    [Fact]
    public async Task MissingOrBlankPathIsRejectedWithBadRequest()
    {
        using var client = await CreateAuthorizedClientAsync();

        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await client.GetAsync("/api/source-control/status")).StatusCode);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await client.GetAsync("/api/source-control/status?path=%20")).StatusCode);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await client.GetAsync("/api/source-control/context")).StatusCode);
    }

    [Fact]
    public async Task GitUnavailableYieldsTypedStateOnBothEndpoints()
    {
        using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IGitProcessRunner>();
                services.AddSingleton<IGitProcessRunner>(new UnavailableGitProcessRunner());
            }));

        using var scope = factory.Services.CreateScope();
        var token = scope.ServiceProvider.GetRequiredService<ISessionTokenProvider>().SessionToken;
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Origin", "http://localhost:5173");
        client.DefaultRequestHeaders.Add("X-DocumentIDE-Session-Token", token);

        string folder = Path.Combine(Path.GetTempPath(), "scm03", $"plain-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);

        try
        {
            var status = await client.GetFromJsonAsync<SourceControlStatusResponse>(
                $"/api/source-control/status?path={Uri.EscapeDataString(folder)}");
            Assert.Equal("GitUnavailable", status!.Status);
            Assert.False(status.HasRepository);
            Assert.NotNull(status.Failure);
            Assert.Equal("GitNotInstalled", status.Failure!.ErrorKind);

            var context = await client.GetFromJsonAsync<SourceControlContextResponse>(
                $"/api/source-control/context?path={Uri.EscapeDataString(folder)}");
            Assert.Equal("GitUnavailable", context!.Status);
            Assert.NotNull(context.Failure);
        }
        finally
        {
            DeleteDirectoryBestEffort(folder);
        }
    }

    // ---------------------------------------------------------------------
    // Privileged API security
    // ---------------------------------------------------------------------

    [Fact]
    public async Task SourceControlRoutesRejectMissingInvalidTokenAndHostileOrigin()
    {
        string path = Uri.EscapeDataString(Path.GetTempPath());

        using (var anonymous = _factory.CreateClient())
        {
            Assert.Equal(
                HttpStatusCode.Unauthorized,
                (await anonymous.GetAsync($"/api/source-control/status?path={path}")).StatusCode);
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var tokenProvider = scope.ServiceProvider.GetRequiredService<ISessionTokenProvider>();

            using var hostileOrigin = _factory.CreateClient();
            hostileOrigin.DefaultRequestHeaders.Add("Origin", "https://attacker.example");
            hostileOrigin.DefaultRequestHeaders.Add("X-DocumentIDE-Session-Token", tokenProvider.SessionToken);
            Assert.Equal(
                HttpStatusCode.Forbidden,
                (await hostileOrigin.GetAsync($"/api/source-control/status?path={path}")).StatusCode);

            using var wrongToken = _factory.CreateClient();
            wrongToken.DefaultRequestHeaders.Add("Origin", "http://localhost:5173");
            wrongToken.DefaultRequestHeaders.Add("X-DocumentIDE-Session-Token", "invalid-token");
            Assert.Equal(
                HttpStatusCode.Unauthorized,
                (await wrongToken.GetAsync($"/api/source-control/context?path={path}")).StatusCode);
        }
    }

    [Fact]
    public async Task BranchManagementRoutesDoNotExist()
    {
        using var client = await CreateAuthorizedClientAsync();

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.PostAsync("/api/source-control/branch", null)).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.PostAsync("/api/source-control/checkout", null)).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.DeleteAsync("/api/source-control/branch/main")).StatusCode);
    }

    // ---------------------------------------------------------------------
    // Document opening/inspection seam (Step 4)
    // ---------------------------------------------------------------------

    [Fact]
    public async Task EntriesCarryResolvedDocumentIdentityForRegisteredDocuments()
    {
        string[] files = { "docs/known.md", "docs/unknown.md" };
        using var repo = CreateRepositoryWithBaselineCommit(files);
        repo.WriteFile("docs/known.md", "known v2");
        repo.WriteFile("docs/unknown.md", "unknown v2");

        string knownPath = Path.Combine(Path.GetFullPath(repo.RootPath), "docs", "known.md");
        string documentId = $"scm03-doc-{Guid.NewGuid():N}";

        using var scope = _factory.Services.CreateScope();
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
        await documents.CreateAsync(new Document
        {
            Id = documentId,
            CurrentPath = knownPath,
            Filename = "known.md",
            Extension = ".md",
        });

        try
        {
            var (_, status) = await GetStatusAsync(repo.RootPath);

            Assert.Equal(2, status!.Sections.Unstaged.Count);
            var known = status.Sections.Unstaged.Single(entry => entry.Path == "docs/known.md");
            var unknown = status.Sections.Unstaged.Single(entry => entry.Path == "docs/unknown.md");

            Assert.Equal(documentId, known.DocumentId);
            Assert.Equal(knownPath, known.AbsolutePath, StringComparer.OrdinalIgnoreCase);
            Assert.Null(unknown.DocumentId);
            Assert.NotNull(unknown.AbsolutePath);
        }
        finally
        {
            await documents.DeleteAsync(documentId);
        }
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    /// <summary>
    /// Creates a repository with branch <c>main</c> and one baseline commit
    /// containing <paramref name="baselineFiles"/>, so later edits to those
    /// files are reported as unstaged modifications.
    /// </summary>
    private static TestGitRepository CreateRepositoryWithBaselineCommit(
        string[] baselineFiles,
        string? directoryName = null)
    {
        var repo = TestGitRepository.Create(directoryName ?? $"scm03-repo-{Guid.NewGuid():N}");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "checkout", "-b", "main"));
        repo.WriteFile("docs/baseline.md", "baseline");
        foreach (string file in baselineFiles)
        {
            repo.WriteFile(file, "v1");
        }

        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "-A"));
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "commit", "-qm", "baseline"));
        return repo;
    }

    private async Task<(HttpStatusCode Code, SourceControlStatusResponse? Body)> GetStatusAsync(string path)
    {
        using var client = await CreateAuthorizedClientAsync();
        using var response = await client.GetAsync(
            $"/api/source-control/status?path={Uri.EscapeDataString(path)}");
        var body = response.StatusCode == HttpStatusCode.OK
            ? await response.Content.ReadFromJsonAsync<SourceControlStatusResponse>()
            : null;
        return (response.StatusCode, body);
    }

    private async Task<(HttpStatusCode Code, SourceControlContextResponse? Body)> GetContextAsync(string path)
    {
        using var client = await CreateAuthorizedClientAsync();
        using var response = await client.GetAsync(
            $"/api/source-control/context?path={Uri.EscapeDataString(path)}");
        var body = response.StatusCode == HttpStatusCode.OK
            ? await response.Content.ReadFromJsonAsync<SourceControlContextResponse>()
            : null;
        return (response.StatusCode, body);
    }

    private async Task<HttpClient> CreateAuthorizedClientAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var tokenProvider = scope.ServiceProvider.GetRequiredService<ISessionTokenProvider>();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Origin", "http://localhost:5173");
        client.DefaultRequestHeaders.Add("X-DocumentIDE-Session-Token", tokenProvider.SessionToken);
        return await Task.FromResult(client);
    }

    private static void DeleteDirectoryBestEffort(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception)
        {
            // Best effort cleanup; a locked temp directory must not fail the test.
        }
    }

    private sealed class UnavailableGitProcessRunner : IGitProcessRunner
    {
        public Task<GitCapabilityInfo> GetCapabilityAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new GitCapabilityInfo(
                GitCapabilityStatus.Unavailable,
                "git",
                Unavailable()));

        public Task<GitProcessResult> RunAsync(
            GitProcessRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Unavailable());

        private static GitProcessResult Unavailable() => new(
            GitProcessStatus.Unavailable,
            null,
            string.Empty,
            string.Empty,
            GitProcessErrorKind.GitNotInstalled,
            "System Git executable was not found.");
    }
}
