using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using DocumentIDE.Core.Context;
using DocumentIDE.Core.Docx;
using Xunit;

namespace DocumentIDE.Core.Tests.Context;

public class DocxContextFormatterTests
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
                    "<Relationships xmlns=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">\r\n" +
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
    public void Format_DocxFixture_BecomesReadableContext()
    {
        // Step 1 & Required Test: DOCX fixture becomes readable context (Headings, Paragraphs, Origin labeling)
        var docXml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                <w:body>
                    <w:p>
                        <w:pPr><w:pStyle w:val="Heading1"/></w:pPr>
                        <w:r><w:t>Project Architecture</w:t></w:r>
                    </w:p>
                    <w:p>
                        <w:r><w:t>This document describes system components.</w:t></w:r>
                    </w:p>
                    <w:p>
                        <w:pPr><w:pStyle w:val="Heading2"/></w:pPr>
                        <w:r><w:t>Core Layer</w:t></w:r>
                    </w:p>
                    <w:p>
                        <w:r><w:t>The core layer handles business logic.</w:t></w:r>
                    </w:p>
                </w:body>
            </w:document>
            """;

        var headerXml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:hdr xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                <w:p><w:r><w:t>Internal Specification</w:t></w:r></w:p>
            </w:hdr>
            """;

        var footerXml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:ftr xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                <w:p><w:r><w:t>Page 1 Confidential</w:t></w:r></w:p>
            </w:ftr>
            """;

        var bytes = CreateDocxPackage(docXml, headerXml, footerXml);
        using var stream = new MemoryStream(bytes);
        var formatter = new DocxContextFormatter();

        var result = formatter.Format(stream);

        Assert.Contains("[Header]", result);
        Assert.Contains("Internal Specification", result);
        Assert.Contains("# Project Architecture", result);
        Assert.Contains("This document describes system components.", result);
        Assert.Contains("## Core Layer", result);
        Assert.Contains("The core layer handles business logic.", result);
        Assert.Contains("[Footer]", result);
        Assert.Contains("Page 1 Confidential", result);
    }

    [Fact]
    public void Format_TableContent_PreservedMeaningfully()
    {
        // Step 1 & Required Test: Table content preserved meaningfully as Markdown table
        var docXml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                <w:body>
                    <w:tbl>
                        <w:tr>
                            <w:tc><w:p><w:r><w:t>Header A</w:t></w:r></w:p></w:tc>
                            <w:tc><w:p><w:r><w:t>Header B</w:t></w:r></w:p></w:tc>
                        </w:tr>
                        <w:tr>
                            <w:tc><w:p><w:r><w:t>Value 1</w:t></w:r></w:p></w:tc>
                            <w:tc><w:p><w:r><w:t>Value 2</w:t></w:r></w:p></w:tc>
                        </w:tr>
                    </w:tbl>
                </w:body>
            </w:document>
            """;

        var bytes = CreateDocxPackage(docXml);
        using var stream = new MemoryStream(bytes);
        var formatter = new DocxContextFormatter();

        var result = formatter.Format(stream);

        Assert.Contains("| Header A | Header B |", result);
        Assert.Contains("| --- | --- |", result);
        Assert.Contains("| Value 1 | Value 2 |", result);
    }

    [Fact]
    public void Format_SourceFingerprint_Unchanged()
    {
        // Required Test: Source fingerprint unchanged
        var docXml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                <w:body>
                    <w:p><w:r><w:t>Fingerprint Test Content</w:t></w:r></w:p>
                </w:body>
            </w:document>
            """;

        var bytes = CreateDocxPackage(docXml);
        var tempFile = Path.Combine(Path.GetTempPath(), $"ctx03_fp_{Guid.NewGuid()}.docx");
        try
        {
            File.WriteAllBytes(tempFile, bytes);
            byte[] initialHash = SHA256.HashData(File.ReadAllBytes(tempFile));

            var formatter = new DocxContextFormatter();
            var formattedResult = formatter.Format(tempFile);

            Assert.False(string.IsNullOrWhiteSpace(formattedResult));

            byte[] finalHash = SHA256.HashData(File.ReadAllBytes(tempFile));
            Assert.Equal(initialHash, finalHash);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public void Format_ExtremelyLargeContent_IsBounded()
    {
        // Step 3 & Edge Case: Bound extremely large extracted content
        var sb = new StringBuilder();
        sb.Append("""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                <w:body>
            """);

        for (int i = 0; i < 100; i++)
        {
            sb.Append($"<w:p><w:r><w:t>Paragraph line {i:D3} with repetitive content to test truncation bounding.</w:t></w:r></w:p>");
        }

        sb.Append("""
                </w:body>
            </w:document>
            """);

        var bytes = CreateDocxPackage(sb.ToString());
        using var stream = new MemoryStream(bytes);
        var formatter = new DocxContextFormatter();
        var options = new DocxContextFormatterOptions { MaxCharacters = 150 };

        var result = formatter.Format(stream, options);

        Assert.Contains("[Content truncated", result);
        Assert.True(result.Length <= 250, $"Expected length <= 250, but was {result.Length}");
    }

    [Fact]
    public void Format_ExcludesFieldInstructions()
    {
        // Step 1: Field instructions like MERGEFORMAT are not part of output text
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
                        <w:r><w:t> of 10</w:t></w:r>
                    </w:p>
                </w:body>
            </w:document>
            """;

        var bytes = CreateDocxPackage(docXml);
        using var stream = new MemoryStream(bytes);
        var formatter = new DocxContextFormatter();

        var result = formatter.Format(stream);

        Assert.DoesNotContain("MERGEFORMAT", result);
        Assert.Contains("Page 1 of 10", result);
    }

    [Fact]
    public void Format_ExcludeHeadersAndFootersOption_ExcludesHeaderFooter()
    {
        var docXml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                <w:body>
                    <w:p><w:r><w:t>Body text</w:t></w:r></w:p>
                </w:body>
            </w:document>
            """;

        var headerXml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:hdr xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                <w:p><w:r><w:t>Header text</w:t></w:r></w:p>
            </w:hdr>
            """;

        var bytes = CreateDocxPackage(docXml, headerXml: headerXml);
        using var stream = new MemoryStream(bytes);
        var formatter = new DocxContextFormatter();

        var result = formatter.Format(stream, new DocxContextFormatterOptions { IncludeHeadersAndFooters = false });

        Assert.DoesNotContain("Header text", result);
        Assert.Contains("Body text", result);
    }

    private class RejectPreflightStub : IDocxPackagePreflight
    {
        public DocxPreflightResult ValidatePackage(string filePath, DocxPreflightOptions? options = null)
        {
            return DocxPreflightResult.Reject(DocxPreflightErrorCode.PartSizeExceeded, "Preflight rejected package");
        }

        public DocxPreflightResult ValidatePackage(Stream stream, DocxPreflightOptions? options = null)
        {
            return DocxPreflightResult.Reject(DocxPreflightErrorCode.PartSizeExceeded, "Preflight rejected package");
        }
    }

    [Fact]
    public void Format_PreflightRejection_ThrowsInvalidOperationException()
    {
        var docXml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                <w:body>
                    <w:p><w:r><w:t>Body</w:t></w:r></w:p>
                </w:body>
            </w:document>
            """;

        var bytes = CreateDocxPackage(docXml);
        using var stream = new MemoryStream(bytes);
        var formatter = new DocxContextFormatter(new RejectPreflightStub());

        var ex = Assert.Throws<InvalidOperationException>(() => formatter.Format(stream));
        Assert.Contains("Package preflight validation failed", ex.Message);
    }
}
