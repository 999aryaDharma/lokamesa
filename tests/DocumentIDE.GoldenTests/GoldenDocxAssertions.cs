using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using DocumentIDE.Infrastructure.Docx;

namespace DocumentIDE.GoldenTests;

/// <summary>
/// Assertion helpers shared by the golden corpus tests:
/// <list type="bullet">
/// <item>package validity: preflight + every XML/relationship part parses with DTD prohibited,</item>
/// <item>unrelated-property invariance: decompressed package parts, paragraphs, formatting
/// elements and run formatting outside the strict replacement must stay identical.</item>
/// </list>
/// </summary>
internal static class GoldenDocxAssertions
{
    internal static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static readonly XmlReaderSettings SafeXmlSettings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null
    };

    /// <summary>
    /// Formatting/property elements that a strict text replacement must never add,
    /// remove or modify (direct structure or formatting semantics).
    /// </summary>
    private static readonly string[] InvariantElementNames =
    {
        "pPr", "sectPr", "tblPr", "trPr", "tcPr", "tblGrid", "gridCol",
        "numPr", "numId", "pStyle", "rStyle",
        "drawing", "inline", "blip", "graphic",
        "hyperlink", "fldChar", "instrText",
        "br", "noBreakHyphen",
        "ins", "del", "delText"
    };

    public static string Sha256Hex(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    public static string Sha256Hex(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    /// <summary>Reads every ZIP entry as decompressed bytes (formatting of the ZIP container itself is irrelevant).</summary>
    public static Dictionary<string, byte[]> ReadAllParts(string path)
    {
        var parts = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        using var archive = ZipFile.OpenRead(path);
        foreach (var entry in archive.Entries)
        {
            parts[Normalize(entry.FullName)] = ReadEntry(entry);
        }
        return parts;
    }

    public static byte[] ReadPart(string path, string partName)
    {
        using var archive = ZipFile.OpenRead(path);
        var entry = archive.Entries.FirstOrDefault(e =>
            string.Equals(Normalize(e.FullName), Normalize(partName), StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Part '{partName}' not found in '{path}'.");
        return ReadEntry(entry);
    }

    public static void AssertValidPackage(string path)
    {
        var validation = new DocxPackagePreflight().ValidatePackage(path);
        Assert.True(
            validation.IsValid,
            $"Package preflight failed: {validation.ErrorCode} — {validation.ErrorMessage}");
        Assert.True(
            validation.Warnings.Count == 0,
            $"Package produced preflight warnings: {string.Join(" | ", validation.Warnings)}");

        using var archive = ZipFile.OpenRead(path);
        foreach (var entry in archive.Entries.Where(IsXmlPart))
        {
            using var stream = entry.Open();
            using var reader = XmlReader.Create(stream, SafeXmlSettings);
            while (reader.Read()) { }
        }
    }

    /// <summary>Every package part except <paramref name="changedParts"/> must be decompressed-byte-identical.</summary>
    public static void AssertPartsUnchanged(
        IReadOnlyDictionary<string, byte[]> beforeParts,
        string afterPath,
        IReadOnlyCollection<string> changedParts)
    {
        var changed = new HashSet<string>(changedParts.Select(Normalize), StringComparer.OrdinalIgnoreCase);
        var afterParts = ReadAllParts(afterPath);

        Assert.True(
            beforeParts.Count == afterParts.Count,
            $"Package part count changed: {beforeParts.Count} -> {afterParts.Count}.");

        foreach (var (name, beforeBytes) in beforeParts)
        {
            Assert.True(afterParts.ContainsKey(name), $"Package part '{name}' disappeared after mutation.");
            if (changed.Contains(name)) continue;
            Assert.True(
                beforeBytes.AsSpan().SequenceEqual(afterParts[name]),
                $"Unrelated package part '{name}' changed during strict replacement.");
        }
    }

    public static XDocument ParsePart(byte[] partBytes)
    {
        using var stream = new MemoryStream(partBytes, writable: false);
        using var reader = XmlReader.Create(stream, SafeXmlSettings);
        return XDocument.Load(reader);
    }

    public static List<XElement> Paragraphs(byte[] partBytes) =>
        ParsePart(partBytes).Descendants(W + "p").ToList();

    /// <summary>
    /// Visible logical text of a paragraph as used by the strict fixtures:
    /// direct w:t values in document order (w:delText and w:instrText are not w:t).
    /// </summary>
    public static string LogicalParagraphText(XElement paragraph)
    {
        var builder = new StringBuilder();
        foreach (var run in paragraph.Descendants(W + "r"))
        {
            foreach (var text in run.Elements(W + "t"))
            {
                builder.Append(text.Value);
            }
        }
        return builder.ToString();
    }

    public static List<int> FindParagraphIndicesContaining(byte[] partBytes, string target)
    {
        var paragraphs = Paragraphs(partBytes);
        var indices = new List<int>();
        for (int i = 0; i < paragraphs.Count; i++)
        {
            if (LogicalParagraphText(paragraphs[i]).Contains(target, StringComparison.Ordinal))
            {
                indices.Add(i);
            }
        }
        return indices;
    }

    /// <summary>
    /// All paragraphs except the matched ones must be canonically identical
    /// (element structure, attributes and text).
    /// </summary>
    public static void AssertOnlyMatchedParagraphsChanged(
        byte[] beforePartBytes,
        byte[] afterPartBytes,
        IReadOnlyCollection<int> matchedParagraphIndices,
        string partName)
    {
        var beforeParagraphs = Paragraphs(beforePartBytes);
        var afterParagraphs = Paragraphs(afterPartBytes);
        Assert.True(
            beforeParagraphs.Count == afterParagraphs.Count,
            $"[{partName}] paragraph count changed: {beforeParagraphs.Count} -> {afterParagraphs.Count}.");

        var matched = new HashSet<int>(matchedParagraphIndices);
        for (int i = 0; i < beforeParagraphs.Count; i++)
        {
            if (matched.Contains(i)) continue;
            Assert.True(
                XNode.DeepEquals(Canonicalize(beforeParagraphs[i]), Canonicalize(afterParagraphs[i])),
                $"[{partName}] paragraph p{i} is outside the replaced target but changed.");
        }
    }

    /// <summary>
    /// Structural/formatting elements outside text content (sections, table/cell
    /// properties, styles, drawings, fields, revisions) must be unchanged.
    /// </summary>
    public static void AssertInvariantElementsUnchanged(byte[] beforePartBytes, byte[] afterPartBytes, string partName)
    {
        var beforeDocument = ParsePart(beforePartBytes);
        var afterDocument = ParsePart(afterPartBytes);

        foreach (string localName in InvariantElementNames)
        {
            var beforeElements = beforeDocument.Descendants().Where(e => e.Name.LocalName == localName).ToList();
            var afterElements = afterDocument.Descendants().Where(e => e.Name.LocalName == localName).ToList();
            Assert.True(
                beforeElements.Count == afterElements.Count,
                $"[{partName}] '{localName}' element count changed: {beforeElements.Count} -> {afterElements.Count}.");

            for (int i = 0; i < beforeElements.Count; i++)
            {
                Assert.True(
                    XNode.DeepEquals(Canonicalize(beforeElements[i]), Canonicalize(afterElements[i])),
                    $"[{partName}] '{localName}' element #{i} changed during strict replacement.");
            }
        }
    }

    /// <summary>
    /// Inside a matched paragraph the strict replacement may only:
    /// keep the paragraph properties, keep every uninvolved run byte-identical,
    /// anchor the replacement on the first matched run's formatting, and never
    /// invent run formatting that did not exist before.
    /// </summary>
    public static void AssertRunFormattingPreserved(
        byte[] beforePartBytes,
        byte[] afterPartBytes,
        int paragraphIndex,
        string target,
        string replacement,
        string partName)
    {
        var beforeParagraph = Paragraphs(beforePartBytes)[paragraphIndex];
        var afterParagraph = Paragraphs(afterPartBytes)[paragraphIndex];

        Assert.True(
            ElementSignature(beforeParagraph.Element(W + "pPr")) == ElementSignature(afterParagraph.Element(W + "pPr")),
            $"[{partName}] p{paragraphIndex} paragraph properties changed.");

        var beforeRuns = beforeParagraph.Descendants(W + "r").ToList();
        var afterRuns = afterParagraph.Descendants(W + "r").ToList();

        string logicalBefore = LogicalParagraphText(beforeParagraph);
        int matchStart = logicalBefore.IndexOf(target, StringComparison.Ordinal);
        Assert.True(matchStart >= 0, $"[{partName}] p{paragraphIndex} no longer contains '{target}' before mutation.");

        // Runs whose text overlaps the matched logical span.
        var involved = new HashSet<int>();
        int position = 0;
        for (int i = 0; i < beforeRuns.Count; i++)
        {
            int length = beforeRuns[i].Elements(W + "t").Sum(t => t.Value.Length);
            if (length > 0 && position < matchStart + target.Length && position + length > matchStart)
            {
                involved.Add(i);
            }
            position += length;
        }
        Assert.True(involved.Count > 0, $"[{partName}] p{paragraphIndex} has no run overlapping the match.");

        // Exactly one after-run carries the replacement text.
        var carriers = afterRuns
            .Where(r => string.Concat(r.Elements(W + "t").Select(t => t.Value))
                .Contains(replacement, StringComparison.Ordinal))
            .ToList();
        Assert.True(
            carriers.Count == 1,
            $"[{partName}] p{paragraphIndex} expected exactly one replacement-carrying run, found {carriers.Count}.");

        // Uninvolved runs must be identical (text + formatting), in order.
        var beforeUninvolved = beforeRuns
            .Where((r, i) => !involved.Contains(i))
            .Select(RunSignature)
            .ToList();
        var afterUninvolved = afterRuns
            .Where(r => !ReferenceEquals(r, carriers[0]))
            .Select(RunSignature)
            .ToList();
        Assert.True(
            beforeUninvolved.Count == afterUninvolved.Count,
            $"[{partName}] p{paragraphIndex} uninvolved run count changed: {beforeUninvolved.Count} -> {afterUninvolved.Count}.");
        for (int i = 0; i < beforeUninvolved.Count; i++)
        {
            Assert.True(
                beforeUninvolved[i] == afterUninvolved[i],
                $"[{partName}] p{paragraphIndex} uninvolved run #{i} changed during strict replacement.");
        }

        // Anchor formatting = formatting of the first matched run.
        var anchorBefore = beforeRuns[involved.Min()];
        Assert.True(
            RunPrSignature(anchorBefore) == RunPrSignature(carriers[0]),
            $"[{partName}] p{paragraphIndex} replacement did not preserve the anchor run's formatting.");

        // No invented run formatting: every after-run rPr existed before.
        var beforeFormatting = beforeRuns
            .Select(RunPrSignature)
            .Where(signature => signature.Length > 0)
            .ToList();
        foreach (var run in afterRuns)
        {
            var signature = RunPrSignature(run);
            if (signature.Length == 0) continue;
            Assert.True(
                beforeFormatting.Contains(signature),
                $"[{partName}] p{paragraphIndex} a run gained formatting that did not exist before.");
        }
    }

    // ------------------------------------------------------------------

    private static bool IsXmlPart(ZipArchiveEntry entry) =>
        entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ||
        entry.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string partName) => partName.Replace('\\', '/');

    private static byte[] ReadEntry(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static XElement Canonicalize(XElement element)
    {
        var clone = new XElement(element);
        var insignificantWhitespace = clone.DescendantNodes()
            .OfType<XText>()
            .Where(t => t.Parent?.Name.LocalName != "t" && string.IsNullOrWhiteSpace(t.Value))
            .ToList();
        foreach (var text in insignificantWhitespace)
        {
            text.Remove();
        }
        return clone;
    }

    private static string ElementSignature(XElement? element) =>
        element == null ? string.Empty : Canonicalize(element).ToString(SaveOptions.DisableFormatting);

    private static string RunSignature(XElement run) =>
        Canonicalize(run).ToString(SaveOptions.DisableFormatting);

    private static string RunPrSignature(XElement run) =>
        ElementSignature(run.Element(W + "rPr"));
}
