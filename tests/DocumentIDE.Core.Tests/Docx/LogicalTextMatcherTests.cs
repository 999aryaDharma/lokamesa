using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using DocumentIDE.Core.Docx;
using Xunit;

namespace DocumentIDE.Core.Tests.Docx;

public class LogicalTextMatcherTests
{
    private static byte[] CreateDocxPackage(
        string documentXml,
        string? headerXml = null,
        string? footerXml = null)
    {
        using var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            // [Content_Types].xml
            var ctEntry = archive.CreateEntry("[Content_Types].xml");
            using (var writer = new StreamWriter(ctEntry.Open(), Encoding.UTF8))
            {
                writer.Write("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n" +
                    "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">\r\n" +
                    "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>\r\n" +
                    "<Default Extension=\"xml\" ContentType=\"application/xml\"/>\r\n" +
                    "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>\r\n" +
                    (headerXml != null ? "<Override PartName=\"/word/header1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml\"/>\r\n" : "") +
                    (footerXml != null ? "<Override PartName=\"/word/footer1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.footer+xml\"/>\r\n" : "") +
                    "</Types>");
            }

            // _rels/.rels
            var relsEntry = archive.CreateEntry("_rels/.rels");
            using (var writer = new StreamWriter(relsEntry.Open(), Encoding.UTF8))
            {
                writer.Write("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n" +
                    "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">\r\n" +
                    "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/>\r\n" +
                    "</Relationships>");
            }

            // word/document.xml
            var docEntry = archive.CreateEntry("word/document.xml");
            using (var writer = new StreamWriter(docEntry.Open(), Encoding.UTF8))
            {
                writer.Write(documentXml);
            }

            if (headerXml != null)
            {
                var hEntry = archive.CreateEntry("word/header1.xml");
                using var writer = new StreamWriter(hEntry.Open(), Encoding.UTF8);
                writer.Write(headerXml);
            }

            if (footerXml != null)
            {
                var fEntry = archive.CreateEntry("word/footer1.xml");
                using var writer = new StreamWriter(fEntry.Open(), Encoding.UTF8);
                writer.Write(footerXml);
            }
        }

        return memoryStream.ToArray();
    }

    [Fact]
    public void Search_NusaDua1_SplitAcrossRuns_ReturnsOneMatch()
    {
        // Required test: NUSA-DUA-1 split across runs is one match.
        var xml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                <w:body>
                    <w:p>
                        <w:r><w:t>Project </w:t></w:r>
                        <w:r><w:t>NUSA-</w:t></w:r>
                        <w:r><w:t>DUA-1</w:t></w:r>
                        <w:r><w:t> is active.</w:t></w:r>
                    </w:p>
                </w:body>
            </w:document>
            """;

        var bytes = CreateDocxPackage(xml);
        using var stream = new MemoryStream(bytes);
        var matcher = new LogicalTextMatcher();

        var matches = matcher.Search(stream, "NUSA-DUA-1");

        Assert.Single(matches);
        var match = matches[0];
        Assert.Equal("NUSA-DUA-1", match.MatchedText);
        Assert.Equal(DocxStructureOrigin.Body, match.Origin);
        Assert.Equal("word/document.xml", match.PartName);
        Assert.Equal(8, match.LogicalStartIndex); // "Project " is 8 chars
        Assert.Equal(10, match.LogicalLength);
        Assert.False(string.IsNullOrWhiteSpace(match.OccurrenceId));
        Assert.Contains("NUSA-DUA-1", match.PreviewContext);
        Assert.Equal(2, match.RunSegments.Count); // Spans across 2 runs
    }

    [Fact]
    public void Search_HeaderTableBodyFooter_CarriesCorrectOrigin()
    {
        // Required test: Header/table matches carry correct origin.
        var docXml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                <w:body>
                    <w:p><w:r><w:t>CONFIDENTIAL report in body.</w:t></w:r></w:p>
                    <w:tbl>
                        <w:tr>
                            <w:tc>
                                <w:p><w:r><w:t>CONFIDENTIAL table cell.</w:t></w:r></w:p>
                            </w:tc>
                        </w:tr>
                    </w:tbl>
                </w:body>
            </w:document>
            """;

        var headerXml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:hdr xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                <w:p><w:r><w:t>CONFIDENTIAL header text.</w:t></w:r></w:p>
            </w:hdr>
            """;

        var footerXml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:ftr xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                <w:p><w:r><w:t>CONFIDENTIAL footer text.</w:t></w:r></w:p>
            </w:ftr>
            """;

        var bytes = CreateDocxPackage(docXml, headerXml, footerXml);
        using var stream = new MemoryStream(bytes);
        var matcher = new LogicalTextMatcher();

        var matches = matcher.Search(stream, "CONFIDENTIAL");

        Assert.Equal(4, matches.Count);

        var bodyMatch = matches.Single(m => m.Origin == DocxStructureOrigin.Body);
        Assert.Equal("word/document.xml", bodyMatch.PartName);
        Assert.Contains("body", bodyMatch.FullParagraphText);

        var tableMatch = matches.Single(m => m.Origin == DocxStructureOrigin.Table);
        Assert.Equal("word/document.xml", tableMatch.PartName);
        Assert.Contains("table cell", tableMatch.FullParagraphText);

        var headerMatch = matches.Single(m => m.Origin == DocxStructureOrigin.Header);
        Assert.Equal("word/header1.xml", headerMatch.PartName);
        Assert.Contains("header text", headerMatch.FullParagraphText);

        var footerMatch = matches.Single(m => m.Origin == DocxStructureOrigin.Footer);
        Assert.Equal("word/footer1.xml", footerMatch.PartName);
        Assert.Contains("footer text", footerMatch.FullParagraphText);
    }

    [Fact]
    public void Search_RepeatedText_YieldsStableOccurrenceIds()
    {
        // Required test: Repeated text yields stable occurrence IDs.
        var docXml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                <w:body>
                    <w:p>
                        <w:r><w:t>TARGET item first occurrence. </w:t></w:r>
                        <w:r><w:t>Second TARGET item here.</w:t></w:r>
                    </w:p>
                    <w:p>
                        <w:r><w:t>Third TARGET in second paragraph.</w:t></w:r>
                    </w:p>
                </w:body>
            </w:document>
            """;

        var bytes = CreateDocxPackage(docXml);
        var matcher = new LogicalTextMatcher();

        // Run search twice on independent streams
        using var stream1 = new MemoryStream(bytes);
        var matches1 = matcher.Search(stream1, "TARGET");

        using var stream2 = new MemoryStream(bytes);
        var matches2 = matcher.Search(stream2, "TARGET");

        Assert.Equal(3, matches1.Count);
        Assert.Equal(3, matches2.Count);

        // Occurrence IDs must be distinct within the result set
        var uniqueIds = matches1.Select(m => m.OccurrenceId).Distinct().ToList();
        Assert.Equal(3, uniqueIds.Count);

        // Occurrence IDs must be 100% stable (identical between runs)
        for (int i = 0; i < matches1.Count; i++)
        {
            Assert.Equal(matches1[i].OccurrenceId, matches2[i].OccurrenceId);
            Assert.Equal(matches1[i].LogicalStartIndex, matches2[i].LogicalStartIndex);
        }
    }

    [Fact]
    public void Search_ExcludesFieldInstructions()
    {
        // Requirement 3: Exclude field instructions unless supported.
        var docXml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                <w:body>
                    <w:p>
                        <w:r><w:t>Page </w:t></w:r>
                        <w:r><w:fldChar w:fldCharType="begin"/></w:r>
                        <w:r><w:instrText xml:space="preserve"> PAGE \* MERGEFORMAT </w:instrText></w:r>
                        <w:r><w:fldChar w:fldCharType="separate"/></w:r>
                        <w:r><w:t>1</w:t></w:r>
                        <w:r><w:fldChar w:fldCharType="end"/></w:r>
                        <w:r><w:t> of 10.</w:t></w:r>
                    </w:p>
                </w:body>
            </w:document>
            """;

        var bytes = CreateDocxPackage(docXml);
        using var stream = new MemoryStream(bytes);
        var matcher = new LogicalTextMatcher();

        // Field instruction "MERGEFORMAT" or "PAGE" inside instrText must NOT be matched
        var mergeMatches = matcher.Search(stream, "MERGEFORMAT");
        Assert.Empty(mergeMatches);

        // Match regular logical text "Page 1 of 10."
        stream.Position = 0;
        var pageMatches = matcher.Search(stream, "Page 1 of 10.");
        Assert.Single(pageMatches);
        Assert.Equal("Page 1 of 10.", pageMatches[0].FullParagraphText);
    }

    [Fact]
    public void Search_ReturnsPreviewContextWithRadius()
    {
        // Requirement 4: Return preview context around each match.
        var docXml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                <w:body>
                    <w:p>
                        <w:r><w:t>The quick brown fox jumps over the LAZY DOG while the sun sets behind the mountains.</w:t></w:r>
                    </w:p>
                </w:body>
            </w:document>
            """;

        var bytes = CreateDocxPackage(docXml);
        using var stream = new MemoryStream(bytes);
        var matcher = new LogicalTextMatcher();

        var options = new DocxSearchOptions { ContextRadius = 15 };
        var matches = matcher.Search(stream, "LAZY DOG", options);

        Assert.Single(matches);
        var match = matches[0];

        Assert.Equal("LAZY DOG", match.MatchedText);
        Assert.Equal("jumps over the ", match.ContextBefore);
        Assert.Equal(" while the sun ", match.ContextAfter);
        Assert.Equal("...jumps over the LAZY DOG while the sun ...", match.PreviewContext);
    }

    [Fact]
    public void Search_FilePathOverload_SearchSuccessfully()
    {
        var docXml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                <w:body>
                    <w:p><w:r><w:t>File path search target.</w:t></w:r></w:p>
                </w:body>
            </w:document>
            """;

        var bytes = CreateDocxPackage(docXml);
        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.docx");
        try
        {
            File.WriteAllBytes(tempFile, bytes);
            var matcher = new LogicalTextMatcher();

            var matches = matcher.Search(tempFile, "path search");

            Assert.Single(matches);
            Assert.Equal("path search", matches[0].MatchedText);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void Search_EmptyOrNullQuery_ReturnsEmptyList()
    {
        var docXml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                <w:body>
                    <w:p><w:r><w:t>Some document content.</w:t></w:r></w:p>
                </w:body>
            </w:document>
            """;

        var bytes = CreateDocxPackage(docXml);
        using var stream = new MemoryStream(bytes);
        var matcher = new LogicalTextMatcher();

        Assert.Empty(matcher.Search(stream, ""));
        Assert.Empty(matcher.Search(stream, null!));
    }

    [Fact]
    public void Search_CaseInsensitiveOption_ReturnsMatch()
    {
        var docXml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                <w:body>
                    <w:p><w:r><w:t>Mixed Case Match</w:t></w:r></w:p>
                </w:body>
            </w:document>
            """;

        var bytes = CreateDocxPackage(docXml);
        using var stream = new MemoryStream(bytes);
        var matcher = new LogicalTextMatcher();

        var options = new DocxSearchOptions { Comparison = StringComparison.OrdinalIgnoreCase };
        var matches = matcher.Search(stream, "mixed case", options);

        Assert.Single(matches);
        Assert.Equal("Mixed Case", matches[0].MatchedText);
    }

    private class FailingPreflightStub : IDocxPackagePreflight
    {
        public DocxPreflightResult ValidatePackage(string filePath, DocxPreflightOptions? options = null)
        {
            return DocxPreflightResult.Reject(DocxPreflightErrorCode.PartSizeExceeded, "Test part size exceeded.");
        }

        public DocxPreflightResult ValidatePackage(Stream stream, DocxPreflightOptions? options = null)
        {
            return DocxPreflightResult.Reject(DocxPreflightErrorCode.PartSizeExceeded, "Test part size exceeded.");
        }
    }

    [Fact]
    public void Search_WithPreflightRejection_ThrowsInvalidOperationException()
    {
        var docXml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                <w:body><w:p><w:r><w:t>Content</w:t></w:r></w:p></w:body>
            </w:document>
            """;

        var bytes = CreateDocxPackage(docXml);
        using var stream = new MemoryStream(bytes);
        var matcher = new LogicalTextMatcher(new FailingPreflightStub());

        var ex = Assert.Throws<InvalidOperationException>(() => matcher.Search(stream, "Content"));
        Assert.Contains("Package preflight validation failed", ex.Message);
    }
}
