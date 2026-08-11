using System;
using System.IO;
using System.Text;
using DocumentIDE.Core.Docx;
using DocumentIDE.Infrastructure.Docx;
using Xunit;

namespace DocumentIDE.IntegrationTests.Docx;

public class DocxResourceBudgetTests : IDisposable
{
    private readonly string _tempDirPath;
    private readonly DocxPackagePreflight _preflight;

    public DocxResourceBudgetTests()
    {
        _tempDirPath = Path.Combine(Path.GetTempPath(), $"idx01_tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirPath);
        _preflight = new DocxPackagePreflight();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirPath))
        {
            try { Directory.Delete(_tempDirPath, recursive: true); } catch { }
        }
    }

    private string SaveFixture(byte[] content, string fileName = "test.docx")
    {
        string filePath = Path.Combine(_tempDirPath, fileName);
        File.WriteAllBytes(filePath, content);
        return filePath;
    }

    [Fact]
    public void ValidatePackage_ValidDocx_ReturnsValidResult()
    {
        byte[] bytes = DocxTestFixtureBuilder.CreateValidDocxPackage();
        string path = SaveFixture(bytes, "valid.docx");

        var result = _preflight.ValidatePackage(path);

        Assert.True(result.IsValid);
        Assert.Equal(DocxPreflightStatus.Valid, result.Status);
        Assert.Equal(DocxPreflightErrorCode.None, result.ErrorCode);
        Assert.True(result.Metrics.EntryCount >= 3);
        Assert.True(result.Metrics.TotalUncompressedSizeBytes > 0);
        Assert.NotEmpty(result.Relationships);
    }

    [Fact]
    public void ValidatePackage_EntryCountBomb_CustomOptions_Rejected()
    {
        byte[] bytes = DocxTestFixtureBuilder.CreateEntryBombPackage(10);
        string path = SaveFixture(bytes, "bomb_custom.docx");

        var options = new DocxPreflightOptions { MaxEntryCount = 5 };
        var result = _preflight.ValidatePackage(path, options);

        Assert.False(result.IsValid);
        Assert.Equal(DocxPreflightStatus.SkippedForSafety, result.Status);
        Assert.Equal(DocxPreflightErrorCode.TooManyEntries, result.ErrorCode);
    }

    [Fact]
    public void ValidatePackage_EntryCountBomb_DefaultOptions_Rejected()
    {
        byte[] bytes = DocxTestFixtureBuilder.CreateEntryBombPackage(10001);
        string path = SaveFixture(bytes, "bomb_default.docx");

        var result = _preflight.ValidatePackage(path);

        Assert.False(result.IsValid);
        Assert.Equal(DocxPreflightStatus.SkippedForSafety, result.Status);
        Assert.Equal(DocxPreflightErrorCode.TooManyEntries, result.ErrorCode);
    }

    [Fact]
    public void ValidatePackage_OversizedSinglePart_Rejected()
    {
        byte[] bytes = DocxTestFixtureBuilder.CreateOversizedPartPackage(2048);
        string path = SaveFixture(bytes, "oversized_part.docx");

        var options = new DocxPreflightOptions { MaxPartUncompressedSizeBytes = 1024 };
        var result = _preflight.ValidatePackage(path, options);

        Assert.False(result.IsValid);
        Assert.Equal(DocxPreflightStatus.SkippedForSafety, result.Status);
        Assert.Equal(DocxPreflightErrorCode.PartSizeExceeded, result.ErrorCode);
    }

    [Fact]
    public void ValidatePackage_TotalUncompressedSizeExceeded_Rejected()
    {
        byte[] bytes = DocxTestFixtureBuilder.CreateValidDocxPackage();
        string path = SaveFixture(bytes, "total_size_exceeded.docx");

        var options = new DocxPreflightOptions { MaxTotalUncompressedSizeBytes = 100 };
        var result = _preflight.ValidatePackage(path, options);

        Assert.False(result.IsValid);
        Assert.Equal(DocxPreflightStatus.SkippedForSafety, result.Status);
        Assert.Equal(DocxPreflightErrorCode.TotalUncompressedSizeExceeded, result.ErrorCode);
    }

    [Fact]
    public void ValidatePackage_CompressedFileTooLarge_Rejected()
    {
        byte[] bytes = DocxTestFixtureBuilder.CreateValidDocxPackage();
        string path = SaveFixture(bytes, "file_too_large.docx");

        var options = new DocxPreflightOptions { MaxCompressedSizeBytes = 50 };
        var result = _preflight.ValidatePackage(path, options);

        Assert.False(result.IsValid);
        Assert.Equal(DocxPreflightStatus.SkippedForSafety, result.Status);
        Assert.Equal(DocxPreflightErrorCode.FileTooLarge, result.ErrorCode);
    }

    [Fact]
    public void ValidatePackage_ZipSlipAttempt_Rejected()
    {
        byte[] bytes = DocxTestFixtureBuilder.CreateZipSlipPackage("../evil.txt");
        string path = SaveFixture(bytes, "zip_slip.docx");

        var result = _preflight.ValidatePackage(path);

        Assert.False(result.IsValid);
        Assert.Equal(DocxPreflightStatus.SkippedForSafety, result.Status);
        Assert.Equal(DocxPreflightErrorCode.ZipSlipAttempt, result.ErrorCode);
    }

    [Fact]
    public void ValidatePackage_DtdInXml_Rejected()
    {
        byte[] bytes = DocxTestFixtureBuilder.CreateDtdDocxPackage();
        string path = SaveFixture(bytes, "dtd_xxe.docx");

        var result = _preflight.ValidatePackage(path);

        Assert.False(result.IsValid);
        Assert.Equal(DocxPreflightStatus.SkippedForSafety, result.Status);
        Assert.Equal(DocxPreflightErrorCode.DtdProhibited, result.ErrorCode);
    }

    [Fact]
    public void ValidatePackage_MissingContentTypes_Rejected()
    {
        byte[] bytes = DocxTestFixtureBuilder.CreateMissingContentTypesPackage();
        string path = SaveFixture(bytes, "missing_ct.docx");

        var result = _preflight.ValidatePackage(path);

        Assert.False(result.IsValid);
        Assert.Equal(DocxPreflightStatus.SkippedForSafety, result.Status);
        Assert.Equal(DocxPreflightErrorCode.MissingContentTypes, result.ErrorCode);
    }

    [Fact]
    public void ValidatePackage_MissingRootRels_Rejected()
    {
        byte[] bytes = DocxTestFixtureBuilder.CreateMissingRootRelsPackage();
        string path = SaveFixture(bytes, "missing_rels.docx");

        var result = _preflight.ValidatePackage(path);

        Assert.False(result.IsValid);
        Assert.Equal(DocxPreflightStatus.SkippedForSafety, result.Status);
        Assert.Equal(DocxPreflightErrorCode.MissingRootRels, result.ErrorCode);
    }

    [Fact]
    public void ValidatePackage_MissingMainDocument_Rejected()
    {
        byte[] bytes = DocxTestFixtureBuilder.CreateMissingMainDocumentPackage();
        string path = SaveFixture(bytes, "missing_doc.docx");

        var result = _preflight.ValidatePackage(path);

        Assert.False(result.IsValid);
        Assert.Equal(DocxPreflightStatus.SkippedForSafety, result.Status);
        Assert.Equal(DocxPreflightErrorCode.MissingMainDocument, result.ErrorCode);
    }

    [Fact]
    public void ValidatePackage_CorruptedZipArchive_ReturnsCorruptedStatus()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("Not a zip file");
        string path = SaveFixture(bytes, "corrupted.docx");

        var result = _preflight.ValidatePackage(path);

        Assert.False(result.IsValid);
        Assert.Equal(DocxPreflightStatus.Corrupted, result.Status);
        Assert.Equal(DocxPreflightErrorCode.InvalidZipArchive, result.ErrorCode);
    }

    [Fact]
    public void ValidatePackage_MalformedXml_ReturnsCorruptedStatus()
    {
        string badXml = "<?xml version=\"1.0\"?><Types><UnclosedTag></Types>";
        byte[] bytes = DocxTestFixtureBuilder.CreateValidDocxPackage(contentTypesXml: badXml);
        string path = SaveFixture(bytes, "bad_xml.docx");

        var result = _preflight.ValidatePackage(path);

        Assert.False(result.IsValid);
        Assert.Equal(DocxPreflightStatus.Corrupted, result.Status);
        Assert.Equal(DocxPreflightErrorCode.XmlMalformed, result.ErrorCode);
    }

    [Fact]
    public void ValidatePackage_StrictOpenXmlNamespace_RecognizedAsValid()
    {
        string strictRelsXml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                <Relationship Id="rId1" Type="http://purl.oclc.org/ooxml/officeDocument/relationships/officeDocument" Target="word/document.xml"/>
            </Relationships>
            """;
        byte[] bytes = DocxTestFixtureBuilder.CreateValidDocxPackage(relsXml: strictRelsXml);
        string path = SaveFixture(bytes, "strict_ooxml.docx");

        var result = _preflight.ValidatePackage(path);

        Assert.True(result.IsValid);
        Assert.Equal(DocxPreflightStatus.Valid, result.Status);
    }

    [Fact]
    public void ValidatePackage_MissingNonCriticalMedia_ProducesWarningButValid()
    {
        string docRelsXml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="media/missing_image.png"/>
            </Relationships>
            """;
        var extraEntries = new (string, byte[])[]
        {
            ("word/_rels/document.xml.rels", Encoding.UTF8.GetBytes(docRelsXml))
        };
        byte[] bytes = DocxTestFixtureBuilder.CreateValidDocxPackage(extraEntries: extraEntries);
        string path = SaveFixture(bytes, "missing_media.docx");

        var result = _preflight.ValidatePackage(path);

        Assert.True(result.IsValid);
        Assert.Equal(DocxPreflightStatus.Valid, result.Status);
        Assert.Single(result.Warnings);
        Assert.Contains("missing_image.png", result.Warnings[0]);
    }

    [Fact]
    public void ValidatePackage_FileNotFound_ReturnsSkippedForSafety()
    {
        string nonExistentPath = Path.Combine(_tempDirPath, "does_not_exist.docx");

        var result = _preflight.ValidatePackage(nonExistentPath);

        Assert.False(result.IsValid);
        Assert.Equal(DocxPreflightStatus.SkippedForSafety, result.Status);
        Assert.Equal(DocxPreflightErrorCode.FileNotFound, result.ErrorCode);
    }
}
