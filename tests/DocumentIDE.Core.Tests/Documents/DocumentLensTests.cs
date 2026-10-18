using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using DocumentIDE.Core.Documents;
using DocumentIDE.Core.Indexing;
using DocumentIDE.Infrastructure.Docx;
using Xunit;

namespace DocumentIDE.Core.Tests.Documents;

/// <summary>
/// DOCX-04 — Document Lens Metadata.
/// These tests exercise the read-only lens pipeline: preflight → IDX-02
/// DocxStructureParser → DocumentLensService. All derived values must come back
/// labeled as summaries, and failures must be typed and inspectable.
/// </summary>
public class DocumentLensTests : IDisposable
{
    private readonly string _tempDir;
    private readonly DocumentLensService _service;

    public DocumentLensTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"docide_lens_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _service = new DocumentLensService(new DocxStructureParser(), new DocxPackagePreflight());
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }

    private string WriteFixture(string name, byte[] bytes)
    {
        string path = Path.Combine(_tempDir, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Fact]
    public void Mixed_sections_show_mixed_summaries()
    {
        string path = WriteFixture("mixed.docx", LensFixtures.BuildMixedSections());

        DocumentLensResult result = _service.Inspect(path);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(2, result.SectionCount);

        Assert.Equal("Mixed (A4, Letter)", result.PaperSummary);
        Assert.Equal("Mixed (Landscape, Portrait)", result.OrientationSummary);
        Assert.StartsWith("Mixed (", result.MarginSummary);
        Assert.Contains("2.54 / 3.18 / 2.54 / 3.18 cm", result.MarginSummary);
        Assert.Contains("1.27 / 1.27 / 1.27 / 1.27 cm", result.MarginSummary);

        // The summary must be stable across repeated inspections.
        DocumentLensResult second = _service.Inspect(path);
        Assert.Equal(result.PaperSummary, second.PaperSummary);
        Assert.Equal(result.OrientationSummary, second.OrientationSummary);
        Assert.Equal(result.MarginSummary, second.MarginSummary);
    }

    [Fact]
    public void Inspect_ComputesCountsSummariesHeadingsAndPresence()
    {
        string path = WriteFixture("full.docx", LensFixtures.BuildFullFixture());

        DocumentLensResult result = _service.Inspect(path);

        Assert.True(result.Success, result.ErrorMessage);

        // Counts ("where reliable").
        Assert.Equal(14, result.PageCount);
        Assert.Equal(1, result.SectionCount);
        Assert.Equal(1, result.TableCount);
        Assert.Equal(2, result.ImageCount);

        // Summaries (derived values named and labeled as summaries).
        Assert.Equal("A4", result.PaperSummary);
        Assert.Equal("Portrait", result.OrientationSummary);
        Assert.Equal("2.54 / 3.18 / 2.54 / 3.18 cm", result.MarginSummary);
        Assert.Equal("Times New Roman", result.PrimaryFontSummary);
        Assert.Equal("12 pt", result.PrimaryFontSizeSummary);

        // Headings + header/footer presence.
        Assert.Equal(2, result.Headings.Count);
        Assert.Equal(new DocumentLensHeading(1, "Pendahuluan"), result.Headings[0]);
        Assert.Equal(new DocumentLensHeading(2, "Kondisi Site"), result.Headings[1]);
        Assert.True(result.HeaderPresent);
        Assert.True(result.FooterPresent);
    }

    [Fact]
    public void Inspect_PrimaryFontAndSizeSummariesAreDeterministic()
    {
        string path = WriteFixture("tie.docx", LensFixtures.BuildFontTie());

        DocumentLensResult first = _service.Inspect(path);
        DocumentLensResult second = _service.Inspect(path);

        Assert.True(first.Success, first.ErrorMessage);
        // Equal usage counts fall back to the ordinal-smallest font name and
        // the smaller size, so the summary never depends on hash ordering.
        Assert.Equal("Arial", first.PrimaryFontSummary);
        Assert.Equal("10 pt", first.PrimaryFontSizeSummary);
        Assert.Equal(first.PrimaryFontSummary, second.PrimaryFontSummary);
        Assert.Equal(first.PrimaryFontSizeSummary, second.PrimaryFontSizeSummary);
    }

    [Fact]
    public void Inspect_DoesNotModifySourceFile()
    {
        string path = WriteFixture("readonly.docx", LensFixtures.BuildFullFixture());
        byte[] before = File.ReadAllBytes(path);
        DateTime beforeWrite = File.GetLastWriteTimeUtc(path);

        DocumentLensResult result = _service.Inspect(path);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Equal(beforeWrite, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void Inspect_MissingFile_ReturnsTypedFailure()
    {
        string path = Path.Combine(_tempDir, "missing.docx");

        DocumentLensResult result = _service.Inspect(path);

        Assert.False(result.Success);
        Assert.Equal(DocumentLensErrorCode.FileNotFound, result.ErrorCode);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public void Inspect_InvalidPackage_ReturnsTypedFailure()
    {
        string path = WriteFixture("broken.docx", new byte[] { 1, 2, 3, 4, 5 });

        DocumentLensResult result = _service.Inspect(path);

        Assert.False(result.Success);
        Assert.Equal(DocumentLensErrorCode.PackageValidationFailed, result.ErrorCode);
        Assert.Contains("InvalidZipArchive", result.ErrorMessage);
    }

    [Fact]
    public void Inspect_MalformedXmlWithoutPreflight_ReturnsTypedUnexpectedStructure()
    {
        var service = new DocumentLensService(new DocxStructureParser());
        string path = WriteFixture("malformed.docx", LensFixtures.BuildMalformedXml());

        DocumentLensResult result = service.Inspect(path);

        Assert.False(result.Success);
        Assert.Equal(DocumentLensErrorCode.UnexpectedStructure, result.ErrorCode);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public void Inspect_MalformedHeadingMetadata_ReturnsTypedFailure()
    {
        var service = new DocumentLensService(new StubDocxParser(new DocxParseResult(
            new DocumentIDE.Core.Domain.DocumentMetadata { HeadingsJson = "{not valid json" },
            SearchableBody: null)));
        string path = WriteFixture("stub.docx", LensFixtures.BuildFullFixture());

        DocumentLensResult result = service.Inspect(path);

        Assert.False(result.Success);
        Assert.Equal(DocumentLensErrorCode.MetadataUnreadable, result.ErrorCode);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public void Inspect_UnusualButWellFormedOoxml_ReturnsDeterministicEmptyResult()
    {
        string path = WriteFixture("unusual.docx", LensFixtures.BuildDocumentWithoutBody());

        DocumentLensResult result = _service.Inspect(path);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(0, result.SectionCount);
        Assert.Equal(0, result.TableCount);
        Assert.Equal(0, result.ImageCount);
        Assert.Null(result.PaperSummary);
        Assert.Null(result.OrientationSummary);
        Assert.Null(result.MarginSummary);
        Assert.Null(result.PrimaryFontSummary);
        Assert.Null(result.PrimaryFontSizeSummary);
        Assert.Empty(result.Headings);
        Assert.False(result.HeaderPresent);
        Assert.False(result.FooterPresent);
    }
}

/// <summary>
/// Minimal but structurally valid DOCX fixtures used by the lens tests.
/// </summary>
internal static class LensFixtures
{
    private const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    public static byte[] BuildFullFixture() => BuildPackage(
        documentXml: FullDocumentXml,
        extraEntries:
        [
            ("word/styles.xml", StylesXml),
            ("word/header1.xml", HeaderXml),
            ("word/footer1.xml", FooterXml),
            ("word/_rels/document.xml.rels", DocumentRelsXml),
            ("docProps/app.xml", AppXml),
        ]);

    public static byte[] BuildMixedSections() => BuildPackage(documentXml: MixedSectionsDocumentXml);

    public static byte[] BuildFontTie() => BuildPackage(documentXml: FontTieDocumentXml);

    public static byte[] BuildMalformedXml() => BuildPackage(documentXml: "<w:document xmlns:w=\"" + W + "\"><w:body><w:p>");

    public static byte[] BuildDocumentWithoutBody() => BuildPackage(documentXml: EmptyDocumentXml);

    private const string FullDocumentXml = $"""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <w:document xmlns:w="{W}" xmlns:r="{R}">
          <w:body>
            <w:p>
              <w:pPr><w:pStyle w:val="Heading1"/></w:pPr>
              <w:r><w:t>Pendahuluan</w:t></w:r>
            </w:p>
            <w:p>
              <w:pPr><w:pStyle w:val="Heading2"/></w:pPr>
              <w:r><w:t>Kondisi Site</w:t></w:r>
            </w:p>
            <w:p>
              <w:pPr><w:pStyle w:val="BodyText"/></w:pPr>
              <w:r><w:t>Paragraf dengan gaya BodyText.</w:t></w:r>
            </w:p>
            <w:p>
              <w:r><w:rPr><w:rFonts w:ascii="Times New Roman" w:hAnsi="Times New Roman"/><w:sz w:val="24"/></w:rPr><w:t>Times satu</w:t></w:r>
              <w:r><w:rPr><w:rFonts w:ascii="Times New Roman" w:hAnsi="Times New Roman"/><w:sz w:val="24"/></w:rPr><w:t>Times dua</w:t></w:r>
              <w:r><w:rPr><w:rFonts w:ascii="Times New Roman" w:hAnsi="Times New Roman"/><w:sz w:val="24"/></w:rPr><w:t>Times tiga</w:t></w:r>
              <w:r><w:rPr><w:rFonts w:ascii="Arial" w:hAnsi="Arial"/><w:sz w:val="28"/></w:rPr><w:t>Arial</w:t></w:r>
            </w:p>
            <w:tbl>
              <w:tr><w:tc><w:p><w:r><w:t>Sel</w:t></w:r></w:p></w:tc></w:tr>
            </w:tbl>
            <w:p><w:r><w:drawing><wp:inline xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing"><wp:extent cx="914400" cy="914400"/></wp:inline></w:drawing></w:r></w:p>
            <w:p><w:r><w:drawing><wp:inline xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing"><wp:extent cx="914400" cy="914400"/></wp:inline></w:drawing></w:r></w:p>
            <w:sectPr>
              <w:headerReference w:type="default" r:id="rIdHeader1"/>
              <w:footerReference w:type="default" r:id="rIdFooter1"/>
              <w:pgSz w:w="11906" w:h="16838"/>
              <w:pgMar w:top="1440" w:right="1800" w:bottom="1440" w:left="1800"/>
            </w:sectPr>
          </w:body>
        </w:document>
        """;

    private const string MixedSectionsDocumentXml = $"""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <w:document xmlns:w="{W}">
          <w:body>
            <w:p><w:r><w:t>Bagian satu.</w:t></w:r></w:p>
            <w:p>
              <w:pPr>
                <w:sectPr>
                  <w:pgSz w:w="11906" w:h="16838"/>
                  <w:pgMar w:top="1440" w:right="1800" w:bottom="1440" w:left="1800"/>
                </w:sectPr>
              </w:pPr>
            </w:p>
            <w:p><w:r><w:t>Bagian dua.</w:t></w:r></w:p>
            <w:sectPr>
              <w:pgSz w:w="15840" w:h="12240" w:orient="landscape"/>
              <w:pgMar w:top="720" w:right="720" w:bottom="720" w:left="720"/>
            </w:sectPr>
          </w:body>
        </w:document>
        """;

    private const string FontTieDocumentXml = $"""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <w:document xmlns:w="{W}">
          <w:body>
            <w:p>
              <w:r><w:rPr><w:rFonts w:ascii="Zulu" w:hAnsi="Zulu"/><w:sz w:val="24"/></w:rPr><w:t>Zulu satu</w:t></w:r>
              <w:r><w:rPr><w:rFonts w:ascii="Arial" w:hAnsi="Arial"/><w:sz w:val="20"/></w:rPr><w:t>Arial satu</w:t></w:r>
            </w:p>
            <w:sectPr>
              <w:pgSz w:w="11906" w:h="16838"/>
              <w:pgMar w:top="1440" w:right="1440" w:bottom="1440" w:left="1440"/>
            </w:sectPr>
          </w:body>
        </w:document>
        """;

    private const string EmptyDocumentXml = $"""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <w:document xmlns:w="{W}"/>
        """;

    private const string StylesXml = $"""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <w:styles xmlns:w="{W}">
          <w:docDefaults>
            <w:rPrDefault>
              <w:rPr><w:rFonts w:ascii="Calibri" w:hAnsi="Calibri"/><w:sz w:val="22"/></w:rPr>
            </w:rPrDefault>
          </w:docDefaults>
          <w:style w:type="paragraph" w:styleId="BodyText">
            <w:name w:val="Body Text"/>
            <w:rPr><w:rFonts w:ascii="Cambria" w:hAnsi="Cambria"/><w:sz w:val="24"/></w:rPr>
          </w:style>
          <w:style w:type="paragraph" w:styleId="Heading1">
            <w:name w:val="heading 1"/>
            <w:pPr><w:outlineLvl w:val="0"/></w:pPr>
            <w:rPr><w:rFonts w:ascii="Calibri Light" w:hAnsi="Calibri Light"/><w:sz w:val="32"/></w:rPr>
          </w:style>
          <w:style w:type="paragraph" w:styleId="Heading2">
            <w:name w:val="heading 2"/>
            <w:pPr><w:outlineLvl w:val="1"/></w:pPr>
            <w:rPr><w:rFonts w:ascii="Calibri Light" w:hAnsi="Calibri Light"/><w:sz w:val="28"/></w:rPr>
          </w:style>
        </w:styles>
        """;

    private const string HeaderXml = $"""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <w:hdr xmlns:w="{W}">
          <w:p><w:r><w:t>Header Laporan</w:t></w:r></w:p>
        </w:hdr>
        """;

    private const string FooterXml = $"""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <w:ftr xmlns:w="{W}">
          <w:p><w:r><w:t>Footer Laporan</w:t></w:r></w:p>
        </w:ftr>
        """;

    private const string DocumentRelsXml = $"""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rIdStyles" Type="{R}/styles" Target="styles.xml"/>
          <Relationship Id="rIdHeader1" Type="{R}/header" Target="header1.xml"/>
          <Relationship Id="rIdFooter1" Type="{R}/footer" Target="footer1.xml"/>
        </Relationships>
        """;

    private const string AppXml = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/extended-properties">
          <Pages>14</Pages>
        </Properties>
        """;

    private const string ContentTypesXml = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
          <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
          <Default Extension="xml" ContentType="application/xml"/>
          <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
          <Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>
          <Override PartName="/word/header1.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml"/>
          <Override PartName="/word/footer1.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.footer+xml"/>
          <Override PartName="/docProps/app.xml" ContentType="application/vnd.openxmlformats-officedocument.extended-properties+xml"/>
        </Types>
        """;

    private const string RootRelsXml = $"""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="{R}/officeDocument" Target="word/document.xml"/>
          <Relationship Id="rId2" Type="{R}/extended-properties" Target="docProps/app.xml"/>
        </Relationships>
        """;

    private static byte[] BuildPackage(
        string documentXml,
        (string Name, string Content)[]? extraEntries = null)
    {
        using var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "[Content_Types].xml", ContentTypesXml);
            WriteEntry(archive, "_rels/.rels", RootRelsXml);
            WriteEntry(archive, "word/document.xml", documentXml);

            foreach (var (name, content) in extraEntries ?? Array.Empty<(string, string)>())
            {
                WriteEntry(archive, name, content);
            }
        }

        return memoryStream.ToArray();
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Fastest);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }
}

/// <summary>
/// Test double that returns a prepared parse result, used to exercise the
/// service's handling of unreadable derived metadata.
/// </summary>
internal sealed class StubDocxParser : IDocxStructureParser
{
    private readonly DocxParseResult _result;

    public StubDocxParser(DocxParseResult result)
    {
        _result = result;
    }

    public DocxParseResult Parse(string filePath) => _result;
}
