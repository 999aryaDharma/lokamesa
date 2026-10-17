using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using DocumentIDE.Core.Docx;
using DocumentIDE.Core.Indexing;
using DocumentIDE.Core.SourceControl;
using DocumentIDE.Infrastructure.Docx;
using DocumentIDE.Infrastructure.SourceControl;
using Xunit;

namespace DocumentIDE.IntegrationTests.SourceControl;

/// <summary>
/// SCM-05 — DOCX Document-Aware Git Diff.
///
/// Required first failing test: "Git versions of a DOCX can be compared without
/// mutation." The tests materialize the compared Git versions read-only
/// (HEAD/index via the SCM-01 controlled runner, the working tree from disk),
/// parse them through the existing DOCX infrastructure (IDX-01 preflight,
/// IDX-02 structural parser, BAT-08 logical run scan), and classify
/// text/table/formatting/metadata changes into a deterministic report
/// (spec 13 §9.2/§9.3) while Git staging stays file-level.
///
/// Fixtures are real DOCX packages generated in-test (QA-01 corpus precedent:
/// the generator source is the version-controlled corpus).
/// </summary>
public sealed class DocxGitDiffTests
{
    // -----------------------------------------------------------------------
    // RED — Step 1: Git versions of a DOCX can be compared without mutation.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GitVersions_OfADocx_CanBeComparedWithoutMutation()
    {
        using var repo = TestGitRepository.Create("scm05 baseline");
        const string relativePath = "docs/report.docx";
        string fullPath = FullPath(repo, relativePath);

        byte[] version1 = BuildDocx(
            P("Hello world"),
            P("Second paragraph"));
        CommitDocx(repo, relativePath, version1);

        byte[] version2 = BuildDocx(
            P("Hello brave world"),
            P("Second paragraph"),
            P("Third paragraph"));
        WriteBytes(fullPath, version2);

        using var scratch = new ScratchRoot();
        try
        {
            byte[] worktreeBefore = File.ReadAllBytes(fullPath);
            byte[] indexBefore = File.ReadAllBytes(Path.Combine(repo.RootPath, ".git", "index"));
            string statusBefore = TestGitRepository.RunGitCapture(
                repo.RootPath, "status", "--porcelain=v2", "--branch");
            var filesBefore = ListRepositoryFiles(repo.RootPath);

            var provider = NewProvider(scratch);

            DocxDiffResult result = await provider.GetDocumentDiffAsync(
                repo.RootPath, relativePath, GitDiffTarget.WorkingTree);

            Assert.Equal(DocxDiffStatus.Available, result.Status);
            DocxDiffReport report = Assert.IsType<DocxDiffReport>(result.Report);
            Assert.Equal(GitDiffTarget.WorkingTree, report.Target);
            Assert.Equal(relativePath, report.Path);
            Assert.Equal("HEAD", report.LeftLabel);
            Assert.Equal("working tree", report.RightLabel);
            Assert.True(report.LeftExists);
            Assert.True(report.RightExists);
            Assert.Null(report.BinarySummary);
            Assert.True(report.HasChanges);

            DocxChange textChange = Assert.Single(
                report.Changes, change => change.Kind == DocxChangeKind.ParagraphTextChanged);
            Assert.Equal("Hello world", textChange.Before);
            Assert.Equal("Hello brave world", textChange.After);

            DocxChange added = Assert.Single(
                report.Changes, change => change.Kind == DocxChangeKind.ParagraphAdded);
            Assert.Equal("Third paragraph", added.After);

            // No mutation: the working-tree file, the Git index, the porcelain
            // status and the repository file set are bit-identical afterwards.
            Assert.Equal(worktreeBefore, File.ReadAllBytes(fullPath));
            Assert.Equal(indexBefore, File.ReadAllBytes(Path.Combine(repo.RootPath, ".git", "index")));
            Assert.Equal(
                statusBefore,
                TestGitRepository.RunGitCapture(repo.RootPath, "status", "--porcelain=v2", "--branch"));
            Assert.Equal(filesBefore, ListRepositoryFiles(repo.RootPath));
        }
        finally
        {
            DeleteDirectoryBestEffort(scratch.Root);
        }
    }

    // -----------------------------------------------------------------------
    // Step 1 continued: read-only temp artifacts, then cleanup.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task MaterializedGitVersions_AreParsedAsReadOnlyArtifacts_AndCleanedUp()
    {
        using var repo = TestGitRepository.Create("scm05 readonly artifacts");
        const string relativePath = "docs/report.docx";
        string fullPath = FullPath(repo, relativePath);

        CommitDocx(repo, relativePath, BuildDocx(P("Hello world")));
        WriteBytes(fullPath, BuildDocx(P("Hello brave world")));

        using var scratch = new ScratchRoot();
        var preflight = new RecordingPreflight();
        var parser = new RecordingParser();
        var provider = new DocxGitDiffProvider(
            new GitProcessRunner(), parser, scratch.Root, preflight);

        DocxDiffResult result = await provider.GetDocumentDiffAsync(
            repo.RootPath, relativePath, GitDiffTarget.WorkingTree);

        Assert.Equal(DocxDiffStatus.Available, result.Status);

        // Both materialized sides were parsed from real files on disk...
        Assert.Equal(2, preflight.Observations.Count);
        Assert.Equal(2, parser.Observations.Count);
        Assert.All(preflight.Observations, observation => Assert.True(observation.Exists));
        Assert.All(parser.Observations, observation => Assert.True(observation.Exists));

        string[] artifacts = parser.Observations
            .Select(observation => observation.Path)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Assert.Equal(2, artifacts.Length);
        Assert.All(
            artifacts,
            artifact => Assert.Contains(
                artifact,
                preflight.Observations.Select(observation => observation.Path),
                StringComparer.OrdinalIgnoreCase));

        // ...under the configured temp root, and every artifact was read-only
        // while it was parsed (spec 13 §9.3: materialized versions are inert).
        string canonicalScratch = Path.GetFullPath(scratch.Root);
        Assert.All(
            artifacts,
            artifact => Assert.StartsWith(
                canonicalScratch + Path.DirectorySeparatorChar,
                Path.GetFullPath(artifact),
                StringComparison.OrdinalIgnoreCase));
        Assert.All(
            parser.Observations.Concat(preflight.Observations),
            observation => Assert.True(
                (observation.Attributes & FileAttributes.ReadOnly) != 0,
                $"Artifact '{observation.Path}' must be read-only while parsed."));

        // The per-call temp directory is removed before the call returns.
        AssertNoLeftoverArtifacts(scratch.Root);
    }

    // -----------------------------------------------------------------------
    // Step 2/3: version pairs and document-aware classification.
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(GitDiffTarget.Staged)]
    [InlineData(GitDiffTarget.Unstaged)]
    [InlineData(GitDiffTarget.WorkingTree)]
    public async Task CleanCommittedDocument_ProducesAvailableReportWithoutChanges(GitDiffTarget target)
    {
        using var repo = TestGitRepository.Create("scm05 clean document");
        const string relativePath = "docs/clean.docx";
        CommitDocx(repo, relativePath, BuildDocx(P("Only paragraph")));

        using var scratch = new ScratchRoot();
        DocxDiffResult result = await NewProvider(scratch).GetDocumentDiffAsync(
            repo.RootPath, relativePath, target);

        Assert.Equal(DocxDiffStatus.Available, result.Status);
        DocxDiffReport report = Assert.IsType<DocxDiffReport>(result.Report);
        Assert.True(report.LeftExists);
        Assert.True(report.RightExists);
        Assert.False(report.HasChanges);
        Assert.Empty(report.Changes);
    }

    [Fact]
    public async Task StagedAndUnstagedTargets_CompareTheCorrectVersionPairs()
    {
        using var repo = TestGitRepository.Create("scm05 version pairs");
        const string relativePath = "docs/three-way.docx";
        string fullPath = FullPath(repo, relativePath);

        CommitDocx(repo, relativePath, BuildDocx(P("version one")));
        WriteBytes(fullPath, BuildDocx(P("version two")));
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "--", relativePath));
        WriteBytes(fullPath, BuildDocx(P("version three")));

        using var scratch = new ScratchRoot();
        var provider = NewProvider(scratch);

        DocxDiffResult staged = await provider.GetDocumentDiffAsync(
            repo.RootPath, relativePath, GitDiffTarget.Staged);
        Assert.Equal(DocxDiffStatus.Available, staged.Status);
        Assert.Equal("HEAD", staged.Report!.LeftLabel);
        Assert.Equal("index", staged.Report.RightLabel);
        AssertSingleTextChange(staged.Report, "version one", "version two");

        DocxDiffResult unstaged = await provider.GetDocumentDiffAsync(
            repo.RootPath, relativePath, GitDiffTarget.Unstaged);
        Assert.Equal(DocxDiffStatus.Available, unstaged.Status);
        Assert.Equal("index", unstaged.Report!.LeftLabel);
        Assert.Equal("working tree", unstaged.Report.RightLabel);
        AssertSingleTextChange(unstaged.Report, "version two", "version three");

        DocxDiffResult complete = await provider.GetDocumentDiffAsync(
            repo.RootPath, relativePath, GitDiffTarget.WorkingTree);
        Assert.Equal(DocxDiffStatus.Available, complete.Status);
        Assert.Equal("HEAD", complete.Report!.LeftLabel);
        Assert.Equal("working tree", complete.Report.RightLabel);
        AssertSingleTextChange(complete.Report, "version one", "version three");
    }

    // -----------------------------------------------------------------------
    // Step 4: regression corpus — text/table/formatting fixtures produce
    // stable, exact document-aware output.
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("paragraph text changed")]
    [InlineData("paragraph added")]
    [InlineData("heading level changed")]
    [InlineData("heading text changed")]
    [InlineData("table cell changed")]
    [InlineData("formatting changed")]
    [InlineData("metadata paper changed")]
    public async Task RegressionCorpus_ProducesStableDocumentAwareOutput(string caseName)
    {
        CorpusCase corpus = CorpusCases.Single(candidate => candidate.Name == caseName);
        using var repo = TestGitRepository.Create($"scm05 corpus {caseName}");
        const string relativePath = "docs/corpus.docx";
        string fullPath = FullPath(repo, relativePath);

        CommitDocx(repo, relativePath, BuildDocxWithSection(corpus.BeforeSection, corpus.Before));
        WriteBytes(fullPath, BuildDocxWithSection(corpus.AfterSection, corpus.After));

        using var scratch = new ScratchRoot();
        DocxDiffResult result = await NewProvider(scratch).GetDocumentDiffAsync(
            repo.RootPath, relativePath, GitDiffTarget.WorkingTree);

        Assert.Equal(DocxDiffStatus.Available, result.Status);
        DocxDiffReport report = Assert.IsType<DocxDiffReport>(result.Report);
        ExpectedChange[] actual = report.Changes
            .Select(change => new ExpectedChange(change.Kind, change.Before, change.After, change.Property))
            .ToArray();
        Assert.Equal(corpus.Expected, actual);
    }

    // -----------------------------------------------------------------------
    // Required test: split-run text changes remain understandable.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SplitRunTextChange_ProducesOneUnderstandableParagraphChange()
    {
        using var repo = TestGitRepository.Create("scm05 split runs");
        const string relativePath = "docs/split.docx";
        string fullPath = FullPath(repo, relativePath);

        CommitDocx(repo, relativePath, BuildDocx(Runs("Hello ", "world")));
        WriteBytes(fullPath, BuildDocx(Runs("Hello ", "brave ", "world")));

        using var scratch = new ScratchRoot();
        DocxDiffResult result = await NewProvider(scratch).GetDocumentDiffAsync(
            repo.RootPath, relativePath, GitDiffTarget.WorkingTree);

        Assert.Equal(DocxDiffStatus.Available, result.Status);
        DocxChange change = Assert.Single(result.Report!.Changes);
        Assert.Equal(DocxChangeKind.ParagraphTextChanged, change.Kind);
        Assert.Equal("Hello world", change.Before);
        Assert.Equal("Hello brave world", change.After);
    }

    [Fact]
    public async Task RunSplitMergeWithoutTextChange_ProducesNoChanges()
    {
        using var repo = TestGitRepository.Create("scm05 run merge");
        const string relativePath = "docs/merge-runs.docx";
        string fullPath = FullPath(repo, relativePath);

        CommitDocx(repo, relativePath, BuildDocx(Runs("Hello ", "brave ", "world")));
        WriteBytes(fullPath, BuildDocx(P("Hello brave world")));

        using var scratch = new ScratchRoot();
        DocxDiffResult result = await NewProvider(scratch).GetDocumentDiffAsync(
            repo.RootPath, relativePath, GitDiffTarget.WorkingTree);

        Assert.Equal(DocxDiffStatus.Available, result.Status);
        Assert.Empty(result.Report!.Changes);
    }

    // -----------------------------------------------------------------------
    // Added / deleted documents (a missing side is valid, not an error).
    // -----------------------------------------------------------------------

    [Fact]
    public async Task AddedDocument_InUnbornRepository_ReportsCompleteAddition()
    {
        using var repo = TestGitRepository.Create("scm05 unborn repository");
        const string relativePath = "docs/added.docx";
        string fullPath = FullPath(repo, relativePath);

        WriteBytes(fullPath, BuildDocx(P("First"), P("Second")));
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "--", relativePath));

        using var scratch = new ScratchRoot();
        DocxDiffResult result = await NewProvider(scratch).GetDocumentDiffAsync(
            repo.RootPath, relativePath, GitDiffTarget.Staged);

        Assert.Equal(DocxDiffStatus.Available, result.Status);
        DocxDiffReport report = result.Report!;
        Assert.False(report.LeftExists);
        Assert.True(report.RightExists);
        Assert.Equal(2, report.Changes.Count);
        Assert.All(report.Changes, change => Assert.Equal(DocxChangeKind.ParagraphAdded, change.Kind));
    }

    [Fact]
    public async Task DeletedDocument_ReportsCompleteRemoval()
    {
        using var repo = TestGitRepository.Create("scm05 deleted document");
        const string relativePath = "docs/deleted.docx";
        string fullPath = FullPath(repo, relativePath);

        CommitDocx(repo, relativePath, BuildDocx(P("First"), P("Second")));
        File.Delete(fullPath);

        using var scratch = new ScratchRoot();
        DocxDiffResult result = await NewProvider(scratch).GetDocumentDiffAsync(
            repo.RootPath, relativePath, GitDiffTarget.WorkingTree);

        Assert.Equal(DocxDiffStatus.Available, result.Status);
        DocxDiffReport report = result.Report!;
        Assert.True(report.LeftExists);
        Assert.False(report.RightExists);
        Assert.Equal(2, report.Changes.Count);
        Assert.All(report.Changes, change => Assert.Equal(DocxChangeKind.ParagraphRemoved, change.Kind));
    }

    // -----------------------------------------------------------------------
    // Safe binary-change fallback (spec 13 §9.3 / acceptance criteria).
    // -----------------------------------------------------------------------

    [Fact]
    public async Task NonDocxContent_ProducesBinaryFallbackWithDeterministicDigestSummary()
    {
        using var repo = TestGitRepository.Create("scm05 binary fallback");
        const string relativePath = "docs/report.docx";
        string fullPath = FullPath(repo, relativePath);

        byte[] committed = BuildDocx(P("Hello"));
        CommitDocx(repo, relativePath, committed);
        byte[] broken = Encoding.ASCII.GetBytes("this is not a docx package at all");
        WriteBytes(fullPath, broken);

        using var scratch = new ScratchRoot();
        DocxDiffResult result = await NewProvider(scratch).GetDocumentDiffAsync(
            repo.RootPath, relativePath, GitDiffTarget.WorkingTree);

        Assert.Equal(DocxDiffStatus.BinaryFallback, result.Status);
        Assert.True(result.IsBinaryFallback);
        DocxDiffReport report = result.Report!;
        Assert.Empty(report.Changes);
        DocxBinarySummary summary = Assert.IsType<DocxBinarySummary>(report.BinarySummary);
        Assert.True(summary.LeftExists);
        Assert.True(summary.RightExists);
        Assert.Equal(committed.LongLength, summary.LeftSizeBytes);
        Assert.Equal(broken.LongLength, summary.RightSizeBytes);
        Assert.Equal(Sha256(committed), summary.LeftSha256);
        Assert.Equal(Sha256(broken), summary.RightSha256);
        Assert.False(summary.ContentIdentical);
        Assert.NotNull(result.Failure);
    }

    // -----------------------------------------------------------------------
    // Typed failures instead of exceptions (spec 13 §20).
    // -----------------------------------------------------------------------

    [Fact]
    public async Task MissingGitExecutable_YieldsTypedGitUnavailableState()
    {
        using var repo = TestGitRepository.Create("scm05 missing git");
        const string relativePath = "docs/report.docx";
        CommitDocx(repo, relativePath, BuildDocx(P("Hello")));

        var options = new GitProcessRunnerOptions
        {
            ExecutablePath = "scm05-definitely-missing-git-executable",
        };
        using var scratch = new ScratchRoot();
        var provider = new DocxGitDiffProvider(new GitProcessRunner(options), tempRoot: scratch.Root);

        DocxDiffResult result = await provider.GetDocumentDiffAsync(
            repo.RootPath, relativePath, GitDiffTarget.WorkingTree);

        Assert.Equal(DocxDiffStatus.GitUnavailable, result.Status);
        Assert.Null(result.Report);
        Assert.NotNull(result.Failure);
        Assert.Equal(GitProcessErrorKind.GitNotInstalled, result.Failure!.ErrorKind);
    }

    [Fact]
    public async Task DisappearedWorktree_YieldsTypedFailure_InsteadOfException()
    {
        string missing = Path.Combine(Path.GetTempPath(), "scm05", $"missing-{Guid.NewGuid():N}");

        using var scratch = new ScratchRoot();
        DocxDiffResult result = await NewProvider(scratch).GetDocumentDiffAsync(
            missing, "docs/report.docx", GitDiffTarget.WorkingTree);

        Assert.Equal(DocxDiffStatus.Failed, result.Status);
        Assert.Null(result.Report);
        Assert.NotNull(result.Failure);
        Assert.Equal(GitProcessErrorKind.GitWorktreeInvalid, result.Failure!.ErrorKind);
    }

    [Fact]
    public async Task UnicodeAndSpacePaths_AreCompared()
    {
        using var repo = TestGitRepository.Create("scm05 \u00fcn\u00efcode sp\u00e4ces");
        const string relativePath = "docs/\u00fcber w\u00f6rter.docx";
        string fullPath = FullPath(repo, relativePath);

        CommitDocx(repo, relativePath, BuildDocx(P("\u00dcber eins")));
        WriteBytes(fullPath, BuildDocx(P("\u00dcber zwei")));

        using var scratch = new ScratchRoot();
        DocxDiffResult result = await NewProvider(scratch).GetDocumentDiffAsync(
            repo.RootPath, relativePath, GitDiffTarget.WorkingTree);

        Assert.Equal(DocxDiffStatus.Available, result.Status);
        AssertSingleTextChange(result.Report!, "\u00dcber eins", "\u00dcber zwei");
    }

    [Theory]
    [InlineData("../outside.docx")]
    [InlineData("..\\outside.docx")]
    [InlineData("docs/../secret.docx")]
    [InlineData("/etc/passwd")]
    [InlineData("C:/Windows/win.ini")]
    [InlineData("C:\\Windows\\win.ini")]
    [InlineData("docs//report.docx")]
    [InlineData("docs/report.docx:stream")]
    public async Task EscapingOrRootedPaths_AreRejected_BeforeAnyGitOrFileAccess(string attack)
    {
        using var repo = TestGitRepository.Create("scm05 rejected paths");
        const string relativePath = "docs/report.docx";
        CommitDocx(repo, relativePath, BuildDocx(P("Hello")));

        using var scratch = new ScratchRoot();
        DocxDiffResult result = await NewProvider(scratch).GetDocumentDiffAsync(
            repo.RootPath, attack, GitDiffTarget.WorkingTree);

        Assert.Equal(DocxDiffStatus.PathRejected, result.Status);
        Assert.Null(result.Report);
        Assert.NotNull(result.Failure);
        Assert.False(string.IsNullOrWhiteSpace(result.Failure!.ErrorMessage));
    }

    [Fact]
    public async Task EscapingOriginalPath_IsRejected_BeforeAnyGitOrFileAccess()
    {
        using var repo = TestGitRepository.Create("scm05 rejected original path");
        const string relativePath = "docs/report.docx";
        CommitDocx(repo, relativePath, BuildDocx(P("Hello")));

        using var scratch = new ScratchRoot();
        DocxDiffResult result = await NewProvider(scratch).GetDocumentDiffAsync(
            repo.RootPath, relativePath, GitDiffTarget.Staged, originalPath: "..\\outside.docx");

        Assert.Equal(DocxDiffStatus.PathRejected, result.Status);
        Assert.Null(result.Report);
        Assert.NotNull(result.Failure);
    }

    [Fact]
    public async Task Diff_IsReadOnly_AndRemainsAvailableWhileAnIndexLockFileExists()
    {
        using var repo = TestGitRepository.Create("scm05 index lock");
        const string relativePath = "docs/report.docx";
        string fullPath = FullPath(repo, relativePath);

        CommitDocx(repo, relativePath, BuildDocx(P("version one")));
        WriteBytes(fullPath, BuildDocx(P("version two")));
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "--", relativePath));
        WriteBytes(fullPath, BuildDocx(P("version three")));

        string indexFile = Path.Combine(repo.RootPath, ".git", "index");
        byte[] indexBefore = File.ReadAllBytes(indexFile);

        // A foreign Git process holds the index lock: the read-only document
        // diff must not need it (spec 13 §18 `--no-optional-locks`).
        string indexLock = indexFile + ".lock";
        File.WriteAllText(indexLock, "held by another git process");

        try
        {
            using var scratch = new ScratchRoot();
            var provider = NewProvider(scratch);

            DocxDiffResult staged = await provider.GetDocumentDiffAsync(
                repo.RootPath, relativePath, GitDiffTarget.Staged);
            Assert.Equal(DocxDiffStatus.Available, staged.Status);
            AssertSingleTextChange(staged.Report!, "version one", "version two");

            DocxDiffResult worktree = await provider.GetDocumentDiffAsync(
                repo.RootPath, relativePath, GitDiffTarget.WorkingTree);
            Assert.Equal(DocxDiffStatus.Available, worktree.Status);
            AssertSingleTextChange(worktree.Report!, "version one", "version three");

            // The foreign lock is untouched...
            Assert.True(File.Exists(indexLock));
        }
        finally
        {
            File.Delete(indexLock);
        }

        // ...and not one byte of the index changed.
        Assert.Equal(indexBefore, File.ReadAllBytes(indexFile));
    }

    [Fact]
    public async Task UnmergedFile_StagedDiff_YieldsTypedConflictFailure()
    {
        using var repo = TestGitRepository.Create("scm05 conflict");
        const string relativePath = "docs/conflicted.docx";
        string fullPath = FullPath(repo, relativePath);

        CommitDocx(repo, relativePath, BuildDocx(P("base")));
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "checkout", "-q", "-b", "side"));
        WriteBytes(fullPath, BuildDocx(P("side change")));
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "--", relativePath));
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "commit", "-qm", "side"));
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "checkout", "-q", "-"));
        WriteBytes(fullPath, BuildDocx(P("main change")));
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "--", relativePath));
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "commit", "-qm", "main"));

        // A conflicted merge exits non-zero by design; that is the fixture state.
        Assert.NotEqual(0, TestGitRepository.RunGit(repo.RootPath, "merge", "side"));

        using var scratch = new ScratchRoot();
        var provider = NewProvider(scratch);

        DocxDiffResult staged = await provider.GetDocumentDiffAsync(
            repo.RootPath, relativePath, GitDiffTarget.Staged);

        Assert.Equal(DocxDiffStatus.Failed, staged.Status);
        Assert.Null(staged.Report);
        Assert.NotNull(staged.Failure);
        Assert.Equal(GitProcessErrorKind.GitConflict, staged.Failure!.ErrorKind);
        Assert.Contains("unmerged", staged.Failure.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        // Spec 13 §21: versions that are available stay inspectable.
        DocxDiffResult workingTree = await provider.GetDocumentDiffAsync(
            repo.RootPath, relativePath, GitDiffTarget.WorkingTree);
        Assert.Equal(DocxDiffStatus.Available, workingTree.Status);
    }

    [Fact]
    public async Task RenamedFile_StagedDiff_UsesOriginalPathForTheHeadSide()
    {
        using var repo = TestGitRepository.Create("scm05 rename");
        const string oldPath = "docs/old.docx";
        const string newPath = "docs/new.docx";
        string newFullPath = FullPath(repo, newPath);

        CommitDocx(repo, oldPath, BuildDocx(P("version one")));
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "mv", "--", oldPath, newPath));
        WriteBytes(newFullPath, BuildDocx(P("version two")));
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "--", newPath));

        using var scratch = new ScratchRoot();
        var provider = NewProvider(scratch);

        // Without the original path the new path simply does not exist in HEAD.
        DocxDiffResult withoutOriginal = await provider.GetDocumentDiffAsync(
            repo.RootPath, newPath, GitDiffTarget.Staged);
        Assert.Equal(DocxDiffStatus.Available, withoutOriginal.Status);
        Assert.False(withoutOriginal.Report!.LeftExists);
        Assert.All(
            withoutOriginal.Report.Changes,
            change => Assert.Equal(DocxChangeKind.ParagraphAdded, change.Kind));

        // With the original path from Git status, only the content change shows.
        DocxDiffResult withOriginal = await provider.GetDocumentDiffAsync(
            repo.RootPath, newPath, GitDiffTarget.Staged, originalPath: oldPath);
        Assert.Equal(DocxDiffStatus.Available, withOriginal.Status);
        Assert.True(withOriginal.Report!.LeftExists);
        AssertSingleTextChange(withOriginal.Report, "version one", "version two");
    }

    // -----------------------------------------------------------------------
    // Regression corpus definition
    // -----------------------------------------------------------------------

    private static readonly CorpusCase[] CorpusCases =
    {
        new(
            "paragraph text changed",
            new[] { P("Hello world") },
            new[] { P("Hello brave world") },
            new[] { new ExpectedChange(DocxChangeKind.ParagraphTextChanged, "Hello world", "Hello brave world", null) }),
        new(
            "paragraph added",
            new[] { P("One") },
            new[] { P("One"), P("Two") },
            new[] { new ExpectedChange(DocxChangeKind.ParagraphAdded, null, "Two", null) }),
        new(
            "heading level changed",
            new[] { Heading(1, "Section"), P("Body") },
            new[] { Heading(2, "Section"), P("Body") },
            new[] { new ExpectedChange(DocxChangeKind.HeadingChanged, "1", "2", "HeadingLevel") }),
        new(
            "heading text changed",
            new[] { Heading(1, "Old title") },
            new[] { Heading(1, "New title") },
            new[] { new ExpectedChange(DocxChangeKind.HeadingChanged, "Old title", "New title", null) }),
        new(
            "table cell changed",
            new[] { P("Body"), Table(new[] { "Alpha" }) },
            new[] { P("Body"), Table(new[] { "Beta" }) },
            new[] { new ExpectedChange(DocxChangeKind.TableCellChanged, "Alpha", "Beta", null) }),
        new(
            "formatting changed",
            new[] { P("Styled text") },
            new[] { Formatted("Styled text", bold: true) },
            new[] { new ExpectedChange(DocxChangeKind.FormattingChanged, "inherit", "on", "Bold") }),
        new(
            "metadata paper changed",
            new[] { P("Body") },
            new[] { P("Body") },
            new[]
            {
                new ExpectedChange(DocxChangeKind.MetadataChanged, "none", "Letter", "PaperSummary"),
                new ExpectedChange(DocxChangeKind.MetadataChanged, "none", "Portrait", "OrientationSummary"),
            },
            BeforeSection: "<w:sectPr/>",
            AfterSection: "<w:sectPr><w:pgSz w:w=\"12240\" w:h=\"15840\"/></w:sectPr>"),
    };

    /// <summary>One corpus case: fixture bodies plus the exact expected change list.</summary>
    private sealed record CorpusCase(
        string Name,
        string[] Before,
        string[] After,
        ExpectedChange[] Expected,
        string BeforeSection = "<w:sectPr/>",
        string AfterSection = "<w:sectPr/>");

    /// <summary>One expected document-aware change, mirrored from <see cref="DocxChange"/>.</summary>
    private sealed record ExpectedChange(
        DocxChangeKind Kind,
        string? Before,
        string? After,
        string? Property);

    // -----------------------------------------------------------------------
    // Fixture helpers
    // -----------------------------------------------------------------------

    private const string WordNamespace =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static byte[] BuildDocx(params string[] bodyXml) =>
        BuildDocxWithSection("<w:sectPr/>", bodyXml);

    private static byte[] BuildDocxWithSection(string sectionProperties, params string[] bodyXml)
    {
        using var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(
                archive,
                "[Content_Types].xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>" +
                "</Types>");

            WriteEntry(
                archive,
                "_rels/.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/>" +
                "</Relationships>");

            WriteEntry(
                archive,
                "word/document.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                $"<w:document xmlns:w=\"{WordNamespace}\">" +
                "<w:body>" +
                string.Concat(bodyXml) +
                sectionProperties +
                "</w:body></w:document>");
        }

        return memoryStream.ToArray();
    }

    /// <summary>One plain paragraph.</summary>
    private static string P(string text) => $"<w:p>{PlainRun(text)}</w:p>";

    /// <summary>One paragraph whose text is split across the given runs (no run formatting).</summary>
    private static string Runs(params string[] texts) =>
        $"<w:p>{string.Concat(texts.Select(PlainRun))}</w:p>";

    /// <summary>One heading paragraph (HeadingN style).</summary>
    private static string Heading(int level, string text) =>
        $"<w:p><w:pPr><w:pStyle w:val=\"Heading{level}\"/></w:pPr>{PlainRun(text)}</w:p>";

    /// <summary>One paragraph with a single formatted run.</summary>
    private static string Formatted(
        string text,
        string? font = null,
        int? sizeHalfPoints = null,
        bool? bold = null,
        bool? italic = null,
        string? underline = null) =>
        $"<w:p>{FormattedRun(text, font, sizeHalfPoints, bold, italic, underline)}</w:p>";

    /// <summary>One simple table: rows of plain-text cells.</summary>
    private static string Table(params string[][] rows) =>
        "<w:tbl>" +
        string.Concat(rows.Select(row =>
            "<w:tr>" +
            string.Concat(row.Select(cell => $"<w:tc>{P(cell)}</w:tc>")) +
            "</w:tr>")) +
        "</w:tbl>";

    private static string PlainRun(string text) => $"<w:r><w:t xml:space=\"preserve\">{text}</w:t></w:r>";

    private static string FormattedRun(
        string text,
        string? font,
        int? sizeHalfPoints,
        bool? bold,
        bool? italic,
        string? underline)
    {
        var properties = new StringBuilder();
        if (font is not null)
        {
            properties.Append($"<w:rFonts w:ascii=\"{font}\" w:hAnsi=\"{font}\"/>");
        }

        if (sizeHalfPoints is int halfPoints)
        {
            properties.Append($"<w:sz w:val=\"{halfPoints}\"/>");
        }

        if (bold is bool boldValue)
        {
            properties.Append($"<w:b w:val=\"{(boldValue ? 1 : 0)}\"/>");
        }

        if (italic is bool italicValue)
        {
            properties.Append($"<w:i w:val=\"{(italicValue ? 1 : 0)}\"/>");
        }

        if (underline is not null)
        {
            properties.Append($"<w:u w:val=\"{underline}\"/>");
        }

        string runProperties = properties.Length == 0 ? string.Empty : $"<w:rPr>{properties}</w:rPr>";
        return $"<w:r>{runProperties}<w:t xml:space=\"preserve\">{text}</w:t></w:r>";
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }

    // -----------------------------------------------------------------------
    // Git / filesystem helpers
    // -----------------------------------------------------------------------

    private static DocxGitDiffProvider NewProvider(ScratchRoot scratch) =>
        new(new GitProcessRunner(), tempRoot: scratch.Root);

    private static void CommitDocx(TestGitRepository repo, string relativePath, byte[] bytes)
    {
        string fullPath = FullPath(repo, relativePath);
        WriteBytes(fullPath, bytes);
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "add", "--", relativePath));
        Assert.Equal(0, TestGitRepository.RunGit(repo.RootPath, "commit", "-qm", $"scm05: {relativePath}"));
    }

    private static string FullPath(TestGitRepository repo, string relativePath) =>
        Path.Combine(repo.RootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static void AssertSingleTextChange(DocxDiffReport report, string before, string after)
    {
        DocxChange change = Assert.Single(
            report.Changes, candidate => candidate.Kind == DocxChangeKind.ParagraphTextChanged);
        Assert.Equal(before, change.Before);
        Assert.Equal(after, change.After);
    }

    private static string Sha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string NewTempRoot() =>
        Path.Combine(Path.GetTempPath(), "scm05-tests", $"run-{Guid.NewGuid():N}");

    private static void WriteBytes(string fullPath, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllBytes(fullPath, bytes);
    }

    private static string[] ListRepositoryFiles(string root) =>
        Directory
            .EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                           && !path.EndsWith($"{Path.DirectorySeparatorChar}.git", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(root, path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

    private static void AssertNoLeftoverArtifacts(string tempRoot) =>
        Assert.True(
            !Directory.Exists(tempRoot) || !Directory.EnumerateFileSystemEntries(tempRoot).Any(),
            $"Temp root '{tempRoot}' must not retain SCM-05 artifacts.");

    private static void DeleteDirectoryBestEffort(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }

                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception)
        {
            // Best effort: a locked temp file must not fail the test run.
        }
    }

    /// <summary>Isolated per-test temp root, removed best-effort on dispose.</summary>
    private sealed class ScratchRoot : IDisposable
    {
        public ScratchRoot() => Root = NewTempRoot();

        public string Root { get; }

        public void Dispose() => DeleteDirectoryBestEffort(Root);
    }

    /// <summary>
    /// Observing preflight shim: records the exact artifact state at the moment
    /// the provider hands the materialized version to the real IDX-01 preflight.
    /// </summary>
    private sealed class RecordingPreflight : IDocxPackagePreflight
    {
        private readonly DocxPackagePreflight _inner = new();

        public List<(string Path, bool Exists, FileAttributes Attributes)> Observations { get; } = new();

        public DocxPreflightResult ValidatePackage(string filePath, DocxPreflightOptions? options = null)
        {
            Observations.Add(Observe(filePath));
            return _inner.ValidatePackage(filePath, options);
        }

        public DocxPreflightResult ValidatePackage(Stream stream, DocxPreflightOptions? options = null) =>
            _inner.ValidatePackage(stream, options);
    }

    /// <summary>Observing parser shim around the real IDX-02 structural parser.</summary>
    private sealed class RecordingParser : IDocxStructureParser
    {
        private readonly DocxStructureParser _inner = new();

        public List<(string Path, bool Exists, FileAttributes Attributes)> Observations { get; } = new();

        public DocxParseResult Parse(string filePath)
        {
            Observations.Add(Observe(filePath));
            return _inner.Parse(filePath);
        }
    }

    private static (string Path, bool Exists, FileAttributes Attributes) Observe(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return (filePath, false, default);
        }

        return (filePath, true, File.GetAttributes(filePath));
    }
}
