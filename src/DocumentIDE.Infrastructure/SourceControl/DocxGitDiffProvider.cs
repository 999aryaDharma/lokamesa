using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using DocumentIDE.Core.Batch.Operations;
using DocumentIDE.Core.Docx;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Indexing;
using DocumentIDE.Core.SourceControl;
using DocumentIDE.Infrastructure.Docx;

namespace DocumentIDE.Infrastructure.SourceControl;

/// <summary>
/// SCM-05 — DOCX document-aware Git diff provider (spec 13 §9.2/§9.3).
///
/// The provider materializes the compared Git versions (HEAD via
/// <c>git show HEAD:&lt;path&gt;</c>, the index via <c>git show :&lt;path&gt;</c>,
/// the working tree from disk) and writes the present sides into a dedicated
/// per-call temporary directory as <b>read-only</b> artifacts; the existing
/// DOCX infrastructure (IDX-01 preflight, IDX-02 structural parser, BAT-08
/// logical run scan) then parses those artifacts, and the pure
/// <see cref="DocxDocumentComparer"/> classifies text/table/formatting/metadata
/// changes. It is strictly read-only for the repository:
/// <list type="bullet">
/// <item>the working-tree file is never written — only read,</item>
/// <item>every Git invocation goes through the SCM-01 controlled process
/// boundary (structured arguments, no shell) and never needs the index lock,</item>
/// <item>temporary artifacts live only under the provider's temp root and are
/// deleted afterwards,</item>
/// <item>the requested path is validated as a worktree-relative document path
/// before any Git or filesystem access,</item>
/// <item>a version that cannot be parsed as a DOCX degrades to an explicit
/// <see cref="DocxDiffStatus.BinaryFallback"/> binary-change summary — never a
/// fake semantic diff (spec 13 §9.3).</item>
/// </list>
///
/// Git staging remains file-level; this provider never stages or writes
/// anything (spec 13 §9.2).
/// </summary>
public sealed class DocxGitDiffProvider
{
    private static readonly string[] HeadProbeArguments =
    {
        "rev-parse", "--verify", "--quiet", "HEAD",
    };

    private const string MaterializedHeadFileName = "left.docx";
    private const string MaterializedRightFileName = "right.docx";

    private readonly IGitProcessRunner _runner;
    private readonly IDocxStructureParser _structureParser;
    private readonly IDocxPackagePreflight _preflight;
    private readonly string _tempRoot;

    /// <param name="runner">SCM-01 controlled Git process boundary.</param>
    /// <param name="structureParser">IDX-02 structural parser; defaults to <see cref="DocxStructureParser"/>.</param>
    /// <param name="tempRoot">Optional temporary root (tests isolate it); defaults to the system temp directory.</param>
    /// <param name="preflight">IDX-01 package preflight; defaults to <see cref="DocxPackagePreflight"/>.</param>
    public DocxGitDiffProvider(
        IGitProcessRunner runner,
        IDocxStructureParser? structureParser = null,
        string? tempRoot = null,
        IDocxPackagePreflight? preflight = null)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _structureParser = structureParser ?? new DocxStructureParser();
        _preflight = preflight ?? new DocxPackagePreflight();
        _tempRoot = string.IsNullOrWhiteSpace(tempRoot)
            ? Path.Combine(Path.GetTempPath(), "documentide", "docx-git-diff")
            : tempRoot;
    }

    /// <summary>
    /// Produces the read-only document-aware comparison of one
    /// worktree-relative DOCX file for the requested version pair.
    /// </summary>
    /// <param name="worktreeRoot">
    /// The true worktree root (e.g. <see cref="GitRepositoryContext.WorktreeRoot"/>).
    /// </param>
    /// <param name="relativePath">
    /// Worktree-root-relative path exactly as Git reported it
    /// (<see cref="GitStatusEntry.Path"/>); treated as untrusted input and
    /// rejected when absolute or escaping the worktree.
    /// </param>
    /// <param name="target">Which version pair to compare.</param>
    /// <param name="originalPath">
    /// Optional pre-rename path from Git status (staged rename/copy), used to
    /// materialize the correct HEAD-side base.
    /// </param>
    /// <param name="cancellationToken">Cancels the Git invocations (typed, never thrown).</param>
    public async Task<DocxDiffResult> GetDocumentDiffAsync(
        string worktreeRoot,
        string relativePath,
        GitDiffTarget target,
        string? originalPath = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(worktreeRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        if (!Enum.IsDefined(target))
        {
            throw new ArgumentOutOfRangeException(nameof(target), target, "Unknown diff target.");
        }

        if (!GitDocumentPath.TryCanonicalizeRoot(worktreeRoot, out string canonicalRoot, out string? rootError))
        {
            return Failure(
                DocxDiffStatus.Failed,
                LocalFailure(
                    GitProcessErrorKind.GitWorktreeInvalid,
                    $"Path '{worktreeRoot}' is not a usable filesystem path: {rootError}"));
        }

        if (!Directory.Exists(canonicalRoot))
        {
            // Spec 13 §20: a worktree that disappeared during an operation is a
            // typed failure, not an empty or guessed report.
            return Failure(
                DocxDiffStatus.Failed,
                LocalFailure(
                    GitProcessErrorKind.GitWorktreeInvalid,
                    $"The worktree directory '{canonicalRoot}' does not exist any more."));
        }

        if (!GitDocumentPath.TryNormalizeRelativePath(relativePath, out string gitPath)
            || !GitDocumentPath.TryResolveContainedPath(canonicalRoot, gitPath, out string fullPath))
        {
            return Failure(
                DocxDiffStatus.PathRejected,
                LocalFailure(
                    GitProcessErrorKind.GitWorktreeInvalid,
                    $"The path '{relativePath}' is not a worktree-relative document path; " +
                    "the request was rejected before any Git or filesystem access."));
        }

        string? originalGitPath = null;
        if (!string.IsNullOrWhiteSpace(originalPath))
        {
            if (!GitDocumentPath.TryNormalizeRelativePath(originalPath, out originalGitPath)
                || !GitDocumentPath.TryResolveContainedPath(canonicalRoot, originalGitPath, out _))
            {
                return Failure(
                    DocxDiffStatus.PathRejected,
                    LocalFailure(
                        GitProcessErrorKind.GitWorktreeInvalid,
                        $"The original path '{originalPath}' is not a worktree-relative document path; " +
                        "the request was rejected before any Git or filesystem access."));
            }
        }

        bool headRequired = target is GitDiffTarget.Staged or GitDiffTarget.WorkingTree;
        bool headExists = false;
        if (headRequired)
        {
            (headExists, GitProcessResult? headProbeFailure) =
                await ProbeHeadAsync(canonicalRoot, cancellationToken).ConfigureAwait(false);
            if (headProbeFailure is not null)
            {
                return Failure(MapFailure(headProbeFailure), headProbeFailure);
            }
        }

        (string leftLabel, string rightLabel) = LabelsFor(target);

        VersionRead left;
        VersionRead right;
        switch (target)
        {
            case GitDiffTarget.Staged:
                left = await ReadHeadVersionAsync(canonicalRoot, gitPath, originalGitPath, headExists, cancellationToken)
                    .ConfigureAwait(false);
                right = await ReadGitVersionAsync(canonicalRoot, $":{gitPath}", indexPath: true, cancellationToken)
                    .ConfigureAwait(false);
                break;

            case GitDiffTarget.WorkingTree:
                left = await ReadHeadVersionAsync(canonicalRoot, gitPath, originalGitPath, headExists, cancellationToken)
                    .ConfigureAwait(false);
                right = ReadWorktreeVersion(fullPath);
                break;

            default:
                left = await ReadGitVersionAsync(canonicalRoot, $":{gitPath}", indexPath: true, cancellationToken)
                    .ConfigureAwait(false);
                right = ReadWorktreeVersion(fullPath);
                break;
        }

        if (left.Failure is not null)
        {
            return Failure(MapFailure(left.Failure), left.Failure);
        }

        if (right.Failure is not null)
        {
            return Failure(MapFailure(right.Failure), right.Failure);
        }

        var binarySummary = new DocxBinarySummary(
            left.Exists,
            right.Exists,
            left.Exists ? left.Bytes.LongLength : 0,
            right.Exists ? right.Bytes.LongLength : 0,
            left.Exists ? ComputeSha256(left.Bytes) : null,
            right.Exists ? ComputeSha256(right.Bytes) : null);

        string? tempDirectory = null;
        try
        {
            if (left.Exists || right.Exists)
            {
                tempDirectory = Path.Combine(_tempRoot, $"scm05-{Guid.NewGuid():N}");
                Directory.CreateDirectory(tempDirectory);
            }

            DocxDocumentSnapshot? leftSnapshot = null;
            DocxDocumentSnapshot? rightSnapshot = null;
            GitProcessResult? parseFailure = null;

            if (left.Exists)
            {
                string artifact = WriteReadOnlyArtifact(tempDirectory!, MaterializedHeadFileName, left.Bytes);
                TryParseSnapshot(artifact, out leftSnapshot, out parseFailure);
            }

            if (parseFailure is null && right.Exists)
            {
                string artifact = WriteReadOnlyArtifact(tempDirectory!, MaterializedRightFileName, right.Bytes);
                TryParseSnapshot(artifact, out rightSnapshot, out parseFailure);
            }

            if (parseFailure is not null)
            {
                // Spec 13 §9.3: cannot be meaningfully diffed as a document —
                // report a binary-change summary with the typed parse failure
                // instead of pretending a semantic diff exists.
                return new DocxDiffResult(
                    DocxDiffStatus.BinaryFallback,
                    new DocxDiffReport(
                        target,
                        gitPath,
                        canonicalRoot,
                        leftLabel,
                        rightLabel,
                        left.Exists,
                        right.Exists,
                        Array.Empty<DocxChange>(),
                        binarySummary),
                    parseFailure);
            }

            IReadOnlyList<DocxChange> changes;
            try
            {
                changes = DocxDocumentComparer.Compare(leftSnapshot, rightSnapshot);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // The comparison itself must never surface an untyped exception;
                // an unexpected comparator failure is reported as a binary
                // change with the failure preserved for inspection.
                return new DocxDiffResult(
                    DocxDiffStatus.BinaryFallback,
                    new DocxDiffReport(
                        target,
                        gitPath,
                        canonicalRoot,
                        leftLabel,
                        rightLabel,
                        left.Exists,
                        right.Exists,
                        Array.Empty<DocxChange>(),
                        binarySummary),
                    LocalFailure(
                        GitProcessErrorKind.Unknown,
                        $"The document-aware comparison failed: {exception.Message}"));
            }

            return new DocxDiffResult(
                DocxDiffStatus.Available,
                new DocxDiffReport(
                    target,
                    gitPath,
                    canonicalRoot,
                    leftLabel,
                    rightLabel,
                    left.Exists,
                    right.Exists,
                    changes,
                    BinarySummary: null));
        }
        finally
        {
            if (tempDirectory is not null)
            {
                DeleteTempDirectoryBestEffort(tempDirectory);
            }
        }
    }

    private static (string Left, string Right) LabelsFor(GitDiffTarget target) => target switch
    {
        GitDiffTarget.Staged => ("HEAD", "index"),
        GitDiffTarget.WorkingTree => ("HEAD", "working tree"),
        GitDiffTarget.Unstaged => ("index", "working tree"),
        _ => throw new ArgumentOutOfRangeException(nameof(target), target, "Unknown diff target."),
    };

    /// <summary>
    /// <c>rev-parse --verify --quiet HEAD</c>: distinguishes "repository with
    /// a born HEAD" from an unborn branch (exit 1, silent) and from real
    /// failures. An unborn repository treats the HEAD side as absent, which is
    /// the correct typed behavior instead of an error.
    /// </summary>
    private async Task<(bool Exists, GitProcessResult? Failure)> ProbeHeadAsync(
        string canonicalRoot,
        CancellationToken cancellationToken)
    {
        GitProcessResult probe = await _runner
            .RunAsync(new GitProcessRequest(HeadProbeArguments, canonicalRoot), cancellationToken)
            .ConfigureAwait(false);

        if (probe.IsSuccess)
        {
            return (true, null);
        }

        if (probe.Status == GitProcessStatus.Failed
            && probe.ExitCode == 1
            && string.IsNullOrWhiteSpace(probe.StandardError))
        {
            return (false, null);
        }

        return (false, probe);
    }

    private async Task<VersionRead> ReadHeadVersionAsync(
        string canonicalRoot,
        string gitPath,
        string? originalGitPath,
        bool headExists,
        CancellationToken cancellationToken)
    {
        if (!headExists)
        {
            return VersionRead.Absent;
        }

        VersionRead primary = await ReadGitVersionAsync(
                canonicalRoot, $"HEAD:{gitPath}", indexPath: false, cancellationToken)
            .ConfigureAwait(false);
        if (primary.Outcome != VersionOutcome.Absent)
        {
            return primary;
        }

        if (originalGitPath is not null
            && !string.Equals(originalGitPath, gitPath, StringComparison.Ordinal))
        {
            // Staged rename/copy: the new path does not exist in HEAD, but the
            // original path holds the content Git treats as the diff base.
            return await ReadGitVersionAsync(
                    canonicalRoot, $"HEAD:{originalGitPath}", indexPath: false, cancellationToken)
                .ConfigureAwait(false);
        }

        return VersionRead.Absent;
    }

    /// <summary>
    /// Materializes one Git version as raw bytes (<c>git show</c> with
    /// byte-capture stdout — DOCX blobs are binary and must never pass through
    /// a lossy text decode).
    /// </summary>
    private async Task<VersionRead> ReadGitVersionAsync(
        string canonicalRoot,
        string objectName,
        bool indexPath,
        CancellationToken cancellationToken)
    {
        GitProcessResult result = await _runner
            .RunAsync(
                new GitProcessRequest(
                    new[] { "--no-optional-locks", "show", objectName },
                    canonicalRoot)
                {
                    CaptureBinaryOutput = true,
                },
                cancellationToken)
            .ConfigureAwait(false);

        if (result.IsSuccess)
        {
            return VersionRead.Present(result.StandardOutputBytes ?? Array.Empty<byte>());
        }

        if (result.Status == GitProcessStatus.Failed
            && (indexPath
                ? GitErrorSignatures.IsIndexPathMissing(result)
                : GitErrorSignatures.IsHeadPathMissing(result)))
        {
            return VersionRead.Absent;
        }

        if (result.Status == GitProcessStatus.Failed
            && indexPath
            && GitErrorSignatures.IsUnmergedIndexPath(result))
        {
            // Spec 13 §21: unmerged paths have no stage-0 version. Report the
            // conflict state explicitly instead of an empty or misleading diff.
            return VersionRead.Failed(result with
            {
                ErrorKind = GitProcessErrorKind.GitConflict,
                ErrorMessage =
                    "The file is unmerged; the index has no stage-0 version to diff. " +
                    "Resolve the conflict before reviewing staged content (spec 13 §21).",
            });
        }

        return VersionRead.Failed(result);
    }

    private static VersionRead ReadWorktreeVersion(string fullPath)
    {
        if (!File.Exists(fullPath))
        {
            // A deleted working-tree file is a valid diff side, not an error.
            return VersionRead.Absent;
        }

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(fullPath);
        }
        catch (UnauthorizedAccessException exception)
        {
            return VersionRead.Failed(LocalFailure(
                GitProcessErrorKind.GitPermissionDenied,
                $"The working-tree file '{fullPath}' could not be read: {exception.Message}"));
        }
        catch (Exception exception) when (
            exception is IOException or System.Security.SecurityException)
        {
            return VersionRead.Failed(LocalFailure(
                GitProcessErrorKind.Unknown,
                $"The working-tree file '{fullPath}' could not be read: {exception.Message}"));
        }

        return VersionRead.Present(bytes);
    }

    /// <summary>
    /// Runs the existing DOCX pipeline on a materialized artifact: IDX-01
    /// preflight (resource budgets) first, then the IDX-02/BAT-08 scan into a
    /// plain-data snapshot. Any docx-level failure is a typed parse failure
    /// that the caller converts into a binary-change fallback.
    /// </summary>
    private void TryParseSnapshot(
        string artifactPath,
        out DocxDocumentSnapshot? snapshot,
        out GitProcessResult? failure)
    {
        snapshot = null;
        failure = null;

        try
        {
            DocxPreflightResult preflightResult = _preflight.ValidatePackage(artifactPath);
            if (!preflightResult.IsValid)
            {
                failure = LocalFailure(
                    GitProcessErrorKind.Unknown,
                    $"The materialized version is not a valid DOCX package: " +
                    $"{preflightResult.ErrorMessage} (Code: {preflightResult.ErrorCode}).");
                return;
            }

            snapshot = DocxVersionSnapshotBuilder.Build(artifactPath, _structureParser);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or InvalidDataException
                or IOException
                or UnauthorizedAccessException
                or NotSupportedException
                or ArgumentException
                or XmlException)
        {
            failure = LocalFailure(
                GitProcessErrorKind.Unknown,
                $"The materialized version could not be parsed as a DOCX document: {exception.Message}");
        }
    }

    /// <summary>
    /// Writes one materialized version into the per-call temp directory and
    /// marks it read-only. The artifact is only ever read afterwards and the
    /// whole directory is deleted before the call returns.
    /// </summary>
    private static string WriteReadOnlyArtifact(string tempDirectory, string fileName, byte[] bytes)
    {
        string artifactPath = Path.Combine(tempDirectory, fileName);
        using (var stream = new FileStream(artifactPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            stream.Write(bytes, 0, bytes.Length);
        }

        File.SetAttributes(artifactPath, File.GetAttributes(artifactPath) | FileAttributes.ReadOnly);
        return artifactPath;
    }

    private static void DeleteTempDirectoryBestEffort(string tempDirectory)
    {
        try
        {
            foreach (string file in Directory.EnumerateFiles(tempDirectory, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(tempDirectory, recursive: true);
        }
        catch (Exception)
        {
            // Best effort: a locked temp artifact must never turn a typed diff
            // outcome into an exception. Nothing outside the temp directory is
            // affected.
        }
    }

    private static string ComputeSha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static DocxDiffStatus MapFailure(GitProcessResult failure) =>
        failure.Status == GitProcessStatus.Unavailable
            || failure.ErrorKind == GitProcessErrorKind.GitNotInstalled
            ? DocxDiffStatus.GitUnavailable
            : failure.ErrorKind == GitProcessErrorKind.GitRepositoryNotFound
                ? DocxDiffStatus.NoRepository
                : DocxDiffStatus.Failed;

    private static GitProcessResult LocalFailure(GitProcessErrorKind kind, string message) =>
        new(GitProcessStatus.Failed, null, string.Empty, string.Empty, kind, message);

    private static DocxDiffResult Failure(DocxDiffStatus status, GitProcessResult failure) =>
        new(status, null, failure);

    private enum VersionOutcome
    {
        Present,
        Absent,
        Failed,
    }

    private readonly record struct VersionRead(
        VersionOutcome Outcome,
        byte[] Bytes,
        GitProcessResult? Failure)
    {
        public static VersionRead Absent { get; } = new(VersionOutcome.Absent, Array.Empty<byte>(), null);

        public static VersionRead Present(byte[] bytes) => new(VersionOutcome.Present, bytes, null);

        public static VersionRead Failed(GitProcessResult failure) =>
            new(VersionOutcome.Failed, Array.Empty<byte>(), failure);

        public bool Exists => Outcome == VersionOutcome.Present;
    }
}

/// <summary>
/// SCM-05 — builds a plain-data <see cref="DocxDocumentSnapshot"/> from a
/// materialized DOCX artifact by reusing the existing document infrastructure:
/// the BAT-08 logical run scan (<see cref="DocxFontScan"/>, the single-sourced
/// paragraph/run map of DOCX-01/IDX-02 lineage) for paragraphs, table cells and
/// direct run formatting, and the IDX-02 structural parser
/// (<see cref="IDocxStructureParser"/>) for document metadata.
///
/// Only words that OOXML itself defines are read; nothing is written and no
/// content is invented. The snapshot carries no XML nodes, so the comparer
/// stays pure.
/// </summary>
internal static class DocxVersionSnapshotBuilder
{
    private static readonly XNamespace WNs = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    public static DocxDocumentSnapshot Build(string artifactPath, IDocxStructureParser structureParser)
    {
        List<DocxFontRun> runs;
        using (var stream = File.Open(artifactPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            runs = DocxFontScan.ScanStream(stream).ToList();
        }

        var paragraphs = new List<DocxParagraphSnapshot>();
        var cellTexts = new Dictionary<(string Part, int Table, int Row, int Cell), List<(int ParagraphIndex, string Text)>>();
        var tableIndexes = new Dictionary<XDocument, Dictionary<XElement, int>>();

        foreach (IGrouping<string, DocxFontRun> partGroup in runs.GroupBy(run => run.PartName))
        {
            string partName = partGroup.Key;
            foreach (IGrouping<int, DocxFontRun> paragraphGroup in partGroup
                         .GroupBy(run => run.ParagraphIndex)
                         .OrderBy(group => group.Key))
            {
                List<DocxFontRun> paragraphRuns = paragraphGroup.OrderBy(run => run.RunIndex).ToList();
                DocxFontRun first = paragraphRuns[0];

                if (first.Origin == DocxStructureOrigin.Table)
                {
                    (int table, int row, int cell) = ResolveCellCoordinates(first.RunElement, tableIndexes);
                    var key = (partName, table, row, cell);
                    if (!cellTexts.TryGetValue(key, out List<(int ParagraphIndex, string Text)>? texts))
                    {
                        texts = new List<(int, string)>();
                        cellTexts[key] = texts;
                    }

                    if (texts.Count == 0 || texts[^1].ParagraphIndex != paragraphGroup.Key)
                    {
                        texts.Add((paragraphGroup.Key, first.ParagraphText));
                    }

                    continue;
                }

                paragraphs.Add(new DocxParagraphSnapshot(
                    partName,
                    first.Origin,
                    paragraphGroup.Key,
                    first.ParagraphText,
                    ResolveHeadingLevel(first.RunElement),
                    paragraphRuns.Select(ToRunSnapshot).ToList()));
            }
        }

        var tableCells = cellTexts
            .Select(pair => new DocxTableCellSnapshot(
                pair.Key.Part,
                pair.Key.Table,
                pair.Key.Row,
                pair.Key.Cell,
                string.Join("\n", pair.Value.Select(text => text.Text))))
            .OrderBy(cell => cell.PartName, StringComparer.Ordinal)
            .ThenBy(cell => cell.TableIndex)
            .ThenBy(cell => cell.RowIndex)
            .ThenBy(cell => cell.CellIndex)
            .ToList();

        DocumentMetadata metadata = structureParser.Parse(artifactPath).Metadata;
        return new DocxDocumentSnapshot(
            paragraphs,
            tableCells,
            new DocxMetadataSnapshot(
                metadata.SectionCount ?? 0,
                metadata.TableCount ?? 0,
                metadata.ImageCount ?? 0,
                metadata.PageCount,
                metadata.PrimaryFont,
                metadata.PrimaryFontSize,
                metadata.PaperSummary,
                metadata.OrientationSummary,
                metadata.MarginSummary,
                metadata.HeaderPresent ?? false,
                metadata.FooterPresent ?? false));
    }

    private static DocxRunSnapshot ToRunSnapshot(DocxFontRun run) => new(
        run.RunIndex,
        run.VisibleText,
        run.DirectFontFamily,
        run.DirectSizeHalfPoints,
        ReadTriState(run.RunElement, "b"),
        ReadTriState(run.RunElement, "i"),
        ReadUnderline(run.RunElement));

    /// <summary>
    /// Resolves the (table, row, cell) coordinates of a table-origin run: the
    /// nearest enclosing table (document order index within its part), the
    /// row's index among the table's direct rows and the cell's index among
    /// the row's direct cells — the same stable vocabulary the document
    /// structure exposes.
    /// </summary>
    private static (int Table, int Row, int Cell) ResolveCellCoordinates(
        XElement runElement,
        Dictionary<XDocument, Dictionary<XElement, int>> tableIndexCache)
    {
        XElement table = runElement.Ancestors(WNs + "tbl").FirstOrDefault()
            ?? throw new InvalidOperationException(
                "A table-origin run has no enclosing w:tbl element in the package part.");

        XElement row = runElement.Ancestors(WNs + "tr").FirstOrDefault()
            ?? throw new InvalidOperationException(
                "A table-origin run has no enclosing w:tr element in the package part.");

        XElement cell = runElement.Ancestors(WNs + "tc").FirstOrDefault()
            ?? throw new InvalidOperationException(
                "A table-origin run has no enclosing w:tc element in the package part.");

        XDocument document = runElement.Document
            ?? throw new InvalidOperationException("The run element is not attached to a package part document.");

        if (!tableIndexCache.TryGetValue(document, out Dictionary<XElement, int>? indexes))
        {
            indexes = new Dictionary<XElement, int>();
            int index = 0;
            foreach (XElement candidate in document.Descendants(WNs + "tbl"))
            {
                indexes[candidate] = index++;
            }

            tableIndexCache[document] = indexes;
        }

        int tableIndex = indexes[table];
        int rowIndex = table.Elements(WNs + "tr").ToList().IndexOf(row);
        int cellIndex = row.Elements(WNs + "tc").ToList().IndexOf(cell);

        if (rowIndex < 0 || cellIndex < 0)
        {
            throw new InvalidOperationException(
                "A table-origin run could not be located inside its table row/cell structure.");
        }

        return (tableIndex, rowIndex, cellIndex);
    }

    /// <summary>
    /// Heading level using the same OOXML vocabulary as the IDX-02 parser:
    /// <c>Title</c> style → 1, <c>HeadingN</c> style → N, else
    /// <c>w:outlineLvl</c> + 1.
    /// </summary>
    private static int ResolveHeadingLevel(XElement runElement)
    {
        XElement? paragraph = runElement.Ancestors(WNs + "p").FirstOrDefault();
        XElement? paragraphProperties = paragraph?
            .Elements()
            .FirstOrDefault(element => element.Name.LocalName == "pPr");
        if (paragraphProperties is null)
        {
            return 0;
        }

        XElement? paragraphStyle = paragraphProperties
            .Elements()
            .FirstOrDefault(element => element.Name.LocalName == "pStyle");
        string? styleId = ReadLocalAttribute(paragraphStyle, "val");
        if (!string.IsNullOrEmpty(styleId))
        {
            if (styleId.Equals("Title", StringComparison.OrdinalIgnoreCase))
            {
                return 1;
            }

            string normalized = styleId.Replace(" ", string.Empty).Replace("_", string.Empty).Replace("-", string.Empty);
            if (normalized.StartsWith("Heading", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(normalized.Substring("Heading".Length), out int level)
                && level is >= 1 and <= 6)
            {
                return level;
            }
        }

        XElement? outlineLevel = paragraphProperties
            .Elements()
            .FirstOrDefault(element => element.Name.LocalName == "outlineLvl");
        string? outlineValue = ReadLocalAttribute(outlineLevel, "val");
        if (int.TryParse(outlineValue, out int outline)
            && outline is >= 0 and <= 5)
        {
            return outline + 1;
        }

        return 0;
    }

    private static bool? ReadTriState(XElement runElement, string localName)
    {
        XElement? property = runElement
            .Element(WNs + "rPr")?
            .Elements()
            .FirstOrDefault(element => element.Name.LocalName == localName);
        if (property is null)
        {
            return null;
        }

        string? value = ReadLocalAttribute(property, "val");
        if (string.IsNullOrEmpty(value))
        {
            return true;
        }

        return value switch
        {
            "0" or "false" or "off" => false,
            _ => true,
        };
    }

    private static string? ReadUnderline(XElement runElement)
    {
        XElement? underline = runElement
            .Element(WNs + "rPr")?
            .Elements()
            .FirstOrDefault(element => element.Name.LocalName == "u");
        if (underline is null)
        {
            return null;
        }

        return ReadLocalAttribute(underline, "val") ?? "single";
    }

    private static string? ReadLocalAttribute(XElement? element, string localName)
    {
        if (element is null)
        {
            return null;
        }

        return element.Attribute(WNs + localName)?.Value
            ?? element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == localName)?.Value;
    }
}
