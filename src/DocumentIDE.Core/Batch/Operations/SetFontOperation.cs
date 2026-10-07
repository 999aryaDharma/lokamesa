using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using DocumentIDE.Core.Docx;

namespace DocumentIDE.Core.Batch.Operations;

/// <summary>
/// BAT-08 — Set Font parameters (spec 07 §17). The new family is required; the
/// optional current-family match narrows the reviewed run set deterministically
/// (ordinal case-insensitive comparison — font names are not case-sensitive).
/// </summary>
public sealed record SetFontParameters
{
    /// <summary>The new font family to apply to reviewed runs.</summary>
    public string FontFamily { get; init; } = string.Empty;

    /// <summary>
    /// Optional current direct font-family filter. When set, only runs whose
    /// current direct family equals this value are targeted. Null/empty targets
    /// every reviewed run that carries direct font formatting.
    /// </summary>
    public string? TargetFontFamily { get; init; }
}

/// <summary>
/// A previewed run-level font-family change: stable occurrence ID, origin,
/// visible-text context, and the effective current→new family for the run.
/// </summary>
public sealed record FontChangePreviewOccurrence(
    string Id,
    string Origin,
    string Context,
    string CurrentFontFamily,
    string NewFontFamily,
    bool Selected = true);

/// <summary>
/// A reviewed plan file bound to its on-disk path, the preview fingerprint that
/// was reviewed, and the occurrence IDs the user selected for mutation.
/// </summary>
public sealed record FontChangeTarget(
    string FilePath,
    string ExpectedFingerprint,
    IReadOnlyList<string> SelectedOccurrenceIds);

/// <summary>A single reviewed run selected for strict font mutation.</summary>
public sealed record DocxFontRunTarget(
    string PartName,
    int ParagraphIndex,
    int RunIndex,
    string? ExpectedFontFamily,
    int? ExpectedSizeHalfPoints);

/// <summary>
/// Occurrence-precise DOCX run-formatting mutation seam. Implemented in
/// Infrastructure by <c>StrictFontMutator</c> so the Core operations never
/// perform raw zip/XML mutation directly (spec 06 §20–21, spec 07 §17).
/// </summary>
public interface IDocxFontMutator
{
    void SetFontFamily(string filePath, IReadOnlyList<DocxFontRunTarget> targets, string fontFamily);
    void SetFontSize(string filePath, IReadOnlyList<DocxFontRunTarget> targets, int sizeHalfPoints);
}

/// <summary>One text-bearing run with its direct run formatting (BAT-08 shared scan).</summary>
public sealed record DocxFontRun(
    string PartName,
    DocxStructureOrigin Origin,
    int ParagraphIndex,
    int RunIndex,
    XElement RunElement,
    string VisibleText,
    string ParagraphText,
    int LogicalStartIndex,
    string? DirectFontFamily,
    int? DirectSizeHalfPoints);

/// <summary>One paragraph's canonical run enumeration result.</summary>
public sealed record DocxParagraphRuns(string ParagraphText, IReadOnlyList<DocxParagraphRun> Runs);

/// <summary>One text-bearing run relative to its paragraph.</summary>
public sealed record DocxParagraphRun(
    int RunIndex,
    XElement RunElement,
    string VisibleText,
    int LogicalStartIndex,
    string? DirectFontFamily,
    int? DirectSizeHalfPoints);

/// <summary>
/// BAT-08 — shared deterministic DOCX run scan. Enumerates text-bearing runs in
/// body, table, header, and footer parts with the same logical paragraph/run
/// indexing used by the BAT-05 logical map, so preview occurrence IDs and
/// apply-time targets are single-sourced and can never drift.
/// </summary>
public static class DocxFontScan
{
    /// <summary>Occurrence budget, identical to the BAT-03/BAT-05 preview budget.</summary>
    public const int OccurrenceBudget = 10000;

    /// <summary>Maximum font family name length accepted by the operations.</summary>
    public const int MaxFontNameLength = 256;

    /// <summary>Smallest valid OOXML half-point size (0.5 pt).</summary>
    public const int MinHalfPoints = 1;

    /// <summary>Largest valid OOXML half-point size (819 pt), per the WordprocessingML schema.</summary>
    public const int MaxHalfPoints = 1638;

    private static readonly XNamespace WNs = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    public static IReadOnlyList<DocxFontRun> ScanStream(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ZipArchive archive;
        try
        {
            archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (Exception ex) when (ex is InvalidDataException || ex is IOException || ex is NotSupportedException)
        {
            throw new InvalidOperationException($"Invalid or corrupted DOCX ZIP archive: {ex.Message}", ex);
        }

        using (archive)
        {
            return ScanArchive(archive, cancellationToken);
        }
    }

    private static IReadOnlyList<DocxFontRun> ScanArchive(ZipArchive archive, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(archive);

        var runs = new List<DocxFontRun>();
        var xmlSettings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreWhitespace = false
        };

        foreach (var entry in IdentifyTargetParts(archive))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string partName = NormalizePartName(entry.FullName);
            XDocument xdoc;
            try
            {
                using var entryStream = entry.Open();
                using var xmlReader = XmlReader.Create(entryStream, xmlSettings);
                xdoc = XDocument.Load(xmlReader);
            }
            catch (Exception ex) when (ex is XmlException || ex is InvalidDataException)
            {
                throw new InvalidOperationException($"Malformed or corrupted XML in part '{partName}': {ex.Message}", ex);
            }

            if (xdoc.Root == null) continue;

            int paragraphIndex = -1;
            foreach (var pElem in xdoc.Descendants(WNs + "p"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                paragraphIndex++;
                var origin = ResolveOrigin(pElem, partName);
                var paragraph = EnumerateParagraphRuns(pElem, cancellationToken);
                foreach (var run in paragraph.Runs)
                {
                    runs.Add(new DocxFontRun(
                        partName,
                        origin,
                        paragraphIndex,
                        run.RunIndex,
                        run.RunElement,
                        run.VisibleText,
                        paragraph.ParagraphText,
                        run.LogicalStartIndex,
                        run.DirectFontFamily,
                        run.DirectSizeHalfPoints));
                }
            }
        }

        return runs;
    }

    /// <summary>
    /// Enumerates the text-bearing runs of one paragraph in document order with
    /// their logical offsets. Run indexes count every <c>w:r</c> encountered, so
    /// the preview and apply-time mutation always agree on run identity. Field
    /// instruction text is excluded, matching the BAT-05 logical map.
    /// </summary>
    public static DocxParagraphRuns EnumerateParagraphRuns(XElement paragraph, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paragraph);

        var text = new StringBuilder();
        var runs = new List<DocxParagraphRun>();
        int runIndexCounter = 0;
        int fieldDepth = 0;

        foreach (var child in paragraph.Elements())
            ProcessElementForRuns(child, text, runs, ref runIndexCounter, ref fieldDepth, cancellationToken);

        return new DocxParagraphRuns(text.ToString(), runs);
    }

    internal static string? GetDirectFontFamily(XElement run)
    {
        var rFonts = run.Element(WNs + "rPr")?.Element(WNs + "rFonts");
        if (rFonts == null) return null;
        var ascii = (string?)rFonts.Attribute(WNs + "ascii");
        if (!string.IsNullOrEmpty(ascii)) return ascii;
        var hAnsi = (string?)rFonts.Attribute(WNs + "hAnsi");
        return string.IsNullOrEmpty(hAnsi) ? null : hAnsi;
    }

    internal static int? GetDirectSizeHalfPoints(XElement run)
    {
        var value = (string?)run.Element(WNs + "rPr")?.Element(WNs + "sz")?.Attribute(WNs + "val");
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var halfPoints)
            ? halfPoints
            : null;
    }

    /// <summary>Normalizes a part name to forward slashes for ordinal comparison.</summary>
    public static string NormalizePartName(string name) => name.Replace('\\', '/');

    private static void ProcessElementForRuns(
        XElement element,
        StringBuilder text,
        List<DocxParagraphRun> runs,
        ref int runIndexCounter,
        ref int fieldDepth,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string localName = element.Name.LocalName;

        if (localName == "fldChar")
        {
            ApplyFieldChar(element, ref fieldDepth);
            return;
        }

        if (localName == "instrText") return;

        if (fieldDepth > 0 && localName == "t") return;

        if (localName == "r")
        {
            int currentRunIndex = runIndexCounter++;
            var runText = new StringBuilder();
            bool hasText = false;

            foreach (var rChild in element.Elements())
            {
                cancellationToken.ThrowIfCancellationRequested();
                string rChildName = rChild.Name.LocalName;

                if (rChildName == "fldChar")
                {
                    ApplyFieldChar(rChild, ref fieldDepth);
                }
                else if (rChildName == "instrText")
                {
                    continue;
                }
                else if (fieldDepth == 0 && rChildName == "t")
                {
                    if (rChild.Value.Length > 0)
                    {
                        runText.Append(rChild.Value);
                        hasText = true;
                    }
                }
                else if (fieldDepth == 0 && rChildName == "tab")
                {
                    runText.Append('\t');
                }
                else if (fieldDepth == 0 && (rChildName == "br" || rChildName == "cr"))
                {
                    runText.Append('\n');
                }
                else if (fieldDepth == 0 && rChildName == "noBreakHyphen")
                {
                    runText.Append('\u2011');
                }
            }

            if (runText.Length > 0)
            {
                int start = text.Length;
                text.Append(runText.ToString());
                if (hasText)
                {
                    runs.Add(new DocxParagraphRun(
                        currentRunIndex,
                        element,
                        runText.ToString(),
                        start,
                        GetDirectFontFamily(element),
                        GetDirectSizeHalfPoints(element)));
                }
            }

            return;
        }

        foreach (var child in element.Elements())
            ProcessElementForRuns(child, text, runs, ref runIndexCounter, ref fieldDepth, cancellationToken);
    }

    private static void ApplyFieldChar(XElement element, ref int fieldDepth)
    {
        string? fldCharType = element.Attribute(WNs + "fldCharType")?.Value
            ?? element.Attributes().FirstOrDefault(a => a.Name.LocalName == "fldCharType")?.Value;

        if (string.Equals(fldCharType, "begin", StringComparison.OrdinalIgnoreCase))
        {
            fieldDepth++;
        }
        else if (string.Equals(fldCharType, "separate", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(fldCharType, "end", StringComparison.OrdinalIgnoreCase))
        {
            if (fieldDepth > 0) fieldDepth--;
        }
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

    private static List<ZipArchiveEntry> IdentifyTargetParts(ZipArchive archive)
    {
        var parts = new List<ZipArchiveEntry>();

        var docEntry = archive.Entries.FirstOrDefault(e =>
            string.Equals(NormalizePartName(e.FullName), "word/document.xml", StringComparison.OrdinalIgnoreCase));
        if (docEntry != null) parts.Add(docEntry);

        foreach (var entry in archive.Entries)
        {
            string normPath = NormalizePartName(entry.FullName).ToLowerInvariant();
            if ((normPath.StartsWith("word/header", StringComparison.Ordinal) || normPath.StartsWith("word/footer", StringComparison.Ordinal)) &&
                normPath.EndsWith(".xml", StringComparison.Ordinal))
            {
                if (!parts.Contains(entry)) parts.Add(entry);
            }
        }

        return parts;
    }
}

/// <summary>
/// BAT-08 — Strict Set Font operation (spec 07 §17). Builds deterministic
/// run-level current→new family previews and supplies the mutate delegate for
/// the BAT-04 transactional apply engine.
/// <para>
/// Explicit V1 target semantics: a target is a text-bearing run in
/// word/document.xml, word/header*.xml, or word/footer*.xml that carries a
/// <b>direct</b> font-family reference (w:rPr/w:rFonts @w:ascii or @w:hAnsi).
/// The optional <see cref="SetFontParameters.TargetFontFamily"/> filter narrows
/// the set to runs whose current direct family matches (ordinal,
/// case-insensitive). Runs whose family resolves from styles or document
/// defaults are deliberately out of scope: V1 never flattens style-based
/// formatting into direct formatting (spec 06 §21). Only reviewed, selected
/// runs are mutated, and only @w:ascii/@w:hAnsi change on their own w:rFonts —
/// never size, style, spacing, or section margins. Non-DOCX files preview as
/// unaffected (zero occurrences).
/// </para>
/// </summary>
public sealed class SetFontOperation
{
    private readonly IDocxFontMutator _docxFontMutator;

    public SetFontOperation(IDocxFontMutator docxFontMutator)
    {
        _docxFontMutator = docxFontMutator ?? throw new ArgumentNullException(nameof(docxFontMutator));
    }

    /// <summary>
    /// Typed validation before any file access: the new family is required and
    /// bounded; an optional target family must be a real name (never whitespace).
    /// </summary>
    private static void ValidateParameters(SetFontParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (string.IsNullOrWhiteSpace(parameters.FontFamily) ||
            parameters.FontFamily.Length > DocxFontScan.MaxFontNameLength)
        {
            throw new ArgumentException("InvalidParameters", nameof(parameters));
        }

        if (parameters.TargetFontFamily is { Length: > 0 } targetFamily &&
            (string.IsNullOrWhiteSpace(targetFamily) || targetFamily.Length > DocxFontScan.MaxFontNameLength))
        {
            throw new ArgumentException("InvalidParameters", nameof(parameters));
        }
    }

    /// <summary>
    /// Builds the reviewed run list for a file: every text run that carries a
    /// direct font-family reference, with the effective current→new family and
    /// the run's visible-text context. The source file is never modified.
    /// </summary>
    public async Task<IReadOnlyList<FontChangePreviewOccurrence>> BuildOccurrencesAsync(
        string filePath,
        SetFontParameters parameters,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateParameters(parameters);

        if (!SearchReplaceOperation.IsDocx(filePath))
            return Array.Empty<FontChangePreviewOccurrence>();

        var bytes = await SearchReplaceOperation.ReadBoundedAsync(filePath, cancellationToken);
        using var stream = new MemoryStream(bytes, writable: false);
        var runs = DocxFontScan.ScanStream(stream, cancellationToken);

        var occurrences = new List<FontChangePreviewOccurrence>();
        foreach (var run in runs)
        {
            if (run.DirectFontFamily == null) continue;
            // A run already using the requested family is not affected.
            if (string.Equals(run.DirectFontFamily, parameters.FontFamily, StringComparison.OrdinalIgnoreCase))
                continue;
            if (parameters.TargetFontFamily is { Length: > 0 } targetFamily &&
                !string.Equals(run.DirectFontFamily, targetFamily, StringComparison.OrdinalIgnoreCase))
                continue;
            if (occurrences.Count == DocxFontScan.OccurrenceBudget)
                throw new InvalidDataException("OccurrenceBudgetExceeded");

            occurrences.Add(new FontChangePreviewOccurrence(
                FontOccurrenceId(run),
                run.Origin.ToString(),
                SearchReplaceOperation.TextPreviewContext(run.ParagraphText, run.LogicalStartIndex, run.VisibleText.Length),
                run.DirectFontFamily,
                parameters.FontFamily));
        }

        return occurrences;
    }

    /// <summary>
    /// Creates the mutate delegate for <c>BatchApplyEngine.ApplyAsync</c>. The
    /// delegate identifies the reviewed plan file by directory + preview
    /// fingerprint (the engine guarantees the temp copy is byte-identical to the
    /// reviewed source) and mutates only the reviewed, selected runs.
    /// </summary>
    public Func<string, CancellationToken, Task> CreateMutate(
        SetFontParameters parameters,
        IReadOnlyList<FontChangeTarget> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ValidateParameters(parameters);

        async Task Mutate(string tempPath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = ResolveTarget(tempPath, targets);
            var selected = new HashSet<string>(target.SelectedOccurrenceIds ?? Array.Empty<string>(), StringComparer.Ordinal);
            if (selected.Count == 0)
                return; // nothing was reviewed/selected for this file → no change

            var bytes = await SearchReplaceOperation.ReadBoundedAsync(tempPath, cancellationToken);
            using var stream = new MemoryStream(bytes, writable: false);
            var runs = DocxFontScan.ScanStream(stream, cancellationToken);

            var chosen = new List<DocxFontRunTarget>();
            int candidateCount = 0;
            foreach (var run in runs)
            {
                if (run.DirectFontFamily == null) continue;
                candidateCount++;
                if (!selected.Contains(FontOccurrenceId(run))) continue;
                chosen.Add(new DocxFontRunTarget(
                    run.PartName, run.ParagraphIndex, run.RunIndex, run.DirectFontFamily, run.DirectSizeHalfPoints));
            }

            SearchReplaceOperation.EnsureAllSelectedFound(chosen.Count, selected, candidateCount);
            if (chosen.Count == 0)
                return;

            _docxFontMutator.SetFontFamily(tempPath, chosen, parameters.FontFamily);
        }

        return Mutate;
    }

    internal static string FontOccurrenceId(DocxFontRun run) =>
        $"font_{run.PartName}_{run.Origin}_p{run.ParagraphIndex}_r{run.RunIndex}";

    /// <summary>
    /// Resolves the reviewed plan file that owns the (fingerprint-verified) temp
    /// copy; fails closed on mismatch or ambiguity (BAT-05 parity).
    /// </summary>
    internal static FontChangeTarget ResolveTarget(string tempPath, IReadOnlyList<FontChangeTarget> targets)
    {
        var tempDirectory = Path.GetDirectoryName(Path.GetFullPath(tempPath))
            ?? throw new InvalidOperationException("Cannot determine the directory of the file being mutated.");

        string fingerprint;
        using (var stream = File.OpenRead(tempPath))
            fingerprint = Convert.ToHexString(SHA256.HashData(stream));

        var candidates = new List<FontChangeTarget>();
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
        // to apply when their reviewed run selections agree; otherwise the correct
        // selection is unknowable here, so mutation fails closed.
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
