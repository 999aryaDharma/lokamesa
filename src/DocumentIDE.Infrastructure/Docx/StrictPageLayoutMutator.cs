using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using DocumentIDE.Core.Batch.Operations;
using DocumentIDE.Core.Docx;

namespace DocumentIDE.Infrastructure.Docx;

/// <summary>
/// BAT-09 — strict page-layout mutation for DOCX packages. Locates reviewed
/// sections/paragraphs by part + section/paragraph identity from the shared
/// <see cref="DocxPageLayoutScan"/> and rewrites only the requested page-layout
/// property; recovery snapshots and atomic replacement stay owned by the BAT-04
/// transactional apply engine, which hands this mutator its same-volume temp copy.
/// <para>
/// Fail-closed by construction: every reviewed target is verified against the
/// file before any byte is written, a masked-scope signature proves that nothing
/// outside the reviewed attributes changed, and the styles part must stay
/// byte-identical (spec 07 §19–20, spec 06 §19–21, master §3.6 “strict
/// operations”). Verification and in-memory mutation happen on a read-only
/// archive, so a failed target or a no-op leaves the package bytes untouched.
/// </para>
/// </summary>
public sealed class StrictPageLayoutMutator : IDocxPageLayoutMutator
{
    private static readonly XNamespace WNs = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static readonly XmlReaderSettings XmlSettings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        IgnoreComments = false,
        IgnoreWhitespace = false
    };

    private static readonly XName PgMarName = WNs + "pgMar";
    private static readonly XName PgSzName = WNs + "pgSz";
    private static readonly XName JcName = WNs + "jc";
    private static readonly XName WidthAttributeName = WNs + "w";
    private static readonly XName HeightAttributeName = WNs + "h";
    private static readonly XName OrientationAttributeName = WNs + "orient";
    private static readonly XName TopAttributeName = WNs + "top";
    private static readonly XName RightAttributeName = WNs + "right";
    private static readonly XName BottomAttributeName = WNs + "bottom";
    private static readonly XName LeftAttributeName = WNs + "left";
    private static readonly XName ValueAttributeName = WNs + "val";

    private static readonly XName[] MarginAttributeNames = { TopAttributeName, RightAttributeName, BottomAttributeName, LeftAttributeName };
    private static readonly XName[] PageSizeAttributeNames = { WidthAttributeName, HeightAttributeName };
    private static readonly XName[] OrientationAttributeNames = { WidthAttributeName, HeightAttributeName, OrientationAttributeName };
    private static readonly XName[] AlignmentAttributeNames = { ValueAttributeName };

    private readonly IDocxPackagePreflight? _preflight;

    public StrictPageLayoutMutator(IDocxPackagePreflight? preflight = null)
    {
        _preflight = preflight;
    }

    public void SetMargins(string filePath, IReadOnlyList<DocxMarginsTarget> targets, int topTwips, int rightTwips, int bottomTwips, int leftTwips)
    {
        SetMarginsCore(filePath, targets, topTwips, rightTwips, bottomTwips, leftTwips);
    }

    public void SetPaperSize(string filePath, IReadOnlyList<DocxPaperSizeTarget> targets, int widthTwips, int heightTwips)
    {
        SetPaperSizeCore(filePath, targets, widthTwips, heightTwips);
    }

    public void SetOrientation(string filePath, IReadOnlyList<DocxOrientationTarget> targets, PageOrientationKind orientation)
    {
        SetOrientationCore(filePath, targets, orientation);
    }

    public void SetParagraphAlignment(string filePath, IReadOnlyList<DocxAlignmentTarget> targets, ParagraphAlignmentKind alignment)
    {
        SetParagraphAlignmentCore(filePath, targets, alignment);
    }

    private void SetParagraphAlignmentCore(string filePath, IReadOnlyList<DocxAlignmentTarget> targets, ParagraphAlignmentKind alignment)
    {
        ValidateArguments(filePath, targets);
        if (!Enum.IsDefined(alignment))
            throw new ArgumentException("InvalidParameters", nameof(alignment));

        if (targets.Count == 0) return; // no-op must not rewrite package bytes (spec 06 §19)

        bool preflightEnabled = _preflight != null;
        if (preflightEnabled) ValidatePreflight(filePath, "source");

        string newValue = PageLayoutOoxml.ToOoxmlValue(alignment);

        // Phase 1: load every affected part and verify every reviewed target on a
        // read-only archive. A failure here leaves the package bytes untouched.
        var parts = new List<PreparedPart>();
        using (var archive = ZipFile.Open(filePath, ZipArchiveMode.Read))
        {
            foreach (var partGroup in targets.GroupBy(t => t.PartName, StringComparer.OrdinalIgnoreCase))
            {
                var (xdoc, entryName) = LoadPart(archive, partGroup.Key);
                var paragraphs = DocxPageLayoutScan.EnumerateAlignmentParagraphs(entryName, xdoc);
                var changes = new List<AttributeChange>();
                var maskedSites = new HashSet<XElement>();

                foreach (var target in partGroup)
                {
                    if (target.ParagraphIndex < 0 || target.ParagraphIndex >= paragraphs.Count)
                        throw new InvalidOperationException(
                            $"PageLayoutTargetNotFound: paragraph p{target.ParagraphIndex} was not found in part '{entryName}'.");

                    var paragraph = paragraphs[target.ParagraphIndex];
                    if (paragraph.DirectAlignment == null ||
                        !string.Equals(paragraph.DirectAlignment, target.ExpectedAlignment, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"PageLayoutTargetMismatch: paragraph p{target.ParagraphIndex} in part '{entryName}' no longer has the reviewed alignment.");
                    }

                    var jc = paragraph.Paragraph.Element(WNs + "pPr")?.Element(JcName)
                        ?? throw new InvalidOperationException(
                            $"PageLayoutTargetNotFound: paragraph p{target.ParagraphIndex} in part '{entryName}' has no direct w:jc justification.");
                    AddStringAttributeChange(changes, jc, ValueAttributeName, newValue);
                    maskedSites.Add(jc);
                }

                parts.Add(PreparePart(entryName, xdoc, changes, maskedSites, AlignmentAttributeNames));
            }
        }

        ExecuteMutation(filePath, parts, preflightEnabled);
    }

    private void SetMarginsCore(string filePath, IReadOnlyList<DocxMarginsTarget> targets, int topTwips, int rightTwips, int bottomTwips, int leftTwips)
    {
        ValidateArguments(filePath, targets);
        ValidateMarginValue(topTwips, nameof(topTwips));
        ValidateMarginValue(rightTwips, nameof(rightTwips));
        ValidateMarginValue(bottomTwips, nameof(bottomTwips));
        ValidateMarginValue(leftTwips, nameof(leftTwips));

        if (targets.Count == 0) return; // no-op must not rewrite package bytes (spec 06 §19)

        bool preflightEnabled = _preflight != null;
        if (preflightEnabled) ValidatePreflight(filePath, "source");

        // Phase 1: load every affected part and verify every reviewed target on a
        // read-only archive. A failure here leaves the package bytes untouched.
        var parts = new List<PreparedPart>();
        using (var archive = ZipFile.Open(filePath, ZipArchiveMode.Read))
        {
            foreach (var partGroup in targets.GroupBy(t => t.PartName, StringComparer.OrdinalIgnoreCase))
            {
                var (xdoc, entryName) = LoadPart(archive, partGroup.Key);
                var sections = DocxPageLayoutScan.EnumerateSections(entryName, xdoc);
                var changes = new List<AttributeChange>();
                var maskedSites = new HashSet<XElement>();

                foreach (var target in partGroup)
                {
                    var section = ResolveSection(sections, entryName, target.SectionIndex);
                    if (!section.HasMargins)
                        throw new InvalidOperationException(
                            $"PageLayoutTargetNotFound: section s{target.SectionIndex} in part '{entryName}' has no explicit w:pgMar margins.");

                    if (section.MarginTopTwips != target.ExpectedTopTwips ||
                        section.MarginRightTwips != target.ExpectedRightTwips ||
                        section.MarginBottomTwips != target.ExpectedBottomTwips ||
                        section.MarginLeftTwips != target.ExpectedLeftTwips)
                    {
                        throw new InvalidOperationException(
                            $"PageLayoutTargetMismatch: section s{target.SectionIndex} in part '{entryName}' no longer has the reviewed margins.");
                    }

                    var pgMar = section.SectPr.Element(PgMarName)!;
                    AddIntAttributeChange(changes, pgMar, TopAttributeName, topTwips);
                    AddIntAttributeChange(changes, pgMar, RightAttributeName, rightTwips);
                    AddIntAttributeChange(changes, pgMar, BottomAttributeName, bottomTwips);
                    AddIntAttributeChange(changes, pgMar, LeftAttributeName, leftTwips);
                    maskedSites.Add(pgMar);
                }

                parts.Add(PreparePart(entryName, xdoc, changes, maskedSites, MarginAttributeNames));
            }
        }

        ExecuteMutation(filePath, parts, preflightEnabled);
    }

    private static void ValidateMarginValue(int twips, string parameterName)
    {
        // Margins may legitimately be zero; they may not exceed the page bound.
        if (twips < 0 || twips > DocxPageLayoutScan.MaxTwips)
            throw new ArgumentException("InvalidParameters", parameterName);
    }

    private void SetOrientationCore(string filePath, IReadOnlyList<DocxOrientationTarget> targets, PageOrientationKind orientation)
    {
        ValidateArguments(filePath, targets);
        if (!Enum.IsDefined(orientation))
            throw new ArgumentException("InvalidParameters", nameof(orientation));

        if (targets.Count == 0) return; // no-op must not rewrite package bytes (spec 06 §19)

        bool preflightEnabled = _preflight != null;
        if (preflightEnabled) ValidatePreflight(filePath, "source");

        string targetOrientation = PageLayoutOoxml.ToOoxmlValue(orientation);

        // Phase 1: load every affected part and verify every reviewed target on a
        // read-only archive. A failure here leaves the package bytes untouched.
        var parts = new List<PreparedPart>();
        using (var archive = ZipFile.Open(filePath, ZipArchiveMode.Read))
        {
            foreach (var partGroup in targets.GroupBy(t => t.PartName, StringComparer.OrdinalIgnoreCase))
            {
                var (xdoc, entryName) = LoadPart(archive, partGroup.Key);
                var sections = DocxPageLayoutScan.EnumerateSections(entryName, xdoc);
                var changes = new List<AttributeChange>();
                var maskedSites = new HashSet<XElement>();

                foreach (var target in partGroup)
                {
                    var section = ResolveSection(sections, entryName, target.SectionIndex);
                    if (!section.HasPageSize)
                        throw new InvalidOperationException(
                            $"PageLayoutTargetNotFound: section s{target.SectionIndex} in part '{entryName}' has no explicit w:pgSz page size.");

                    if (!string.Equals(section.Orientation, target.ExpectedOrientation, StringComparison.Ordinal) ||
                        section.PageWidthTwips != target.ExpectedWidthTwips ||
                        section.PageHeightTwips != target.ExpectedHeightTwips)
                    {
                        throw new InvalidOperationException(
                            $"PageLayoutTargetMismatch: section s{target.SectionIndex} in part '{entryName}' no longer has the reviewed orientation.");
                    }

                    var pgSz = section.SectPr.Element(PgSzName)!;
                    int width = section.PageWidthTwips!.Value;
                    int height = section.PageHeightTwips!.Value;

                    // Only write w:orient when the effective orientation actually
                    // changes (an absent attribute already means portrait).
                    if (!string.Equals(PageLayoutOoxml.NormalizeOrientation((string?)pgSz.Attribute(OrientationAttributeName)), targetOrientation, StringComparison.Ordinal))
                        AddStringAttributeChange(changes, pgSz, OrientationAttributeName, targetOrientation);

                    // Preserve the physical page size: swap dims only when they are
                    // ordered the other way for the requested orientation.
                    bool needsSwap = PageLayoutOoxml.NeedsDimensionSwap(orientation, width, height);
                    AddIntAttributeChange(changes, pgSz, WidthAttributeName, needsSwap ? height : width);
                    AddIntAttributeChange(changes, pgSz, HeightAttributeName, needsSwap ? width : height);
                    maskedSites.Add(pgSz);
                }

                parts.Add(PreparePart(entryName, xdoc, changes, maskedSites, OrientationAttributeNames));
            }
        }

        ExecuteMutation(filePath, parts, preflightEnabled);
    }

    private void SetPaperSizeCore(string filePath, IReadOnlyList<DocxPaperSizeTarget> targets, int widthTwips, int heightTwips)
    {
        ValidateArguments(filePath, targets);
        if (widthTwips < DocxPageLayoutScan.MinPageTwips || widthTwips > DocxPageLayoutScan.MaxTwips ||
            heightTwips < DocxPageLayoutScan.MinPageTwips || heightTwips > DocxPageLayoutScan.MaxTwips)
        {
            throw new ArgumentException("InvalidParameters", nameof(widthTwips));
        }

        if (targets.Count == 0) return; // no-op must not rewrite package bytes (spec 06 §19)

        bool preflightEnabled = _preflight != null;
        if (preflightEnabled) ValidatePreflight(filePath, "source");

        // Phase 1: load every affected part and verify every reviewed target on a
        // read-only archive. A failure here leaves the package bytes untouched.
        var parts = new List<PreparedPart>();
        using (var archive = ZipFile.Open(filePath, ZipArchiveMode.Read))
        {
            foreach (var partGroup in targets.GroupBy(t => t.PartName, StringComparer.OrdinalIgnoreCase))
            {
                var (xdoc, entryName) = LoadPart(archive, partGroup.Key);
                var sections = DocxPageLayoutScan.EnumerateSections(entryName, xdoc);
                var changes = new List<AttributeChange>();
                var maskedSites = new HashSet<XElement>();

                foreach (var target in partGroup)
                {
                    var section = ResolveSection(sections, entryName, target.SectionIndex);
                    if (!section.HasPageSize)
                        throw new InvalidOperationException(
                            $"PageLayoutTargetNotFound: section s{target.SectionIndex} in part '{entryName}' has no explicit w:pgSz page size.");

                    if (section.PageWidthTwips != target.ExpectedWidthTwips || section.PageHeightTwips != target.ExpectedHeightTwips)
                        throw new InvalidOperationException(
                            $"PageLayoutTargetMismatch: section s{target.SectionIndex} in part '{entryName}' no longer has the reviewed page size.");

                    var pgSz = section.SectPr.Element(PgSzName)!;
                    AddIntAttributeChange(changes, pgSz, WidthAttributeName, widthTwips);
                    AddIntAttributeChange(changes, pgSz, HeightAttributeName, heightTwips);
                    maskedSites.Add(pgSz);
                }

                parts.Add(PreparePart(entryName, xdoc, changes, maskedSites, PageSizeAttributeNames));
            }
        }

        ExecuteMutation(filePath, parts, preflightEnabled);
    }

    /// <summary>
    /// Phase 2+3: apply the collected attribute changes in memory, prove that only
    /// the masked reviewed attributes changed, and persist only parts with a real
    /// change. The styles part must stay byte-identical across persistence.
    /// </summary>
    private void ExecuteMutation(string filePath, List<PreparedPart> parts, bool preflightEnabled)
    {
        bool anyChanged = false;
        foreach (var part in parts)
        {
            part.Changed = ApplyChanges(part.Changes);
            if (part.Changed &&
                !string.Equals(part.SignatureBefore, ComputeScopeSignature(part.Xdoc, part.MaskedSites, part.MaskedAttributes), StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "PageLayoutInvariantViolated: the mutation changed properties outside its reviewed scope; mutation blocked.");
            }

            anyChanged |= part.Changed;
        }

        if (anyChanged)
        {
            using var archive = ZipFile.Open(filePath, ZipArchiveMode.Update);
            string? stylesHashBefore = ReadEntryHash(archive, "word/styles.xml");
            foreach (var part in parts.Where(p => p.Changed))
            {
                var entry = FindEntry(archive, part.EntryName)
                    ?? throw new InvalidOperationException($"PageLayoutTargetNotFound: part '{part.EntryName}' disappeared before persistence.");
                PersistPart(archive, entry, part.Xdoc);
            }

            string? stylesHashAfter = ReadEntryHash(archive, "word/styles.xml");
            if (!string.Equals(stylesHashBefore, stylesHashAfter, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "PageLayoutInvariantViolated: the styles part changed during page-layout mutation; mutation blocked.");
            }
        }

        if (preflightEnabled) ValidatePreflight(filePath, "mutated");
        VerifyPackageIntegrity(filePath);
    }

    private static PreparedPart PreparePart(
        string entryName,
        XDocument xdoc,
        List<AttributeChange> changes,
        HashSet<XElement> maskedSites,
        XName[] maskedAttributes)
    {
        // The signature is taken while the reviewed attributes are masked, so the
        // before/after comparison isolates exactly the values this operation owns.
        string signature = ComputeScopeSignature(xdoc, maskedSites, maskedAttributes);
        return new PreparedPart(entryName, xdoc, changes, maskedSites, maskedAttributes, signature);
    }

    private static DocxSectionInfo ResolveSection(IReadOnlyList<DocxSectionInfo> sections, string entryName, int sectionIndex)
    {
        if (sectionIndex < 0 || sectionIndex >= sections.Count)
            throw new InvalidOperationException(
                $"PageLayoutTargetNotFound: section s{sectionIndex} was not found in part '{entryName}'.");

        return sections[sectionIndex];
    }

    private static void AddIntAttributeChange(List<AttributeChange> changes, XElement element, XName attribute, int value) =>
        changes.Add(new AttributeChange(element, attribute, value.ToString(CultureInfo.InvariantCulture)));

    private static void AddStringAttributeChange(List<AttributeChange> changes, XElement element, XName attribute, string value) =>
        changes.Add(new AttributeChange(element, attribute, value));

    private static bool ApplyChanges(List<AttributeChange> changes)
    {
        bool changed = false;
        foreach (var change in changes)
        {
            if (string.Equals((string?)change.Element.Attribute(change.Attribute), change.NewValue, StringComparison.Ordinal))
                continue; // already exactly the requested value → do not rewrite bytes

            change.Element.SetAttributeValue(change.Attribute, change.NewValue);
            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// Canonical form of the part with the reviewed attribute values masked on the
    /// reviewed sites. Verifying this signature is unchanged after the in-memory
    /// mutation proves the mutation touched nothing outside the reviewed scope.
    /// Attribute values are removed and restored around serialization only; the
    /// persisted values are written later by <see cref="ApplyChanges"/>.
    /// </summary>
    private static string ComputeScopeSignature(XDocument xdoc, IReadOnlyCollection<XElement> maskedSites, IReadOnlyCollection<XName> maskedAttributes)
    {
        var saved = new List<(XElement Element, XName Attribute, string Value)>();
        try
        {
            foreach (var site in maskedSites)
            {
                foreach (var attribute in site.Attributes().Where(a => maskedAttributes.Contains(a.Name)).ToList())
                {
                    saved.Add((site, attribute.Name, attribute.Value));
                    attribute.Remove();
                }
            }

            return xdoc.ToString(SaveOptions.DisableFormatting);
        }
        finally
        {
            foreach (var (element, attribute, value) in saved)
                element.SetAttributeValue(attribute, value);
        }
    }

    private static (XDocument Xdoc, string EntryName) LoadPart(ZipArchive archive, string partName)
    {
        var entry = FindEntry(archive, partName)
            ?? throw new InvalidOperationException($"PageLayoutTargetNotFound: part '{partName}' was not found in the package.");

        XDocument xdoc;
        using (var entryStream = entry.Open())
        using (var xmlReader = XmlReader.Create(entryStream, XmlSettings))
        {
            xdoc = XDocument.Load(xmlReader);
        }

        if (xdoc.Root == null)
            throw new InvalidOperationException($"PageLayoutTargetNotFound: part '{entry.FullName}' has no XML root; mutation blocked.");

        return (xdoc, entry.FullName);
    }

    private static void PersistPart(ZipArchive archive, ZipArchiveEntry entry, XDocument xdoc)
    {
        string entryName = entry.FullName;
        entry.Delete();
        var newEntry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
        using var newStream = newEntry.Open();
        using var writer = XmlWriter.Create(newStream, new XmlWriterSettings { Encoding = Encoding.UTF8, Indent = false });
        xdoc.Save(writer);
    }

    private static ZipArchiveEntry? FindEntry(ZipArchive archive, string partName) =>
        archive.Entries.FirstOrDefault(e =>
            string.Equals(
                DocxPageLayoutScan.NormalizePartName(e.FullName),
                DocxPageLayoutScan.NormalizePartName(partName),
                StringComparison.OrdinalIgnoreCase));

    private static string? ReadEntryHash(ZipArchive archive, string partName)
    {
        var entry = FindEntry(archive, partName);
        if (entry == null) return null;

        using var stream = entry.Open();
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private void ValidatePreflight(string filePath, string stage)
    {
        var result = _preflight!.ValidatePackage(filePath);
        if (!result.IsValid)
        {
            throw new InvalidOperationException(
                $"Package preflight validation failed ({stage}): {result.ErrorMessage} (Code: {result.ErrorCode})");
        }
    }

    private static void VerifyPackageIntegrity(string docxPath)
    {
        using var archive = ZipFile.OpenRead(docxPath);
        var entries = archive.Entries.Where(e => e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)).ToList();

        var xmlSettings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null
        };

        foreach (var entry in entries)
        {
            using var stream = entry.Open();
            using var reader = XmlReader.Create(stream, xmlSettings);
            while (reader.Read()) { }
        }
    }

    private static void ValidateArguments<T>(string filePath, IReadOnlyList<T> targets)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            throw new ArgumentException($"File does not exist or invalid path: '{filePath}'", nameof(filePath));
        ArgumentNullException.ThrowIfNull(targets);
    }

    private sealed record AttributeChange(XElement Element, XName Attribute, string NewValue);

    private sealed class PreparedPart
    {
        public PreparedPart(
            string entryName,
            XDocument xdoc,
            List<AttributeChange> changes,
            IReadOnlyCollection<XElement> maskedSites,
            IReadOnlyCollection<XName> maskedAttributes,
            string signatureBefore)
        {
            EntryName = entryName;
            Xdoc = xdoc;
            Changes = changes;
            MaskedSites = maskedSites;
            MaskedAttributes = maskedAttributes;
            SignatureBefore = signatureBefore;
        }

        public string EntryName { get; }
        public XDocument Xdoc { get; }
        public List<AttributeChange> Changes { get; }
        public IReadOnlyCollection<XElement> MaskedSites { get; }
        public IReadOnlyCollection<XName> MaskedAttributes { get; }
        public string SignatureBefore { get; }
        public bool Changed { get; set; }
    }
}
