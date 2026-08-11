using DocumentIDE.Core.Docx;
using Xunit;

namespace DocumentIDE.Core.Tests.Docx;

public class DocxPreflightUnitTests
{
    [Fact]
    public void DocxPreflightOptions_DefaultValues_MatchSpecificationLimits()
    {
        var options = new DocxPreflightOptions();

        Assert.Equal(100L * 1024 * 1024, options.MaxCompressedSizeBytes);
        Assert.Equal(10000, options.MaxEntryCount);
        Assert.Equal(512L * 1024 * 1024, options.MaxTotalUncompressedSizeBytes);
        Assert.Equal(128L * 1024 * 1024, options.MaxPartUncompressedSizeBytes);
    }

    [Fact]
    public void DocxPreflightResult_SuccessFactory_SetsPropertiesCorrectly()
    {
        var metrics = new DocxPackageMetrics(
            CompressedSizeBytes: 1024,
            EntryCount: 5,
            TotalUncompressedSizeBytes: 4096,
            PartCount: 4,
            MediaCount: 1
        );
        var rels = new[]
        {
            new DocxRelationshipInfo("rId1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument", "word/document.xml")
        };
        var warnings = new[] { "Missing media item" };

        var result = DocxPreflightResult.Success(metrics, rels, warnings);

        Assert.True(result.IsValid);
        Assert.Equal(DocxPreflightStatus.Valid, result.Status);
        Assert.Equal(DocxPreflightErrorCode.None, result.ErrorCode);
        Assert.Null(result.ErrorMessage);
        Assert.Equal(metrics, result.Metrics);
        Assert.Single(result.Relationships);
        Assert.Single(result.Warnings);
    }

    [Fact]
    public void DocxPreflightResult_RejectFactory_SetsSkippedForSafety()
    {
        var result = DocxPreflightResult.Reject(
            DocxPreflightErrorCode.TooManyEntries,
            "ZIP entry count exceeds limit of 10000 entries."
        );

        Assert.False(result.IsValid);
        Assert.Equal(DocxPreflightStatus.SkippedForSafety, result.Status);
        Assert.Equal(DocxPreflightErrorCode.TooManyEntries, result.ErrorCode);
        Assert.Equal("ZIP entry count exceeds limit of 10000 entries.", result.ErrorMessage);
    }

    [Fact]
    public void DocxPreflightResult_CorruptedFactory_SetsCorruptedStatus()
    {
        var result = DocxPreflightResult.Corrupted(
            DocxPreflightErrorCode.InvalidZipArchive,
            "Invalid or truncated ZIP central directory."
        );

        Assert.False(result.IsValid);
        Assert.Equal(DocxPreflightStatus.Corrupted, result.Status);
        Assert.Equal(DocxPreflightErrorCode.InvalidZipArchive, result.ErrorCode);
        Assert.Equal("Invalid or truncated ZIP central directory.", result.ErrorMessage);
    }
}
