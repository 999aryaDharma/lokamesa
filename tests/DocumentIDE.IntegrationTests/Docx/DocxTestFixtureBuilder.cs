using System.IO;
using System.IO.Compression;
using System.Text;

namespace DocumentIDE.IntegrationTests.Docx;

public static class DocxTestFixtureBuilder
{
    public const string DefaultContentTypesXml = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
            <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
            <Default Extension="xml" ContentType="application/xml"/>
            <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
        </Types>
        """;

    public const string DefaultRelsXml = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
            <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
        </Relationships>
        """;

    public const string DefaultDocumentXml = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
            <w:body>
                <w:p>
                    <w:r>
                        <w:t>Hello Document IDE</w:t>
                    </w:r>
                </w:p>
            </w:body>
        </w:document>
        """;

    public static byte[] CreateValidDocxPackage(
        string? contentTypesXml = null,
        string? relsXml = null,
        string? documentXml = null,
        (string name, byte[] content)[]? extraEntries = null)
    {
        using var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            // [Content_Types].xml
            var ctEntry = archive.CreateEntry("[Content_Types].xml", CompressionLevel.Fastest);
            using (var writer = new StreamWriter(ctEntry.Open(), Encoding.UTF8))
            {
                writer.Write(contentTypesXml ?? DefaultContentTypesXml);
            }

            // _rels/.rels
            var relsEntry = archive.CreateEntry("_rels/.rels", CompressionLevel.Fastest);
            using (var writer = new StreamWriter(relsEntry.Open(), Encoding.UTF8))
            {
                writer.Write(relsXml ?? DefaultRelsXml);
            }

            // word/document.xml
            var docEntry = archive.CreateEntry("word/document.xml", CompressionLevel.Fastest);
            using (var writer = new StreamWriter(docEntry.Open(), Encoding.UTF8))
            {
                writer.Write(documentXml ?? DefaultDocumentXml);
            }

            if (extraEntries != null)
            {
                foreach (var (name, content) in extraEntries)
                {
                    var entry = archive.CreateEntry(name, CompressionLevel.Fastest);
                    using var stream = entry.Open();
                    stream.Write(content, 0, content.Length);
                }
            }
        }

        return memoryStream.ToArray();
    }

    public static byte[] CreateEntryBombPackage(int entryCount)
    {
        using var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            for (int i = 0; i < entryCount; i++)
            {
                var entry = archive.CreateEntry($"entry_{i}.txt", CompressionLevel.NoCompression);
                using var writer = new StreamWriter(entry.Open());
                writer.Write("a");
            }
        }
        return memoryStream.ToArray();
    }

    public static byte[] CreateOversizedPartPackage(int partSizeBytes)
    {
        using var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var ctEntry = archive.CreateEntry("[Content_Types].xml");
            using (var writer = new StreamWriter(ctEntry.Open()))
                writer.Write(DefaultContentTypesXml);

            var relsEntry = archive.CreateEntry("_rels/.rels");
            using (var writer = new StreamWriter(relsEntry.Open()))
                writer.Write(DefaultRelsXml);

            var largeEntry = archive.CreateEntry("word/document.xml", CompressionLevel.NoCompression);
            using (var stream = largeEntry.Open())
            {
                byte[] buffer = new byte[partSizeBytes];
                stream.Write(buffer, 0, buffer.Length);
            }
        }
        return memoryStream.ToArray();
    }

    public static byte[] CreateZipSlipPackage(string zipSlipPath)
    {
        using var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var ctEntry = archive.CreateEntry("[Content_Types].xml");
            using (var writer = new StreamWriter(ctEntry.Open()))
                writer.Write(DefaultContentTypesXml);

            var relsEntry = archive.CreateEntry("_rels/.rels");
            using (var writer = new StreamWriter(relsEntry.Open()))
                writer.Write(DefaultRelsXml);

            var slipEntry = archive.CreateEntry(zipSlipPath);
            using (var writer = new StreamWriter(slipEntry.Open()))
                writer.Write("malicious content");
        }
        return memoryStream.ToArray();
    }

    public static byte[] CreateDtdDocxPackage()
    {
        string dtdDocumentXml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE foo [ <!ENTITY xxe SYSTEM "file:///etc/passwd"> ]>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                <w:body>
                    <w:p><w:r><w:t>&xxe;</w:t></w:r></w:p>
                </w:body>
            </w:document>
            """;

        return CreateValidDocxPackage(documentXml: dtdDocumentXml);
    }

    public static byte[] CreateMissingContentTypesPackage()
    {
        using var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var relsEntry = archive.CreateEntry("_rels/.rels");
            using (var writer = new StreamWriter(relsEntry.Open()))
                writer.Write(DefaultRelsXml);

            var docEntry = archive.CreateEntry("word/document.xml");
            using (var writer = new StreamWriter(docEntry.Open()))
                writer.Write(DefaultDocumentXml);
        }
        return memoryStream.ToArray();
    }

    public static byte[] CreateMissingRootRelsPackage()
    {
        using var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var ctEntry = archive.CreateEntry("[Content_Types].xml");
            using (var writer = new StreamWriter(ctEntry.Open()))
                writer.Write(DefaultContentTypesXml);

            var docEntry = archive.CreateEntry("word/document.xml");
            using (var writer = new StreamWriter(docEntry.Open()))
                writer.Write(DefaultDocumentXml);
        }
        return memoryStream.ToArray();
    }

    public static byte[] CreateMissingMainDocumentPackage()
    {
        using var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var ctEntry = archive.CreateEntry("[Content_Types].xml");
            using (var writer = new StreamWriter(ctEntry.Open()))
                writer.Write(DefaultContentTypesXml);

            var relsEntry = archive.CreateEntry("_rels/.rels");
            using (var writer = new StreamWriter(relsEntry.Open()))
                writer.Write(DefaultRelsXml);

            // Notice word/document.xml is omitted!
        }
        return memoryStream.ToArray();
    }
}
