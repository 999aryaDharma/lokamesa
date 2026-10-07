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
/// BAT-08 — strict run-level font mutation for DOCX packages. Locates reviewed
/// runs by part/paragraph/run identity from the shared <see cref="DocxFontScan"/>
/// and rewrites only the requested run property; recovery snapshots and atomic
/// replacement stay owned by the BAT-04 transactional apply engine, which hands
/// this mutator its same-volume temp copy.
/// <para>
/// Fail-closed by construction: every reviewed target is verified against the
/// file before any byte is written, paragraph/section formatting is invariant-
/// checked, and the styles part must stay byte-identical (spec 07 §17–18,
/// spec 06 §19–21, master §3.6 "strict operations"). Verification and in-memory
/// mutation happen on a read-only archive, so a failed target or a no-op leaves
/// the package bytes untouched.
/// </para>
/// </summary>
public sealed class StrictFontMutator : IDocxFontMutator
{
    private static readonly XNamespace WNs = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static readonly XmlReaderSettings XmlSettings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        IgnoreComments = false,
        IgnoreWhitespace = false
    };

    private readonly IDocxPackagePreflight? _preflight;

    public StrictFontMutator(IDocxPackagePreflight? preflight = null)
    {
        _preflight = preflight;
    }

    public void SetFontFamily(string filePath, IReadOnlyList<DocxFontRunTarget> targets, string fontFamily)
    {
        MutateCore(filePath, targets, FontProperty.Family, fontFamily, 0);
    }

    public void SetFontSize(string filePath, IReadOnlyList<DocxFontRunTarget> targets, int sizeHalfPoints)
    {
        MutateCore(filePath, targets, FontProperty.Size, null, sizeHalfPoints);
    }

    private void MutateCore(
        string filePath,
        IReadOnlyList<DocxFontRunTarget> targets,
        FontProperty property,
        string? fontFamily,
        int sizeHalfPoints)
    {
        ValidateArguments(filePath, targets);
        if (targets.Count == 0) return; // no-op must not rewrite package bytes (spec 06 §19)

        if (property == FontProperty.Family)
        {
            if (string.IsNullOrWhiteSpace(fontFamily) || fontFamily.Length > DocxFontScan.MaxFontNameLength)
                throw new ArgumentException("InvalidParameters", nameof(fontFamily));
        }
        else if (sizeHalfPoints < DocxFontScan.MinHalfPoints || sizeHalfPoints > DocxFontScan.MaxHalfPoints)
        {
            throw new ArgumentException("InvalidParameters", nameof(sizeHalfPoints));
        }

        bool preflightEnabled = _preflight != null;
        if (preflightEnabled) ValidatePreflight(filePath, "source");

        // Phase 1: load every affected part and verify every reviewed target on a
        // read-only archive. A failure here leaves the package bytes untouched.
        var parts = new List<PreparedPart>();
        using (var archive = ZipFile.Open(filePath, ZipArchiveMode.Read))
        {
            foreach (var partGroup in targets.GroupBy(t => t.PartName, StringComparer.OrdinalIgnoreCase))
            {
                var entry = FindEntry(archive, partGroup.Key)
                    ?? throw new InvalidOperationException($"FontTargetNotFound: part '{partGroup.Key}' was not found in the package.");

                XDocument xdoc;
                using (var entryStream = entry.Open())
                using (var xmlReader = XmlReader.Create(entryStream, XmlSettings))
                {
                    xdoc = XDocument.Load(xmlReader);
                }

                if (xdoc.Root == null)
                    throw new InvalidOperationException($"FontTargetNotFound: part '{partGroup.Key}' has no XML root; mutation blocked.");

                var paragraphs = xdoc.Descendants(WNs + "p").ToList();
                var changes = new List<PlannedChange>();

                foreach (var paragraphGroup in partGroup.GroupBy(t => t.ParagraphIndex))
                {
                    if (paragraphGroup.Key < 0 || paragraphGroup.Key >= paragraphs.Count)
                        throw new InvalidOperationException(
                            $"FontTargetNotFound: paragraph p{paragraphGroup.Key} was not found in part '{partGroup.Key}'.");

                    var paragraphRuns = DocxFontScan.EnumerateParagraphRuns(paragraphs[paragraphGroup.Key]);

                    foreach (var target in paragraphGroup)
                    {
                        var run = paragraphRuns.Runs.FirstOrDefault(r => r.RunIndex == target.RunIndex)
                            ?? throw new InvalidOperationException(
                                $"FontTargetNotFound: run r{target.RunIndex} was not found in paragraph p{target.ParagraphIndex}.");

                        VerifyExpectedValue(property, run, target);
                        changes.Add(new PlannedChange(run.RunElement));
                    }
                }

                parts.Add(new PreparedPart(entry.FullName, xdoc, changes, ComputeProtectedSignature(xdoc)));
            }
        }

        // Phase 2: apply in memory and prove the strict-operation invariants:
        // paragraph properties (spacing/alignment/style references) and section
        // properties (margins/page size) must not change.
        bool anyChanged = false;
        foreach (var part in parts)
        {
            part.Changed = ApplyChanges(part.Changes, property, fontFamily, sizeHalfPoints);
            if (part.Changed && !string.Equals(part.ProtectedSignature, ComputeProtectedSignature(part.Xdoc), StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "FontMutationInvariantViolated: paragraph or section properties changed during font mutation; mutation blocked.");
            anyChanged |= part.Changed;
        }

        // Phase 3: persist only when a reviewed property actually changed; the
        // styles part must stay byte-identical across persistence.
        if (anyChanged)
        {
            using var archive = ZipFile.Open(filePath, ZipArchiveMode.Update);
            string? stylesHashBefore = ReadEntryHash(archive, "word/styles.xml");
            foreach (var part in parts.Where(p => p.Changed))
            {
                var entry = FindEntry(archive, part.EntryName)
                    ?? throw new InvalidOperationException($"FontTargetNotFound: part '{part.EntryName}' disappeared before persistence.");
                PersistPart(archive, entry, part.Xdoc);
            }
            string? stylesHashAfter = ReadEntryHash(archive, "word/styles.xml");
            if (!string.Equals(stylesHashBefore, stylesHashAfter, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "FontMutationInvariantViolated: the styles part changed during font mutation; mutation blocked.");
        }

        if (preflightEnabled) ValidatePreflight(filePath, "mutated");
        VerifyPackageIntegrity(filePath);
    }

    private static void VerifyExpectedValue(FontProperty property, DocxParagraphRun run, DocxFontRunTarget target)
    {
        if (property == FontProperty.Family)
        {
            if (target.ExpectedFontFamily == null)
                throw new InvalidOperationException(
                    "FontTargetInvalid: the reviewed font-family target carries no expected value.");

            if (run.DirectFontFamily == null ||
                !string.Equals(run.DirectFontFamily, target.ExpectedFontFamily, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"FontTargetMismatch: run r{target.RunIndex} in paragraph p{target.ParagraphIndex} no longer has the reviewed font family.");
            }
        }
        else
        {
            if (target.ExpectedSizeHalfPoints == null)
                throw new InvalidOperationException(
                    "FontTargetInvalid: the reviewed font-size target carries no expected value.");

            if (run.DirectSizeHalfPoints == null || run.DirectSizeHalfPoints != target.ExpectedSizeHalfPoints)
                throw new InvalidOperationException(
                    $"FontTargetMismatch: run r{target.RunIndex} in paragraph p{target.ParagraphIndex} no longer has the reviewed font size.");
        }
    }

    private static bool ApplyChanges(
        List<PlannedChange> changes,
        FontProperty property,
        string? fontFamily,
        int sizeHalfPoints)
    {
        bool changed = false;
        foreach (var change in changes)
        {
            var rPr = change.RunElement.Element(WNs + "rPr")
                ?? throw new InvalidOperationException("FontTargetNotFound: the reviewed run has no run properties.");

            if (property == FontProperty.Family)
            {
                var rFonts = rPr.Element(WNs + "rFonts")
                    ?? throw new InvalidOperationException("FontTargetNotFound: the reviewed run has no font reference.");

                if (string.Equals((string?)rFonts.Attribute(WNs + "ascii"), fontFamily, StringComparison.Ordinal) &&
                    string.Equals((string?)rFonts.Attribute(WNs + "hAnsi"), fontFamily, StringComparison.Ordinal))
                {
                    continue; // already exactly the requested family → do not rewrite bytes
                }

                rFonts.SetAttributeValue(WNs + "ascii", fontFamily);
                rFonts.SetAttributeValue(WNs + "hAnsi", fontFamily);
            }
            else
            {
                var size = rPr.Element(WNs + "sz")
                    ?? throw new InvalidOperationException("FontTargetNotFound: the reviewed run has no font size.");

                string newValue = sizeHalfPoints.ToString(CultureInfo.InvariantCulture);
                if (string.Equals((string?)size.Attribute(WNs + "val"), newValue, StringComparison.Ordinal))
                {
                    continue; // already exactly the requested size → do not rewrite bytes
                }

                size.SetAttributeValue(WNs + "val", newValue);
            }

            changed = true;
        }

        return changed;
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

    /// <summary>
    /// Canonical form of every paragraph property and section property in the
    /// part. Font mutation never touches these nodes; any difference means the
    /// mutation overstepped its reviewed scope and must fail closed.
    /// </summary>
    private static string ComputeProtectedSignature(XDocument xdoc)
    {
        var builder = new StringBuilder();
        foreach (var element in xdoc.Descendants())
        {
            string localName = element.Name.LocalName;
            if (localName is "pPr" or "sectPr")
            {
                builder.Append(element.ToString(SaveOptions.DisableFormatting));
                builder.Append('\u001f');
            }
        }
        return builder.ToString();
    }

    private static ZipArchiveEntry? FindEntry(ZipArchive archive, string partName) =>
        archive.Entries.FirstOrDefault(e =>
            string.Equals(
                DocxFontScan.NormalizePartName(e.FullName),
                DocxFontScan.NormalizePartName(partName),
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
            throw new InvalidOperationException(
                $"Package preflight validation failed ({stage}): {result.ErrorMessage} (Code: {result.ErrorCode})");
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

    private static void ValidateArguments(string filePath, IReadOnlyList<DocxFontRunTarget> targets)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            throw new ArgumentException($"File does not exist or invalid path: '{filePath}'", nameof(filePath));
        ArgumentNullException.ThrowIfNull(targets);
    }

    private enum FontProperty
    {
        Family,
        Size
    }

    private sealed record PlannedChange(XElement RunElement);

    private sealed class PreparedPart
    {
        public PreparedPart(string entryName, XDocument xdoc, List<PlannedChange> changes, string protectedSignature)
        {
            EntryName = entryName;
            Xdoc = xdoc;
            Changes = changes;
            ProtectedSignature = protectedSignature;
        }

        public string EntryName { get; }
        public XDocument Xdoc { get; }
        public List<PlannedChange> Changes { get; }
        public string ProtectedSignature { get; }
        public bool Changed { get; set; }
    }
}
