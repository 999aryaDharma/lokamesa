using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using DocumentIDE.Core.Docx;

namespace DocumentIDE.Core.Context;

public class DocxContextFormatterOptions
{
    public int? MaxCharacters { get; set; }
    public bool IncludeHeadersAndFooters { get; set; } = true;
}

public class DocxContextFormatter
{
    private readonly IDocxPackagePreflight? _preflight;

    public DocxContextFormatter(IDocxPackagePreflight? preflight = null)
    {
        _preflight = preflight;
    }

    public string Format(string filePath, DocxContextFormatterOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            throw new ArgumentException($"File path is invalid or does not exist: '{filePath}'", nameof(filePath));

        if (_preflight != null)
        {
            var preflightResult = _preflight.ValidatePackage(filePath);
            if (!preflightResult.IsValid)
            {
                throw new InvalidOperationException($"Package preflight validation failed: {preflightResult.ErrorMessage} (Code: {preflightResult.ErrorCode})");
            }
        }

        using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return FormatInternal(stream, options, skipPreflight: true);
    }

    public string Format(Stream stream, DocxContextFormatterOptions? options = null)
    {
        if (stream == null || !stream.CanRead)
            throw new ArgumentException("Stream is null or unreadable.", nameof(stream));

        return FormatInternal(stream, options, skipPreflight: false);
    }

    private string FormatInternal(Stream stream, DocxContextFormatterOptions? options, bool skipPreflight)
    {
        options ??= new DocxContextFormatterOptions();

        if (!skipPreflight && _preflight != null)
        {
            var preflightResult = _preflight.ValidatePackage(stream);
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
            return FormatArchive(archive, options);
        }
    }

    private string FormatArchive(ZipArchive archive, DocxContextFormatterOptions options)
    {
        var xmlSettings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreWhitespace = false
        };

        var sections = new List<string>();

        // Process Headers
        if (options.IncludeHeadersAndFooters)
        {
            var headerEntries = archive.Entries
                .Where(e => e.FullName.Replace('\\', '/').StartsWith("word/header", StringComparison.OrdinalIgnoreCase) &&
                            e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                .OrderBy(e => e.FullName)
                .ToList();

            foreach (var hEntry in headerEntries)
            {
                var xdoc = LoadXml(hEntry, xmlSettings);
                if (xdoc?.Root != null)
                {
                    var headerLines = new List<string>();
                    ProcessPartContent(xdoc, "[Header]", headerLines);
                    if (headerLines.Count > 0)
                    {
                        sections.Add(string.Join("\n\n", headerLines));
                    }
                }
            }
        }

        // Process Main Document
        var docEntry = archive.Entries.FirstOrDefault(e =>
            string.Equals(e.FullName.Replace('\\', '/'), "word/document.xml", StringComparison.OrdinalIgnoreCase));

        if (docEntry != null)
        {
            var xdoc = LoadXml(docEntry, xmlSettings);
            if (xdoc?.Root != null)
            {
                var bodyElem = xdoc.Root.Elements().FirstOrDefault(e => e.Name.LocalName == "body");
                if (bodyElem != null)
                {
                    var bodyLines = new List<string>();
                    ProcessBody(bodyElem, bodyLines);
                    if (bodyLines.Count > 0)
                    {
                        sections.Add(string.Join("\n\n", bodyLines));
                    }
                }
            }
        }

        // Process Footers
        if (options.IncludeHeadersAndFooters)
        {
            var footerEntries = archive.Entries
                .Where(e => e.FullName.Replace('\\', '/').StartsWith("word/footer", StringComparison.OrdinalIgnoreCase) &&
                            e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                .OrderBy(e => e.FullName)
                .ToList();

            foreach (var fEntry in footerEntries)
            {
                var xdoc = LoadXml(fEntry, xmlSettings);
                if (xdoc?.Root != null)
                {
                    var footerLines = new List<string>();
                    ProcessPartContent(xdoc, "[Footer]", footerLines);
                    if (footerLines.Count > 0)
                    {
                        sections.Add(string.Join("\n\n", footerLines));
                    }
                }
            }
        }

        string fullOutput = string.Join("\n\n", sections).Trim();

        if (options.MaxCharacters.HasValue && options.MaxCharacters.Value > 0 && fullOutput.Length > options.MaxCharacters.Value)
        {
            int limit = options.MaxCharacters.Value;
            string truncated = fullOutput.Substring(0, limit);
            return $"{truncated}\n\n[Content truncated: maximum limit reached]";
        }

        return fullOutput;
    }

    private static XDocument? LoadXml(ZipArchiveEntry entry, XmlReaderSettings settings)
    {
        try
        {
            using var entryStream = entry.Open();
            using var xmlReader = XmlReader.Create(entryStream, settings);
            return XDocument.Load(xmlReader);
        }
        catch (Exception ex) when (ex is XmlException || ex is InvalidDataException)
        {
            throw new InvalidOperationException($"Malformed or corrupted XML in part '{entry.FullName}': {ex.Message}", ex);
        }
    }

    private static void ProcessBody(XElement bodyElem, List<string> lines)
    {
        foreach (var child in bodyElem.Elements())
        {
            string localName = child.Name.LocalName;
            if (localName == "p")
            {
                string text = ExtractParagraphText(child).Trim();
                if (string.IsNullOrEmpty(text)) continue;

                int level = GetHeadingLevel(child);
                if (level >= 1 && level <= 6)
                {
                    lines.Add($"{new string('#', level)} {text}");
                }
                else
                {
                    lines.Add(text);
                }
            }
            else if (localName == "tbl")
            {
                ProcessTable(child, lines);
            }
            else if (localName == "sdt")
            {
                var sdtContent = child.Elements().FirstOrDefault(e => e.Name.LocalName == "sdtContent");
                if (sdtContent != null)
                {
                    ProcessBody(sdtContent, lines);
                }
            }
        }
    }

    private static void ProcessPartContent(XDocument xdoc, string headerTitle, List<string> lines)
    {
        if (xdoc.Root == null) return;
        var paras = xdoc.Descendants().Where(e => e.Name.LocalName == "p").ToList();
        var texts = new List<string>();
        foreach (var p in paras)
        {
            if (p.Ancestors().Any(a => a.Name.LocalName == "tbl")) continue;
            string text = ExtractParagraphText(p).Trim();
            if (!string.IsNullOrEmpty(text))
                texts.Add(text);
        }

        if (texts.Count > 0)
        {
            lines.Add($"### {headerTitle}");
            lines.AddRange(texts);
        }
    }

    private static void ProcessTable(XElement tblElem, List<string> lines)
    {
        var rows = tblElem.Elements().Where(e => e.Name.LocalName == "tr").ToList();
        if (rows.Count == 0) return;

        bool isFirstRow = true;
        foreach (var tr in rows)
        {
            var cells = tr.Elements().Where(e => e.Name.LocalName == "tc").ToList();
            if (cells.Count == 0) continue;

            var cellTexts = new List<string>();
            foreach (var tc in cells)
            {
                var cellParas = tc.Elements().Where(e => e.Name.LocalName == "p");
                var paraTexts = new List<string>();
                foreach (var p in cellParas)
                {
                    string text = ExtractParagraphText(p).Trim();
                    if (!string.IsNullOrEmpty(text))
                        paraTexts.Add(text);
                }
                string combinedCell = string.Join("<br/>", paraTexts).Replace("|", "\\|");
                cellTexts.Add(combinedCell);
            }

            lines.Add($"| {string.Join(" | ", cellTexts)} |");

            if (isFirstRow)
            {
                var separators = cellTexts.Select(_ => "---");
                lines.Add($"| {string.Join(" | ", separators)} |");
                isFirstRow = false;
            }
        }
    }

    private static int GetHeadingLevel(XElement pElem)
    {
        var pPr = pElem.Elements().FirstOrDefault(e => e.Name.LocalName == "pPr");
        if (pPr == null) return 0;

        var pStyle = pPr.Elements().FirstOrDefault(e => e.Name.LocalName == "pStyle");
        if (pStyle != null)
        {
            string? val = pStyle.Attribute(XName.Get("val", pStyle.Name.NamespaceName))?.Value
                ?? pStyle.Attributes().FirstOrDefault(a => a.Name.LocalName == "val")?.Value;

            if (!string.IsNullOrEmpty(val))
            {
                if (string.Equals(val, "Title", StringComparison.OrdinalIgnoreCase)) return 1;

                string norm = val.Replace(" ", "").Replace("_", "").Replace("-", "");
                if (norm.StartsWith("Heading", StringComparison.OrdinalIgnoreCase))
                {
                    string numStr = norm.Substring(7);
                    if (int.TryParse(numStr, out int level) && level >= 1 && level <= 6)
                    {
                        return level;
                    }
                }
            }
        }

        var outlineLvl = pPr.Elements().FirstOrDefault(e => e.Name.LocalName == "outlineLvl");
        if (outlineLvl != null)
        {
            string? val = outlineLvl.Attribute(XName.Get("val", outlineLvl.Name.NamespaceName))?.Value
                ?? outlineLvl.Attributes().FirstOrDefault(a => a.Name.LocalName == "val")?.Value;

            if (int.TryParse(val, out int lvl) && lvl >= 0 && lvl <= 5)
            {
                return lvl + 1;
            }
        }

        return 0;
    }

    private static string ExtractParagraphText(XElement pElem)
    {
        var sb = new StringBuilder();
        int fieldDepth = 0;

        foreach (var child in pElem.Elements())
        {
            ProcessElementForText(child, sb, ref fieldDepth);
        }

        return sb.ToString();
    }

    private static void ProcessElementForText(XElement element, StringBuilder sb, ref int fieldDepth)
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
            return;
        }

        if (fieldDepth > 0 && (localName == "instrText" || localName == "t"))
        {
            return;
        }

        if (localName == "r")
        {
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
                    continue;
                }
                else if (fieldDepth == 0 && rChildName == "t")
                {
                    sb.Append(rChild.Value);
                }
                else if (fieldDepth == 0 && rChildName == "tab")
                {
                    sb.Append('\t');
                }
                else if (fieldDepth == 0 && (rChildName == "br" || rChildName == "cr"))
                {
                    sb.Append('\n');
                }
                else if (fieldDepth == 0 && rChildName == "noBreakHyphen")
                {
                    sb.Append('\u2011');
                }
            }
            return;
        }

        foreach (var child in element.Elements())
        {
            ProcessElementForText(child, sb, ref fieldDepth);
        }
    }
}
