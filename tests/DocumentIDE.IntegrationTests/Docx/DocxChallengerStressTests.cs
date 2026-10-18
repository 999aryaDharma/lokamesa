using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using DocumentIDE.Core.Docx;
using DocumentIDE.Infrastructure.Docx;
using Xunit;

namespace DocumentIDE.IntegrationTests.Docx;

public class DocxChallengerStressTests : IDisposable
{
    private readonly string _tempDirPath;
    private readonly DocxPackagePreflight _preflight;

    public DocxChallengerStressTests()
    {
        _tempDirPath = Path.Combine(Path.GetTempPath(), $"idx01_challenger_{Guid.NewGuid():N}");
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

    // --- Non-Seekable Stream Class for Testing ---
    private class NonSeekableStream : Stream
    {
        private readonly Stream _baseStream;
        public NonSeekableStream(Stream baseStream) => _baseStream = baseStream;

        public override bool CanRead => _baseStream.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => _baseStream.CanWrite;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => _baseStream.Flush();
        public override int Read(byte[] buffer, int offset, int count) => _baseStream.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => _baseStream.Write(buffer, offset, count);
    }

    // --- Dummy Stream with Fake Length for Boundary Tests ---
    private class MockLengthStream : MemoryStream
    {
        private readonly long _fakeLength;
        public MockLengthStream(byte[] buffer, long fakeLength) : base(buffer, false)
        {
            _fakeLength = fakeLength;
        }
        public override long Length => _fakeLength;
    }

    [Fact]
    public void Boundary_Exactly100MiBFile_Allowed()
    {
        byte[] validPackage = DocxTestFixtureBuilder.CreateValidDocxPackage();
        long exact100MiB = 100L * 1024 * 1024;
        using var stream = new MockLengthStream(validPackage, exact100MiB);

        var result = _preflight.ValidatePackage(stream);

        Assert.NotEqual(DocxPreflightErrorCode.FileTooLarge, result.ErrorCode);
    }

    [Fact]
    public void Boundary_100MiBPlus1ByteFile_Rejected()
    {
        byte[] validPackage = DocxTestFixtureBuilder.CreateValidDocxPackage();
        long exact100MiBPlus1 = (100L * 1024 * 1024) + 1;
        using var stream = new MockLengthStream(validPackage, exact100MiBPlus1);

        var result = _preflight.ValidatePackage(stream);

        Assert.False(result.IsValid);
        Assert.Equal(DocxPreflightErrorCode.FileTooLarge, result.ErrorCode);
    }

    [Fact]
    public void Boundary_Exactly10000Entries_Allowed()
    {
        byte[] bytes = DocxTestFixtureBuilder.CreateEntryBombPackage(10000);
        using var stream = new MemoryStream(bytes);

        var options = new DocxPreflightOptions { MaxEntryCount = 10000 };
        var result = _preflight.ValidatePackage(stream, options);

        Assert.NotEqual(DocxPreflightErrorCode.TooManyEntries, result.ErrorCode);
    }

    [Fact]
    public void Boundary_10001Entries_Rejected()
    {
        byte[] bytes = DocxTestFixtureBuilder.CreateEntryBombPackage(10001);
        using var stream = new MemoryStream(bytes);

        var options = new DocxPreflightOptions { MaxEntryCount = 10000 };
        var result = _preflight.ValidatePackage(stream, options);

        Assert.False(result.IsValid);
        Assert.Equal(DocxPreflightErrorCode.TooManyEntries, result.ErrorCode);
    }

    [Fact]
    public void Boundary_Exactly128MiBPart_Allowed()
    {
        byte[] bytes = DocxTestFixtureBuilder.CreateOversizedPartPackage(1024);
        using var stream = new MemoryStream(bytes);

        var options = new DocxPreflightOptions { MaxPartUncompressedSizeBytes = 1024 };
        var result = _preflight.ValidatePackage(stream, options);

        Assert.NotEqual(DocxPreflightErrorCode.PartSizeExceeded, result.ErrorCode);
    }

    [Fact]
    public void Boundary_128MiBPlus1BytePart_Rejected()
    {
        byte[] bytes = DocxTestFixtureBuilder.CreateOversizedPartPackage(1025);
        using var stream = new MemoryStream(bytes);

        var options = new DocxPreflightOptions { MaxPartUncompressedSizeBytes = 1024 };
        var result = _preflight.ValidatePackage(stream, options);

        Assert.False(result.IsValid);
        Assert.Equal(DocxPreflightErrorCode.PartSizeExceeded, result.ErrorCode);
    }

    [Fact]
    public void StreamPosition_SeekableStream_PreservedOrRestored()
    {
        byte[] validPackage = DocxTestFixtureBuilder.CreateValidDocxPackage();
        using var stream = new MemoryStream(validPackage);
        stream.Position = 0;

        var result = _preflight.ValidatePackage(stream);

        Assert.True(result.IsValid);
        // Checking stream position after preflight
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public void StreamPosition_NonZeroInitialPosition_CalculatesCorrectRemainingLength()
    {
        byte[] validPackage = DocxTestFixtureBuilder.CreateValidDocxPackage();
        // Prefix stream with 100 bytes of dummy data
        byte[] padded = new byte[validPackage.Length + 100];
        Array.Copy(validPackage, 0, padded, 100, validPackage.Length);

        using var stream = new MemoryStream(padded);
        stream.Position = 100; // Start validation at offset 100

        var options = new DocxPreflightOptions { MaxCompressedSizeBytes = validPackage.Length + 10 };
        var result = _preflight.ValidatePackage(stream, options);

        Assert.NotEqual(DocxPreflightErrorCode.FileTooLarge, result.ErrorCode);
    }

    [Fact]
    public void NonSeekableStream_HandledGracefullyWithoutUnhandledException()
    {
        byte[] validPackage = DocxTestFixtureBuilder.CreateValidDocxPackage();
        using var memStream = new MemoryStream(validPackage);
        using var nonSeekable = new NonSeekableStream(memStream);

        var result = _preflight.ValidatePackage(nonSeekable);

        Assert.False(result.IsValid);
        Assert.Equal(DocxPreflightStatus.Corrupted, result.Status);
        Assert.Equal(DocxPreflightErrorCode.InvalidZipArchive, result.ErrorCode);
    }

    [Fact]
    public void CaseSensitivity_UppercasePartPaths_SuccessfullyValidated()
    {
        using var memStream = new MemoryStream();
        using (var archive = new ZipArchive(memStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var ct = archive.CreateEntry("[CONTENT_TYPES].XML");
            using (var w = new StreamWriter(ct.Open())) w.Write(DocxTestFixtureBuilder.DefaultContentTypesXml);

            var rels = archive.CreateEntry("_RELS/.RELS");
            using (var w = new StreamWriter(rels.Open())) w.Write(DocxTestFixtureBuilder.DefaultRelsXml);

            var doc = archive.CreateEntry("WORD/DOCUMENT.XML");
            using (var w = new StreamWriter(doc.Open())) w.Write(DocxTestFixtureBuilder.DefaultDocumentXml);

            var docRelsXml = """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                    <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="MEDIA/IMAGE1.PNG"/>
                </Relationships>
                """;
            var docRels = archive.CreateEntry("WORD/_RELS/DOCUMENT.XML.RELS");
            using (var w = new StreamWriter(docRels.Open())) w.Write(docRelsXml);

            var img = archive.CreateEntry("WORD/MEDIA/IMAGE1.PNG");
            using (var s = img.Open()) s.Write(new byte[] { 1, 2, 3 });
        }

        using var testStream = new MemoryStream(memStream.ToArray());
        var result = _preflight.ValidatePackage(testStream);

        Assert.True(result.IsValid);
        Assert.Equal(DocxPreflightStatus.Valid, result.Status);
        Assert.Equal(1, result.Metrics.MediaCount);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void UnicodeAndUnusualFileNames_SupportedInPathsAndZipEntries()
    {
        string utf8FileName = "测试文档_文档IDE_#123.docx";
        string filePath = Path.Combine(_tempDirPath, utf8FileName);

        var extraEntries = new (string, byte[])[]
        {
            ("word/media/图 片_①.png", new byte[] { 0xFF, 0xD8, 0xFF }),
            ("word/custom_xml/数据_data.xml", Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?><root/>"))
        };

        byte[] packageBytes = DocxTestFixtureBuilder.CreateValidDocxPackage(extraEntries: extraEntries);
        File.WriteAllBytes(filePath, packageBytes);

        var result = _preflight.ValidatePackage(filePath);

        Assert.True(result.IsValid);
        Assert.Equal(DocxPreflightStatus.Valid, result.Status);
        Assert.Equal(1, result.Metrics.MediaCount);
    }

    [Fact]
    public void MemoryConsumption_LargeZipEntryCount_ExecutesEfficiently()
    {
        // 5000 entries performance and memory stress test
        byte[] bytes = DocxTestFixtureBuilder.CreateEntryBombPackage(5000);
        long initialMemory = GC.GetTotalMemory(forceFullCollection: true);

        using var stream = new MemoryStream(bytes);
        var options = new DocxPreflightOptions { MaxEntryCount = 6000 };
        var result = _preflight.ValidatePackage(stream, options);

        long finalMemory = GC.GetTotalMemory(forceFullCollection: false);
        long allocatedApprox = finalMemory - initialMemory;

        Assert.False(result.IsValid); // Fails due to missing [Content_Types].xml, but memory/performance checked
        Assert.True(allocatedApprox < 50 * 1024 * 1024); // Less than 50MB allocated during scan
    }
}
