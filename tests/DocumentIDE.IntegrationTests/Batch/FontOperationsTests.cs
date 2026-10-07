using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using DocumentIDE.Core.Batch;
using DocumentIDE.Core.Batch.Operations;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.History;
using DocumentIDE.Infrastructure.Docx;
using DocumentIDE.Infrastructure.Filesystem;
using DocumentIDE.Infrastructure.Persistence;
using DocumentIDE.Infrastructure.Persistence.Repositories;
using DocumentIDE.IntegrationTests.Docx;

namespace DocumentIDE.IntegrationTests.Batch;

/// <summary>
/// BAT-08 — Strict Font &amp; Font-Size Operations tests.
/// Covers the sprint's implementation steps and the three required tests:
/// font change leaves size/margins unchanged, size change leaves family/margins
/// unchanged, and mixed-formatting preview identifies the affected text.
/// </summary>
public sealed class FontOperationsTests : IDisposable
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private readonly string _root;
    private readonly AppDb _db;
    private readonly BatchPlanRepository _plans;
    private readonly DocumentRepository _documents;
    private readonly FakeHistoryRecorder _recorder;
    private readonly FakeReindexTrigger _reindex;
    private readonly BatchApplyEngine _engine;
    private readonly SetFontOperation _setFont;
    private readonly SetFontSizeOperation _setFontSize;

    public FontOperationsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "font-ops-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _db = new AppDb(Path.Combine(_root, "state.db"));
        _db.InitializeAsync().GetAwaiter().GetResult();
        _plans = new BatchPlanRepository(_db);
        _documents = new DocumentRepository(_db);
        new WorkspaceRepository(_db).CreateAsync(new Workspace { Id = "ws", Name = "font-ops" }).GetAwaiter().GetResult();
        _recorder = new FakeHistoryRecorder();
        _reindex = new FakeReindexTrigger();
        _engine = new BatchApplyEngine(
            _plans, _documents,
            new RecoverySnapshotService(new FakeHistoryStorage(_root)),
            new WordEditStateProbe(), new AtomicFileReplacer(), _recorder, _reindex);
        _setFont = new SetFontOperation(new StrictFontMutator());
        _setFontSize = new SetFontSizeOperation(new StrictFontMutator());
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_root, true);
    }

    [Fact]
    public async Task Apply_FontChangeLeavesSizeAndMarginsUnchanged()
    {
        // Sprint required test #1: a reviewed font-family change must not alter the
        // run's size, paragraph spacing, section margins, or any other package part.
        var package = DocxTestFixtureBuilder.CreateValidDocxPackage(
            documentXml: FontFixtureDocument,
            extraEntries: new[] { ("word/styles.xml", Encoding.UTF8.GetBytes(StylesXml)) });
        var (planId, path) = await SeedAsync("doc", "font-change.docx", package);

        var parameters = new SetFontParameters { FontFamily = "Times New Roman" };
        var occurrences = await _setFont.BuildOccurrencesAsync(path, parameters);
        var occurrence = Assert.Single(occurrences);
        Assert.Equal("Arial", occurrence.CurrentFontFamily);
        Assert.Equal("Times New Roman", occurrence.NewFontFamily);
        await SelectAsync(planId, "doc", new[] { occurrence.Id });

        var mutate = _setFont.CreateMutate(parameters, await CreateTargetsAsync(planId));
        var result = await _engine.ApplyAsync(planId, mutate);

        var success = Assert.Single(result.Files);
        Assert.Equal(BatchApplyFileOutcome.Success, success.Outcome);
        Assert.Null(success.ReasonCode);

        // Family changed; every other reviewed property is byte-for-byte intact.
        var doc = LoadPart(path, "word/document.xml");
        var rFonts = doc.Descendants(W + "rFonts").Single();
        Assert.Equal("Times New Roman", (string?)rFonts.Attribute(W + "ascii"));
        Assert.Equal("Times New Roman", (string?)rFonts.Attribute(W + "hAnsi"));
        Assert.Equal("22", (string?)doc.Descendants(W + "sz").Single().Attribute(W + "val"));
        Assert.Equal("240", (string?)doc.Descendants(W + "spacing").Single().Attribute(W + "after"));
        Assert.Equal("Alpha body", doc.Descendants(W + "t").Single().Value);
        var pgMar = doc.Descendants(W + "pgMar").Single();
        Assert.Equal("1440", (string?)pgMar.Attribute(W + "top"));
        Assert.Equal("1800", (string?)pgMar.Attribute(W + "right"));
        Assert.Equal("1440", (string?)pgMar.Attribute(W + "bottom"));
        Assert.Equal("1800", (string?)pgMar.Attribute(W + "left"));
        var pgSz = doc.Descendants(W + "pgSz").Single();
        Assert.Equal("12240", (string?)pgSz.Attribute(W + "w"));
        Assert.Equal("15840", (string?)pgSz.Attribute(W + "h"));

        // Acceptance "no normalize-to-template": only the targeted part changed;
        // styles.xml (document defaults) and every other entry are byte-identical.
        AssertOnlyPartChanged(package, path, "word/document.xml");
        AssertFormattingInvariantsUnchanged(package, path);
        Assert.Equal(
            Encoding.UTF8.GetBytes(StylesXml),
            ReadEntryBytes(path, "word/styles.xml"));
    }

    [Fact]
    public async Task Apply_FontSizeChangeLeavesFamilyAndMarginsUnchanged()
    {
        // Sprint required test #2: a reviewed font-size change must not alter the
        // run's family, paragraph spacing, section margins, or any other part.
        var package = DocxTestFixtureBuilder.CreateValidDocxPackage(
            documentXml: FontFixtureDocument,
            extraEntries: new[] { ("word/styles.xml", Encoding.UTF8.GetBytes(StylesXml)) });
        var (planId, path) = await SeedAsync("doc", "size-change.docx", package);

        var parameters = new SetFontSizeParameters { SizeHalfPoints = 28 };
        var occurrences = await _setFontSize.BuildOccurrencesAsync(path, parameters);
        var occurrence = Assert.Single(occurrences);
        Assert.Equal(22, occurrence.CurrentSizeHalfPoints);
        Assert.Equal(28, occurrence.NewSizeHalfPoints);
        await SelectAsync(planId, "doc", new[] { occurrence.Id });

        var mutate = _setFontSize.CreateMutate(parameters, await CreateTargetsAsync(planId));
        var result = await _engine.ApplyAsync(planId, mutate);

        var success = Assert.Single(result.Files);
        Assert.Equal(BatchApplyFileOutcome.Success, success.Outcome);
        Assert.Null(success.ReasonCode);

        var doc = LoadPart(path, "word/document.xml");
        Assert.Equal("28", (string?)doc.Descendants(W + "sz").Single().Attribute(W + "val"));
        var rFonts = doc.Descendants(W + "rFonts").Single();
        Assert.Equal("Arial", (string?)rFonts.Attribute(W + "ascii"));
        Assert.Equal("Arial", (string?)rFonts.Attribute(W + "hAnsi"));
        Assert.Equal("240", (string?)doc.Descendants(W + "spacing").Single().Attribute(W + "after"));
        var pgMar = doc.Descendants(W + "pgMar").Single();
        Assert.Equal("1440", (string?)pgMar.Attribute(W + "top"));
        Assert.Equal("1800", (string?)pgMar.Attribute(W + "right"));
        Assert.Equal("1440", (string?)pgMar.Attribute(W + "bottom"));
        Assert.Equal("1800", (string?)pgMar.Attribute(W + "left"));

        AssertOnlyPartChanged(package, path, "word/document.xml");
        AssertFormattingInvariantsUnchanged(package, path);
        Assert.Equal(Encoding.UTF8.GetBytes(StylesXml), ReadEntryBytes(path, "word/styles.xml"));
    }

    [Fact]
    public async Task Build_MixedFormattingPreviewIdentifiesAffectedText()
    {
        // Sprint required test #3: inside a paragraph with mixed run formatting the
        // preview must identify exactly the affected runs (current→new formatting),
        // skip style-inherited and already-at-target text, support the optional
        // current-family match, and report body/table/header origins.
        var package = DocxTestFixtureBuilder.CreateValidDocxPackage(
            documentXml: MixedFormattingDocument,
            extraEntries: new[]
            {
                ("word/styles.xml", Encoding.UTF8.GetBytes(StylesXml)),
                ("word/header1.xml", Encoding.UTF8.GetBytes(HeaderXml))
            });
        var path = Path.Combine(_root, "mixed.docx");
        await File.WriteAllBytesAsync(path, package);

        // Family preview: affected = direct Arial/Calibri runs; "inherited text"
        // (no direct formatting) and "already target" (Times New Roman) are not.
        var fontOccurrences = await _setFont.BuildOccurrencesAsync(path, new SetFontParameters { FontFamily = "Times New Roman" });
        Assert.Equal(new[]
        {
            "font_word/document.xml_Body_p0_r0",
            "font_word/document.xml_Body_p0_r1",
            "font_word/document.xml_Table_p1_r0",
            "font_word/header1.xml_Header_p0_r0"
        }, fontOccurrences.Select(o => o.Id));
        Assert.Equal(new[] { "Body", "Body", "Table", "Header" }, fontOccurrences.Select(o => o.Origin));
        Assert.Equal(new[] { "Arial", "Calibri", "Arial", "Arial" }, fontOccurrences.Select(o => o.CurrentFontFamily));
        Assert.All(fontOccurrences, o => Assert.Equal("Times New Roman", o.NewFontFamily));
        Assert.Contains("Arial text", fontOccurrences[0].Context);
        Assert.Contains("Calibri text", fontOccurrences[1].Context);
        Assert.Contains("table Arial", fontOccurrences[2].Context);
        Assert.Contains("header Arial", fontOccurrences[3].Context);
        // The style-inherited run (r2) and the already-at-target run (r3) are not affected.
        Assert.DoesNotContain(fontOccurrences, o => o.Id.EndsWith("_r2", StringComparison.Ordinal) || o.Id.EndsWith("_r3", StringComparison.Ordinal));

        // Optional current-family match is case-insensitive and narrows the set.
        var arialOnly = await _setFont.BuildOccurrencesAsync(path, new SetFontParameters { FontFamily = "Times New Roman", TargetFontFamily = "arial" });
        Assert.Equal(new[] { "Arial", "Arial", "Arial" }, arialOnly.Select(o => o.CurrentFontFamily));
        Assert.DoesNotContain(arialOnly, o => o.CurrentFontFamily == "Calibri");

        var noMatch = await _setFont.BuildOccurrencesAsync(path, new SetFontParameters { FontFamily = "Times New Roman", TargetFontFamily = "MissingFont" });
        Assert.Empty(noMatch);

        // Size preview: direct sizes only, already-at-target size excluded.
        var sizeOccurrences = await _setFontSize.BuildOccurrencesAsync(path, new SetFontSizeParameters { SizeHalfPoints = 24 });
        Assert.Equal(new[]
        {
            "size_word/document.xml_Body_p0_r0",
            "size_word/document.xml_Body_p0_r1"
        }, sizeOccurrences.Select(o => o.Id));
        Assert.Equal(new[] { 22, 20 }, sizeOccurrences.Select(o => o.CurrentSizeHalfPoints));
        Assert.All(sizeOccurrences, o => Assert.Equal(24, o.NewSizeHalfPoints));
        Assert.Contains("Arial text", sizeOccurrences[0].Context);
        Assert.Contains("Calibri text", sizeOccurrences[1].Context);
        // Only direct sizes of the body paragraph change: the already-24pt run (r3)
        // and the size-less table/header runs are not affected.
        Assert.DoesNotContain(sizeOccurrences, o => o.Id.EndsWith("_r3", StringComparison.Ordinal));
        Assert.DoesNotContain(sizeOccurrences, o => o.Id.Contains("_Table_", StringComparison.Ordinal));

        // Preview never mutates the source.
        Assert.Equal(package, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task Mutator_FailsClosedOnMismatchedOrMissingTargetsAndLeavesFileUnchanged()
    {
        // The strict mutator must verify every reviewed target against the file it
        // is about to change and must not write anything when any target fails.
        var package = DocxTestFixtureBuilder.CreateValidDocxPackage(documentXml: FontFixtureDocument);
        var path = Path.Combine(_root, "fail-closed.docx");
        await File.WriteAllBytesAsync(path, package);
        var mutator = new StrictFontMutator();

        // Reviewed family says "Calibri"; the file says "Arial" → mismatch blocks.
        var mismatch = Assert.Throws<InvalidOperationException>(() => mutator.SetFontFamily(
            path,
            new[] { new DocxFontRunTarget("word/document.xml", 0, 0, "Calibri", null) },
            "Times New Roman"));
        Assert.Contains("FontTargetMismatch", mismatch.Message, StringComparison.Ordinal);
        Assert.Equal(package, await File.ReadAllBytesAsync(path));

        // Missing paragraph identity → typed not-found block.
        var missingParagraph = Assert.Throws<InvalidOperationException>(() => mutator.SetFontSize(
            path,
            new[] { new DocxFontRunTarget("word/document.xml", 9, 0, "Arial", 22) },
            24));
        Assert.Contains("FontTargetNotFound", missingParagraph.Message, StringComparison.Ordinal);
        Assert.Equal(package, await File.ReadAllBytesAsync(path));

        // Missing run identity inside a valid paragraph → typed not-found block.
        var missingRun = Assert.Throws<InvalidOperationException>(() => mutator.SetFontSize(
            path,
            new[] { new DocxFontRunTarget("word/document.xml", 0, 7, "Arial", 22) },
            24));
        Assert.Contains("FontTargetNotFound", missingRun.Message, StringComparison.Ordinal);
        Assert.Equal(package, await File.ReadAllBytesAsync(path));

        // A target that carries no expected value is rejected, never guessed.
        var invalid = Assert.Throws<InvalidOperationException>(() => mutator.SetFontFamily(
            path,
            new[] { new DocxFontRunTarget("word/document.xml", 0, 0, null, null) },
            "Times New Roman"));
        Assert.Contains("FontTargetInvalid", invalid.Message, StringComparison.Ordinal);
        Assert.Equal(package, await File.ReadAllBytesAsync(path));

        // Missing part identity → typed not-found block.
        var missingPart = Assert.Throws<InvalidOperationException>(() => mutator.SetFontFamily(
            path,
            new[] { new DocxFontRunTarget("word/header9.xml", 0, 0, "Arial", null) },
            "Times New Roman"));
        Assert.Contains("FontTargetNotFound", missingPart.Message, StringComparison.Ordinal);
        Assert.Equal(package, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task Parameters_RejectInvalidValuesBeforeAnyFileAccess()
    {
        // Validation must fire before any file access; "missing.docx" never exists.
        var missingPath = Path.Combine(_root, "missing.docx");

        foreach (var invalid in new[]
        {
            new SetFontParameters { FontFamily = "" },
            new SetFontParameters { FontFamily = null! },
            new SetFontParameters { FontFamily = "   " },
            new SetFontParameters { FontFamily = new string('F', DocxFontScan.MaxFontNameLength + 1) },
            new SetFontParameters { FontFamily = "Arial", TargetFontFamily = "   " },
            new SetFontParameters { FontFamily = "Arial", TargetFontFamily = new string('A', DocxFontScan.MaxFontNameLength + 1) }
        })
        {
            var ex = await Assert.ThrowsAsync<ArgumentException>(() => _setFont.BuildOccurrencesAsync(missingPath, invalid));
            Assert.Contains("InvalidParameters", ex.Message, StringComparison.Ordinal);
            Assert.Throws<ArgumentException>(() => _setFont.CreateMutate(invalid, Array.Empty<FontChangeTarget>()));
        }

        foreach (var invalid in new[] { 0, -2, DocxFontScan.MaxHalfPoints + 1 })
        {
            var parameters = new SetFontSizeParameters { SizeHalfPoints = invalid };
            var ex = await Assert.ThrowsAsync<ArgumentException>(() => _setFontSize.BuildOccurrencesAsync(missingPath, parameters));
            Assert.Contains("InvalidParameters", ex.Message, StringComparison.Ordinal);
            Assert.Throws<ArgumentException>(() => _setFontSize.CreateMutate(parameters, Array.Empty<FontChangeTarget>()));
        }
    }

    [Fact]
    public async Task Apply_ChangedFileAfterPreviewIsSkippedAsStale()
    {
        var package = DocxTestFixtureBuilder.CreateValidDocxPackage(documentXml: FontFixtureDocument);
        var (planId, path) = await SeedAsync("doc", "stale.docx", package);

        var parameters = new SetFontParameters { FontFamily = "Times New Roman" };
        var occurrences = await _setFont.BuildOccurrencesAsync(path, parameters);
        await SelectAsync(planId, "doc", occurrences.Select(o => o.Id).ToList());

        // The file changes after preview: the reviewed fingerprint no longer matches.
        var modified = DocxTestFixtureBuilder.CreateValidDocxPackage(documentXml:
            FontFixtureDocument.Replace("Alpha body", "Alpha body changed after preview"));
        await File.WriteAllBytesAsync(path, modified);

        var mutate = _setFont.CreateMutate(parameters, await CreateTargetsAsync(planId));
        var result = await _engine.ApplyAsync(planId, mutate);

        var skipped = Assert.Single(result.Files);
        Assert.Equal(BatchApplyFileOutcome.Skipped, skipped.Outcome);
        Assert.Equal("Stale", skipped.ReasonCode);
        Assert.Equal(modified, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task Apply_OneFileFailureYieldsTypedPerFileOutcomes()
    {
        var goodPackage = DocxTestFixtureBuilder.CreateValidDocxPackage(documentXml: FontFixtureDocument);
        // Not a DOCX package: its reviewed mutation fails at apply time while
        // the other file is valid (sprint edge case "one file fails").
        var badBytes = Encoding.UTF8.GetBytes("not a docx package");
        var (planId, goodPath) = await SeedAsync("aaa", "good.docx", goodPackage);
        var (_, badPath) = await SeedAsync("bbb", "bad.docx", badBytes, planId);

        var parameters = new SetFontParameters { FontFamily = "Times New Roman" };
        var goodOccurrences = await _setFont.BuildOccurrencesAsync(goodPath, parameters);
        await SelectAsync(planId, "aaa", goodOccurrences.Select(o => o.Id).ToList());
        // The reviewed plan believed this package contained the run; the mutation
        // failure must surface as a typed per-file outcome, not a silent skip.
        await SelectAsync(planId, "bbb", new[] { "font_word/document.xml_Body_p0_r0" });

        var mutate = _setFont.CreateMutate(parameters, await CreateTargetsAsync(planId));
        var result = await _engine.ApplyAsync(planId, mutate);

        Assert.Equal(BatchApplyResultStatus.Completed, result.Status);
        Assert.Equal(2, result.Files.Count);

        var good = result.Files.Single(f => f.DocumentId == "aaa");
        Assert.Equal(BatchApplyFileOutcome.Success, good.Outcome);
        var goodDoc = LoadPart(goodPath, "word/document.xml");
        Assert.Equal("Times New Roman", (string?)goodDoc.Descendants(W + "rFonts").Single().Attribute(W + "ascii"));

        var bad = result.Files.Single(f => f.DocumentId == "bbb");
        Assert.Equal(BatchApplyFileOutcome.Failure, bad.Outcome);
        Assert.Equal("MutationFailed", bad.ReasonCode);
        Assert.Equal(badBytes, await File.ReadAllBytesAsync(badPath));

        // Typed per-file outcomes are recorded for both the success and the failure.
        Assert.Equal(2, _recorder.Records.Count);
        Assert.Contains(_recorder.Records, r => r.DocumentId == "aaa" && r.Outcome == BatchApplyFileOutcome.Success);
        Assert.Contains(_recorder.Records, r => r.DocumentId == "bbb" && r.Outcome == BatchApplyFileOutcome.Failure && r.ReasonCode == "MutationFailed");
    }

    [Fact]
    public async Task Apply_DeselectedOccurrencesRemainUnchanged()
    {
        // Two direct Arial runs in one paragraph: only the reviewed, selected run
        // may change; the deselected sibling stays byte-for-byte Arial.
        var package = DocxTestFixtureBuilder.CreateValidDocxPackage(documentXml: """
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:body>
                <w:p>
                  <w:r><w:rPr><w:rFonts w:ascii="Arial" w:hAnsi="Arial"/></w:rPr><w:t>first Arial</w:t></w:r>
                  <w:r><w:rPr><w:rFonts w:ascii="Arial" w:hAnsi="Arial"/></w:rPr><w:t>second Arial</w:t></w:r>
                </w:p>
              </w:body>
            </w:document>
            """);
        var (planId, path) = await SeedAsync("doc", "deselect.docx", package);

        var parameters = new SetFontParameters { FontFamily = "Times New Roman" };
        var occurrences = await _setFont.BuildOccurrencesAsync(path, parameters);
        Assert.Equal(2, occurrences.Count);

        // Review: the first occurrence stays Arial, only the second is selected.
        await SelectAsync(planId, "doc", new[] { occurrences[1].Id });

        var mutate = _setFont.CreateMutate(parameters, await CreateTargetsAsync(planId));
        var result = await _engine.ApplyAsync(planId, mutate);

        var success = Assert.Single(result.Files);
        Assert.Equal(BatchApplyFileOutcome.Success, success.Outcome);

        var doc = LoadPart(path, "word/document.xml");
        var fonts = doc.Descendants(W + "rFonts").ToList();
        Assert.Equal("Arial", (string?)fonts[0].Attribute(W + "ascii"));
        Assert.Equal("Times New Roman", (string?)fonts[1].Attribute(W + "ascii"));
        Assert.Equal("first Arial", doc.Descendants(W + "t").First().Value);
        Assert.Equal("second Arial", doc.Descendants(W + "t").Last().Value);
    }

    [Fact]
    public async Task Build_NonDocxFilesHaveNoFontOccurrences()
    {
        // Font operations target DOCX run formatting only; other file types are
        // never touched and preview as unaffected (zero occurrences), not failed.
        var mdPath = Path.Combine(_root, "notes.md");
        await File.WriteAllBytesAsync(mdPath, Encoding.UTF8.GetBytes("# Notes\n\nSome text."));

        Assert.Empty(await _setFont.BuildOccurrencesAsync(mdPath, new SetFontParameters { FontFamily = "Times New Roman" }));
        Assert.Empty(await _setFontSize.BuildOccurrencesAsync(mdPath, new SetFontSizeParameters { SizeHalfPoints = 24 }));
        Assert.Equal("# Notes\n\nSome text.", await File.ReadAllTextAsync(mdPath));
    }

    private const string FontFixtureDocument = """
        <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
          <w:body>
            <w:p>
              <w:pPr><w:spacing w:after="240"/></w:pPr>
              <w:r><w:rPr><w:rFonts w:ascii="Arial" w:hAnsi="Arial"/><w:sz w:val="22"/></w:rPr><w:t>Alpha body</w:t></w:r>
            </w:p>
            <w:sectPr><w:pgMar w:top="1440" w:right="1800" w:bottom="1440" w:left="1800"/><w:pgSz w:w="12240" w:h="15840"/></w:sectPr>
          </w:body>
        </w:document>
        """;

    private const string StylesXml = """
        <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
          <w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii="Arial" w:hAnsi="Arial"/><w:sz w:val="22"/></w:rPr></w:rPrDefault></w:docDefaults>
        </w:styles>
        """;

    private const string MixedFormattingDocument = """
        <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
          <w:body>
            <w:p>
              <w:r><w:rPr><w:rFonts w:ascii="Arial" w:hAnsi="Arial"/><w:sz w:val="22"/></w:rPr><w:t>Arial text</w:t></w:r>
              <w:r><w:rPr><w:rFonts w:ascii="Calibri" w:hAnsi="Calibri"/><w:sz w:val="20"/></w:rPr><w:t>Calibri text</w:t></w:r>
              <w:r><w:t>inherited text</w:t></w:r>
              <w:r><w:rPr><w:rFonts w:ascii="Times New Roman" w:hAnsi="Times New Roman"/><w:sz w:val="24"/></w:rPr><w:t>already target</w:t></w:r>
            </w:p>
            <w:tbl><w:tr><w:tc><w:p><w:r><w:rPr><w:rFonts w:ascii="Arial" w:hAnsi="Arial"/></w:rPr><w:t>table Arial</w:t></w:r></w:p></w:tc></w:tr></w:tbl>
          </w:body>
        </w:document>
        """;

    private const string HeaderXml = """
        <w:hdr xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
          <w:p><w:r><w:rPr><w:rFonts w:ascii="Arial" w:hAnsi="Arial"/></w:rPr><w:t>header Arial</w:t></w:r></w:p>
        </w:hdr>
        """;

    private static XDocument LoadPart(string docxPath, string entryName)
    {
        using var archive = ZipFile.OpenRead(docxPath);
        using var stream = archive.GetEntry(entryName)!.Open();
        return XDocument.Load(stream);
    }

    private static byte[] ReadEntryBytes(string docxPath, string entryName)
    {
        using var archive = ZipFile.OpenRead(docxPath);
        using var stream = archive.GetEntry(entryName)!.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    /// <summary>Every decompressed entry except <paramref name="changedPartName"/> must be byte-identical.</summary>
    private static void AssertOnlyPartChanged(byte[] originalBytes, string mutatedPath, string changedPartName)
    {
        using var original = new ZipArchive(new MemoryStream(originalBytes), ZipArchiveMode.Read);
        using var mutated = ZipFile.OpenRead(mutatedPath);
        var originalNames = original.Entries.Select(e => e.FullName).OrderBy(n => n, StringComparer.Ordinal).ToList();
        var mutatedNames = mutated.Entries.Select(e => e.FullName).OrderBy(n => n, StringComparer.Ordinal).ToList();
        Assert.Equal(originalNames, mutatedNames);

        foreach (var name in originalNames)
        {
            var before = ReadAll(original.GetEntry(name)!);
            var after = ReadAll(mutated.GetEntry(name)!);
            if (string.Equals(name, changedPartName, StringComparison.OrdinalIgnoreCase))
            {
                Assert.False(before.SequenceEqual(after), $"Entry '{name}' was expected to change but is byte-identical.");
            }
            else
            {
                Assert.True(before.SequenceEqual(after), $"Entry '{name}' changed but must remain byte-identical.");
            }
        }

        static byte[] ReadAll(ZipArchiveEntry entry)
        {
            using var stream = entry.Open();
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return buffer.ToArray();
        }
    }

    /// <summary>
    /// Sprint step 5: margins/spacing/style invariants. Paragraph properties and
    /// section properties must be canonically identical before/after the mutation;
    /// the styles part is separately asserted byte-identical by the callers.
    /// </summary>
    private static void AssertFormattingInvariantsUnchanged(byte[] originalBytes, string mutatedPath)
    {
        using var original = new ZipArchive(new MemoryStream(originalBytes), ZipArchiveMode.Read);
        XDocument before;
        using (var stream = original.GetEntry("word/document.xml")!.Open())
            before = XDocument.Load(stream);
        var after = LoadPart(mutatedPath, "word/document.xml");

        Assert.Equal(FormattingSignature(before), FormattingSignature(after));

        static string FormattingSignature(XDocument doc) => string.Join('\u001f', doc.Descendants()
            .Where(e => e.Name.LocalName is "pPr" or "sectPr")
            .Select(e => e.ToString(SaveOptions.DisableFormatting)));
    }

    private async Task<(string PlanId, string Path)> SeedAsync(string docId, string fileName, byte[] content, string? planId = null)
    {
        var path = Path.Combine(_root, fileName);
        await File.WriteAllBytesAsync(path, content);
        await _documents.CreateAsync(new Document { Id = docId, CurrentPath = path, Filename = fileName, Extension = Path.GetExtension(fileName) });
        BatchOperationPlan plan;
        if (planId == null)
        {
            plan = new BatchOperationPlan
            {
                WorkspaceId = "ws",
                Status = BatchPlanStatus.Previewed,
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30)
            };
            await _plans.SavePlanAsync(plan);
        }
        else
        {
            plan = (await _plans.GetByIdAsync(planId))!;
        }
        plan.Files.Add(new BatchOperationPlanFile
        {
            PlanId = plan.Id,
            DocumentId = docId,
            ExpectedFingerprint = Convert.ToHexString(SHA256.HashData(content)),
            Selected = true
        });
        await _plans.SavePlanAsync(plan);
        return (plan.Id, path);
    }

    private async Task SelectAsync(string planId, string docId, IReadOnlyList<string> occurrenceIds)
    {
        var plan = (await _plans.GetByIdAsync(planId))!;
        var file = plan.Files.Single(f => f.DocumentId == docId);
        file.Selected = true;
        file.SelectedChangeIds = occurrenceIds.Distinct(StringComparer.Ordinal).ToList();
        await _plans.SavePlanAsync(plan);
    }

    private async Task<IReadOnlyList<FontChangeTarget>> CreateTargetsAsync(string planId)
    {
        var plan = (await _plans.GetByIdAsync(planId))!;
        var targets = new List<FontChangeTarget>();
        foreach (var file in plan.Files)
        {
            var document = await _documents.GetByIdAsync(file.DocumentId);
            targets.Add(new FontChangeTarget(document!.CurrentPath, file.ExpectedFingerprint, file.SelectedChangeIds));
        }
        return targets;
    }

    private sealed class FakeHistoryRecorder : IBatchApplyHistoryRecorder
    {
        public List<BatchApplyHistoryRecord> Records { get; } = new();
        public Task RecordAsync(BatchApplyHistoryRecord record, CancellationToken cancellationToken = default)
        {
            Records.Add(record);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeReindexTrigger : IBatchApplyReindexTrigger
    {
        public List<string> DocumentIds { get; } = new();
        public Task TriggerAsync(string documentId, CancellationToken cancellationToken = default)
        {
            DocumentIds.Add(documentId);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeHistoryStorage : IHistoryStorage
    {
        private readonly string _snapshotRoot;
        private readonly List<SnapshotMetadata> _metadata = new();

        public FakeHistoryStorage(string root, long capacityBytes = 5L * 1024 * 1024 * 1024)
        {
            _snapshotRoot = Path.Combine(root, "snapshots");
            CapacityBytes = capacityBytes;
        }

        public long CapacityBytes { get; }

        public string GetSnapshotRootPath(string snapshotId) => Path.Combine(_snapshotRoot, snapshotId);
        public Task SaveMetadataAsync(SnapshotMetadata metadata) { _metadata.RemoveAll(m => m.SnapshotId == metadata.SnapshotId); _metadata.Add(metadata); return Task.CompletedTask; }
        public Task<IReadOnlyCollection<SnapshotMetadata>> GetAllMetadataAsync() => Task.FromResult<IReadOnlyCollection<SnapshotMetadata>>(_metadata);
        public Task DeleteSnapshotAsync(string snapshotId) { _metadata.RemoveAll(m => m.SnapshotId == snapshotId); return Task.CompletedTask; }
        public Task<long> GetTotalSizeAsync() => Task.FromResult(0L);
        public Task<long> GetCapacityBytesAsync() => Task.FromResult(CapacityBytes);
    }
}
