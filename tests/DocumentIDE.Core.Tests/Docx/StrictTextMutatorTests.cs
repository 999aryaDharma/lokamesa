using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using DocumentIDE.Core.Docx;
using DocumentIDE.Infrastructure.Docx;
using Xunit;

namespace DocumentIDE.Core.Tests.Docx;

public class StrictTextMutatorTests : IDisposable
{
    private readonly string _tempDirectory;
    private static readonly XNamespace WNs = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    public StrictTextMutatorTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"DocxMutatorTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            try { Directory.Delete(_tempDirectory, recursive: true); } catch { }
        }
    }

    [Fact]
    public void SplitRunReplacement_ProducesCorrectVisibleText()
    {
        // Given: a DOCX with split runs: <w:r><w:t>NUSA-</w:t></w:r> <w:r><w:t>DUA-1</w:t></w:r>
        string documentXml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                <w:body>
                    <w:p>
                        <w:r>
                            <w:rPr><w:b/></w:rPr>
                            <w:t>NUSA-</w:t>
                        </w:r>
                        <w:r>
                            <w:rPr><w:i/></w:rPr>
                            <w:t>DUA-1</w:t>
                        </w:r>
                    </w:p>
                </w:body>
            </w:document>
            """;

        string filePath = CreateTestDocx(documentXml);
        var mutator = new StrictTextMutator(new DocxPackagePreflight());

        // When replacing logical string "NUSA-DUA-1" with "BALI-RESORT-2"
        var result = mutator.MutateText(filePath, "NUSA-DUA-1", "BALI-RESORT-2");

        // Then:
        Assert.True(result.Success);
        Assert.Equal(1, result.ReplacementsApplied);

        // Re-read file and search using LogicalTextMatcher
        var matcher = new LogicalTextMatcher(new DocxPackagePreflight());
        var matches = matcher.Search(filePath, "BALI-RESORT-2");
        Assert.Single(matches);
        Assert.Equal("BALI-RESORT-2", matches[0].MatchedText);
        Assert.Empty(matcher.Search(filePath, "NUSA-DUA-1"));
    }

    [Fact]
    public void UnrelatedParagraphFormatting_Unchanged()
    {
        // Given: a DOCX with 2 paragraphs, one with w:pPr alignment & spacing
        string documentXml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                <w:body>
                    <w:p>
                        <w:pPr>
                            <w:jc w:val="center"/>
                            <w:spacing w:before="120" w:after="240"/>
                        </w:pPr>
                        <w:r>
                            <w:t>Target phrase here</w:t>
                        </w:r>
                    </w:p>
                    <w:p>
                        <w:pPr>
                            <w:jc w:val="right"/>
                        </w:pPr>
                        <w:r>
                            <w:t>Unrelated paragraph</w:t>
                        </w:r>
                    </w:p>
                </w:body>
            </w:document>
            """;

        string filePath = CreateTestDocx(documentXml);
        var mutator = new StrictTextMutator(new DocxPackagePreflight());

        // When mutating text in first paragraph
        var result = mutator.MutateText(filePath, "Target phrase", "New phrase");
        Assert.True(result.Success);

        // Then: inspect XML of resulting document
        using var archive = ZipFile.OpenRead(filePath);
        var entry = archive.GetEntry("word/document.xml")!;
        using var stream = entry.Open();
        var xdoc = XDocument.Load(stream);

        var paragraphs = xdoc.Descendants(WNs + "p").ToList();
        Assert.Equal(2, paragraphs.Count);

        // Para 1 pPr check
        var p1Pr = paragraphs[0].Element(WNs + "pPr");
        Assert.NotNull(p1Pr);
        Assert.Equal("center", p1Pr.Element(WNs + "jc")?.Attribute(WNs + "val")?.Value);
        Assert.Equal("120", p1Pr.Element(WNs + "spacing")?.Attribute(WNs + "before")?.Value);
        Assert.Equal("240", p1Pr.Element(WNs + "spacing")?.Attribute(WNs + "after")?.Value);

        // Para 2 pPr check
        var p2Pr = paragraphs[1].Element(WNs + "pPr");
        Assert.NotNull(p2Pr);
        Assert.Equal("right", p2Pr.Element(WNs + "jc")?.Attribute(WNs + "val")?.Value);
    }

    [Fact]
    public void InvalidResult_NeverReplacesOriginal()
    {
        // Given: a corrupted package or preflight failure condition
        string documentXml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                <w:body>
                    <w:p><w:r><w:t>Original Content</w:t></w:r></w:p>
                </w:body>
            </w:document>
            """;

        string filePath = CreateTestDocx(documentXml);
        byte[] originalBytes = File.ReadAllBytes(filePath);

        var mutator = new StrictTextMutator(new DocxPackagePreflight());

        // Attempting to mutate non-existent file throws ArgumentException
        Assert.Throws<ArgumentException>(() => { mutator.MutateText("C:\\invalid\\nonexistent_file.docx", "foo", "bar"); });

        // File remains unchanged
        Assert.Equal(originalBytes, File.ReadAllBytes(filePath));
    }

    [Fact]
    public void AnchorFormatting_PreservedOnFirstRun()
    {
        string documentXml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                <w:body>
                    <w:p>
                        <w:r>
                            <w:rPr>
                                <w:rFonts w:ascii="Arial"/>
                                <w:sz w:val="28"/>
                                <w:b/>
                            </w:rPr>
                            <w:t>Hello </w:t>
                        </w:r>
                        <w:r>
                            <w:rPr><w:i/></w:rPr>
                            <w:t>World</w:t>
                        </w:r>
                    </w:p>
                </w:body>
            </w:document>
            """;

        string filePath = CreateTestDocx(documentXml);
        var mutator = new StrictTextMutator(new DocxPackagePreflight());

        var result = mutator.MutateText(filePath, "Hello World", "Welcome Everyone");
        Assert.True(result.Success);

        using var archive = ZipFile.OpenRead(filePath);
        var entry = archive.GetEntry("word/document.xml")!;
        using var stream = entry.Open();
        var xdoc = XDocument.Load(stream);

        var run = xdoc.Descendants(WNs + "r").FirstOrDefault();
        Assert.NotNull(run);
        var rPr = run.Element(WNs + "rPr");
        Assert.NotNull(rPr);
        Assert.Equal("Arial", rPr.Element(WNs + "rFonts")?.Attribute(WNs + "ascii")?.Value);
        Assert.Equal("28", rPr.Element(WNs + "sz")?.Attribute(WNs + "val")?.Value);
        Assert.NotNull(rPr.Element(WNs + "b"));
        Assert.Equal("Welcome Everyone", run.Element(WNs + "t")?.Value);
    }

    [Fact]
    public void RunContainingNonTextElement_NotRemovedWhenTextEmptied()
    {
        string documentXml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                <w:body>
                    <w:p>
                        <w:r>
                            <w:t>Target phrase </w:t>
                        </w:r>
                        <w:r>
                            <w:t>to remove</w:t>
                            <w:br/>
                        </w:r>
                    </w:p>
                </w:body>
            </w:document>
            """;

        string filePath = CreateTestDocx(documentXml);
        var mutator = new StrictTextMutator(new DocxPackagePreflight());

        var result = mutator.MutateText(filePath, "Target phrase to remove", "Replaced text");
        Assert.True(result.Success);

        using var archive = ZipFile.OpenRead(filePath);
        var entry = archive.GetEntry("word/document.xml")!;
        using var stream = entry.Open();
        var xdoc = XDocument.Load(stream);

        var runs = xdoc.Descendants(WNs + "r").ToList();
        Assert.Equal(2, runs.Count);
        Assert.NotNull(runs[1].Element(WNs + "br"));
    }

    [Fact]
    public void MultipleMatchesInSameParagraph_ReplacedCorrectly()
    {
        string documentXml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                <w:body>
                    <w:p>
                        <w:r><w:t>apple banana apple orange apple</w:t></w:r>
                    </w:p>
                </w:body>
            </w:document>
            """;

        string filePath = CreateTestDocx(documentXml);
        var mutator = new StrictTextMutator(new DocxPackagePreflight());

        var result = mutator.MutateText(filePath, "apple", "grape");
        Assert.True(result.Success);
        Assert.Equal(3, result.ReplacementsApplied);

        var matcher = new LogicalTextMatcher(new DocxPackagePreflight());
        var matches = matcher.Search(filePath, "grape");
        Assert.Equal(3, matches.Count);
        Assert.Empty(matcher.Search(filePath, "apple"));
    }

    [Fact]
    public void HeaderAndFooterText_MutatedCorrectly()
    {
        string documentXml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                <w:body>
                    <w:p><w:r><w:t>Body content</w:t></w:r></w:p>
                </w:body>
            </w:document>
            """;

        string headerXml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:hdr xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                <w:p><w:r><w:t>Header Old Company</w:t></w:r></w:p>
            </w:hdr>
            """;

        string filePath = CreateTestDocx(documentXml, extraParts: new[] { ("word/header1.xml", headerXml) });
        var mutator = new StrictTextMutator(new DocxPackagePreflight());

        var result = mutator.MutateText(filePath, "Old Company", "New Corporation");
        Assert.True(result.Success);

        var matcher = new LogicalTextMatcher(new DocxPackagePreflight());
        var matches = matcher.Search(filePath, "New Corporation");
        Assert.Single(matches);
        Assert.Equal(DocxStructureOrigin.Header, matches[0].Origin);
    }

    [Fact]
    public void RecoverySnapshotCreated_BeforeAtomicReplace()
    {
        string documentXml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                <w:body>
                    <w:p><w:r><w:t>Original text</w:t></w:r></w:p>
                </w:body>
            </w:document>
            """;

        string filePath = CreateTestDocx(documentXml);
        string recoveryDir = Path.Combine(_tempDirectory, "recovery");

        var mutator = new StrictTextMutator(new DocxPackagePreflight());
        var options = new StrictMutationOptions
        {
            CreateRecoverySnapshot = true,
            RecoveryDirectory = recoveryDir
        };

        var result = mutator.MutateText(filePath, "Original text", "New text", options);
        Assert.True(result.Success);
        Assert.NotNull(result.RecoverySnapshotPath);
        Assert.True(File.Exists(result.RecoverySnapshotPath));

        var matcher = new LogicalTextMatcher(new DocxPackagePreflight());
        var recoveryMatches = matcher.Search(result.RecoverySnapshotPath, "Original text");
        Assert.Single(recoveryMatches);
    }

    private string CreateTestDocx(string documentXml, (string name, string content)[]? extraParts = null)
    {
        string filePath = Path.Combine(_tempDirectory, $"test_{Guid.NewGuid():N}.docx");
        using (var fs = File.Create(filePath))
        using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            var ct = archive.CreateEntry("[Content_Types].xml");
            using (var w = new StreamWriter(ct.Open()))
            {
                w.Write("""
                    <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                    <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                        <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                        <Default Extension="xml" ContentType="application/xml"/>
                        <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                    </Types>
                    """);
            }

            var rels = archive.CreateEntry("_rels/.rels");
            using (var w = new StreamWriter(rels.Open()))
            {
                w.Write("""
                    <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                    <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                        <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
                    </Relationships>
                    """);
            }

            var doc = archive.CreateEntry("word/document.xml");
            using (var w = new StreamWriter(doc.Open()))
            {
                w.Write(documentXml);
            }

            if (extraParts != null)
            {
                foreach (var (name, content) in extraParts)
                {
                    var extraEntry = archive.CreateEntry(name);
                    using var w = new StreamWriter(extraEntry.Open());
                    w.Write(content);
                }
            }
        }
        return filePath;
    }
}
