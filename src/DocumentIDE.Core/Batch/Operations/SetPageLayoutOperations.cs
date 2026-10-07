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
/// A previewed per-section margin change: stable occurrence ID, section identity,
/// display context, and the current→new four-margin values in twips.
/// </summary>
public sealed record MarginsChangePreviewOccurrence(
    string Id,
    int SectionIndex,
    string Context,
    int CurrentTopTwips,
    int CurrentRightTwips,
    int CurrentBottomTwips,
    int CurrentLeftTwips,
    int NewTopTwips,
    int NewRightTwips,
    int NewBottomTwips,
    int NewLeftTwips,
    bool Selected = true);

/// <summary>A previewed per-section paper-size change with current→new dimensions in twips.</summary>
public sealed record PaperSizeChangePreviewOccurrence(
    string Id,
    int SectionIndex,
    string Context,
    int CurrentWidthTwips,
    int CurrentHeightTwips,
    int NewWidthTwips,
    int NewHeightTwips,
    bool Selected = true);

/// <summary>
/// A previewed per-section orientation change. The new dimensions are the exact
/// dimensions the mutation will install (the section's physical size is
/// preserved, so a real orientation change swaps the stored width/height).
/// </summary>
public sealed record OrientationChangePreviewOccurrence(
    string Id,
    int SectionIndex,
    string Context,
    string CurrentOrientation,
    string NewOrientation,
    int CurrentWidthTwips,
    int CurrentHeightTwips,
    int NewWidthTwips,
    int NewHeightTwips,
    bool Selected = true);

/// <summary>A previewed per-paragraph alignment change: stable ID, origin, text context, current→new w:jc value.</summary>
public sealed record AlignmentChangePreviewOccurrence(
    string Id,
    string Origin,
    string Context,
    string CurrentAlignment,
    string NewAlignment,
    bool Selected = true);

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
/// BAT-09 — Strict Set Paper Size operation (spec 07 §20). Changes only the
/// reviewed sections' w:pgSz @w:w/@w:h values; margins, orientation and every
/// other property are untouched. Sections without explicit page dimensions are
/// out of scope (V1 never invents page geometry). Already-at-target sections are
/// not affected.
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
    /// Builds the reviewed per-section page-size list: every section with explicit
    /// w:pgSz dimensions whose current size differs from the requested size, in
    /// document order. The source file is never modified.
    /// </summary>
    public async Task<IReadOnlyList<PaperSizeChangePreviewOccurrence>> BuildOccurrencesAsync(
        string filePath,
        SetPaperSizeParameters parameters,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateParameters(parameters);

        if (!SearchReplaceOperation.IsDocx(filePath))
            return Array.Empty<PaperSizeChangePreviewOccurrence>();

        var bytes = await SearchReplaceOperation.ReadBoundedAsync(filePath, cancellationToken);
        using var stream = new MemoryStream(bytes, writable: false);
        var sections = DocxPageLayoutScan.ScanSectionsStream(stream, cancellationToken);

        var occurrences = new List<PaperSizeChangePreviewOccurrence>();
        foreach (var section in sections)
        {
            if (!section.HasPageSize) continue;
            // A section already carrying the requested dimensions is not affected.
            if (section.PageWidthTwips == parameters.WidthTwips && section.PageHeightTwips == parameters.HeightTwips)
                continue;
            if (occurrences.Count == DocxPageLayoutScan.OccurrenceBudget)
                throw new InvalidDataException("OccurrenceBudgetExceeded");

            occurrences.Add(new PaperSizeChangePreviewOccurrence(
                PaperSizeOccurrenceId(section),
                section.SectionIndex,
                PageLayoutOoxml.SectionContext(section.SectionIndex),
                section.PageWidthTwips!.Value,
                section.PageHeightTwips!.Value,
                parameters.WidthTwips,
                parameters.HeightTwips));
        }

        return occurrences;
    }

    /// <summary>
    /// Creates the mutate delegate for <c>BatchApplyEngine.ApplyAsync</c>. The
    /// delegate identifies the reviewed plan file by directory + preview
    /// fingerprint and mutates only the reviewed, selected sections.
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

            var chosen = new List<DocxPaperSizeTarget>();
            int candidateCount = 0;
            foreach (var section in sections)
            {
                if (!section.HasPageSize) continue;
                candidateCount++;
                if (!selected.Contains(PaperSizeOccurrenceId(section))) continue;
                chosen.Add(new DocxPaperSizeTarget(
                    section.PartName, section.SectionIndex, section.PageWidthTwips!.Value, section.PageHeightTwips!.Value));
            }

            SearchReplaceOperation.EnsureAllSelectedFound(chosen.Count, selected, candidateCount);
            if (chosen.Count == 0)
                return;

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
/// property are untouched. Sections without explicit margins are out of scope
/// (V1 never invents page geometry). Already-at-target sections are not affected.
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
    /// Builds the reviewed per-section margin list: every section with explicit
    /// w:pgMar values whose current margins differ from the requested margins, in
    /// document order. The source file is never modified.
    /// </summary>
    public async Task<IReadOnlyList<MarginsChangePreviewOccurrence>> BuildOccurrencesAsync(
        string filePath,
        SetMarginsParameters parameters,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateParameters(parameters);

        if (!SearchReplaceOperation.IsDocx(filePath))
            return Array.Empty<MarginsChangePreviewOccurrence>();

        var bytes = await SearchReplaceOperation.ReadBoundedAsync(filePath, cancellationToken);
        using var stream = new MemoryStream(bytes, writable: false);
        var sections = DocxPageLayoutScan.ScanSectionsStream(stream, cancellationToken);

        var occurrences = new List<MarginsChangePreviewOccurrence>();
        foreach (var section in sections)
        {
            if (!section.HasMargins) continue;
            // A section already carrying the requested margins is not affected.
            if (section.MarginTopTwips == parameters.TopTwips &&
                section.MarginRightTwips == parameters.RightTwips &&
                section.MarginBottomTwips == parameters.BottomTwips &&
                section.MarginLeftTwips == parameters.LeftTwips)
            {
                continue;
            }

            if (occurrences.Count == DocxPageLayoutScan.OccurrenceBudget)
                throw new InvalidDataException("OccurrenceBudgetExceeded");

            occurrences.Add(new MarginsChangePreviewOccurrence(
                MarginsOccurrenceId(section),
                section.SectionIndex,
                PageLayoutOoxml.SectionContext(section.SectionIndex),
                section.MarginTopTwips!.Value,
                section.MarginRightTwips!.Value,
                section.MarginBottomTwips!.Value,
                section.MarginLeftTwips!.Value,
                parameters.TopTwips,
                parameters.RightTwips,
                parameters.BottomTwips,
                parameters.LeftTwips));
        }

        return occurrences;
    }

    /// <summary>
    /// Creates the mutate delegate for <c>BatchApplyEngine.ApplyAsync</c>. The
    /// delegate identifies the reviewed plan file by directory + preview
    /// fingerprint and mutates only the reviewed, selected sections.
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

            var chosen = new List<DocxMarginsTarget>();
            int candidateCount = 0;
            foreach (var section in sections)
            {
                if (!section.HasMargins) continue;
                candidateCount++;
                if (!selected.Contains(MarginsOccurrenceId(section))) continue;
                chosen.Add(new DocxMarginsTarget(
                    section.PartName,
                    section.SectionIndex,
                    section.MarginTopTwips!.Value,
                    section.MarginRightTwips!.Value,
                    section.MarginBottomTwips!.Value,
                    section.MarginLeftTwips!.Value));
            }

            SearchReplaceOperation.EnsureAllSelectedFound(chosen.Count, selected, candidateCount);
            if (chosen.Count == 0)
                return;

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
    /// Builds the reviewed per-section orientation list: every section with
    /// explicit w:pgSz dimensions that is not already in the requested
    /// orientation (including dimension ordering), in document order. The new
    /// dimensions shown are the exact dimensions the mutation will install.
    /// The source file is never modified.
    /// </summary>
    public async Task<IReadOnlyList<OrientationChangePreviewOccurrence>> BuildOccurrencesAsync(
        string filePath,
        SetOrientationParameters parameters,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateParameters(parameters);

        if (!SearchReplaceOperation.IsDocx(filePath))
            return Array.Empty<OrientationChangePreviewOccurrence>();

        var bytes = await SearchReplaceOperation.ReadBoundedAsync(filePath, cancellationToken);
        using var stream = new MemoryStream(bytes, writable: false);
        var sections = DocxPageLayoutScan.ScanSectionsStream(stream, cancellationToken);

        string targetOrientation = PageLayoutOoxml.ToOoxmlValue(parameters.Orientation);
        var occurrences = new List<OrientationChangePreviewOccurrence>();
        foreach (var section in sections)
        {
            if (!section.HasPageSize) continue;

            int width = section.PageWidthTwips!.Value;
            int height = section.PageHeightTwips!.Value;
            bool needsSwap = PageLayoutOoxml.NeedsDimensionSwap(parameters.Orientation, width, height);
            // A section already in the requested orientation with correctly ordered
            // dimensions is not affected.
            if (string.Equals(section.Orientation, targetOrientation, StringComparison.Ordinal) && !needsSwap)
                continue;

            if (occurrences.Count == DocxPageLayoutScan.OccurrenceBudget)
                throw new InvalidDataException("OccurrenceBudgetExceeded");

            occurrences.Add(new OrientationChangePreviewOccurrence(
                OrientationOccurrenceId(section),
                section.SectionIndex,
                PageLayoutOoxml.SectionContext(section.SectionIndex),
                section.Orientation,
                targetOrientation,
                width,
                height,
                needsSwap ? height : width,
                needsSwap ? width : height));
        }

        return occurrences;
    }

    /// <summary>
    /// Creates the mutate delegate for <c>BatchApplyEngine.ApplyAsync</c>. The
    /// delegate identifies the reviewed plan file by directory + preview
    /// fingerprint and mutates only the reviewed, selected sections.
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

            var chosen = new List<DocxOrientationTarget>();
            int candidateCount = 0;
            foreach (var section in sections)
            {
                if (!section.HasPageSize) continue;
                candidateCount++;
                if (!selected.Contains(OrientationOccurrenceId(section))) continue;
                chosen.Add(new DocxOrientationTarget(
                    section.PartName,
                    section.SectionIndex,
                    section.Orientation,
                    section.PageWidthTwips!.Value,
                    section.PageHeightTwips!.Value));
            }

            SearchReplaceOperation.EnsureAllSelectedFound(chosen.Count, selected, candidateCount);
            if (chosen.Count == 0)
                return;

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
/// comes from styles are deliberately out of scope (no flattening, spec 06 §21),
/// and section properties are never touched. Already-at-target paragraphs are
/// not affected. Targets body, table, header and footer paragraphs.
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
    /// Builds the reviewed paragraph list: every paragraph with a direct w:jc
    /// justification that differs from the requested alignment, in part order →
    /// paragraph order, with origin and visible-text context. Paragraphs whose
    /// alignment is style-inherited are deliberately out of scope (spec 06 §21).
    /// The source file is never modified.
    /// </summary>
    public async Task<IReadOnlyList<AlignmentChangePreviewOccurrence>> BuildOccurrencesAsync(
        string filePath,
        SetAlignmentParameters parameters,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateParameters(parameters);

        if (!SearchReplaceOperation.IsDocx(filePath))
            return Array.Empty<AlignmentChangePreviewOccurrence>();

        var bytes = await SearchReplaceOperation.ReadBoundedAsync(filePath, cancellationToken);
        using var stream = new MemoryStream(bytes, writable: false);
        var paragraphs = DocxPageLayoutScan.ScanAlignmentStream(stream, cancellationToken);

        string newValue = PageLayoutOoxml.ToOoxmlValue(parameters.Alignment);
        var occurrences = new List<AlignmentChangePreviewOccurrence>();
        foreach (var paragraph in paragraphs)
        {
            if (paragraph.DirectAlignment == null) continue;
            // A paragraph already carrying the requested alignment is not affected.
            if (string.Equals(paragraph.DirectAlignment, newValue, StringComparison.Ordinal)) continue;
            if (occurrences.Count == DocxPageLayoutScan.OccurrenceBudget)
                throw new InvalidDataException("OccurrenceBudgetExceeded");

            occurrences.Add(new AlignmentChangePreviewOccurrence(
                AlignmentOccurrenceId(paragraph),
                paragraph.Origin.ToString(),
                SearchReplaceOperation.TextPreviewContext(
                    paragraph.ParagraphText, 0, Math.Min(paragraph.ParagraphText.Length, 30)),
                paragraph.DirectAlignment,
                newValue));
        }

        return occurrences;
    }

    /// <summary>
    /// Creates the mutate delegate for <c>BatchApplyEngine.ApplyAsync</c>. The
    /// delegate identifies the reviewed plan file by directory + preview
    /// fingerprint and mutates only the reviewed, selected paragraphs.
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

            var chosen = new List<DocxAlignmentTarget>();
            int candidateCount = 0;
            foreach (var paragraph in paragraphs)
            {
                if (paragraph.DirectAlignment == null) continue;
                candidateCount++;
                if (!selected.Contains(AlignmentOccurrenceId(paragraph))) continue;
                chosen.Add(new DocxAlignmentTarget(
                    paragraph.PartName, paragraph.ParagraphIndex, paragraph.DirectAlignment));
            }

            SearchReplaceOperation.EnsureAllSelectedFound(chosen.Count, selected, candidateCount);
            if (chosen.Count == 0)
                return;

            _mutator.SetParagraphAlignment(tempPath, chosen, parameters.Alignment);
        }

        return Mutate;
    }

    internal static string AlignmentOccurrenceId(DocxAlignmentParagraph paragraph) =>
        $"align_{paragraph.PartName}_{paragraph.Origin}_p{paragraph.ParagraphIndex}";
}
