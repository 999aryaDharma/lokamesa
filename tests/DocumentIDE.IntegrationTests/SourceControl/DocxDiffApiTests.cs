using System;
using System.Collections.Generic;
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
using DocumentIDE.Core.SourceControl;
using DocumentIDE.IntegrationTests.Docx;
using DocumentIDE.Infrastructure.SourceControl;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace DocumentIDE.IntegrationTests.SourceControl;

/// <summary>
/// SCM-19 — DOCX Diff API Surface (spec 13 §9.2/§9.3/§18/§20).
///
/// These tests drive the privileged HTTP route that exposes the already-tested
/// SCM-05 document-aware diff provider. They use real Git repositories and real
/// DOCX packages (the existing <see cref="DocxTestFixtureBuilder"/> assembles
/// the OOXML package); no report, change list or failure DTO is fabricated in
/// the test. The route must stay a thin, typed, strictly read-only surface:
/// nothing here may stage, commit, index, open or write a document.
///
/// HTTP 400 is reserved for input rejected before any Git or filesystem access;
/// every inspectable outcome (not a repository, Git unavailable, path rejected,
/// document missing, non-DOCX/binary fallback, diff failure) is HTTP 200 with a
/// typed body.
/// </summary>
public sealed class DocxDiffApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Route = "/api/source-control/docx-diff";
    private const string SessionTokenHeader = "X-DocumentIDE-Session-Token";
    private const string TestOrigin = "http://localhost:5173";
    private const string WordNamespace = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private readonly WebApplicationFactory<Program> _factory;

    public DocxDiffApiTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    // ---------------------------------------------------------------------
    // A real structural report from two committed DOCX versions
    // ---------------------------------------------------------------------

    [Fact]
    public async Task WorkingTreeDiffOfTwoCommittedDocxVersions_ReturnsRealStructuralReport()
    {
        using TestGitRepository repo = TestGitRepository.Create($"scm19-report-{Guid.NewGuid():N}");
        const string relativePath = "docs/report.docx";
        string fullPath = FullPath(repo, relativePath);

        CommitDocx(
            repo,
            relativePath,
            BuildDocx(
                Heading(1, "Quarterly report"),
                Paragraph("Hello world"),
                Paragraph("Second paragraph"),
                Table(Row("Alpha", "Beta"))));

        WriteBytes(
            fullPath,
            BuildDocx(
                Heading(2, "Quarterly report"),
                Paragraph("Hello brave world"),
                Paragraph("Second paragraph"),
                Paragraph("Third paragraph"),
                Table(Row("Alpha", "Beta revised"))));

        using HttpClient client = CreateAuthorizedClient(_factory);
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            Route,
            new SourceControlDocxDiffRequest(repo.RootPath, relativePath, "WorkingTree"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        SourceControlDocxDiffResponse? body =
            await response.Content.ReadFromJsonAsync<SourceControlDocxDiffResponse>();

        Assert.NotNull(body);
        Assert.Equal("Available", body!.Status);
        Assert.True(body.IsSuccess);
        Assert.Null(body.ErrorCode);
        Assert.Null(body.Message);
        Assert.Null(body.Failure);

        SourceControlDocxDiffReportResponse report = Assert.IsType<SourceControlDocxDiffReportResponse>(body.Report);
        Assert.Equal("WorkingTree", report.Target);
        Assert.Equal(relativePath, report.Path);
        Assert.Equal(repo.RootPath, report.WorktreeRoot, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("HEAD", report.LeftLabel);
        Assert.Equal("working tree", report.RightLabel);
        Assert.True(report.LeftExists);
        Assert.True(report.RightExists);
        Assert.Null(report.BinarySummary);
        Assert.True(report.HasChanges);

        // The typed summary count is the real change count, not a placeholder.
        Assert.Equal(report.Changes.Count, report.ChangeCount);
        Assert.True(report.ChangeCount >= 4, $"Expected at least 4 structural changes, got {report.ChangeCount}.");

        // Every reported class is a real document-aware class from the engine.
        Assert.Contains(report.Changes, change => change.Kind == "ParagraphTextChanged");
        Assert.Contains(report.Changes, change => change.Kind == "ParagraphAdded");
        Assert.Contains(report.Changes, change => change.Kind == "HeadingChanged");
        Assert.Contains(report.Changes, change => change.Kind == "TableCellChanged");

        // Exact paragraph-level before/after content (not an empty payload).
        SourceControlDocxChangeResponse textChange = Assert.Single(
            report.Changes, change => change.Kind == "ParagraphTextChanged");
        Assert.Equal("Hello world", textChange.Before);
        Assert.Equal("Hello brave world", textChange.After);
        Assert.Equal("word/document.xml", textChange.PartName);
        Assert.NotNull(textChange.OldParagraphIndex);
        Assert.NotNull(textChange.NewParagraphIndex);

        SourceControlDocxChangeResponse added = Assert.Single(
            report.Changes, change => change.Kind == "ParagraphAdded");
        Assert.Null(added.Before);
        Assert.Equal("Third paragraph", added.After);

        SourceControlDocxChangeResponse heading = Assert.Single(
            report.Changes, change => change.Kind == "HeadingChanged");
        Assert.Equal("HeadingLevel", heading.Property);
        Assert.Equal("1", heading.Before);
        Assert.Equal("2", heading.After);

        SourceControlDocxChangeResponse cell = Assert.Single(
            report.Changes, change => change.Kind == "TableCellChanged");
        Assert.Equal("Beta", cell.Before);
        Assert.Equal("Beta revised", cell.After);
        Assert.Equal(0, cell.TableIndex);
        Assert.Equal(0, cell.RowIndex);
        Assert.Equal(1, cell.CellIndex);
    }

    // ---------------------------------------------------------------------
    // Read-only proof: no mutation queue, no index/document change
    // ---------------------------------------------------------------------

    [Fact]
    public async Task WorkingTreeDiff_IsReadOnly_NoMutationQueueAndNoRepositoryChange()
    {
        using TestGitRepository repo = TestGitRepository.Create($"scm19-readonly-{Guid.NewGuid():N}");
        const string relativePath = "docs/report.docx";
        string fullPath = FullPath(repo, relativePath);

        CommitDocx(repo, relativePath, BuildDocx(Paragraph("version one")));
        WriteBytes(fullPath, BuildDocx(Paragraph("version two")));

        string indexPath = Path.Combine(repo.RootPath, ".git", "index");
        // Capture the status snapshot before the index bytes: Git may refresh
        // racy-clean stat data (and rewrite the index once) on the first status
        // read after a same-second write. The snapshot then reflects a settled
        // index, so any later write would be the route's, not the fixture's.
        string statusBefore = RunGitCapture(repo.RootPath, "status", "--porcelain=v2", "--branch");
        byte[] documentBefore = File.ReadAllBytes(fullPath);
        byte[] indexBefore = File.ReadAllBytes(indexPath);
        string headBefore = RunGitCapture(repo.RootPath, "rev-parse", "HEAD");
        string[] filesBefore = ListRepositoryFiles(repo.RootPath);

        using WebApplicationFactory<Program> factory = CreateRecordingFactory();
        var runner = Assert.IsType<RecordingGitProcessRunner>(
            factory.Services.GetRequiredService<IGitProcessRunner>());
        var queue = factory.Services.GetRequiredService<GitWorktreeMutationQueue>();

        using HttpClient client = CreateAuthorizedClient(factory);
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            Route,
            new SourceControlDocxDiffRequest(repo.RootPath, relativePath, "WorkingTree"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        SourceControlDocxDiffResponse? body =
            await response.Content.ReadFromJsonAsync<SourceControlDocxDiffResponse>();
        Assert.NotNull(body);
        Assert.Equal("Available", body!.Status);

        // The per-worktree mutation queue was never entered (concurrency stays 0)...
        Assert.Equal(0, queue.MaxObservedConcurrency);

        // ...and every Git invocation on this route is a read-only command.
        Assert.NotEmpty(runner.Requests);
        foreach (GitProcessRequest request in runner.Requests)
        {
            string command = request.Arguments.First(argument => !argument.StartsWith('-'));
            Assert.True(
                command is "rev-parse" or "show",
                $"The DOCX diff route must only run read-only Git commands; saw '{command}' " +
                $"({string.Join(' ', request.Arguments)}).");
        }

        // Nothing in the repository, the index or the document changed.
        Assert.Equal(documentBefore, File.ReadAllBytes(fullPath));
        Assert.Equal(indexBefore, File.ReadAllBytes(indexPath));
        Assert.Equal(headBefore, RunGitCapture(repo.RootPath, "rev-parse", "HEAD"));
        Assert.Equal(statusBefore, RunGitCapture(repo.RootPath, "status", "--porcelain=v2", "--branch"));
        Assert.Equal(filesBefore, ListRepositoryFiles(repo.RootPath));
    }

    // ---------------------------------------------------------------------
    // Non-DOCX path: the engine's own typed binary fallback, never a text diff
    // ---------------------------------------------------------------------

    [Fact]
    public async Task NonDocxPath_IsRefusedAsTypedBinaryFallback_NotDiffedAsText()
    {
        using TestGitRepository repo = TestGitRepository.Create($"scm19-markdown-{Guid.NewGuid():N}");
        const string relativePath = "notes.md";
        string fullPath = FullPath(repo, relativePath);

        repo.WriteFile(relativePath, "before text");
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "--", relativePath));
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "commit", "-qm", "scm19: notes"));
        File.WriteAllText(fullPath, "after text");

        using HttpClient client = CreateAuthorizedClient(_factory);
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            Route,
            new SourceControlDocxDiffRequest(repo.RootPath, relativePath, "WorkingTree"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        SourceControlDocxDiffResponse? body =
            await response.Content.ReadFromJsonAsync<SourceControlDocxDiffResponse>();

        Assert.NotNull(body);
        Assert.Equal("BinaryFallback", body!.Status);
        Assert.False(body.IsSuccess);
        Assert.Equal("BinaryFallback", body.ErrorCode);

        // Refused as a document: no paragraph/table change list is invented.
        SourceControlDocxDiffReportResponse report = Assert.IsType<SourceControlDocxDiffReportResponse>(body.Report);
        Assert.Empty(report.Changes);
        Assert.Equal(0, report.ChangeCount);
        Assert.False(report.HasChanges);

        // ...instead the exact version bytes are summarised.
        SourceControlDocxBinarySummaryResponse summary =
            Assert.IsType<SourceControlDocxBinarySummaryResponse>(report.BinarySummary);
        Assert.True(summary.LeftExists);
        Assert.True(summary.RightExists);
        Assert.True(summary.LeftSizeBytes > 0);
        Assert.True(summary.RightSizeBytes > 0);
        Assert.False(string.IsNullOrWhiteSpace(summary.LeftSha256));
        Assert.False(string.IsNullOrWhiteSpace(summary.RightSha256));
        Assert.NotEqual(summary.LeftSha256, summary.RightSha256);
        Assert.False(summary.ContentIdentical);

        Assert.NotNull(body.Failure);
        Assert.Contains("DOCX", body.Failure!.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------------
    // Path safety: rejected before any Git invocation
    // ---------------------------------------------------------------------

    [Fact]
    public async Task UnsafePaths_AreRejectedTypedBeforeGitRuns()
    {
        using TestGitRepository repo = TestGitRepository.Create($"scm19-escape-{Guid.NewGuid():N}");
        string absoluteDocumentPath = Path.Combine(repo.RootPath, "docs", "report.docx");

        using WebApplicationFactory<Program> factory = CreateRecordingFactory();
        var runner = Assert.IsType<RecordingGitProcessRunner>(
            factory.Services.GetRequiredService<IGitProcessRunner>());
        using HttpClient client = CreateAuthorizedClient(factory);

        var attempts = new (string Label, SourceControlDocxDiffRequest Request)[]
        {
            ("absolute", new SourceControlDocxDiffRequest(repo.RootPath, absoluteDocumentPath, "WorkingTree")),
            ("rooted", new SourceControlDocxDiffRequest(repo.RootPath, Path.DirectorySeparatorChar + "outside.docx", "WorkingTree")),
            ("traversal", new SourceControlDocxDiffRequest(repo.RootPath, "docs/../../outside.docx", "WorkingTree")),
            ("drive-qualified", new SourceControlDocxDiffRequest(repo.RootPath, "C:/Windows/outside.docx", "WorkingTree")),
            ("original-path", new SourceControlDocxDiffRequest(repo.RootPath, "docs/report.docx", "Staged", "../outside.docx")),
        };

        foreach ((string label, SourceControlDocxDiffRequest request) in attempts)
        {
            using HttpResponseMessage response = await client.PostAsJsonAsync(Route, request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            SourceControlDocxDiffResponse? body =
                await response.Content.ReadFromJsonAsync<SourceControlDocxDiffResponse>();
            Assert.NotNull(body);
            Assert.True(body!.Status == "PathRejected", $"{label}: expected PathRejected, got {body.Status}.");
            Assert.False(body.IsSuccess);
            Assert.Equal("PathRejected", body.ErrorCode);
            Assert.Null(body.Report);
            Assert.NotNull(body.Message);
        }

        Assert.Empty(runner.Requests);
    }

    [Fact]
    public async Task InvalidInput_IsRejectedWith400BeforeGitOrFilesystemAccess()
    {
        using TestGitRepository repo = TestGitRepository.Create($"scm19-input-{Guid.NewGuid():N}");

        using WebApplicationFactory<Program> factory = CreateRecordingFactory();
        var runner = Assert.IsType<RecordingGitProcessRunner>(
            factory.Services.GetRequiredService<IGitProcessRunner>());
        using HttpClient client = CreateAuthorizedClient(factory);

        var attempts = new (string Label, object? Body)[]
        {
            ("null body", null),
            ("blank path", new SourceControlDocxDiffRequest(" ", "docs/report.docx", "WorkingTree")),
            ("missing document path", new SourceControlDocxDiffRequest(repo.RootPath, null, "WorkingTree")),
            ("missing target", new SourceControlDocxDiffRequest(repo.RootPath, "docs/report.docx", null)),
            ("unknown target", new SourceControlDocxDiffRequest(repo.RootPath, "docs/report.docx", "bogus")),
        };

        foreach ((string label, object? body) in attempts)
        {
            using HttpResponseMessage response = await client.PostAsJsonAsync(Route, body);
            Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{label} must be HTTP 400.");

            Type400Error? error = await response.Content.ReadFromJsonAsync<Type400Error>();
            Assert.NotNull(error);
            Assert.False(string.IsNullOrWhiteSpace(error!.ErrorCode), $"{label} must carry a typed error code.");
        }

        Assert.Empty(runner.Requests);
    }

    // ---------------------------------------------------------------------
    // A document absent at the compared version stays typed
    // ---------------------------------------------------------------------

    [Fact]
    public async Task DocumentAbsentAtComparedVersion_ReturnsTypedRemovalReport()
    {
        using TestGitRepository repo = TestGitRepository.Create($"scm19-deleted-{Guid.NewGuid():N}");
        const string relativePath = "docs/report.docx";
        string fullPath = FullPath(repo, relativePath);

        CommitDocx(
            repo,
            relativePath,
            BuildDocx(Heading(1, "Title"), Paragraph("Hello world")));
        File.Delete(fullPath);

        using HttpClient client = CreateAuthorizedClient(_factory);
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            Route,
            new SourceControlDocxDiffRequest(repo.RootPath, relativePath, "WorkingTree"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        SourceControlDocxDiffResponse? body =
            await response.Content.ReadFromJsonAsync<SourceControlDocxDiffResponse>();

        Assert.NotNull(body);
        Assert.Equal("Available", body!.Status);
        Assert.True(body.IsSuccess);

        SourceControlDocxDiffReportResponse report = Assert.IsType<SourceControlDocxDiffReportResponse>(body.Report);
        Assert.True(report.LeftExists);
        Assert.False(report.RightExists);
        Assert.Null(report.BinarySummary);

        SourceControlDocxChangeResponse removed = Assert.Single(
            report.Changes, change => change.Kind == "ParagraphRemoved" && change.Before == "Hello world");
        Assert.Null(removed.After);
    }

    // ---------------------------------------------------------------------
    // Not a repository / Git missing are typed HTTP 200 outcomes
    // ---------------------------------------------------------------------

    [Fact]
    public async Task NotARepository_IsTypedHttp200()
    {
        string folder = Path.Combine(Path.GetTempPath(), "scm19", $"plain-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            using HttpClient client = CreateAuthorizedClient(_factory);
            using HttpResponseMessage response = await client.PostAsJsonAsync(
                Route,
                new SourceControlDocxDiffRequest(folder, "docs/report.docx", "WorkingTree"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            SourceControlDocxDiffResponse? body =
                await response.Content.ReadFromJsonAsync<SourceControlDocxDiffResponse>();

            Assert.NotNull(body);
            Assert.Equal("NoRepository", body!.Status);
            Assert.False(body.IsSuccess);
            Assert.Equal("NoRepository", body.ErrorCode);
            Assert.Null(body.Report);
            Assert.NotNull(body.Failure);
        }
        finally
        {
            DeleteDirectoryBestEffort(folder);
        }
    }

    [Fact]
    public async Task GitUnavailable_IsTypedHttp200()
    {
        using TestGitRepository repo = TestGitRepository.Create($"scm19-nogit-{Guid.NewGuid():N}");

        using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IGitProcessRunner>();
                services.AddSingleton<IGitProcessRunner>(new UnavailableGitProcessRunner());
            }));

        using HttpClient client = CreateAuthorizedClient(factory);
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            Route,
            new SourceControlDocxDiffRequest(repo.RootPath, "docs/report.docx", "WorkingTree"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        SourceControlDocxDiffResponse? body =
            await response.Content.ReadFromJsonAsync<SourceControlDocxDiffResponse>();

        Assert.NotNull(body);
        Assert.Equal("GitUnavailable", body!.Status);
        Assert.False(body.IsSuccess);
        Assert.Equal("GitUnavailable", body.ErrorCode);
        Assert.Null(body.Report);
        Assert.NotNull(body.Failure);
    }

    // ---------------------------------------------------------------------
    // Privileged API security
    // ---------------------------------------------------------------------

    [Fact]
    public async Task DocxDiffRouteRejectsMissingInvalidTokenAndHostileOriginOrHost()
    {
        SourceControlDocxDiffRequest request =
            new(Path.GetTempPath(), "docs/report.docx", "WorkingTree");

        using (var anonymous = _factory.CreateClient())
        {
            Assert.Equal(
                HttpStatusCode.Unauthorized,
                (await anonymous.PostAsJsonAsync(Route, request)).StatusCode);
        }

        using var scope = _factory.Services.CreateScope();
        var tokenProvider = scope.ServiceProvider.GetRequiredService<ISessionTokenProvider>();
        string token = tokenProvider.SessionToken;

        using (var wrongToken = _factory.CreateClient())
        {
            wrongToken.DefaultRequestHeaders.Add("Origin", TestOrigin);
            wrongToken.DefaultRequestHeaders.Add(SessionTokenHeader, "invalid-token");
            Assert.Equal(
                HttpStatusCode.Unauthorized,
                (await wrongToken.PostAsJsonAsync(Route, request)).StatusCode);
        }

        using (var hostileOrigin = _factory.CreateClient())
        {
            hostileOrigin.DefaultRequestHeaders.Add("Origin", "https://attacker.example");
            hostileOrigin.DefaultRequestHeaders.Add(SessionTokenHeader, token);
            Assert.Equal(
                HttpStatusCode.Forbidden,
                (await hostileOrigin.PostAsJsonAsync(Route, request)).StatusCode);
        }

        using (var hostileHost = _factory.CreateClient())
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, Route)
            {
                Content = JsonContent.Create(request),
            };
            message.Headers.Host = "attacker.example";
            message.Headers.Add("Origin", TestOrigin);
            message.Headers.Add(SessionTokenHeader, token);
            Assert.Equal(HttpStatusCode.Forbidden, (await hostileHost.SendAsync(message)).StatusCode);
        }
    }

    // ---------------------------------------------------------------------
    // Both composition paths expose the provider
    // ---------------------------------------------------------------------

    [Fact]
    public void DocxDiffProvider_IsRegisteredInBothCompositionRoots()
    {
        // The dev API host (Program.cs) can construct it.
        Assert.NotNull(_factory.Services.GetRequiredService<DocxGitDiffProvider>());

        // The shipped desktop host composes through AddSourceControlServices.
        var services = new ServiceCollection();
        services.AddSourceControlServices();
        using ServiceProvider provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetRequiredService<DocxGitDiffProvider>());
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    private static HttpClient CreateAuthorizedClient(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var tokenProvider = scope.ServiceProvider.GetRequiredService<ISessionTokenProvider>();
        HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Origin", TestOrigin);
        client.DefaultRequestHeaders.Add(SessionTokenHeader, tokenProvider.SessionToken);
        return client;
    }

    /// <summary>
    /// Real application host with the runner replaced by a recording wrapper
    /// that still executes the real runner (with the DI mutation queue), so a
    /// mutating call would be observable both in the request log and in the
    /// queue's concurrency counter.
    /// </summary>
    private WebApplicationFactory<Program> CreateRecordingFactory() =>
        _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IGitProcessRunner>();
                services.AddSingleton<IGitProcessRunner>(sp => new RecordingGitProcessRunner(
                    new GitProcessRunner(
                        sp.GetRequiredService<GitProcessRunnerOptions>(),
                        sp.GetRequiredService<GitWorktreeMutationQueue>())));
            }));

    private static byte[] BuildDocx(params string[] bodyXml) =>
        DocxTestFixtureBuilder.CreateValidDocxPackage(
            documentXml:
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                $"<w:document xmlns:w=\"{WordNamespace}\">" +
                "<w:body>" +
                string.Concat(bodyXml) +
                "<w:sectPr>" +
                "<w:pgSz w:w=\"12240\" w:h=\"15840\"/>" +
                "<w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\"/>" +
                "</w:sectPr>" +
                "</w:body></w:document>");

    private static string Paragraph(string text) =>
        $"<w:p><w:r><w:t xml:space=\"preserve\">{text}</w:t></w:r></w:p>";

    private static string Heading(int level, string text) =>
        $"<w:p><w:pPr><w:pStyle w:val=\"Heading{level}\"/></w:pPr>" +
        $"<w:r><w:t xml:space=\"preserve\">{text}</w:t></w:r></w:p>";

    private static string Row(params string[] cells) =>
        "<w:tr>" +
        string.Concat(cells.Select(cell => $"<w:tc>{Paragraph(cell)}</w:tc>")) +
        "</w:tr>";

    private static string Table(params string[] rows) =>
        "<w:tbl>" + string.Concat(rows) + "</w:tbl>";

    private static void CommitDocx(TestGitRepository repo, string relativePath, byte[] bytes)
    {
        string fullPath = FullPath(repo, relativePath);
        WriteBytes(fullPath, bytes);
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "--", relativePath));
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "commit", "-qm", $"scm19: {relativePath}"));
    }

    private static string FullPath(TestGitRepository repo, string relativePath) =>
        Path.Combine(repo.RootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static void WriteBytes(string fullPath, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllBytes(fullPath, bytes);
    }

    private static string RunGitCapture(string workingDirectory, params string[] arguments) =>
        TestGitRepository.RunGitCapture(workingDirectory, arguments);

    private static string[] ListRepositoryFiles(string root) =>
        Directory
            .EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                           && !path.EndsWith($"{Path.DirectorySeparatorChar}.git", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(root, path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

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
    private sealed record Type400Error(string? ErrorCode, string? Error);

    /// <summary>Wraps the real runner and records every invocation.</summary>
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

    /// <summary>A machine without a usable Git executable, as the runner reports it.</summary>
    private sealed class UnavailableGitProcessRunner : IGitProcessRunner
    {
        private static GitProcessResult Unavailable() =>
            new(
                GitProcessStatus.Unavailable,
                null,
                string.Empty,
                string.Empty,
                GitProcessErrorKind.GitNotInstalled,
                "Git is not installed on this machine.");

        public Task<GitCapabilityInfo> GetCapabilityAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new GitCapabilityInfo(
                GitCapabilityStatus.Unavailable,
                "git",
                Unavailable()));

        public Task<GitProcessResult> RunAsync(
            GitProcessRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Unavailable());
    }
}
