using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DocumentIDE.Core.Docx;

namespace DocumentIDE.Core.SourceControl;

/// <summary>
/// SCM-05 — typed outcome of one document-aware DOCX diff request
/// (spec 13 §9.2/§9.3). Failures are inspectable statuses, never silent
/// fallbacks: a document-aware comparison that cannot be produced degrades to
/// a <see cref="BinaryFallback"/> with a binary-change summary instead of
/// pretending a semantic diff exists.
/// </summary>
public enum DocxDiffStatus
{
    /// <summary>A document-aware report was produced (it may contain zero changes).</summary>
    Available = 0,

    /// <summary>
    /// The compared versions were materialized but at least one side is not a
    /// parseable DOCX package; a binary-change summary is provided instead
    /// (spec 13 §9.3). <see cref="DocxDiffResult.Failure"/> carries the typed
    /// parse failure for inspection.
    /// </summary>
    BinaryFallback = 1,

    /// <summary>The worktree/path is not inside a usable Git repository any more.</summary>
    NoRepository = 2,

    /// <summary>The system Git executable is missing or unusable.</summary>
    GitUnavailable = 3,

    /// <summary>The requested path is not a safe worktree-relative document path; it was rejected before any access.</summary>
    PathRejected = 4,

    /// <summary>The comparison could not be produced for another reason; inspect <see cref="DocxDiffResult.Failure"/>.</summary>
    Failed = 5,
}

/// <summary>
/// SCM-05 — the document-aware change vocabulary (spec 13 §9.2): visible text,
/// added/removed paragraphs, changed table cells, changed headings, meaningful
/// formatting changes and metadata changes.
/// </summary>
public enum DocxChangeKind
{
    /// <summary>A non-table paragraph exists only in the newer version.</summary>
    ParagraphAdded = 0,

    /// <summary>A non-table paragraph exists only in the older version.</summary>
    ParagraphRemoved = 1,

    /// <summary>A non-table paragraph's visible text changed.</summary>
    ParagraphTextChanged = 2,

    /// <summary>A heading paragraph's text or level changed.</summary>
    HeadingChanged = 3,

    /// <summary>A table cell exists only in the newer version.</summary>
    TableCellAdded = 4,

    /// <summary>A table cell exists only in the older version.</summary>
    TableCellRemoved = 5,

    /// <summary>A table cell's visible text changed.</summary>
    TableCellChanged = 6,

    /// <summary>Direct run formatting changed on a paragraph whose text is unchanged.</summary>
    FormattingChanged = 7,

    /// <summary>A document-level metadata field changed (sections, tables, font, paper, ...).</summary>
    MetadataChanged = 8,
}

/// <summary>
/// SCM-05 — one document-aware change between two versions of a DOCX.
///
/// Index fields identify the change inside each compared version; they are
/// never a cross-version identity (paragraph/table/run order may shift).
/// <see cref="Before"/> / <see cref="After"/> carry exact version content and
/// are rendered as inert text by consumers (never HTML-interpreted).
/// </summary>
/// <param name="Kind">The document-aware change class.</param>
/// <param name="Origin">Structure origin of the changed content.</param>
/// <param name="PartName">OOXML part name (e.g. <c>word/document.xml</c>); empty for metadata changes.</param>
/// <param name="Summary">Deterministic, truncated human-readable description.</param>
/// <param name="Before">Exact older-version content/property value; null when the change is a pure addition.</param>
/// <param name="After">Exact newer-version content/property value; null when the change is a pure removal.</param>
/// <param name="Property">Changed formatting/metadata property name when the change is field-scoped; otherwise null.</param>
/// <param name="OldParagraphIndex">0-based paragraph index in the older version, when the change is paragraph-scoped.</param>
/// <param name="NewParagraphIndex">0-based paragraph index in the newer version, when the change is paragraph-scoped.</param>
/// <param name="RunIndex">0-based run index for formatting changes.</param>
/// <param name="TableIndex">0-based table index within the part, for table-cell changes.</param>
/// <param name="RowIndex">0-based row index within the table, for table-cell changes.</param>
/// <param name="CellIndex">0-based cell index within the row, for table-cell changes.</param>
public sealed record DocxChange(
    DocxChangeKind Kind,
    DocxStructureOrigin Origin,
    string PartName,
    string Summary,
    string? Before = null,
    string? After = null,
    string? Property = null,
    int? OldParagraphIndex = null,
    int? NewParagraphIndex = null,
    int? RunIndex = null,
    int? TableIndex = null,
    int? RowIndex = null,
    int? CellIndex = null);

/// <summary>
/// SCM-05 — binary-change summary for a version pair that cannot be compared
/// as DOCX documents (spec 13 §9.3). Sizes and hashes are of the exact
/// compared version bytes (Git blob / working-tree file); no content is faked.
/// </summary>
public sealed record DocxBinarySummary(
    bool LeftExists,
    bool RightExists,
    long LeftSizeBytes,
    long RightSizeBytes,
    string? LeftSha256,
    string? RightSha256)
{
    /// <summary>True when both sides exist with identical bytes.</summary>
    public bool ContentIdentical =>
        LeftExists
        && RightExists
        && LeftSha256 is not null
        && string.Equals(LeftSha256, RightSha256, StringComparison.Ordinal);
}

/// <summary>
/// SCM-05 — document-aware comparison report of one DOCX file between two
/// Git-managed versions (spec 13 §9.2). Git staging remains file-level; this
/// report explains the change, it never stages partial content.
/// </summary>
public sealed record DocxDiffReport(
    GitDiffTarget Target,
    string Path,
    string WorktreeRoot,
    string LeftLabel,
    string RightLabel,
    bool LeftExists,
    bool RightExists,
    IReadOnlyList<DocxChange> Changes,
    DocxBinarySummary? BinarySummary)
{
    /// <summary>True when the document-aware comparison found at least one change.</summary>
    public bool HasChanges => Changes.Count > 0;
}

/// <summary>
/// SCM-05 — typed outcome of one DOCX diff request, mirroring the SCM-02/04
/// result shape: either an available report (document-aware or binary
/// fallback) or a typed failure with the originating
/// <see cref="GitProcessResult"/>.
/// </summary>
public sealed record DocxDiffResult(
    DocxDiffStatus Status,
    DocxDiffReport? Report,
    GitProcessResult? Failure = null)
{
    /// <summary>True only when a document-aware report is present.</summary>
    public bool IsAvailable => Status == DocxDiffStatus.Available && Report is not null;

    /// <summary>True only when the compared versions fell back to a binary-change summary.</summary>
    public bool IsBinaryFallback => Status == DocxDiffStatus.BinaryFallback && Report is not null;
}

/// <summary>SCM-05 — one text-bearing run with its direct formatting, as extracted from one version.</summary>
public sealed record DocxRunSnapshot(
    int RunIndex,
    string Text,
    string? FontFamily,
    int? SizeHalfPoints,
    bool? Bold,
    bool? Italic,
    string? Underline);

/// <summary>SCM-05 — one non-table paragraph of one version.</summary>
public sealed record DocxParagraphSnapshot(
    string PartName,
    DocxStructureOrigin Origin,
    int ParagraphIndex,
    string Text,
    int HeadingLevel,
    IReadOnlyList<DocxRunSnapshot> Runs);

/// <summary>SCM-05 — one table cell (row-major position) of one version.</summary>
public sealed record DocxTableCellSnapshot(
    string PartName,
    int TableIndex,
    int RowIndex,
    int CellIndex,
    string Text);

/// <summary>
/// SCM-05 — document-level metadata of one version (IDX-02 structural parser
/// fields); compared as a fixed, deterministic field list.
/// </summary>
public sealed record DocxMetadataSnapshot(
    int SectionCount,
    int TableCount,
    int ImageCount,
    int? PageCount,
    string? PrimaryFont,
    double? PrimaryFontSize,
    string? PaperSummary,
    string? OrientationSummary,
    string? MarginSummary,
    bool HeaderPresent,
    bool FooterPresent);

/// <summary>
/// SCM-05 — plain-data snapshot of one parsed DOCX version. The snapshot is
/// produced by the Infrastructure parser from a read-only materialized
/// artifact; the comparer stays pure and IO-free.
/// </summary>
public sealed record DocxDocumentSnapshot(
    IReadOnlyList<DocxParagraphSnapshot> Paragraphs,
    IReadOnlyList<DocxTableCellSnapshot> TableCells,
    DocxMetadataSnapshot Metadata);

/// <summary>
/// SCM-05 — pure, deterministic document-aware comparator (spec 13 §9.2).
///
/// The comparator is a pure function from two snapshots to a change list; it
/// never touches the filesystem or a Git process. Paragraphs and table cells
/// are aligned with the SCM-04 Myers shortest-edit-script engine (exact
/// ordinal matching only — no fuzzy/semantic matching), so run-boundary
/// fragmentation cannot split one logical text change into unrelated pieces.
/// </summary>
public static class DocxDocumentComparer
{
    /// <summary>Maximum preview length used inside deterministic summaries.</summary>
    public const int SummaryPreviewLength = 60;

    private static readonly string[] MetadataFieldOrder =
    {
        "SectionCount",
        "TableCount",
        "ImageCount",
        "PageCount",
        "PrimaryFont",
        "PrimaryFontSize",
        "PaperSummary",
        "OrientationSummary",
        "MarginSummary",
        "HeaderPresent",
        "FooterPresent",
    };

    /// <summary>
    /// Compares two parsed document versions. A null snapshot means the
    /// version does not exist (added/deleted file); the existing side then
    /// reports complete additions/removals without metadata/formatting noise.
    /// </summary>
    public static IReadOnlyList<DocxChange> Compare(
        DocxDocumentSnapshot? before,
        DocxDocumentSnapshot? after)
    {
        var changes = new List<DocxChange>();
        if (before is null && after is null)
        {
            return changes;
        }

        CompareParagraphs(changes, before, after);
        CompareTableCells(changes, before, after);

        if (before is not null && after is not null)
        {
            CompareFormatting(changes, before, after);
            CompareMetadata(changes, before, after);
        }

        return changes;
    }

    // -------------------------------------------------------------------
    // Paragraphs (non-table paragraphs of body/header/footer origins)
    // -------------------------------------------------------------------

    private static void CompareParagraphs(
        List<DocxChange> changes,
        DocxDocumentSnapshot? before,
        DocxDocumentSnapshot? after)
    {
        foreach (string partName in PartNamesOf(before?.Paragraphs, after?.Paragraphs))
        {
            List<DocxParagraphSnapshot> oldParagraphs = ParagraphsOf(before, partName);
            List<DocxParagraphSnapshot> newParagraphs = ParagraphsOf(after, partName);
            PartDiff diff = DiffPart(
                oldParagraphs.Select(paragraph => paragraph.Text).ToArray(),
                newParagraphs.Select(paragraph => paragraph.Text).ToArray());

            foreach ((int oldIndex, int newIndex) in diff.Unchanged)
            {
                DocxParagraphSnapshot oldParagraph = oldParagraphs[oldIndex];
                DocxParagraphSnapshot newParagraph = newParagraphs[newIndex];
                if (oldParagraph.HeadingLevel != newParagraph.HeadingLevel)
                {
                    changes.Add(new DocxChange(
                        DocxChangeKind.HeadingChanged,
                        newParagraph.Origin,
                        partName,
                        $"Heading level changed from {oldParagraph.HeadingLevel} to " +
                        $"{newParagraph.HeadingLevel}: \"{Preview(newParagraph.Text)}\"",
                        Before: oldParagraph.HeadingLevel.ToString(CultureInfo.InvariantCulture),
                        After: newParagraph.HeadingLevel.ToString(CultureInfo.InvariantCulture),
                        Property: "HeadingLevel",
                        OldParagraphIndex: oldParagraph.ParagraphIndex,
                        NewParagraphIndex: newParagraph.ParagraphIndex));
                }
            }

            foreach (Region region in diff.Regions)
            {
                int paired = Math.Min(region.Removed.Count, region.Added.Count);
                for (int i = 0; i < paired; i++)
                {
                    DocxParagraphSnapshot oldParagraph = oldParagraphs[region.Removed[i]];
                    DocxParagraphSnapshot newParagraph = newParagraphs[region.Added[i]];
                    bool isHeading = oldParagraph.HeadingLevel > 0 || newParagraph.HeadingLevel > 0;
                    changes.Add(new DocxChange(
                        isHeading ? DocxChangeKind.HeadingChanged : DocxChangeKind.ParagraphTextChanged,
                        newParagraph.Origin,
                        partName,
                        $"{(isHeading ? "Heading" : "Paragraph")} text changed: " +
                        $"\"{Preview(oldParagraph.Text)}\" -> \"{Preview(newParagraph.Text)}\"",
                        Before: oldParagraph.Text,
                        After: newParagraph.Text,
                        OldParagraphIndex: oldParagraph.ParagraphIndex,
                        NewParagraphIndex: newParagraph.ParagraphIndex));
                }

                for (int i = paired; i < region.Removed.Count; i++)
                {
                    DocxParagraphSnapshot oldParagraph = oldParagraphs[region.Removed[i]];
                    changes.Add(new DocxChange(
                        DocxChangeKind.ParagraphRemoved,
                        oldParagraph.Origin,
                        partName,
                        $"Paragraph removed: \"{Preview(oldParagraph.Text)}\"",
                        Before: oldParagraph.Text,
                        OldParagraphIndex: oldParagraph.ParagraphIndex));
                }

                for (int i = paired; i < region.Added.Count; i++)
                {
                    DocxParagraphSnapshot newParagraph = newParagraphs[region.Added[i]];
                    changes.Add(new DocxChange(
                        DocxChangeKind.ParagraphAdded,
                        newParagraph.Origin,
                        partName,
                        $"Paragraph added: \"{Preview(newParagraph.Text)}\"",
                        After: newParagraph.Text,
                        NewParagraphIndex: newParagraph.ParagraphIndex));
                }
            }
        }
    }

    // -------------------------------------------------------------------
    // Table cells (row-major cell text sequences per table)
    // -------------------------------------------------------------------

    private static void CompareTableCells(
        List<DocxChange> changes,
        DocxDocumentSnapshot? before,
        DocxDocumentSnapshot? after)
    {
        foreach (string partName in PartNamesOf(before?.TableCells, after?.TableCells))
        {
            var tableIndexes = new SortedSet<int>();
            foreach (DocxTableCellSnapshot cell in (before?.TableCells ?? Array.Empty<DocxTableCellSnapshot>())
                     .Concat(after?.TableCells ?? Array.Empty<DocxTableCellSnapshot>())
                     .Where(cell => string.Equals(cell.PartName, partName, StringComparison.Ordinal)))
            {
                tableIndexes.Add(cell.TableIndex);
            }

            foreach (int tableIndex in tableIndexes)
            {
                List<DocxTableCellSnapshot> oldCells = CellsOf(before, partName, tableIndex);
                List<DocxTableCellSnapshot> newCells = CellsOf(after, partName, tableIndex);
                PartDiff diff = DiffPart(
                    oldCells.Select(cell => cell.Text).ToArray(),
                    newCells.Select(cell => cell.Text).ToArray());

                foreach (Region region in diff.Regions)
                {
                    int paired = Math.Min(region.Removed.Count, region.Added.Count);
                    for (int i = 0; i < paired; i++)
                    {
                        DocxTableCellSnapshot oldCell = oldCells[region.Removed[i]];
                        DocxTableCellSnapshot newCell = newCells[region.Added[i]];
                        changes.Add(new DocxChange(
                            DocxChangeKind.TableCellChanged,
                            DocxStructureOrigin.Table,
                            partName,
                            $"Table cell changed: \"{Preview(oldCell.Text)}\" -> \"{Preview(newCell.Text)}\"",
                            Before: oldCell.Text,
                            After: newCell.Text,
                            TableIndex: newCell.TableIndex,
                            RowIndex: newCell.RowIndex,
                            CellIndex: newCell.CellIndex));
                    }

                    for (int i = paired; i < region.Removed.Count; i++)
                    {
                        DocxTableCellSnapshot oldCell = oldCells[region.Removed[i]];
                        changes.Add(new DocxChange(
                            DocxChangeKind.TableCellRemoved,
                            DocxStructureOrigin.Table,
                            partName,
                            $"Table cell removed: \"{Preview(oldCell.Text)}\"",
                            Before: oldCell.Text,
                            TableIndex: oldCell.TableIndex,
                            RowIndex: oldCell.RowIndex,
                            CellIndex: oldCell.CellIndex));
                    }

                    for (int i = paired; i < region.Added.Count; i++)
                    {
                        DocxTableCellSnapshot newCell = newCells[region.Added[i]];
                        changes.Add(new DocxChange(
                            DocxChangeKind.TableCellAdded,
                            DocxStructureOrigin.Table,
                            partName,
                            $"Table cell added: \"{Preview(newCell.Text)}\"",
                            After: newCell.Text,
                            TableIndex: newCell.TableIndex,
                            RowIndex: newCell.RowIndex,
                            CellIndex: newCell.CellIndex));
                    }
                }
            }
        }
    }

    // -------------------------------------------------------------------
    // Formatting (direct run formatting of unchanged-text paragraphs)
    // -------------------------------------------------------------------

    private static void CompareFormatting(
        List<DocxChange> changes,
        DocxDocumentSnapshot before,
        DocxDocumentSnapshot after)
    {
        foreach (string partName in PartNamesOf(before.Paragraphs, after.Paragraphs))
        {
            List<DocxParagraphSnapshot> oldParagraphs = ParagraphsOf(before, partName);
            List<DocxParagraphSnapshot> newParagraphs = ParagraphsOf(after, partName);
            PartDiff diff = DiffPart(
                oldParagraphs.Select(paragraph => paragraph.Text).ToArray(),
                newParagraphs.Select(paragraph => paragraph.Text).ToArray());

            foreach ((int oldIndex, int newIndex) in diff.Unchanged)
            {
                CompareParagraphFormatting(
                    changes,
                    partName,
                    oldParagraphs[oldIndex],
                    newParagraphs[newIndex]);
            }
        }
    }

    /// <summary>
    /// Compares the run-formatting descriptor sequences of two paragraphs with
    /// identical text: the descriptors are aligned over the shared character
    /// offsets, so a run split/merge alone (identical effective formatting)
    /// produces no change, while a partially re-formatted text region is
    /// reported as one contiguous formatting change.
    /// </summary>
    private static void CompareParagraphFormatting(
        List<DocxChange> changes,
        string partName,
        DocxParagraphSnapshot oldParagraph,
        DocxParagraphSnapshot newParagraph)
    {
        if (!string.Equals(oldParagraph.Text, newParagraph.Text, StringComparison.Ordinal)
            || oldParagraph.Runs.Count == 0
            || newParagraph.Runs.Count == 0)
        {
            return;
        }

        int oldRun = 0;
        int newRun = 0;
        int oldOffset = 0;
        int newOffset = 0;
        int textOffset = 0;

        RunFormat? pendingBefore = null;
        RunFormat? pendingAfter = null;
        int pendingStart = 0;
        int pendingEnd = 0;

        while (oldRun < oldParagraph.Runs.Count && newRun < newParagraph.Runs.Count)
        {
            DocxRunSnapshot oldSide = oldParagraph.Runs[oldRun];
            DocxRunSnapshot newSide = newParagraph.Runs[newRun];
            int take = Math.Min(oldSide.Text.Length - oldOffset, newSide.Text.Length - newOffset);
            if (take <= 0)
            {
                break;
            }

            RunFormat beforeFormat = RunFormat.Of(oldSide);
            RunFormat afterFormat = RunFormat.Of(newSide);
            bool differs = !beforeFormat.Equals(afterFormat);

            if (differs)
            {
                bool continues = pendingBefore is RunFormat previousBefore
                                 && pendingAfter is RunFormat previousAfter
                                 && previousBefore.Equals(beforeFormat)
                                 && previousAfter.Equals(afterFormat)
                                 && pendingEnd == textOffset;
                if (!continues)
                {
                    FlushFormattingChange(
                        changes, partName, oldParagraph, newParagraph,
                        pendingBefore, pendingAfter, pendingStart, pendingEnd);
                    pendingBefore = beforeFormat;
                    pendingAfter = afterFormat;
                    pendingStart = textOffset;
                }

                pendingEnd = textOffset + take;
            }
            else
            {
                FlushFormattingChange(
                    changes, partName, oldParagraph, newParagraph,
                    pendingBefore, pendingAfter, pendingStart, pendingEnd);
                pendingBefore = null;
                pendingAfter = null;
            }

            oldOffset += take;
            newOffset += take;
            textOffset += take;
            if (oldOffset == oldSide.Text.Length)
            {
                oldRun++;
                oldOffset = 0;
            }

            if (newOffset == newSide.Text.Length)
            {
                newRun++;
                newOffset = 0;
            }
        }

        FlushFormattingChange(
            changes, partName, oldParagraph, newParagraph,
            pendingBefore, pendingAfter, pendingStart, pendingEnd);
    }

    private static void FlushFormattingChange(
        List<DocxChange> changes,
        string partName,
        DocxParagraphSnapshot oldParagraph,
        DocxParagraphSnapshot newParagraph,
        RunFormat? before,
        RunFormat? after,
        int start,
        int end)
    {
        if (before is not RunFormat beforeFormat || after is not RunFormat afterFormat || end <= start)
        {
            return;
        }

        string text = newParagraph.Text.Substring(start, end - start);
        List<(string Field, string Before, string After)> differing = beforeFormat.Differences(afterFormat);
        bool singleField = differing.Count == 1;
        string summary = differing.Count == 0
            ? $"Formatting changed for \"{Preview(text)}\""
            : $"Formatting changed for \"{Preview(text)}\": " +
              string.Join("; ", differing.Select(field => $"{field.Field} {field.Before} -> {field.After}"));

        changes.Add(new DocxChange(
            DocxChangeKind.FormattingChanged,
            newParagraph.Origin,
            partName,
            summary,
            Before: singleField ? differing[0].Before : beforeFormat.Describe(),
            After: singleField ? differing[0].After : afterFormat.Describe(),
            Property: singleField ? differing[0].Field : null,
            OldParagraphIndex: oldParagraph.ParagraphIndex,
            NewParagraphIndex: newParagraph.ParagraphIndex));
    }

    // -------------------------------------------------------------------
    // Metadata
    // -------------------------------------------------------------------

    private static void CompareMetadata(
        List<DocxChange> changes,
        DocxDocumentSnapshot before,
        DocxDocumentSnapshot after)
    {
        DocxMetadataSnapshot oldMetadata = before.Metadata;
        DocxMetadataSnapshot newMetadata = after.Metadata;

        foreach (string field in MetadataFieldOrder)
        {
            (string OldValue, string NewValue) = field switch
            {
                "SectionCount" => Value(oldMetadata.SectionCount, newMetadata.SectionCount),
                "TableCount" => Value(oldMetadata.TableCount, newMetadata.TableCount),
                "ImageCount" => Value(oldMetadata.ImageCount, newMetadata.ImageCount),
                "PageCount" => ValueText(oldMetadata.PageCount, newMetadata.PageCount),
                "PrimaryFont" => ValueText(oldMetadata.PrimaryFont, newMetadata.PrimaryFont),
                "PrimaryFontSize" => ValueText(
                    FormatSize(oldMetadata.PrimaryFontSize),
                    FormatSize(newMetadata.PrimaryFontSize)),
                "PaperSummary" => ValueText(oldMetadata.PaperSummary, newMetadata.PaperSummary),
                "OrientationSummary" => ValueText(oldMetadata.OrientationSummary, newMetadata.OrientationSummary),
                "MarginSummary" => ValueText(oldMetadata.MarginSummary, newMetadata.MarginSummary),
                "HeaderPresent" => ValueText(oldMetadata.HeaderPresent, newMetadata.HeaderPresent),
                "FooterPresent" => ValueText(oldMetadata.FooterPresent, newMetadata.FooterPresent),
                _ => throw new InvalidOperationException($"Unknown metadata field '{field}'."),
            };

            if (!string.Equals(OldValue, NewValue, StringComparison.Ordinal))
            {
                changes.Add(new DocxChange(
                    DocxChangeKind.MetadataChanged,
                    DocxStructureOrigin.Body,
                    string.Empty,
                    $"Metadata '{field}' changed from '{OldValue}' to '{NewValue}'",
                    Before: OldValue,
                    After: NewValue,
                    Property: field));
            }
        }
    }

    private static (string OldValue, string NewValue) Value(int oldValue, int newValue) =>
        (oldValue.ToString(CultureInfo.InvariantCulture), newValue.ToString(CultureInfo.InvariantCulture));

    private static (string OldValue, string NewValue) ValueText(object? oldValue, object? newValue) =>
        (oldValue?.ToString() ?? "none", newValue?.ToString() ?? "none");

    private static string? FormatSize(double? size) =>
        size?.ToString("0.###", CultureInfo.InvariantCulture);

    // -------------------------------------------------------------------
    // Sequence alignment helpers
    // -------------------------------------------------------------------

    /// <summary>
    /// Exact-ordinal alignment of two text sequences with the SCM-04 Myers
    /// engine: unchanged indexes pair in order; each change region carries the
    /// removed (older) and added (newer) indexes.
    /// </summary>
    private static PartDiff DiffPart(string[] oldTexts, string[] newTexts)
    {
        var diff = new PartDiff();
        if (oldTexts.Length == 0 && newTexts.Length == 0)
        {
            return diff;
        }

        IReadOnlyList<GitDiffHunk> hunks = GitUnifiedDiffFormatter.ComputeHunks(
            oldTexts, oldEndsWithNewline: true, newTexts, newEndsWithNewline: true, contextLines: 0);

        int oldCursor = 0;
        int newCursor = 0;
        foreach (GitDiffHunk hunk in hunks)
        {
            // Unified convention: when a side's count is 0 its start is the
            // number of the line before the hunk (0 at the start of a file).
            int oldStart = hunk.OldCount > 0 ? hunk.OldStart - 1 : hunk.OldStart;
            int newStart = hunk.NewCount > 0 ? hunk.NewStart - 1 : hunk.NewStart;

            // Unchanged zone between the previous change region and this one.
            while (oldCursor < oldStart && newCursor < newStart)
            {
                diff.Unchanged.Add((oldCursor, newCursor));
                oldCursor++;
                newCursor++;
            }

            var region = new Region();
            foreach (GitDiffLine line in hunk.Lines)
            {
                if (line.Kind == GitDiffLineKind.Removed && line.OldLineNumber is int oldNumber)
                {
                    region.Removed.Add(oldNumber - 1);
                }
                else if (line.Kind == GitDiffLineKind.Added && line.NewLineNumber is int newNumber)
                {
                    region.Added.Add(newNumber - 1);
                }
            }

            if (region.Removed.Count > 0 || region.Added.Count > 0)
            {
                diff.Regions.Add(region);
            }

            oldCursor = oldStart + hunk.OldCount;
            newCursor = newStart + hunk.NewCount;
        }

        while (oldCursor < oldTexts.Length && newCursor < newTexts.Length)
        {
            diff.Unchanged.Add((oldCursor, newCursor));
            oldCursor++;
            newCursor++;
        }

        return diff;
    }

    private static List<DocxParagraphSnapshot> ParagraphsOf(DocxDocumentSnapshot? snapshot, string partName) =>
        snapshot is null
            ? new List<DocxParagraphSnapshot>()
            : snapshot.Paragraphs
                .Where(paragraph => string.Equals(paragraph.PartName, partName, StringComparison.Ordinal))
                .OrderBy(paragraph => paragraph.ParagraphIndex)
                .ToList();

    private static List<DocxTableCellSnapshot> CellsOf(
        DocxDocumentSnapshot? snapshot,
        string partName,
        int tableIndex) =>
        snapshot is null
            ? new List<DocxTableCellSnapshot>()
            : snapshot.TableCells
                .Where(cell => string.Equals(cell.PartName, partName, StringComparison.Ordinal)
                               && cell.TableIndex == tableIndex)
                .OrderBy(cell => cell.RowIndex)
                .ThenBy(cell => cell.CellIndex)
                .ToList();

    private static IEnumerable<string> PartNamesOf(
        IReadOnlyList<DocxParagraphSnapshot>? before,
        IReadOnlyList<DocxParagraphSnapshot>? after)
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);
        foreach (DocxParagraphSnapshot paragraph in before ?? Array.Empty<DocxParagraphSnapshot>())
        {
            names.Add(paragraph.PartName);
        }

        foreach (DocxParagraphSnapshot paragraph in after ?? Array.Empty<DocxParagraphSnapshot>())
        {
            names.Add(paragraph.PartName);
        }

        return names;
    }

    private static IEnumerable<string> PartNamesOf(
        IReadOnlyList<DocxTableCellSnapshot>? before,
        IReadOnlyList<DocxTableCellSnapshot>? after)
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);
        foreach (DocxTableCellSnapshot cell in before ?? Array.Empty<DocxTableCellSnapshot>())
        {
            names.Add(cell.PartName);
        }

        foreach (DocxTableCellSnapshot cell in after ?? Array.Empty<DocxTableCellSnapshot>())
        {
            names.Add(cell.PartName);
        }

        return names;
    }

    /// <summary>Deterministic truncated preview used inside summaries.</summary>
    public static string Preview(string text) =>
        text.Length <= SummaryPreviewLength
            ? text
            : string.Concat(text.AsSpan(0, SummaryPreviewLength - 3), "...");

    private sealed class PartDiff
    {
        public List<(int OldIndex, int NewIndex)> Unchanged { get; } = new();

        public List<Region> Regions { get; } = new();
    }

    private sealed class Region
    {
        public List<int> Removed { get; } = new();

        public List<int> Added { get; } = new();
    }

    /// <summary>Canonical direct run formatting of one snapshot run.</summary>
    private readonly record struct RunFormat(
        string FontFamily,
        string Size,
        string Bold,
        string Italic,
        string Underline)
    {
        public static RunFormat Of(DocxRunSnapshot run) => new(
            run.FontFamily ?? "inherit",
            run.SizeHalfPoints is int halfPoints
                ? (halfPoints / 2.0).ToString("0.###", CultureInfo.InvariantCulture) + "pt"
                : "inherit",
            TriState(run.Bold),
            TriState(run.Italic),
            run.Underline ?? "inherit");

        public List<(string Field, string Before, string After)> Differences(RunFormat other)
        {
            var differences = new List<(string Field, string Before, string After)>();
            Add("FontFamily", FontFamily, other.FontFamily);
            Add("FontSize", Size, other.Size);
            Add("Bold", Bold, other.Bold);
            Add("Italic", Italic, other.Italic);
            Add("Underline", Underline, other.Underline);
            return differences;

            void Add(string field, string before, string after)
            {
                if (!string.Equals(before, after, StringComparison.Ordinal))
                {
                    differences.Add((field, before, after));
                }
            }
        }

        public string Describe() =>
            $"font={FontFamily};size={Size};bold={Bold};italic={Italic};underline={Underline}";

        private static string TriState(bool? value) =>
            value switch
            {
                true => "on",
                false => "off",
                _ => "inherit",
            };
    }
}
