using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using DocumentIDE.Core.Docx;
using DocumentIDE.Core.Documents;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Indexing;

namespace DocumentIDE.Infrastructure.Docx
{
    /// <summary>
    /// IDX-02 structural DOCX parser, extended by DOCX-04 to extract the
    /// deterministic document-lens metadata shape (<see cref="DocumentMetadata"/>).
    ///
    /// Read-only: the package is opened with <see cref="ZipArchiveMode.Read"/> on a
    /// read-shared file handle and is never opened for update. The searchable body
    /// and headings text payloads carry the document's own words (IDX-02 + the
    /// UI-02 preview route); table / header / footer text payloads remain unset.
    ///
    /// Extraction definitions:
    /// <list type="bullet">
    /// <item><description>section count = number of <c>w:sectPr</c> elements,</description></item>
    /// <item><description>table count = number of <c>w:tbl</c> elements in the main document,</description></item>
    /// <item><description>image count = number of <c>w:drawing</c> + <c>w:pict</c> elements in the main document,</description></item>
    /// <item><description>page count = Word's last-saved <c>docProps/app.xml</c> value when present,</description></item>
    /// <item><description>paper/orientation/margin summaries from every section's <c>w:pgSz</c>/<c>w:pgMar</c>,</description></item>
    /// <item><description>primary font/size = most-used effective run formatting (direct formatting →
    /// paragraph style → run style → document defaults; empty runs ignored; ties broken by
    /// ordinal-smallest font name / smallest size),</description></item>
    /// <item><description>headings = paragraphs styled <c>HeadingN</c>/<c>Title</c> or carrying <c>w:outlineLvl</c>.</description></item>
    /// </list>
    /// </summary>
    public class DocxStructureParser : IDocxStructureParser
    {
        private static readonly XNamespace WNs = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        private static readonly XNamespace AppNs = "http://schemas.openxmlformats.org/officeDocument/2006/extended-properties";

        /// <summary>OOXML default margin in twips for every side of a section (1 inch).</summary>
        private const int DefaultMarginTwips = 1440;

        private static readonly XmlReaderSettings SecureXmlSettings = new()
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreWhitespace = true
        };

        /// <summary>Known paper dimensions in twips, independent of orientation.</summary>
        private static readonly (int MinTwips, int MaxTwips, string Name)[] KnownPaperSizes =
        {
            (8391, 11906, "A5"),
            (9979, 14173, "B5"),
            (10440, 15120, "Executive"),
            (11906, 16838, "A4"),
            (12240, 15840, "Letter"),
            (12240, 20160, "Legal"),
            (16838, 23811, "A3")
        };

        private readonly IDocxPackagePreflight? _preflight;

        public DocxStructureParser(IDocxPackagePreflight? preflight = null)
        {
            _preflight = preflight;
        }

        /// <summary>
        /// Parses the given DOCX file and returns its derived metadata.
        /// </summary>
        /// <param name="filePath">Path to the DOCX file.</param>
        /// <returns>Parsed document structure result.</returns>
        public DocxParseResult Parse(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                throw new ArgumentException("DOCX file path is invalid or file does not exist.", nameof(filePath));

            if (_preflight != null)
            {
                var preflightResult = _preflight.ValidatePackage(filePath);
                if (!preflightResult.IsValid)
                {
                    throw new InvalidOperationException(
                        $"Package preflight validation failed: {preflightResult.ErrorMessage} (Code: {preflightResult.ErrorCode})");
                }
            }

            var metadata = new DocumentMetadata
            {
                ExtractedAt = DateTime.UtcNow.ToString("O"),
                ParserVersion = "1.0.0"
            };

            string? bodyText = null;
            string? headingsText = null;

            using (var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false))
            {
                var documentXml = LoadXmlPart(archive, "word/document.xml")
                    ?? throw new InvalidOperationException("Main document part 'word/document.xml' is missing from the package.");

                var stylesXml = LoadXmlPart(archive, "word/styles.xml");
                var appXml = LoadXmlPart(archive, "docProps/app.xml");

                (bodyText, headingsText) = ExtractMetadata(documentXml, stylesXml, appXml, archive, metadata);
            }

            return new DocxParseResult(metadata, SearchableBody: bodyText, HeadingsText: headingsText);
        }

        private static (string? BodyText, string? HeadingsText) ExtractMetadata(
            XDocument documentXml,
            XDocument? stylesXml,
            XDocument? appXml,
            ZipArchive archive,
            DocumentMetadata metadata)
        {
            var body = documentXml.Root?.Elements(WNs + "body").FirstOrDefault();
            var contentScope = body ?? documentXml.Root;

            var sectionProperties = documentXml.Descendants(WNs + "sectPr").ToList();
            metadata.SectionCount = sectionProperties.Count;
            metadata.TableCount = contentScope?.Descendants(WNs + "tbl").Count() ?? 0;
            metadata.ImageCount = CountPictureElements(contentScope);

            var papers = new List<string>();
            var orientations = new List<string>();
            var margins = new List<string>();
            foreach (var sectPr in sectionProperties)
            {
                var pageSize = FindChild(sectPr, "pgSz");
                int? widthTwips = ReadIntAttribute(pageSize, "w");
                int? heightTwips = ReadIntAttribute(pageSize, "h");
                if (widthTwips.HasValue && heightTwips.HasValue)
                {
                    papers.Add(MapPaperName(widthTwips.Value, heightTwips.Value));
                    orientations.Add(ResolveOrientation(pageSize, widthTwips.Value, heightTwips.Value));
                }

                margins.Add(FormatSectionMargins(sectPr));
            }

            metadata.PaperSummary = MetadataNormalizer.NormalizePaperSummary(papers);
            metadata.OrientationSummary = SummarizeDistinct(orientations);
            metadata.MarginSummary = MetadataNormalizer.NormalizeMarginSummary(margins);

            var headings = ExtractHeadings(contentScope);
            metadata.HeadingsJson = headings.Count > 0
                ? JsonSerializer.Serialize(headings)
                : null;

            // IDX-02 body/heading text payloads consumed by the search index and by
            // the UI-02 read-only preview route: the document's own words, never
            // synthesized. Read-only extraction from the already-loaded document part.
            var headingsText = headings.Count > 0
                ? string.Join("\n", headings.Select(h => h.Text))
                : null;
            var bodyText = ExtractBodyText(contentScope);

            var (primaryFont, primarySizePoints) = ComputePrimaryFormatting(contentScope, stylesXml);
            metadata.PrimaryFont = primaryFont;
            metadata.PrimaryFontSize = primarySizePoints;

            metadata.PageCount = ReadSavedPageCount(appXml);

            metadata.HeaderPresent = HasPart(archive, "word/header");
            metadata.FooterPresent = HasPart(archive, "word/footer");

            return (bodyText, headingsText);
        }

        /// <summary>
        /// Extracts the body paragraph text in document order (each paragraph's
        /// concatenated <c>w:t</c> runs), newline separated, for the searchable
        /// body payload. Returns <c>null</c> when the body carries no text.
        /// </summary>
        private static string? ExtractBodyText(XElement? scope)
        {
            if (scope == null) return null;

            var paragraphs = new List<string>();
            foreach (var paragraph in scope.Descendants(WNs + "p"))
            {
                string text = ExtractParagraphText(paragraph);
                if (text.Length == 0) continue;
                paragraphs.Add(text);
            }

            return paragraphs.Count == 0 ? null : string.Join("\n", paragraphs);
        }

        private static string ExtractParagraphText(XElement paragraph)
            => string.Concat(paragraph.Descendants(WNs + "t").Select(t => t.Value)).Trim();

        private static int CountPictureElements(XElement? scope)
        {
            if (scope == null) return 0;
            return scope.Descendants(WNs + "drawing").Count()
                 + scope.Descendants(WNs + "pict").Count();
        }

        private static List<DocumentLensHeading> ExtractHeadings(XElement? scope)
        {
            var headings = new List<DocumentLensHeading>();
            if (scope == null) return headings;

            foreach (var paragraph in scope.Descendants(WNs + "p"))
            {
                int level = GetHeadingLevel(paragraph);
                if (level == 0) continue;

                string text = ExtractParagraphText(paragraph);
                if (text.Length == 0) continue;

                headings.Add(new DocumentLensHeading(level, text));
            }

            return headings;
        }

        private static int GetHeadingLevel(XElement paragraph)
        {
            var paragraphProperties = FindChild(paragraph, "pPr");
            if (paragraphProperties == null) return 0;

            var paragraphStyle = FindChild(paragraphProperties, "pStyle");
            string? styleId = ReadAttribute(paragraphStyle, "val");
            if (!string.IsNullOrEmpty(styleId))
            {
                if (styleId.Equals("Title", StringComparison.OrdinalIgnoreCase)) return 1;

                string normalized = styleId.Replace(" ", "").Replace("_", "").Replace("-", "");
                if (normalized.StartsWith("Heading", StringComparison.OrdinalIgnoreCase))
                {
                    string number = normalized.Substring("Heading".Length);
                    if (int.TryParse(number, NumberStyles.Integer, CultureInfo.InvariantCulture, out int level)
                        && level >= 1 && level <= 6)
                    {
                        return level;
                    }
                }
            }

            var outlineLevel = FindChild(paragraphProperties, "outlineLvl");
            int? outlineValue = ReadIntAttribute(outlineLevel, "val");
            if (outlineValue.HasValue && outlineValue.Value >= 0 && outlineValue.Value <= 5)
            {
                return outlineValue.Value + 1;
            }

            return 0;
        }

        private static (string? Font, double? SizePoints) ComputePrimaryFormatting(XElement? scope, XDocument? stylesXml)
        {
            var styleFormats = new Dictionary<string, (string? Font, int? SizeHalfPoints)>(StringComparer.OrdinalIgnoreCase);
            string? defaultFont = null;
            int? defaultSizeHalfPoints = null;

            var stylesRoot = stylesXml?.Root;
            if (stylesRoot != null)
            {
                var docDefaults = stylesRoot.Elements().FirstOrDefault(e => e.Name.LocalName == "docDefaults");
                var defaultRunProperties = FindDescendant(docDefaults, "rPrDefault", "rPr");
                defaultFont = ReadFontName(defaultRunProperties);
                defaultSizeHalfPoints = ReadSizeHalfPoints(defaultRunProperties);

                foreach (var style in stylesRoot.Elements().Where(e => e.Name.LocalName == "style"))
                {
                    string? styleId = ReadAttribute(style, "styleId");
                    if (string.IsNullOrEmpty(styleId)) continue;

                    var styleRunProperties = FindChild(style, "rPr");
                    styleFormats[styleId] = (ReadFontName(styleRunProperties), ReadSizeHalfPoints(styleRunProperties));
                }
            }

            if (scope == null) return (null, null);

            var fontCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            var sizeCounts = new Dictionary<int, int>();

            foreach (var run in scope.Descendants(WNs + "r"))
            {
                var textNodes = run.Descendants(WNs + "t").ToList();
                if (!textNodes.Any(t => !string.IsNullOrEmpty(t.Value))) continue;

                var runProperties = FindChild(run, "rPr");
                string? font = ReadFontName(runProperties);
                int? sizeHalfPoints = ReadSizeHalfPoints(runProperties);

                var paragraph = run.Ancestors(WNs + "p").FirstOrDefault();
                string? paragraphStyleId = ReadAttribute(
                    FindChild(FindChild(paragraph, "pPr"), "pStyle"),
                    "val");
                string? runStyleId = ReadAttribute(FindChild(runProperties, "rStyle"), "val");

                font ??= LookupStyle(styleFormats, paragraphStyleId)?.Font;
                font ??= LookupStyle(styleFormats, runStyleId)?.Font;
                font ??= defaultFont;

                sizeHalfPoints ??= LookupStyle(styleFormats, paragraphStyleId)?.SizeHalfPoints;
                sizeHalfPoints ??= LookupStyle(styleFormats, runStyleId)?.SizeHalfPoints;
                sizeHalfPoints ??= defaultSizeHalfPoints;

                if (!string.IsNullOrEmpty(font))
                {
                    fontCounts[font] = fontCounts.TryGetValue(font, out int fontCount) ? fontCount + 1 : 1;
                }

                if (sizeHalfPoints.HasValue)
                {
                    sizeCounts[sizeHalfPoints.Value] = sizeCounts.TryGetValue(sizeHalfPoints.Value, out int sizeCount)
                        ? sizeCount + 1
                        : 1;
                }
            }

            string? primaryFont = fontCounts.Count == 0
                ? null
                : fontCounts
                    .OrderByDescending(pair => pair.Value)
                    .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                    .First().Key;

            int? primarySizeHalfPoints = sizeCounts.Count == 0
                ? null
                : sizeCounts
                    .OrderByDescending(pair => pair.Value)
                    .ThenBy(pair => pair.Key)
                    .First().Key;

            return (primaryFont, primarySizeHalfPoints / 2.0);
        }

        private static (string? Font, int? SizeHalfPoints)? LookupStyle(
            Dictionary<string, (string? Font, int? SizeHalfPoints)> styles,
            string? styleId)
        {
            if (string.IsNullOrEmpty(styleId)) return null;
            return styles.TryGetValue(styleId, out var format) ? format : null;
        }

        private static string? ReadFontName(XElement? runProperties)
        {
            var fonts = FindChild(runProperties, "rFonts");
            if (fonts == null) return null;

            return ReadAttribute(fonts, "ascii")
                ?? ReadAttribute(fonts, "hAnsi")
                ?? ReadAttribute(fonts, "cs")
                ?? ReadAttribute(fonts, "eastAsia");
        }

        private static int? ReadSizeHalfPoints(XElement? runProperties)
        {
            return ReadIntAttribute(FindChild(runProperties, "sz"), "val");
        }

        private static string MapPaperName(int widthTwips, int heightTwips)
        {
            int min = Math.Min(widthTwips, heightTwips);
            int max = Math.Max(widthTwips, heightTwips);

            foreach (var (minTwips, maxTwips, name) in KnownPaperSizes)
            {
                if (minTwips == min && maxTwips == max) return name;
            }

            return $"Custom ({widthTwips} x {heightTwips} twips)";
        }

        private static string ResolveOrientation(XElement? pageSize, int widthTwips, int heightTwips)
        {
            string? orient = ReadAttribute(pageSize, "orient");
            if (string.Equals(orient, "landscape", StringComparison.OrdinalIgnoreCase)) return "Landscape";
            if (string.Equals(orient, "portrait", StringComparison.OrdinalIgnoreCase)) return "Portrait";
            return widthTwips > heightTwips ? "Landscape" : "Portrait";
        }

        private static string FormatSectionMargins(XElement sectPr)
        {
            var pageMargins = FindChild(sectPr, "pgMar");
            int top = ReadIntAttribute(pageMargins, "top") ?? DefaultMarginTwips;
            int right = ReadIntAttribute(pageMargins, "right") ?? DefaultMarginTwips;
            int bottom = ReadIntAttribute(pageMargins, "bottom") ?? DefaultMarginTwips;
            int left = ReadIntAttribute(pageMargins, "left") ?? DefaultMarginTwips;

            return $"{ToCentimeters(top)} / {ToCentimeters(right)} / {ToCentimeters(bottom)} / {ToCentimeters(left)} cm";
        }

        private static string ToCentimeters(int twips)
        {
            // Integer math in hundredths of a centimeter keeps the rounding
            // deterministic across platforms.
            int hundredths = (int)Math.Round(twips * 254.0 / 1440.0, MidpointRounding.AwayFromZero);
            if (hundredths % 100 == 0)
            {
                return (hundredths / 100).ToString(CultureInfo.InvariantCulture);
            }
            if (hundredths % 10 == 0)
            {
                return (hundredths / 100.0).ToString("0.0", CultureInfo.InvariantCulture);
            }
            return (hundredths / 100.0).ToString("0.00", CultureInfo.InvariantCulture);
        }

        private static string? SummarizeDistinct(IEnumerable<string> values)
        {
            var cleaned = values
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (cleaned.Count == 0) return null;
            if (cleaned.Count == 1) return cleaned[0];

            return $"Mixed ({string.Join(", ", cleaned)})";
        }

        private static int? ReadSavedPageCount(XDocument? appXml)
        {
            string? pagesText = appXml?.Root?
                .Elements()
                .FirstOrDefault(e => e.Name.LocalName == "Pages")?
                .Value;

            if (int.TryParse(pagesText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int pages) && pages > 0)
            {
                return pages;
            }

            return null;
        }

        private static bool HasPart(ZipArchive archive, string partPrefix)
        {
            return archive.Entries.Any(entry =>
            {
                string normalized = entry.FullName.Replace('\\', '/').ToLowerInvariant();
                return normalized.StartsWith(partPrefix, StringComparison.Ordinal)
                    && normalized.EndsWith(".xml", StringComparison.Ordinal);
            });
        }

        private static XDocument? LoadXmlPart(ZipArchive archive, string partName)
        {
            string target = partName.Replace('\\', '/');
            var entry = archive.Entries.FirstOrDefault(e =>
                string.Equals(e.FullName.Replace('\\', '/'), target, StringComparison.OrdinalIgnoreCase));
            if (entry == null) return null;

            try
            {
                using var entryStream = entry.Open();
                using var reader = XmlReader.Create(entryStream, SecureXmlSettings);
                return XDocument.Load(reader);
            }
            catch (Exception ex) when (ex is XmlException || ex is InvalidDataException || ex is IOException)
            {
                throw new InvalidOperationException(
                    $"Malformed or unreadable XML in package part '{entry.FullName}': {ex.Message}", ex);
            }
        }

        private static XElement? FindChild(XElement? parent, string localName)
        {
            return parent?.Elements().FirstOrDefault(e => e.Name.LocalName == localName);
        }

        private static XElement? FindDescendant(XElement? parent, string firstLocalName, string secondLocalName)
        {
            return parent?
                .Elements().FirstOrDefault(e => e.Name.LocalName == firstLocalName)?
                .Elements().FirstOrDefault(e => e.Name.LocalName == secondLocalName);
        }

        private static string? ReadAttribute(XElement? element, string localName)
        {
            if (element == null) return null;

            // Primary lookup uses the transitional OOXML namespace; the local-name
            // fallback keeps strict-namespace packages readable.
            return element.Attribute(WNs + localName)?.Value
                ?? element.Attributes().FirstOrDefault(a => a.Name.LocalName == localName)?.Value;
        }

        private static int? ReadIntAttribute(XElement? element, string localName)
        {
            string? raw = ReadAttribute(element, localName);
            return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : null;
        }
    }
}
