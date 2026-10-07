using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using DocumentIDE.Core.Docx;

namespace DocumentIDE.Core.Batch.Operations;

/// <summary>Page orientation as requested by a strict SetOrientation operation (OOXML w:orient).</summary>
public enum PageOrientationKind
{
    Portrait,
    Landscape
}

/// <summary>Paragraph justification as requested by a strict SetAlignment operation (OOXML w:jc values).</summary>
public enum ParagraphAlignmentKind
{
    Left,
    Center,
    Right,
    Justify
}

/// <summary>
/// OOXML value mapping and geometry helpers shared by the BAT-09 operations and
/// the Infrastructure mutator, so preview display, mutation and verification can
/// never disagree on the representation of a value.
/// </summary>
public static class PageLayoutOoxml
{
    /// <summary>Maps an orientation to its OOXML w:orient value.</summary>
    public static string ToOoxmlValue(PageOrientationKind orientation) => orientation switch
    {
        PageOrientationKind.Portrait => "portrait",
        PageOrientationKind.Landscape => "landscape",
        _ => throw new ArgumentOutOfRangeException(nameof(orientation))
    };

    /// <summary>Maps an alignment to its OOXML w:jc value ("both" is Word's justify).</summary>
    public static string ToOoxmlValue(ParagraphAlignmentKind alignment) => alignment switch
    {
        ParagraphAlignmentKind.Left => "left",
        ParagraphAlignmentKind.Center => "center",
        ParagraphAlignmentKind.Right => "right",
        ParagraphAlignmentKind.Justify => "both",
        _ => throw new ArgumentOutOfRangeException(nameof(alignment))
    };

    /// <summary>Absent/empty w:orient means portrait; otherwise the raw attribute value is preserved.</summary>
    public static string NormalizeOrientation(string? orientValue) =>
        string.IsNullOrEmpty(orientValue) ? "portrait" : orientValue;

    /// <summary>
    /// True when the stored width/height are ordered the opposite way for the
    /// requested orientation (landscape expects width &gt;= height, portrait
    /// expects height &gt;= width) and must be swapped to preserve the physical
    /// page size.
    /// </summary>
    public static bool NeedsDimensionSwap(PageOrientationKind orientation, int widthTwips, int heightTwips) => orientation switch
    {
        PageOrientationKind.Landscape => widthTwips < heightTwips,
        PageOrientationKind.Portrait => widthTwips > heightTwips,
        _ => false
    };

    /// <summary>Human-readable section label for previews (spec 07 §19 uses 1-based sections).</summary>
    public static string SectionContext(int sectionIndex) => $"Section {sectionIndex + 1}";
}

/// <summary>
/// A reviewed plan file bound to its on-disk path, the preview fingerprint that
/// was reviewed, and the occurrence IDs the user selected for mutation.
/// </summary>
public sealed record PageLayoutChangeTarget(
    string FilePath,
    string ExpectedFingerprint,
    IReadOnlyList<string> SelectedOccurrenceIds);

/// <summary>BAT-09 — SetMargins parameters (spec 07 §19). OOXML w:pgMar unit is twips (1/20 pt).</summary>
public sealed record SetMarginsParameters
{
    public int TopTwips { get; init; }
    public int RightTwips { get; init; }
    public int BottomTwips { get; init; }
    public int LeftTwips { get; init; }
}

/// <summary>BAT-09 — SetPaperSize parameters (spec 07 §20). Absolute page dimensions in twips; orientation is never implied.</summary>
public sealed record SetPaperSizeParameters
{
    public int WidthTwips { get; init; }
    public int HeightTwips { get; init; }
}

/// <summary>BAT-09 — SetOrientation parameters (spec 07 §2).</summary>
public sealed record SetOrientationParameters
{
    public PageOrientationKind Orientation { get; init; }
}

/// <summary>BAT-09 — SetAlignment parameters (spec 06 §20).</summary>
public sealed record SetAlignmentParameters
{
    public ParagraphAlignmentKind Alignment { get; init; }
}

/// <summary>Canonical Word paper sizes in twips (1/20 pt).</summary>
public static class PaperSizeTwips
{
    public const int A4Width = 11906;
    public const int A4Height = 16838;
    public const int LetterWidth = 12240;
    public const int LetterHeight = 15840;
}

/// <summary>
/// How a previewed page-layout target relates to the requested value (spec 07 §19).
/// <see cref="Changed"/> targets differ from the request and are applicable;
/// <see cref="Unchanged"/> targets already equal the request (rendered as
/// “unchanged”, current == new); <see cref="Unspecified"/> targets carry no
/// explicit geometry/formatting and are outside V1’s mutation scope. Only
/// <see cref="Changed"/> entries are applicable — the others are preview-only and
/// never selectable for apply.
/// </summary>
public enum PageLayoutTargetStatus
{
    Changed,
    Unchanged,
    Unspecified
}

/// <summary>
/// The reviewed SetMargins preview (spec 07 §19): every section of the document
/// with a typed status, so the spec’s full shape (“Section 1: old→new”,
/// “Section 2: unchanged”) is producible. <see cref="AllSectionsTargeted"/>
/// states whether the operation’s scope covers all sections of the document
/// (“If operation targets all sections, state so.”).
/// </summary>
public sealed record MarginsChangePreview(
    IReadOnlyList<MarginsChangePreviewOccurrence> Sections,
    bool AllSectionsTargeted);

/// <summary>
/// The reviewed SetPaperSize preview (spec 07 §20): every section with a typed
/// status; <see cref="AllSectionsTargeted"/> states whether the operation’s
/// scope covers all sections.
/// </summary>
public sealed record PaperSizeChangePreview(
    IReadOnlyList<PaperSizeChangePreviewOccurrence> Sections,
    bool AllSectionsTargeted);

/// <summary>
/// The reviewed SetOrientation preview: every section with a typed status;
/// <see cref="AllSectionsTargeted"/> states whether the operation’s scope covers
/// all sections.
/// </summary>
public sealed record OrientationChangePreview(
    IReadOnlyList<OrientationChangePreviewOccurrence> Sections,
    bool AllSectionsTargeted);

/// <summary>
/// The reviewed SetAlignment preview: every paragraph that carries a direct w:jc
/// justification with a typed status. Paragraphs without direct justification
/// are outside the operation’s target scope (spec 06 §21) and are not listed;
/// they are never conflated with <see cref="PageLayoutTargetStatus.Unchanged"/>.
/// </summary>
public sealed record AlignmentChangePreview(
    IReadOnlyList<AlignmentChangePreviewOccurrence> Paragraphs);

/// <summary>
/// A previewed per-section margin row: stable occurrence ID, section identity,
/// display context, current→new four-margin values in twips, and the typed
/// status. Current values are null when the section has no explicit w:pgMar
/// (<see cref="PageLayoutTargetStatus.Unspecified"/>). Only
/// <see cref="PageLayoutTargetStatus.Changed"/> rows are selected; the others
/// are preview-only.
/// </summary>
public sealed record MarginsChangePreviewOccurrence(
    string Id,
    int SectionIndex,
    string Context,
    int? CurrentTopTwips,
    int? CurrentRightTwips,
    int? CurrentBottomTwips,
    int? CurrentLeftTwips,
    int NewTopTwips,
    int NewRightTwips,
    int NewBottomTwips,
    int NewLeftTwips,
    PageLayoutTargetStatus Status,
    bool Selected);

/// <summary>A previewed per-section paper-size row with current→new dimensions and typed status.</summary>
public sealed record PaperSizeChangePreviewOccurrence(
    string Id,
    int SectionIndex,
    string Context,
    int? CurrentWidthTwips,
    int? CurrentHeightTwips,
    int NewWidthTwips,
    int NewHeightTwips,
    PageLayoutTargetStatus Status,
    bool Selected);

/// <summary>
/// A previewed per-section orientation row. The new dimensions are the exact
/// dimensions the mutation will install (the section’s physical size is
/// preserved, so a real orientation change swaps the stored width/height).
/// Current/new dimensions are null when the section has no explicit w:pgSz.
/// </summary>
public sealed record OrientationChangePreviewOccurrence(
    string Id,
    int SectionIndex,
    string Context,
    string? CurrentOrientation,
    string NewOrientation,
    int? CurrentWidthTwips,
    int? CurrentHeightTwips,
    int? NewWidthTwips,
    int? NewHeightTwips,
    PageLayoutTargetStatus Status,
    bool Selected);

/// <summary>A previewed per-paragraph alignment row: stable ID, origin, text context, current→new w:jc value, typed status.</summary>
public sealed record AlignmentChangePreviewOccurrence(
    string Id,
    string Origin,
    string Context,
    string? CurrentAlignment,
    string NewAlignment,
    PageLayoutTargetStatus Status,
    bool Selected);

/// <summary>A reviewed section for strict margin mutation with its reviewed expected values.</summary>
public sealed record DocxMarginsTarget(
    string PartName,
    int SectionIndex,
    int ExpectedTopTwips,
    int ExpectedRightTwips,
    int ExpectedBottomTwips,
    int ExpectedLeftTwips);

/// <summary>A reviewed section for strict paper-size mutation with its reviewed expected dimensions.</summary>
public sealed record DocxPaperSizeTarget(
    string PartName,
    int SectionIndex,
    int ExpectedWidthTwips,
    int ExpectedHeightTwips);

/// <summary>A reviewed section for strict orientation mutation with its reviewed expected state.</summary>
public sealed record DocxOrientationTarget(
    string PartName,
    int SectionIndex,
    string ExpectedOrientation,
    int ExpectedWidthTwips,
    int ExpectedHeightTwips);

/// <summary>A reviewed paragraph for strict alignment mutation with its reviewed expected w:jc value.</summary>
public sealed record DocxAlignmentTarget(
    string PartName,
    int ParagraphIndex,
    string ExpectedAlignment);

/// <summary>
/// Section/paragraph-level page-layout mutation seam. Implemented in
/// Infrastructure by <c>StrictPageLayoutMutator</c> so the Core operations never
/// perform raw zip/XML mutation directly (spec 06 §20–21, spec 07 §19–20).
/// </summary>
public interface IDocxPageLayoutMutator
{
    void SetMargins(string filePath, IReadOnlyList<DocxMarginsTarget> targets, int topTwips, int rightTwips, int bottomTwips, int leftTwips);
    void SetPaperSize(string filePath, IReadOnlyList<DocxPaperSizeTarget> targets, int widthTwips, int heightTwips);
    void SetOrientation(string filePath, IReadOnlyList<DocxOrientationTarget> targets, PageOrientationKind orientation);
    void SetParagraphAlignment(string filePath, IReadOnlyList<DocxAlignmentTarget> targets, ParagraphAlignmentKind alignment);
}

/// <summary>
/// One section (wordprocessingml w:sectPr) in document order. A section without
/// an explicit value (for example no w:pgMar) is reported with nulls and is not
/// a valid witness target: V1 never invents page geometry.
/// </summary>
public sealed record DocxSectionInfo(
    string PartName,
    int SectionIndex,
    XElement SectPr,
    int? MarginTopTwips,
    int? MarginRightTwips,
    int? MarginBottomTwips,
    int? MarginLeftTwips,
    int? PageWidthTwips,
    int? PageHeightTwips,
    string Orientation)
{
    /// <summary>True when all four w:pgMar margin values are explicitly present.</summary>
    public bool HasMargins =>
        MarginTopTwips is not null && MarginRightTwips is not null &&
        MarginBottomTwips is not null && MarginLeftTwips is not null;

    /// <summary>True when both w:pgSz page dimensions are explicitly present.</summary>
    public bool HasPageSize => PageWidthTwips is not null && PageHeightTwips is not null;
}

/// <summary>One paragraph with its direct justification (BAT-09 alignment scan).</summary>
public sealed record DocxAlignmentParagraph(
    string PartName,
    DocxStructureOrigin Origin,
    int ParagraphIndex,
    XElement Paragraph,
    string? DirectAlignment,
    string ParagraphText);

/// <summary>
/// BAT-09 — deterministic DOCX page-layout scan. Sections are the w:sectPr
/// elements of word/document.xml in document order (paragraph-level section
/// breaks then the body-level final section), so preview occurrence IDs and
/// apply-time targets are single-sourced and can never drift.
/// </summary>
public static class DocxPageLayoutScan
{
    /// <summary>Occurrence budget, identical to the BAT-03/BAT-05/BAT-08 preview budget.</summary>
    public const int OccurrenceBudget = 10000;

    /// <summary>Smallest page dimension accepted by the operations (twips).</summary>
    public const int MinPageTwips = 1;

    /// <summary>Largest page dimension accepted by the operations: 22 in (Word's practical limit), in twips.</summary>
    public const int MaxTwips = 31680;

    private static readonly XNamespace WNs = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static readonly XmlReaderSettings XmlSettings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        IgnoreComments = true,
        IgnoreWhitespace = false
    };

    /// <summary>Reads the sections of a DOCX package stream (word/document.xml only).</summary>
    public static IReadOnlyList<DocxSectionInfo> ScanSectionsStream(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        cancellationToken.ThrowIfCancellationRequested();

        using var archive = OpenArchive(stream);
        var entry = IdentifyMainDocumentPart(archive)
            ?? throw new InvalidOperationException("Invalid or corrupted DOCX package: word/document.xml was not found.");
        string partName = DocxFontScan.NormalizePartName(entry.FullName);
        var xdoc = LoadPart(entry, partName);
        return EnumerateSections(partName, xdoc);
    }

    /// <summary>
    /// Enumerates the w:sectPr elements of a part in document order. The last
    /// sectPr is the body-level final section; earlier ones are paragraph-level
    /// section breaks.
    /// </summary>
    public static IReadOnlyList<DocxSectionInfo> EnumerateSections(string partName, XDocument xdoc)
    {
        ArgumentNullException.ThrowIfNull(xdoc);

        var sections = new List<DocxSectionInfo>();
        int sectionIndex = 0;
        foreach (var sectPr in xdoc.Descendants(WNs + "sectPr"))
        {
            var pgMar = sectPr.Element(WNs + "pgMar");
            var pgSz = sectPr.Element(WNs + "pgSz");
            sections.Add(new DocxSectionInfo(
                partName,
                sectionIndex++,
                sectPr,
                ReadTwips(pgMar, "top"),
                ReadTwips(pgMar, "right"),
                ReadTwips(pgMar, "bottom"),
                ReadTwips(pgMar, "left"),
                ReadTwips(pgSz, "w"),
                ReadTwips(pgSz, "h"),
                PageLayoutOoxml.NormalizeOrientation((string?)pgSz?.Attribute(WNs + "orient"))));
        }

        return sections;
    }

    /// <summary>
    /// Reads every paragraph of a DOCX package stream in body, table, header and
    /// footer parts (word/document.xml, word/header*.xml, word/footer*.xml) with
    /// the same logical paragraph indexing used by the BAT-05 logical map and the
    /// BAT-08 run scan.
    /// </summary>
    public static IReadOnlyList<DocxAlignmentParagraph> ScanAlignmentStream(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        cancellationToken.ThrowIfCancellationRequested();

        using var archive = OpenArchive(stream);
        var paragraphs = new List<DocxAlignmentParagraph>();

        var mainEntry = IdentifyMainDocumentPart(archive);
        if (mainEntry != null)
        {
            string mainPartName = NormalizePartName(mainEntry.FullName);
            paragraphs.AddRange(EnumerateAlignmentParagraphs(mainPartName, LoadPart(mainEntry, mainPartName)));
        }

        foreach (var entry in IdentifyHeaderFooterParts(archive))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string partName = NormalizePartName(entry.FullName);
            paragraphs.AddRange(EnumerateAlignmentParagraphs(partName, LoadPart(entry, partName)));
        }

        return paragraphs;
    }

    /// <summary>
    /// Enumerates a part's paragraphs in document order with their direct
    /// w:pPr/w:jc justification (null when justification is style-inherited) and
    /// visible paragraph text. Paragraph indexes count every w:p encountered,
    /// matching the shared logical map and run-scan conventions.
    /// </summary>
    public static IReadOnlyList<DocxAlignmentParagraph> EnumerateAlignmentParagraphs(string partName, XDocument xdoc)
    {
        ArgumentNullException.ThrowIfNull(xdoc);

        var paragraphs = new List<DocxAlignmentParagraph>();
        int paragraphIndex = -1;
        foreach (var pElem in xdoc.Descendants(WNs + "p"))
        {
            paragraphIndex++;
            var jc = pElem.Element(WNs + "pPr")?.Element(WNs + "jc");
            string? alignment = (string?)jc?.Attribute(WNs + "val");
            string text = DocxFontScan.EnumerateParagraphRuns(pElem).ParagraphText;
            paragraphs.Add(new DocxAlignmentParagraph(
                partName,
                ResolveOrigin(pElem, partName),
                paragraphIndex,
                pElem,
                alignment,
                text));
        }

        return paragraphs;
    }

    internal static ZipArchive OpenArchive(Stream stream)
    {
        try
        {
            return new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (Exception ex) when (ex is InvalidDataException || ex is IOException || ex is NotSupportedException)
        {
            throw new InvalidOperationException($"Invalid or corrupted DOCX ZIP archive: {ex.Message}", ex);
        }
    }

    internal static XDocument LoadPart(ZipArchiveEntry entry, string partName)
    {
        try
        {
            using var entryStream = entry.Open();
            using var xmlReader = XmlReader.Create(entryStream, XmlSettings);
            return XDocument.Load(xmlReader);
        }
        catch (Exception ex) when (ex is XmlException || ex is InvalidDataException)
        {
            throw new InvalidOperationException($"Malformed or corrupted XML in part '{partName}': {ex.Message}", ex);
        }
    }

    internal static ZipArchiveEntry? IdentifyMainDocumentPart(ZipArchive archive) =>
        archive.Entries.FirstOrDefault(e =>
            string.Equals(NormalizePartName(e.FullName), "word/document.xml", StringComparison.OrdinalIgnoreCase));

    internal static List<ZipArchiveEntry> IdentifyHeaderFooterParts(ZipArchive archive)
    {
        var parts = new List<ZipArchiveEntry>();
        foreach (var entry in archive.Entries)
        {
            string normPath = DocxFontScan.NormalizePartName(entry.FullName).ToLowerInvariant();
            if ((normPath.StartsWith("word/header", StringComparison.Ordinal) ||
                 normPath.StartsWith("word/footer", StringComparison.Ordinal)) &&
                normPath.EndsWith(".xml", StringComparison.Ordinal))
            {
                parts.Add(entry);
            }
        }

        return parts;
    }

    /// <summary>Normalizes a part name to forward slashes for ordinal comparison.</summary>
    public static string NormalizePartName(string name) => DocxFontScan.NormalizePartName(name);

    private static int? ReadTwips(XElement? parent, string attributeLocalName)
    {
        if (parent == null) return null;
        var value = (string?)parent.Attribute(WNs + attributeLocalName);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var twips) ? twips : null;
    }

    private static DocxStructureOrigin ResolveOrigin(XElement pElem, string partName)
    {
        if (pElem.Ancestors().Any(a => a.Name.LocalName == "tbl"))
            return DocxStructureOrigin.Table;

        string normPart = partName.ToLowerInvariant();
        if (normPart.Contains("header")) return DocxStructureOrigin.Header;
        if (normPart.Contains("footer")) return DocxStructureOrigin.Footer;
        return DocxStructureOrigin.Body;
    }
}

/// <summary>
/// Shared fail-closed resolution of the reviewed plan file that owns a temp copy
/// being mutated by <c>BatchApplyEngine</c> (BAT-05/BAT-08 parity: directory +
/// SHA-256 fingerprint, ambiguity fails closed).
/// </summary>
internal static class PageLayoutTargetResolver
{
    public static PageLayoutChangeTarget ResolveTarget(string tempPath, IReadOnlyList<PageLayoutChangeTarget> targets)
    {
        var tempDirectory = Path.GetDirectoryName(Path.GetFullPath(tempPath))
            ?? throw new InvalidOperationException("Cannot determine the directory of the file being mutated.");

        string fingerprint;
        using (var stream = File.OpenRead(tempPath))
            fingerprint = Convert.ToHexString(SHA256.HashData(stream));

        var candidates = new List<PageLayoutChangeTarget>();
        foreach (var target in targets)
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(target.FilePath));
            if (string.Equals(directory, tempDirectory, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(target.ExpectedFingerprint, fingerprint, StringComparison.Ordinal))
                candidates.Add(target);
        }

        if (candidates.Count == 0)
            throw new InvalidOperationException(
                "ApplyFingerprintMismatch: the file being mutated does not match its reviewed preview fingerprint.");

        // Byte-identical copies of the same content in one directory are only safe
        // to apply when their reviewed occurrence selections agree; otherwise the
        // correct selection is unknowable here, so mutation fails closed.
        var selections = candidates
            .Select(c => SelectionKey(c.SelectedOccurrenceIds))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (selections.Count > 1)
            throw new InvalidOperationException(
                "AmbiguousApplyTarget: multiple reviewed files match this file with different occurrence selections.");

        return candidates[0];

        static string SelectionKey(IReadOnlyList<string>? ids) => ids == null
            ? string.Empty
            : string.Join('\u001f', ids.Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal));
    }
}

/// <summary>
/// Review-selection semantics shared by the BAT-09 operations. Unchanged and
/// Unspecified rows are preview-only: they never enter a mutator target list.
/// A selected ID that does not exist in the preview at all fails closed
/// (BAT-05/BAT-08 parity) instead of being silently ignored.
/// </summary>
internal static class PageLayoutSelection
{
    public static void EnsureAllSelectedKnown(HashSet<string> selected, IEnumerable<string> knownIds)
    {
        var known = new HashSet<string>(knownIds, StringComparer.Ordinal);
        int unknown = 0;
        foreach (var id in selected)
        {
            if (!known.Contains(id)) unknown++;
        }

        if (unknown > 0)
        {
            throw new InvalidOperationException(
                $"SelectedOccurrenceNotFound: {unknown} of {selected.Count} selected occurrence IDs were not found ({known.Count} previewed targets present).");
        }
    }
}

/// <summary>
/// BAT-09 — Strict Set Paper Size operation (spec 07 §20). Changes only the
/// reviewed sections' w:pgSz @w:w/@w:h values; margins, orientation and every
/// other property are untouched. Sections without explicit page dimensions are
/// listed as <see cref="PageLayoutTargetStatus.Unspecified"/> and are never
/// applied (V1 never invents page geometry); already-at-target sections are
/// listed as <see cref="PageLayoutTargetStatus.Unchanged"/> (current == new,
/// preview-only). Only <see cref="PageLayoutTargetStatus.Changed"/> sections are
/// applicable.
/// </summary>
public sealed class SetPaperSizeOperation
{
    private readonly IDocxPageLayoutMutator _mutator;

    public SetPaperSizeOperation(IDocxPageLayoutMutator mutator)
    {
        _mutator = mutator ?? throw new ArgumentNullException(nameof(mutator));
    }

    private static void ValidateParameters(SetPaperSizeParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (parameters.WidthTwips < DocxPageLayoutScan.MinPageTwips || parameters.WidthTwips > DocxPageLayoutScan.MaxTwips ||
            parameters.HeightTwips < DocxPageLayoutScan.MinPageTwips || parameters.HeightTwips > DocxPageLayoutScan.MaxTwips)
        {
            throw new ArgumentException("InvalidParameters", nameof(parameters));
        }
    }

    /// <summary>
    /// Builds the reviewed per-section page-size preview: every section of the
    /// document in document order with a typed status — Changed, Unchanged
    /// (current == requested; preview-only) or Unspecified (no explicit w:pgSz;
    /// never applicable). <see cref="PaperSizeChangePreview.AllSectionsTargeted"/>
    /// states when every section is in the operation’s scope. The source file is
    /// never modified.
    /// </summary>
    public async Task<PaperSizeChangePreview> BuildOccurrencesAsync(
        string filePath,
        SetPaperSizeParameters parameters,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateParameters(parameters);

        if (!SearchReplaceOperation.IsDocx(filePath))
            return new PaperSizeChangePreview(Array.Empty<PaperSizeChangePreviewOccurrence>(), AllSectionsTargeted: false);

        var bytes = await SearchReplaceOperation.ReadBoundedAsync(filePath, cancellationToken);
        using var stream = new MemoryStream(bytes, writable: false);
        var sections = DocxPageLayoutScan.ScanSectionsStream(stream, cancellationToken);
        return BuildPreview(sections, parameters);
    }

    private static PaperSizeChangePreview BuildPreview(IReadOnlyList<DocxSectionInfo> sections, SetPaperSizeParameters parameters)
    {
        var rows = new List<PaperSizeChangePreviewOccurrence>();
        foreach (var section in sections)
        {
            if (rows.Count == DocxPageLayoutScan.OccurrenceBudget)
                throw new InvalidDataException("OccurrenceBudgetExceeded");

            PageLayoutTargetStatus status;
            if (!section.HasPageSize)
                status = PageLayoutTargetStatus.Unspecified;
            else if (section.PageWidthTwips == parameters.WidthTwips && section.PageHeightTwips == parameters.HeightTwips)
                status = PageLayoutTargetStatus.Unchanged;
            else
                status = PageLayoutTargetStatus.Changed;

            rows.Add(new PaperSizeChangePreviewOccurrence(
                PaperSizeOccurrenceId(section),
                section.SectionIndex,
                PageLayoutOoxml.SectionContext(section.SectionIndex),
                section.PageWidthTwips,
                section.PageHeightTwips,
                parameters.WidthTwips,
                parameters.HeightTwips,
                status,
                Selected: status == PageLayoutTargetStatus.Changed));
        }

        return new PaperSizeChangePreview(rows, sections.Count > 0 && sections.All(s => s.HasPageSize));
    }

    /// <summary>
    /// Creates the mutate delegate for <c>BatchApplyEngine.ApplyAsync</c>. The
    /// delegate identifies the reviewed plan file by directory + preview
    /// fingerprint and mutates only the reviewed, selected <b>Changed</b>
    /// sections; Unchanged/Unspecified rows are preview-only and can never enter
    /// the target list.
    /// </summary>
    public Func<string, CancellationToken, Task> CreateMutate(
        SetPaperSizeParameters parameters,
        IReadOnlyList<PageLayoutChangeTarget> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ValidateParameters(parameters);

        async Task Mutate(string tempPath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = PageLayoutTargetResolver.ResolveTarget(tempPath, targets);
            var selected = new HashSet<string>(target.SelectedOccurrenceIds ?? Array.Empty<string>(), StringComparer.Ordinal);
            if (selected.Count == 0)
                return; // nothing was reviewed/selected for this file → no change

            var bytes = await SearchReplaceOperation.ReadBoundedAsync(tempPath, cancellationToken);
            using var stream = new MemoryStream(bytes, writable: false);
            var sections = DocxPageLayoutScan.ScanSectionsStream(stream, cancellationToken);
            var preview = BuildPreview(sections, parameters);
            PageLayoutSelection.EnsureAllSelectedKnown(selected, preview.Sections.Select(r => r.Id));

            var byId = new Dictionary<string, DocxSectionInfo>(StringComparer.Ordinal);
            foreach (var section in sections)
                byId[PaperSizeOccurrenceId(section)] = section;

            var chosen = new List<DocxPaperSizeTarget>();
            foreach (var row in preview.Sections)
            {
                if (row.Status != PageLayoutTargetStatus.Changed || !selected.Contains(row.Id)) continue;
                var section = byId[row.Id];
                chosen.Add(new DocxPaperSizeTarget(
                    section.PartName, section.SectionIndex, section.PageWidthTwips!.Value, section.PageHeightTwips!.Value));
            }

            if (chosen.Count == 0)
                return; // nothing applicable was selected → no change (bytes preserved)

            _mutator.SetPaperSize(tempPath, chosen, parameters.WidthTwips, parameters.HeightTwips);
        }

        return Mutate;
    }

    internal static string PaperSizeOccurrenceId(DocxSectionInfo section) =>
        $"papersize_{section.PartName}_s{section.SectionIndex}";
}

/// <summary>
/// BAT-09 — Strict Set Margins operation (spec 07 §19). Changes only the reviewed
/// sections' four w:pgMar values; fonts, alignment, page size and every other
/// property are untouched. Sections without explicit margins are listed as
/// <see cref="PageLayoutTargetStatus.Unspecified"/> and are never applied (V1
/// never invents page geometry); already-at-target sections are listed as
/// <see cref="PageLayoutTargetStatus.Unchanged"/> (current == new,
/// preview-only), so the spec’s “Section 2: unchanged” rendering is producible.
/// Only <see cref="PageLayoutTargetStatus.Changed"/> sections are applicable.
/// </summary>
public sealed class SetMarginsOperation
{
    private readonly IDocxPageLayoutMutator _mutator;

    public SetMarginsOperation(IDocxPageLayoutMutator mutator)
    {
        _mutator = mutator ?? throw new ArgumentNullException(nameof(mutator));
    }

    private static void ValidateParameters(SetMarginsParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (!IsValidTwips(parameters.TopTwips) || !IsValidTwips(parameters.RightTwips) ||
            !IsValidTwips(parameters.BottomTwips) || !IsValidTwips(parameters.LeftTwips))
        {
            throw new ArgumentException("InvalidParameters", nameof(parameters));
        }

        // Margins may legitimately be zero; they may not exceed the page bound.
        static bool IsValidTwips(int twips) => twips >= 0 && twips <= DocxPageLayoutScan.MaxTwips;
    }

    /// <summary>
    /// Builds the reviewed per-section margin preview: every section of the
    /// document in document order with a typed status — Changed, Unchanged
    /// (current == requested; preview-only) or Unspecified (no explicit w:pgMar;
    /// never applicable, so an out-of-scope section is never conflated with an
    /// unchanged one). <see cref="MarginsChangePreview.AllSectionsTargeted"/>
    /// states when every section is in the operation’s scope (spec 07 §19 “If
    /// operation targets all sections, state so.”). The source file is never
    /// modified.
    /// </summary>
    public async Task<MarginsChangePreview> BuildOccurrencesAsync(
        string filePath,
        SetMarginsParameters parameters,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateParameters(parameters);

        if (!SearchReplaceOperation.IsDocx(filePath))
            return new MarginsChangePreview(Array.Empty<MarginsChangePreviewOccurrence>(), AllSectionsTargeted: false);

        var bytes = await SearchReplaceOperation.ReadBoundedAsync(filePath, cancellationToken);
        using var stream = new MemoryStream(bytes, writable: false);
        var sections = DocxPageLayoutScan.ScanSectionsStream(stream, cancellationToken);
        return BuildPreview(sections, parameters);
    }

    private static MarginsChangePreview BuildPreview(IReadOnlyList<DocxSectionInfo> sections, SetMarginsParameters parameters)
    {
        var rows = new List<MarginsChangePreviewOccurrence>();
        foreach (var section in sections)
        {
            if (rows.Count == DocxPageLayoutScan.OccurrenceBudget)
                throw new InvalidDataException("OccurrenceBudgetExceeded");

            PageLayoutTargetStatus status;
            if (!section.HasMargins)
                status = PageLayoutTargetStatus.Unspecified;
            else if (section.MarginTopTwips == parameters.TopTwips &&
                     section.MarginRightTwips == parameters.RightTwips &&
                     section.MarginBottomTwips == parameters.BottomTwips &&
                     section.MarginLeftTwips == parameters.LeftTwips)
                status = PageLayoutTargetStatus.Unchanged;
            else
                status = PageLayoutTargetStatus.Changed;

            rows.Add(new MarginsChangePreviewOccurrence(
                MarginsOccurrenceId(section),
                section.SectionIndex,
                PageLayoutOoxml.SectionContext(section.SectionIndex),
                section.MarginTopTwips,
                section.MarginRightTwips,
                section.MarginBottomTwips,
                section.MarginLeftTwips,
                parameters.TopTwips,
                parameters.RightTwips,
                parameters.BottomTwips,
                parameters.LeftTwips,
                status,
                Selected: status == PageLayoutTargetStatus.Changed));
        }

        return new MarginsChangePreview(rows, sections.Count > 0 && sections.All(s => s.HasMargins));
    }

    /// <summary>
    /// Creates the mutate delegate for <c>BatchApplyEngine.ApplyAsync</c>. The
    /// delegate identifies the reviewed plan file by directory + preview
    /// fingerprint and mutates only the reviewed, selected <b>Changed</b>
    /// sections; Unchanged/Unspecified rows are preview-only and can never enter
    /// the target list.
    /// </summary>
    public Func<string, CancellationToken, Task> CreateMutate(
        SetMarginsParameters parameters,
        IReadOnlyList<PageLayoutChangeTarget> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ValidateParameters(parameters);

        async Task Mutate(string tempPath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = PageLayoutTargetResolver.ResolveTarget(tempPath, targets);
            var selected = new HashSet<string>(target.SelectedOccurrenceIds ?? Array.Empty<string>(), StringComparer.Ordinal);
            if (selected.Count == 0)
                return; // nothing was reviewed/selected for this file → no change

            var bytes = await SearchReplaceOperation.ReadBoundedAsync(tempPath, cancellationToken);
            using var stream = new MemoryStream(bytes, writable: false);
            var sections = DocxPageLayoutScan.ScanSectionsStream(stream, cancellationToken);
            var preview = BuildPreview(sections, parameters);
            PageLayoutSelection.EnsureAllSelectedKnown(selected, preview.Sections.Select(r => r.Id));

            var byId = new Dictionary<string, DocxSectionInfo>(StringComparer.Ordinal);
            foreach (var section in sections)
                byId[MarginsOccurrenceId(section)] = section;

            var chosen = new List<DocxMarginsTarget>();
            foreach (var row in preview.Sections)
            {
                if (row.Status != PageLayoutTargetStatus.Changed || !selected.Contains(row.Id)) continue;
                var section = byId[row.Id];
                chosen.Add(new DocxMarginsTarget(
                    section.PartName,
                    section.SectionIndex,
                    section.MarginTopTwips!.Value,
                    section.MarginRightTwips!.Value,
                    section.MarginBottomTwips!.Value,
                    section.MarginLeftTwips!.Value));
            }

            if (chosen.Count == 0)
                return; // nothing applicable was selected → no change (bytes preserved)

            _mutator.SetMargins(tempPath, chosen, parameters.TopTwips, parameters.RightTwips, parameters.BottomTwips, parameters.LeftTwips);
        }

        return Mutate;
    }

    internal static string MarginsOccurrenceId(DocxSectionInfo section) =>
        $"margin_{section.PartName}_s{section.SectionIndex}";
}

/// <summary>
/// BAT-09 — Strict Set Orientation operation (spec 07 §2). Sets only the
/// reviewed sections' w:orient and ensures the stored dimensions are ordered for
/// that orientation (swapping width/height when required), preserving the
/// physical page size; margins and every other property are untouched.
/// </summary>
public sealed class SetOrientationOperation
{
    private readonly IDocxPageLayoutMutator _mutator;

    public SetOrientationOperation(IDocxPageLayoutMutator mutator)
    {
        _mutator = mutator ?? throw new ArgumentNullException(nameof(mutator));
    }

    private static void ValidateParameters(SetOrientationParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (!Enum.IsDefined(parameters.Orientation))
            throw new ArgumentException("InvalidParameters", nameof(parameters));
    }

    /// <summary>
    /// Builds the reviewed per-section orientation preview: every section of the
    /// document in document order with a typed status — Changed, Unchanged
    /// (already in the requested orientation with correctly ordered dimensions;
    /// preview-only) or Unspecified (no explicit w:pgSz; never applicable). The
    /// new dimensions shown are the exact dimensions the mutation will install.
    /// <see cref="OrientationChangePreview.AllSectionsTargeted"/> states when
    /// every section is in the operation’s scope. The source file is never
    /// modified.
    /// </summary>
    public async Task<OrientationChangePreview> BuildOccurrencesAsync(
        string filePath,
        SetOrientationParameters parameters,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateParameters(parameters);

        if (!SearchReplaceOperation.IsDocx(filePath))
            return new OrientationChangePreview(Array.Empty<OrientationChangePreviewOccurrence>(), AllSectionsTargeted: false);

        var bytes = await SearchReplaceOperation.ReadBoundedAsync(filePath, cancellationToken);
        using var stream = new MemoryStream(bytes, writable: false);
        var sections = DocxPageLayoutScan.ScanSectionsStream(stream, cancellationToken);
        return BuildPreview(sections, parameters);
    }

    private static OrientationChangePreview BuildPreview(IReadOnlyList<DocxSectionInfo> sections, SetOrientationParameters parameters)
    {
        string targetOrientation = PageLayoutOoxml.ToOoxmlValue(parameters.Orientation);
        var rows = new List<OrientationChangePreviewOccurrence>();
        foreach (var section in sections)
        {
            if (rows.Count == DocxPageLayoutScan.OccurrenceBudget)
                throw new InvalidDataException("OccurrenceBudgetExceeded");

            if (!section.HasPageSize)
            {
                rows.Add(new OrientationChangePreviewOccurrence(
                    OrientationOccurrenceId(section),
                    section.SectionIndex,
                    PageLayoutOoxml.SectionContext(section.SectionIndex),
                    CurrentOrientation: null,
                    targetOrientation,
                    CurrentWidthTwips: null,
                    CurrentHeightTwips: null,
                    NewWidthTwips: null,
                    NewHeightTwips: null,
                    PageLayoutTargetStatus.Unspecified,
                    Selected: false));
                continue;
            }

            int width = section.PageWidthTwips!.Value;
            int height = section.PageHeightTwips!.Value;
            bool needsSwap = PageLayoutOoxml.NeedsDimensionSwap(parameters.Orientation, width, height);
            var status = string.Equals(section.Orientation, targetOrientation, StringComparison.Ordinal) && !needsSwap
                ? PageLayoutTargetStatus.Unchanged
                : PageLayoutTargetStatus.Changed;

            rows.Add(new OrientationChangePreviewOccurrence(
                OrientationOccurrenceId(section),
                section.SectionIndex,
                PageLayoutOoxml.SectionContext(section.SectionIndex),
                section.Orientation,
                targetOrientation,
                width,
                height,
                needsSwap ? height : width,
                needsSwap ? width : height,
                status,
                Selected: status == PageLayoutTargetStatus.Changed));
        }

        return new OrientationChangePreview(rows, sections.Count > 0 && sections.All(s => s.HasPageSize));
    }

    /// <summary>
    /// Creates the mutate delegate for <c>BatchApplyEngine.ApplyAsync</c>. The
    /// delegate identifies the reviewed plan file by directory + preview
    /// fingerprint and mutates only the reviewed, selected <b>Changed</b>
    /// sections; Unchanged/Unspecified rows are preview-only and can never enter
    /// the target list.
    /// </summary>
    public Func<string, CancellationToken, Task> CreateMutate(
        SetOrientationParameters parameters,
        IReadOnlyList<PageLayoutChangeTarget> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ValidateParameters(parameters);

        async Task Mutate(string tempPath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = PageLayoutTargetResolver.ResolveTarget(tempPath, targets);
            var selected = new HashSet<string>(target.SelectedOccurrenceIds ?? Array.Empty<string>(), StringComparer.Ordinal);
            if (selected.Count == 0)
                return; // nothing was reviewed/selected for this file → no change

            var bytes = await SearchReplaceOperation.ReadBoundedAsync(tempPath, cancellationToken);
            using var stream = new MemoryStream(bytes, writable: false);
            var sections = DocxPageLayoutScan.ScanSectionsStream(stream, cancellationToken);
            var preview = BuildPreview(sections, parameters);
            PageLayoutSelection.EnsureAllSelectedKnown(selected, preview.Sections.Select(r => r.Id));

            var byId = new Dictionary<string, DocxSectionInfo>(StringComparer.Ordinal);
            foreach (var section in sections)
                byId[OrientationOccurrenceId(section)] = section;

            var chosen = new List<DocxOrientationTarget>();
            foreach (var row in preview.Sections)
            {
                if (row.Status != PageLayoutTargetStatus.Changed || !selected.Contains(row.Id)) continue;
                var section = byId[row.Id];
                chosen.Add(new DocxOrientationTarget(
                    section.PartName,
                    section.SectionIndex,
                    section.Orientation,
                    section.PageWidthTwips!.Value,
                    section.PageHeightTwips!.Value));
            }

            if (chosen.Count == 0)
                return; // nothing applicable was selected → no change (bytes preserved)

            _mutator.SetOrientation(tempPath, chosen, parameters.Orientation);
        }

        return Mutate;
    }

    internal static string OrientationOccurrenceId(DocxSectionInfo section) =>
        $"orientation_{section.PartName}_s{section.SectionIndex}";
}

/// <summary>
/// BAT-09 — Strict Set Alignment operation (spec 06 §20). Changes only the
/// reviewed paragraphs' direct w:jc justification; paragraphs whose alignment
/// comes from styles are deliberately outside the target scope and are not
/// listed (no flattening, spec 06 §21), and section properties are never
/// touched. Already-at-target paragraphs are listed as
/// <see cref="PageLayoutTargetStatus.Unchanged"/> (current == new,
/// preview-only); only <see cref="PageLayoutTargetStatus.Changed"/> paragraphs
/// are applicable. Targets body, table, header and footer paragraphs.
/// </summary>
public sealed class SetAlignmentOperation
{
    private readonly IDocxPageLayoutMutator _mutator;

    public SetAlignmentOperation(IDocxPageLayoutMutator mutator)
    {
        _mutator = mutator ?? throw new ArgumentNullException(nameof(mutator));
    }

    private static void ValidateParameters(SetAlignmentParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (!Enum.IsDefined(parameters.Alignment))
            throw new ArgumentException("InvalidParameters", nameof(parameters));
    }

    /// <summary>
    /// Builds the reviewed paragraph preview: every paragraph with a direct w:jc
    /// justification in part order → paragraph order with a typed status —
    /// Changed or Unchanged (current == requested; preview-only), plus origin and
    /// visible-text context. Paragraphs without direct justification are outside
    /// the operation’s target scope (spec 06 §21) and are not listed; they are
    /// never conflated with <see cref="PageLayoutTargetStatus.Unchanged"/>. The
    /// source file is never modified.
    /// </summary>
    public async Task<AlignmentChangePreview> BuildOccurrencesAsync(
        string filePath,
        SetAlignmentParameters parameters,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateParameters(parameters);

        if (!SearchReplaceOperation.IsDocx(filePath))
            return new AlignmentChangePreview(Array.Empty<AlignmentChangePreviewOccurrence>());

        var bytes = await SearchReplaceOperation.ReadBoundedAsync(filePath, cancellationToken);
        using var stream = new MemoryStream(bytes, writable: false);
        var paragraphs = DocxPageLayoutScan.ScanAlignmentStream(stream, cancellationToken);
        return BuildPreview(paragraphs, parameters);
    }

    private static AlignmentChangePreview BuildPreview(IReadOnlyList<DocxAlignmentParagraph> paragraphs, SetAlignmentParameters parameters)
    {
        string newValue = PageLayoutOoxml.ToOoxmlValue(parameters.Alignment);
        var rows = new List<AlignmentChangePreviewOccurrence>();
        foreach (var paragraph in paragraphs)
        {
            if (paragraph.DirectAlignment == null) continue; // outside the target scope; not listed
            if (rows.Count == DocxPageLayoutScan.OccurrenceBudget)
                throw new InvalidDataException("OccurrenceBudgetExceeded");

            var status = string.Equals(paragraph.DirectAlignment, newValue, StringComparison.Ordinal)
                ? PageLayoutTargetStatus.Unchanged
                : PageLayoutTargetStatus.Changed;

            rows.Add(new AlignmentChangePreviewOccurrence(
                AlignmentOccurrenceId(paragraph),
                paragraph.Origin.ToString(),
                SearchReplaceOperation.TextPreviewContext(
                    paragraph.ParagraphText, 0, Math.Min(paragraph.ParagraphText.Length, 30)),
                paragraph.DirectAlignment,
                newValue,
                status,
                Selected: status == PageLayoutTargetStatus.Changed));
        }

        return new AlignmentChangePreview(rows);
    }

    /// <summary>
    /// Creates the mutate delegate for <c>BatchApplyEngine.ApplyAsync</c>. The
    /// delegate identifies the reviewed plan file by directory + preview
    /// fingerprint and mutates only the reviewed, selected <b>Changed</b>
    /// paragraphs; Unchanged rows are preview-only and can never enter the target
    /// list.
    /// </summary>
    public Func<string, CancellationToken, Task> CreateMutate(
        SetAlignmentParameters parameters,
        IReadOnlyList<PageLayoutChangeTarget> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ValidateParameters(parameters);

        async Task Mutate(string tempPath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = PageLayoutTargetResolver.ResolveTarget(tempPath, targets);
            var selected = new HashSet<string>(target.SelectedOccurrenceIds ?? Array.Empty<string>(), StringComparer.Ordinal);
            if (selected.Count == 0)
                return; // nothing was reviewed/selected for this file → no change

            var bytes = await SearchReplaceOperation.ReadBoundedAsync(tempPath, cancellationToken);
            using var stream = new MemoryStream(bytes, writable: false);
            var paragraphs = DocxPageLayoutScan.ScanAlignmentStream(stream, cancellationToken);
            var preview = BuildPreview(paragraphs, parameters);
            PageLayoutSelection.EnsureAllSelectedKnown(selected, preview.Paragraphs.Select(r => r.Id));

            var byId = new Dictionary<string, DocxAlignmentParagraph>(StringComparer.Ordinal);
            foreach (var paragraph in paragraphs)
            {
                if (paragraph.DirectAlignment == null) continue;
                byId[AlignmentOccurrenceId(paragraph)] = paragraph;
            }

            var chosen = new List<DocxAlignmentTarget>();
            foreach (var row in preview.Paragraphs)
            {
                if (row.Status != PageLayoutTargetStatus.Changed || !selected.Contains(row.Id)) continue;
                var paragraph = byId[row.Id];
                chosen.Add(new DocxAlignmentTarget(
                    paragraph.PartName, paragraph.ParagraphIndex, paragraph.DirectAlignment!));
            }

            if (chosen.Count == 0)
                return; // nothing applicable was selected → no change (bytes preserved)

            _mutator.SetParagraphAlignment(tempPath, chosen, parameters.Alignment);
        }

        return Mutate;
    }

    internal static string AlignmentOccurrenceId(DocxAlignmentParagraph paragraph) =>
        $"align_{paragraph.PartName}_{paragraph.Origin}_p{paragraph.ParagraphIndex}";
}
