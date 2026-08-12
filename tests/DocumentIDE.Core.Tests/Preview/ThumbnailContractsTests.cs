using System;
using DocumentIDE.Core.Preview;
using Xunit;

namespace DocumentIDE.Core.Tests.Preview;

public class ThumbnailContractsTests
{
    [Fact]
    public void ThumbnailMetadata_CreationAndProperties_Valid()
    {
        var metadata = new ThumbnailMetadata(
            DocumentId: "doc-123",
            ContentHash: "abc123hash",
            Status: ThumbnailStatus.Ready,
            Width: 200,
            Height: 280,
            Format: ThumbnailFormat.Png,
            DataUrl: "data:image/png;base64,iVBORw0KGgo...",
            GeneratedAt: DateTimeOffset.UtcNow,
            ErrorMessage: null
        );

        Assert.Equal("doc-123", metadata.DocumentId);
        Assert.Equal(ThumbnailStatus.Ready, metadata.Status);
        Assert.Equal(ThumbnailFormat.Png, metadata.Format);
        Assert.Equal(200, metadata.Width);
        Assert.Equal(280, metadata.Height);
        Assert.Null(metadata.ErrorMessage);
    }

    [Fact]
    public void ThumbnailRequest_Properties_Valid()
    {
        var request = new ThumbnailRequest(
            DocumentId: "doc-456",
            FilePath: @"C:\docs\report.docx",
            DocumentType: "docx",
            ForceRefresh: true
        );

        Assert.Equal("doc-456", request.DocumentId);
        Assert.Equal(@"C:\docs\report.docx", request.FilePath);
        Assert.Equal("docx", request.DocumentType);
        Assert.True(request.ForceRefresh);
    }
}
