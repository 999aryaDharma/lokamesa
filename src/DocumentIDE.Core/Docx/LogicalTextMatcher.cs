using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace DocumentIDE.Core.Docx;

public enum DocxStructureOrigin
{
    Body,
    Table,
    Header,
    Footer
}

public record DocxRunSegmentMatch(
    int RunIndex,
    int LocalStartIndex,
    int LocalLength,
    string Text
);

public class DocxMatchResult
{
    public required string OccurrenceId { get; init; }
    public required DocxStructureOrigin Origin { get; init; }
    public required string PartName { get; init; }
    public required int ParagraphIndex { get; init; }
    public required int LogicalStartIndex { get; init; }
    public required int LogicalLength { get; init; }
    public required string MatchedText { get; init; }
    public required string ContextBefore { get; init; }
    public required string ContextAfter { get; init; }
    public required string PreviewContext { get; init; }
    public required string FullParagraphText { get; init; }
    public required IReadOnlyList<DocxRunSegmentMatch> RunSegments { get; init; }
}

public class DocxSearchOptions
{
    public StringComparison Comparison { get; set; } = StringComparison.Ordinal;
    public int ContextRadius { get; set; } = 30;
    public DocxPreflightOptions? PreflightOptions { get; set; }
}

public class LogicalTextMatcher
{
    private readonly IDocxPackagePreflight? _preflight;

    public LogicalTextMatcher(IDocxPackagePreflight? preflight = null)
    {
        _preflight = preflight;
    }

    public IReadOnlyList<DocxMatchResult> Search(string filePath, string query, DocxSearchOptions? options = null)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            throw new ArgumentException($"File does not exist or invalid path: '{filePath}'", nameof(filePath));

        if (_preflight != null)
        {
            var preflightResult = _preflight.ValidatePackage(filePath, options?.PreflightOptions);
            if (!preflightResult.IsValid)
            {
                throw new InvalidOperationException($"Package preflight validation failed: {preflightResult.ErrorMessage} (Code: {preflightResult.ErrorCode})");
            }
        }

        using var fileStream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Search(fileStream, query, options, skipPreflightCheck: true);
    }

    public IReadOnlyList<DocxMatchResult> Search(Stream stream, string query, DocxSearchOptions? options = null)
    {
        return Search(stream, query, options, skipPreflightCheck: false);
    }

    private IReadOnlyList<DocxMatchResult> Search(Stream stream, string query, DocxSearchOptions? options, bool skipPreflightCheck)
    {
        if (stream == null || !stream.CanRead)
            throw new ArgumentException("Stream is null or unreadable.", nameof(stream));

        if (string.IsNullOrEmpty(query))
            return Array.Empty<DocxMatchResult>();

        options ??= new DocxSearchOptions();

        if (!skipPreflightCheck && _preflight != null)
        {
            var preflightResult = _preflight.ValidatePackage(stream, options.PreflightOptions);
            if (!preflightResult.IsValid)
            {
                throw new InvalidOperationException($"Package preflight validation failed: {preflightResult.ErrorMessage} (Code: {preflightResult.ErrorCode})");
            }
        }

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
            return Search(archive, query, options);
        }
    }

    public IReadOnlyList<DocxMatchResult> Search(ZipArchive archive, string query, DocxSearchOptions? options = null)
    {
        if (archive == null)
            throw new ArgumentNullException(nameof(archive));

        if (string.IsNullOrEmpty(query))
            return Array.Empty<DocxMatchResult>();

        options ??= new DocxSearchOptions();

        var matches = new List<DocxMatchResult>();

        // Identify target XML parts in the archive (document.xml, header*.xml, footer*.xml)
        var partsToSearch = IdentifyPartsToSearch(archive);

        var xmlSettings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreWhitespace = false
        };

        foreach (var entry in partsToSearch)
        {
            string partName = entry.FullName.Replace('\\', '/');
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

            SearchPart(xdoc, partName, query, options, matches);
        }

        return matches;
    }

    private static List<ZipArchiveEntry> IdentifyPartsToSearch(ZipArchive archive)
    {
        var parts = new List<ZipArchiveEntry>();

        // Main document part
        var docEntry = archive.Entries.FirstOrDefault(e =>
            string.Equals(e.FullName.Replace('\\', '/'), "word/document.xml", StringComparison.OrdinalIgnoreCase));
        if (docEntry != null)
        {
            parts.Add(docEntry);
        }

        // Header and footer parts
        foreach (var entry in archive.Entries)
        {
            string normPath = entry.FullName.Replace('\\', '/').ToLowerInvariant();
            if (normPath.StartsWith("word/header") && normPath.EndsWith(".xml"))
            {
                if (!parts.Contains(entry)) parts.Add(entry);
            }
            else if (normPath.StartsWith("word/footer") && normPath.EndsWith(".xml"))
            {
                if (!parts.Contains(entry)) parts.Add(entry);
            }
        }

        return parts;
    }

    private static void SearchPart(
        XDocument xdoc,
        string partName,
        string query,
        DocxSearchOptions options,
        List<DocxMatchResult> matches)
    {
        if (xdoc.Root == null) return;

        // Find all paragraph elements <w:p>
        var paragraphs = xdoc.Descendants().Where(e => e.Name.LocalName == "p").ToList();

        for (int pIdx = 0; pIdx < paragraphs.Count; pIdx++)
        {
            var pElem = paragraphs[pIdx];
            var origin = ResolveOrigin(pElem, partName);

            var (logicalText, runSegments) = ExtractLogicalTextAndRunSegments(pElem);
            if (string.IsNullOrEmpty(logicalText)) continue;

            int matchPos = 0;
            while (matchPos < logicalText.Length)
            {
                int foundIndex = logicalText.IndexOf(query, matchPos, options.Comparison);
                if (foundIndex < 0) break;

                int queryLen = query.Length;

                // Calculate preview context
                int contextStart = Math.Max(0, foundIndex - options.ContextRadius);
                string contextBefore = logicalText.Substring(contextStart, foundIndex - contextStart);

                int matchEnd = foundIndex + queryLen;
                int contextEnd = Math.Min(logicalText.Length, matchEnd + options.ContextRadius);
                string contextAfter = logicalText.Substring(matchEnd, contextEnd - matchEnd);

                string prefixEllipsis = contextStart > 0 ? "..." : "";
                string suffixEllipsis = contextEnd < logicalText.Length ? "..." : "";
                string matchedSubstring = logicalText.Substring(foundIndex, queryLen);
                string previewContext = $"{prefixEllipsis}{contextBefore}{matchedSubstring}{contextAfter}{suffixEllipsis}";

                // Identify overlapping run segments
                var matchedRunSegments = GetOverlappingRunSegments(runSegments, foundIndex, queryLen);

                // Build stable occurrence ID
                string occurrenceId = $"occ_{partName}_{origin}_p{pIdx}_idx{foundIndex}_len{queryLen}";

                var matchResult = new DocxMatchResult
                {
                    OccurrenceId = occurrenceId,
                    Origin = origin,
                    PartName = partName,
                    ParagraphIndex = pIdx,
                    LogicalStartIndex = foundIndex,
                    LogicalLength = queryLen,
                    MatchedText = matchedSubstring,
                    ContextBefore = contextBefore,
                    ContextAfter = contextAfter,
                    PreviewContext = previewContext,
                    FullParagraphText = logicalText,
                    RunSegments = matchedRunSegments
                };

                matches.Add(matchResult);

                matchPos = foundIndex + Math.Max(1, queryLen);
            }
        }
    }

    private static DocxStructureOrigin ResolveOrigin(XElement pElem, string partName)
    {
        // If paragraph is inside a table <w:tbl>, origin is Table
        bool isInsideTable = pElem.Ancestors().Any(a => a.Name.LocalName == "tbl");
        if (isInsideTable)
        {
            return DocxStructureOrigin.Table;
        }

        string normPart = partName.ToLowerInvariant();
        if (normPart.Contains("header"))
        {
            return DocxStructureOrigin.Header;
        }
        if (normPart.Contains("footer"))
        {
            return DocxStructureOrigin.Footer;
        }

        return DocxStructureOrigin.Body;
    }

    private class InternalSegment
    {
        public required int RunIndex { get; init; }
        public required XElement RunElement { get; init; }
        public required int GlobalStartIndex { get; init; }
        public required int Length { get; init; }
        public required string Text { get; init; }
    }

    private static (string LogicalText, List<InternalSegment> Segments) ExtractLogicalTextAndRunSegments(XElement pElem)
    {
        var sb = new StringBuilder();
        var segments = new List<InternalSegment>();

        int runIndexCounter = 0;
        int fieldInstructionDepth = 0;

        // Traverse child elements of paragraph in document order
        foreach (var node in pElem.DescendantsAndSelf())
        {
            if (node is XElement elem)
            {
                string localName = elem.Name.LocalName;

                if (localName == "fldChar")
                {
                    string? fldCharType = elem.Attribute(XName.Get("fldCharType", elem.Name.NamespaceName))?.Value
                        ?? elem.Attributes().FirstOrDefault(a => a.Name.LocalName == "fldCharType")?.Value;

                    if (string.Equals(fldCharType, "begin", StringComparison.OrdinalIgnoreCase))
                    {
                        fieldInstructionDepth++;
                    }
                    else if (string.Equals(fldCharType, "separate", StringComparison.OrdinalIgnoreCase))
                    {
                        if (fieldInstructionDepth > 0) fieldInstructionDepth--;
                    }
                    else if (string.Equals(fldCharType, "end", StringComparison.OrdinalIgnoreCase))
                    {
                        if (fieldInstructionDepth > 0) fieldInstructionDepth--;
                    }
                }
            }
        }

        // Re-traverse runs directly to build logical text safely
        int currentFieldDepth = 0;

        foreach (var child in pElem.Elements())
        {
            ProcessElementForText(child, sb, segments, ref runIndexCounter, ref currentFieldDepth);
        }

        return (sb.ToString(), segments);
    }

    private static void ProcessElementForText(
        XElement element,
        StringBuilder sb,
        List<InternalSegment> segments,
        ref int runIndexCounter,
        ref int fieldDepth)
    {
        string localName = element.Name.LocalName;

        if (localName == "fldChar")
        {
            string? fldCharType = element.Attribute(XName.Get("fldCharType", element.Name.NamespaceName))?.Value
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
            return;
        }

        if (localName == "instrText")
        {
            // Skip field instruction text entirely
            return;
        }

        if (fieldDepth > 0 && (localName == "instrText" || localName == "t"))
        {
            // Inside field instructions - skip text
            return;
        }

        if (localName == "r")
        {
            int currentRunIndex = runIndexCounter++;
            var runSb = new StringBuilder();

            foreach (var rChild in element.Elements())
            {
                string rChildName = rChild.Name.LocalName;

                if (rChildName == "fldChar")
                {
                    string? fldCharType = rChild.Attribute(XName.Get("fldCharType", rChild.Name.NamespaceName))?.Value
                        ?? rChild.Attributes().FirstOrDefault(a => a.Name.LocalName == "fldCharType")?.Value;

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
                else if (rChildName == "instrText")
                {
                    // Skip field instructions inside run
                    continue;
                }
                else if (fieldDepth == 0 && rChildName == "t")
                {
                    runSb.Append(rChild.Value);
                }
                else if (fieldDepth == 0 && rChildName == "tab")
                {
                    runSb.Append('\t');
                }
                else if (fieldDepth == 0 && (rChildName == "br" || rChildName == "cr"))
                {
                    runSb.Append('\n');
                }
                else if (fieldDepth == 0 && rChildName == "noBreakHyphen")
                {
                    runSb.Append('\u2011');
                }
            }

            string runText = runSb.ToString();
            if (runText.Length > 0)
            {
                int globalStart = sb.Length;
                sb.Append(runText);
                segments.Add(new InternalSegment
                {
                    RunIndex = currentRunIndex,
                    RunElement = element,
                    GlobalStartIndex = globalStart,
                    Length = runText.Length,
                    Text = runText
                });
            }

            return;
        }

        // Recurse into child containers (e.g. w:hyperlink, w:sdt, w:fldSimple)
        foreach (var child in element.Elements())
        {
            ProcessElementForText(child, sb, segments, ref runIndexCounter, ref fieldDepth);
        }
    }

    private static List<DocxRunSegmentMatch> GetOverlappingRunSegments(
        List<InternalSegment> segments,
        int matchStart,
        int matchLength)
    {
        var result = new List<DocxRunSegmentMatch>();
        int matchEnd = matchStart + matchLength;

        foreach (var seg in segments)
        {
            int segStart = seg.GlobalStartIndex;
            int segEnd = segStart + seg.Length;

            // Check overlap between [matchStart, matchEnd) and [segStart, segEnd)
            int overlapStart = Math.Max(matchStart, segStart);
            int overlapEnd = Math.Min(matchEnd, segEnd);

            if (overlapStart < overlapEnd)
            {
                int localStart = overlapStart - segStart;
                int localLen = overlapEnd - overlapStart;
                string subText = seg.Text.Substring(localStart, localLen);

                result.Add(new DocxRunSegmentMatch(
                    RunIndex: seg.RunIndex,
                    LocalStartIndex: localStart,
                    LocalLength: localLen,
                    Text: subText
                ));
            }
        }

        return result;
    }
}
