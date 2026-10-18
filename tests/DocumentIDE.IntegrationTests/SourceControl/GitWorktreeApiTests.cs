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
using DocumentIDE.Core.Domain.Repositories;
using DocumentIDE.Core.SourceControl;
using DocumentIDE.Infrastructure.SourceControl;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace DocumentIDE.IntegrationTests.SourceControl;

/// <summary>
/// SCM-18 — Worktree API Surface (spec 13 §16/§18/§20).
///
/// These tests drive the privileged HTTP routes that expose the already-tested
/// SCM-15/SCM-16 worktree services. They run through the real application host
/// and real Git repositories/worktrees created with raw Git invocations, never
/// fabricated DTOs: the routes must be a thin, typed surface over Git and must
/// never attach, remove, or move anything Git did not register.
/// </summary>
public sealed class GitWorktreeApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string SessionTokenHeader = "X-DocumentIDE-Session-Token";
    private const string TestOrigin = "http://localhost:5173";

    private readonly WebApplicationFactory<Program> _factory;

    public GitWorktreeApiTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    // ---------------------------------------------------------------------
    // GET api/source-control/worktrees
    // ---------------------------------------------------------------------

    [Fact]
    public async Task ListReturnsMainAndCreatedWorktree()
    {
        using WorktreeFixture fixture = WorktreeFixture.Create("list");
        string worktreePath = fixture.NewTargetPath("wt-list");
        fixture.AddDetachedWorktree(worktreePath);

        using HttpClient client = await CreateAuthorizedClientAsync(_factory);
        using HttpResponseMessage response = await client.GetAsync(
            $"/api/source-control/worktrees?path={Uri.EscapeDataString(fixture.Repo.RootPath)}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        SourceControlWorktreeListResponse? body =
            await response.Content.ReadFromJsonAsync<SourceControlWorktreeListResponse>();

        Assert.NotNull(body);
        Assert.Equal("Succeeded", body!.Status);
        Assert.True(body.IsSuccess);
        Assert.Null(body.ErrorCode);
        Assert.Equal(2, body.Worktrees.Count);

        SourceControlWorktreeInfoResponse main = Assert.Single(body.Worktrees, worktree => worktree.IsMainWorktree);
        Assert.Equal(fixture.Repo.RootPath, main.Path, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(fixture.HeadCommit, main.HeadCommit);
        Assert.False(main.IsDetached);

        SourceControlWorktreeInfoResponse created =
            Assert.Single(body.Worktrees, worktree => !worktree.IsMainWorktree);
        Assert.Equal(worktreePath, created.Path, StringComparer.OrdinalIgnoreCase);
        Assert.True(created.IsDetached);
        Assert.Null(created.BranchName);
        Assert.False(created.IsLocked);
        Assert.False(created.IsPrunable);
    }

    [Fact]
    public async Task ListReturnsTypedNotARepositoryForPlainFolder()
    {
        string folder = NewPlainFolder();
        try
        {
            using HttpClient client = await CreateAuthorizedClientAsync(_factory);
            using HttpResponseMessage response = await client.GetAsync(
                $"/api/source-control/worktrees?path={Uri.EscapeDataString(folder)}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            SourceControlWorktreeListResponse? body =
                await response.Content.ReadFromJsonAsync<SourceControlWorktreeListResponse>();

            Assert.NotNull(body);
            Assert.Equal("NotARepository", body!.Status);
            Assert.False(body.IsSuccess);
            Assert.Equal("NotARepository", body.ErrorCode);
            Assert.Empty(body.Worktrees);
        }
        finally
        {
            DeleteDirectoryBestEffort(folder);
        }
    }

    // ---------------------------------------------------------------------
    // POST api/source-control/worktrees
    // ---------------------------------------------------------------------

    [Fact]
    public async Task CreateFromExistingBranchCreatesRealWorktree()
    {
        using WorktreeFixture fixture = WorktreeFixture.Create("create-branch");
        string targetPath = fixture.NewTargetPath("wt-branch");
        string branchesBefore = RunGitCapture(fixture.Repo.RootPath, "for-each-ref", "--format=%(refname)", "refs/heads/");

        using HttpClient client = await CreateAuthorizedClientAsync(_factory);
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/source-control/worktrees",
            new SourceControlWorktreeCreateRequest(fixture.Repo.RootPath, targetPath, "feature-x"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        SourceControlWorktreeCreateResponse? body =
            await response.Content.ReadFromJsonAsync<SourceControlWorktreeCreateResponse>();

        Assert.NotNull(body);
        Assert.Equal("Succeeded", body!.Status);
        Assert.True(body.IsSuccess);
        Assert.True(Directory.Exists(targetPath), "The worktree directory must exist after a successful create.");

        Assert.NotNull(body.Worktree);
        Assert.Equal("refs/heads/feature-x", body.Worktree!.BranchRef);
        Assert.Equal("feature-x", body.Worktree.BranchName);
        Assert.False(body.Worktree.IsDetached);

        // The registry the API re-read contains the new worktree.
        Assert.NotNull(body.Worktrees);
        Assert.Contains(body.Worktrees!, worktree => PathEquals(worktree.Path, targetPath));

        // Spec 13 §16.3: no branch is created as a side effect.
        Assert.Equal(
            branchesBefore,
            RunGitCapture(fixture.Repo.RootPath, "for-each-ref", "--format=%(refname)", "refs/heads/"));
    }

    [Fact]
    public async Task CreateFromTagAndExplicitCommitCreatesDetachedWorktrees()
    {
        using WorktreeFixture fixture = WorktreeFixture.Create("create-refs");
        string tagTarget = fixture.NewTargetPath("wt-tag");
        string commitTarget = fixture.NewTargetPath("wt-commit");

        using HttpClient client = await CreateAuthorizedClientAsync(_factory);

        using HttpResponseMessage tagResponse = await client.PostAsJsonAsync(
            "/api/source-control/worktrees",
            new SourceControlWorktreeCreateRequest(fixture.Repo.RootPath, tagTarget, "v1"));
        Assert.Equal(HttpStatusCode.OK, tagResponse.StatusCode);
        SourceControlWorktreeCreateResponse? tagBody =
            await tagResponse.Content.ReadFromJsonAsync<SourceControlWorktreeCreateResponse>();
        Assert.Equal("Succeeded", tagBody!.Status);
        Assert.True(tagBody.IsSuccess);
        Assert.True(Directory.Exists(tagTarget));
        Assert.NotNull(tagBody.Worktree);
        Assert.True(tagBody.Worktree!.IsDetached);
        Assert.Equal(fixture.HeadCommit, tagBody.Worktree.HeadCommit);

        using HttpResponseMessage commitResponse = await client.PostAsJsonAsync(
            "/api/source-control/worktrees",
            new SourceControlWorktreeCreateRequest(fixture.Repo.RootPath, commitTarget, fixture.HeadCommit));
        Assert.Equal(HttpStatusCode.OK, commitResponse.StatusCode);
        SourceControlWorktreeCreateResponse? commitBody =
            await commitResponse.Content.ReadFromJsonAsync<SourceControlWorktreeCreateResponse>();
        Assert.Equal("Succeeded", commitBody!.Status);
        Assert.True(Directory.Exists(commitTarget));
        Assert.NotNull(commitBody.Worktree);
        Assert.True(commitBody.Worktree!.IsDetached);
        Assert.Equal(fixture.HeadCommit, commitBody.Worktree.HeadCommit);

        string porcelain = RunGitCapture(fixture.Repo.RootPath, "worktree", "list", "--porcelain");
        Assert.Contains(NormalizedPath(tagTarget), porcelain);
        Assert.Contains(NormalizedPath(commitTarget), porcelain);
    }

    [Fact]
    public async Task CreateWithNonExistentRefIsRefusedAndCreatesNothing()
    {
        using WorktreeFixture fixture = WorktreeFixture.Create("create-noref");
        string targetPath = fixture.NewTargetPath("wt-noref");

        using HttpClient client = await CreateAuthorizedClientAsync(_factory);
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/source-control/worktrees",
            new SourceControlWorktreeCreateRequest(fixture.Repo.RootPath, targetPath, "does-not-exist"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        SourceControlWorktreeCreateResponse? body =
            await response.Content.ReadFromJsonAsync<SourceControlWorktreeCreateResponse>();

        Assert.NotNull(body);
        Assert.Equal("RefNotFound", body!.Status);
        Assert.False(body.IsSuccess);
        Assert.Equal("RefNotFound", body.ErrorCode);
        Assert.NotNull(body.Message);
        Assert.Null(body.Worktree);
        Assert.False(Directory.Exists(targetPath), "A refused create must not create the target directory.");

        string porcelain = RunGitCapture(fixture.Repo.RootPath, "worktree", "list", "--porcelain");
        Assert.DoesNotContain(NormalizedPath(targetPath), porcelain);
    }

    // ---------------------------------------------------------------------
    // DELETE api/source-control/worktrees
    // ---------------------------------------------------------------------

    [Fact]
    public async Task RemoveCleanRegisteredWorktreeSucceeds()
    {
        using WorktreeFixture fixture = WorktreeFixture.Create("remove-clean");
        string worktreePath = fixture.NewTargetPath("wt-clean");
        fixture.AddDetachedWorktree(worktreePath);

        using HttpClient client = await CreateAuthorizedClientAsync(_factory);
        using HttpResponseMessage response = await DeleteWorktreeAsync(client, fixture.Repo.RootPath, worktreePath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        SourceControlWorktreeRemoveResponse? body =
            await response.Content.ReadFromJsonAsync<SourceControlWorktreeRemoveResponse>();

        Assert.NotNull(body);
        Assert.Equal("Succeeded", body!.Status);
        Assert.True(body.IsSuccess);
        Assert.False(Directory.Exists(worktreePath), "The worktree directory must be gone after a successful removal.");

        string porcelain = RunGitCapture(fixture.Repo.RootPath, "worktree", "list", "--porcelain");
        Assert.DoesNotContain(NormalizedPath(worktreePath), porcelain);

        Assert.NotNull(body.Worktrees);
        Assert.DoesNotContain(body.Worktrees!, worktree => PathEquals(worktree.Path, worktreePath));
    }

    [Fact]
    public async Task RemoveDirtyWorktreeIsRefusedAndStillExists()
    {
        using WorktreeFixture fixture = WorktreeFixture.Create("remove-dirty");
        string worktreePath = fixture.NewTargetPath("wt-dirty");
        fixture.AddDetachedWorktree(worktreePath);
        File.WriteAllText(Path.Combine(worktreePath, "untracked.txt"), "new");
        File.WriteAllText(Path.Combine(worktreePath, "tracked.txt"), "changed");

        using HttpClient client = await CreateAuthorizedClientAsync(_factory);
        using HttpResponseMessage response = await DeleteWorktreeAsync(client, fixture.Repo.RootPath, worktreePath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        SourceControlWorktreeRemoveResponse? body =
            await response.Content.ReadFromJsonAsync<SourceControlWorktreeRemoveResponse>();

        Assert.NotNull(body);
        Assert.Equal("DirtyWorktree", body!.Status);
        Assert.False(body.IsSuccess);
        Assert.Equal("DirtyWorktree", body.ErrorCode);
        Assert.NotEmpty(body.Changes);
        Assert.Contains(body.Changes, change => change.Path == "untracked.txt");
        Assert.True(Directory.Exists(worktreePath), "A blocked removal must not delete the worktree directory.");

        string porcelain = RunGitCapture(fixture.Repo.RootPath, "worktree", "list", "--porcelain");
        Assert.Contains(NormalizedPath(worktreePath), porcelain);
    }

    [Fact]
    public async Task RemoveMainWorktreeIsRefused()
    {
        using WorktreeFixture fixture = WorktreeFixture.Create("remove-main");

        using HttpClient client = await CreateAuthorizedClientAsync(_factory);
        using HttpResponseMessage response =
            await DeleteWorktreeAsync(client, fixture.Repo.RootPath, fixture.Repo.RootPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        SourceControlWorktreeRemoveResponse? body =
            await response.Content.ReadFromJsonAsync<SourceControlWorktreeRemoveResponse>();

        Assert.NotNull(body);
        Assert.Equal("IsMainWorktree", body!.Status);
        Assert.False(body.IsSuccess);
        Assert.True(Directory.Exists(fixture.Repo.RootPath));
        Assert.True(Directory.Exists(Path.Combine(fixture.Repo.RootPath, ".git")));
    }

    // ---------------------------------------------------------------------
    // POST api/source-control/worktrees/open
    // ---------------------------------------------------------------------

    [Fact]
    public async Task OpenRegisteredUnattachedWorktreeRequiresAttachmentFlowAndAttachesNothing()
    {
        using WorktreeFixture fixture = WorktreeFixture.Create("open");
        string worktreePath = fixture.NewTargetPath("wt-open");
        fixture.AddDetachedWorktree(worktreePath);
        string workspaceId = $"scm18-ws-{Guid.NewGuid():N}";

        using HttpClient client = await CreateAuthorizedClientAsync(_factory);
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/source-control/worktrees/open",
            new SourceControlWorktreeOpenRequest(workspaceId, fixture.Repo.RootPath, worktreePath));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        SourceControlWorktreeOpenResponse? body =
            await response.Content.ReadFromJsonAsync<SourceControlWorktreeOpenResponse>();

        Assert.NotNull(body);
        Assert.Equal("RequiresAttachmentFlow", body!.Status);
        Assert.False(body.IsSuccess);
        Assert.False(body.IsAttachedToWorkspace);
        Assert.Null(body.ErrorCode);
        Assert.NotNull(body.Message);
        Assert.NotNull(body.Worktree);
        Assert.Equal(worktreePath, body.Worktree!.Path, StringComparer.OrdinalIgnoreCase);

        // Spec 13 §16.2: the open action is a signal, never a silent attach.
        using var scope = _factory.Services.CreateScope();
        var sources = scope.ServiceProvider.GetRequiredService<IFolderSourceRepository>();
        Assert.Empty(await sources.GetByWorkspaceAsync(workspaceId));

        Assert.True(Directory.Exists(worktreePath));
    }

    // ---------------------------------------------------------------------
    // POST api/source-control/worktrees/prune
    // ---------------------------------------------------------------------

    [Fact]
    public async Task PruneReturnsRereadRegistryAndDeletesNoValidWorktree()
    {
        using WorktreeFixture fixture = WorktreeFixture.Create("prune");
        string worktreePath = fixture.NewTargetPath("wt-prune");
        fixture.AddDetachedWorktree(worktreePath);

        using HttpClient client = await CreateAuthorizedClientAsync(_factory);
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/source-control/worktrees/prune",
            new SourceControlWorktreeRefreshRequest(fixture.Repo.RootPath));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        SourceControlWorktreeRefreshResponse? body =
            await response.Content.ReadFromJsonAsync<SourceControlWorktreeRefreshResponse>();

        Assert.NotNull(body);
        Assert.Equal("Succeeded", body!.Status);
        Assert.True(body.IsSuccess);
        Assert.NotNull(body.Worktrees);
        Assert.Equal(2, body.Worktrees!.Count);
        Assert.Contains(body.Worktrees, worktree => worktree.IsMainWorktree);
        Assert.Contains(body.Worktrees, worktree => PathEquals(worktree.Path, worktreePath));
        Assert.True(Directory.Exists(worktreePath), "Pruning must never delete a valid worktree.");
    }

    // ---------------------------------------------------------------------
    // Privileged API security
    // ---------------------------------------------------------------------

    [Fact]
    public async Task WorktreeRoutesRejectMissingInvalidTokenAndHostileOriginOrHost()
    {
        string query = Uri.EscapeDataString(Path.GetTempPath());

        using (var anonymous = _factory.CreateClient())
        {
            Assert.Equal(
                HttpStatusCode.Unauthorized,
                (await anonymous.GetAsync($"/api/source-control/worktrees?path={query}")).StatusCode);
            Assert.Equal(
                HttpStatusCode.Unauthorized,
                (await anonymous.PostAsJsonAsync(
                    "/api/source-control/worktrees/prune",
                    new SourceControlWorktreeRefreshRequest(Path.GetTempPath()))).StatusCode);
        }

        using var scope = _factory.Services.CreateScope();
        var tokenProvider = scope.ServiceProvider.GetRequiredService<ISessionTokenProvider>();
        string token = tokenProvider.SessionToken;

        using (var hostileOrigin = _factory.CreateClient())
        {
            hostileOrigin.DefaultRequestHeaders.Add("Origin", "https://attacker.example");
            hostileOrigin.DefaultRequestHeaders.Add(SessionTokenHeader, token);
            Assert.Equal(
                HttpStatusCode.Forbidden,
                (await hostileOrigin.GetAsync($"/api/source-control/worktrees?path={query}")).StatusCode);
            Assert.Equal(
                HttpStatusCode.Forbidden,
                (await hostileOrigin.PostAsJsonAsync(
                    "/api/source-control/worktrees/prune",
                    new SourceControlWorktreeRefreshRequest(Path.GetTempPath()))).StatusCode);
        }

        using (var hostileHost = _factory.CreateClient())
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"/api/source-control/worktrees?path={query}");
            request.Headers.Host = "attacker.example";
            request.Headers.Add("Origin", TestOrigin);
            request.Headers.Add(SessionTokenHeader, token);
            Assert.Equal(HttpStatusCode.Forbidden, (await hostileHost.SendAsync(request)).StatusCode);
        }

        using (var wrongToken = _factory.CreateClient())
        {
            wrongToken.DefaultRequestHeaders.Add("Origin", TestOrigin);
            wrongToken.DefaultRequestHeaders.Add(SessionTokenHeader, "invalid-token");
            Assert.Equal(
                HttpStatusCode.Unauthorized,
                (await wrongToken.GetAsync($"/api/source-control/worktrees?path={query}")).StatusCode);
        }
    }

    // ---------------------------------------------------------------------
    // Path safety: rejected before any Git invocation
    // ---------------------------------------------------------------------

    [Fact]
    public async Task UnsafePathsAndMalformedRefsAreRejectedBeforeGitRuns()
    {
        var runner = new RecordingGitProcessRunner(new GitProcessRunner());
        using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IGitProcessRunner>();
                services.AddSingleton<IGitProcessRunner>(runner);
            }));

        string workingDirectory = Path.GetTempPath();
        string traversal = Path.Combine(Path.GetTempPath(), "scm18", "..", "escaped-worktree");
        string relative = Path.Combine("scm18", "relative-worktree");
        string? driveRoot = Path.GetPathRoot(Path.GetFullPath(Path.GetTempPath()));
        string gitMetadata = Path.Combine(Path.GetTempPath(), "scm18", ".git");

        using HttpClient client = await CreateAuthorizedClientAsync(factory);

        foreach (string target in new[] { traversal, relative, driveRoot!, gitMetadata })
        {
            using HttpResponseMessage response = await client.PostAsJsonAsync(
                "/api/source-control/worktrees",
                new SourceControlWorktreeCreateRequest(workingDirectory, target, "HEAD"));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        using (HttpResponseMessage relativeRemove = await DeleteWorktreeAsync(client, workingDirectory, "relative-worktree"))
        {
            Assert.Equal(HttpStatusCode.BadRequest, relativeRemove.StatusCode);
        }

        string malformedRefTarget = Path.Combine(
            Path.GetTempPath(),
            "scm18",
            $"malformed-ref-{Guid.NewGuid():N}");
        using (HttpResponseMessage malformedRef = await client.PostAsJsonAsync(
            "/api/source-control/worktrees",
            new SourceControlWorktreeCreateRequest(workingDirectory, malformedRefTarget, "bad ref")))
        {
            Assert.Equal(HttpStatusCode.BadRequest, malformedRef.StatusCode);
            ApiError? error = await malformedRef.Content.ReadFromJsonAsync<ApiError>();
            Assert.NotNull(error);
            Assert.False(string.IsNullOrWhiteSpace(error!.ErrorCode));
        }

        Assert.Empty(runner.Requests);
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    private async Task<HttpClient> CreateAuthorizedClientAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var tokenProvider = scope.ServiceProvider.GetRequiredService<ISessionTokenProvider>();
        HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Origin", TestOrigin);
        client.DefaultRequestHeaders.Add(SessionTokenHeader, tokenProvider.SessionToken);
        return await Task.FromResult(client);
    }

    private static async Task<HttpResponseMessage> DeleteWorktreeAsync(
        HttpClient client,
        string workingDirectory,
        string worktreePath)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, "/api/source-control/worktrees")
        {
            Content = JsonContent.Create(
                new SourceControlWorktreeRemoveRequest(workingDirectory, worktreePath)),
        };

        return await client.SendAsync(request);
    }

    private static string NewPlainFolder()
    {
        string folder = Path.Combine(Path.GetTempPath(), "scm18", $"plain-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static string RunGitCapture(string workingDirectory, params string[] arguments) =>
        TestGitRepository.RunGitCapture(workingDirectory, arguments);

    private static string NormalizedPath(string path) => path.Replace('\\', '/');

    private static bool PathEquals(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

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

    /// <summary>Typed 400 body used by the existing SCM routes.</summary>
    private sealed record ApiError(string? ErrorCode, string? Error);

    /// <summary>
    /// A disposable repository with one commit, a <c>feature-x</c> branch and a
    /// <c>v1</c> tag, plus cleanup for every worktree path a test creates.
    /// </summary>
    private sealed class WorktreeFixture : IDisposable
    {
        private readonly List<string> _targetPaths = new();

        private WorktreeFixture(TestGitRepository repo, string headCommit)
        {
            Repo = repo;
            HeadCommit = headCommit;
        }

        public TestGitRepository Repo { get; }

        public string HeadCommit { get; }

        public static WorktreeFixture Create(string name)
        {
            TestGitRepository repo = TestGitRepository.Create($"scm18-{name}-{Guid.NewGuid():N}");
            Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "commit", "--allow-empty", "-m", "initial"));
            Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "branch", "feature-x"));
            Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "tag", "v1"));

            string headCommit = RunGitCapture(repo.RootPath, "rev-parse", "HEAD").Trim();
            return new WorktreeFixture(repo, headCommit);
        }

        public string NewTargetPath(string label)
        {
            string path = Path.Combine(Path.GetTempPath(), "scm18", $"{label}-{Guid.NewGuid():N}");
            _targetPaths.Add(path);
            return path;
        }

        public void AddDetachedWorktree(string targetPath) =>
            Assert.Equal(
                0,
                TestGitRepository.RunGit(Repo.RootPath, "worktree", "add", "--detach", targetPath));

        public void Dispose()
        {
            foreach (string targetPath in _targetPaths)
            {
                try
                {
                    if (Directory.Exists(targetPath))
                    {
                        Directory.Delete(targetPath, recursive: true);
                    }
                }
                catch (IOException)
                {
                    // Best effort: a locked worktree file must not fail the test run.
                }
                catch (UnauthorizedAccessException)
                {
                    // Best effort (same as above).
                }
            }

            Repo.Dispose();
        }
    }

    /// <summary>
    /// Wraps the real runner and records every invocation, so a test can prove
    /// that a rejected request never spawned Git at all.
    /// </summary>
    private sealed class RecordingGitProcessRunner : IGitProcessRunner
    {
        private readonly IGitProcessRunner _inner;
        private readonly List<GitProcessRequest> _requests = new();

        public RecordingGitProcessRunner(IGitProcessRunner inner)
        {
            _inner = inner;
        }

        public IReadOnlyList<GitProcessRequest> Requests
        {
            get
            {
                lock (_requests)
                {
                    return _requests.ToArray();
                }
            }
        }

        public Task<GitCapabilityInfo> GetCapabilityAsync(CancellationToken cancellationToken = default) =>
            _inner.GetCapabilityAsync(cancellationToken);

        public Task<GitProcessResult> RunAsync(
            GitProcessRequest request,
            CancellationToken cancellationToken = default)
        {
            lock (_requests)
            {
                _requests.Add(request);
            }

            return _inner.RunAsync(request, cancellationToken);
        }
    }
}
