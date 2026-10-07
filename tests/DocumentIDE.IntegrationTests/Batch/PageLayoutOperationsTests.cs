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
/// BAT-09 — Strict Page-Layout Operations tests.
/// Covers the sprint's implementation steps and the three required tests:
/// an A4 paper-size change leaves margins unchanged, a margin change leaves the
/// font unchanged, and an alignment change leaves section properties unchanged.
/// </summary>
public sealed class PageLayoutOperationsTests : IDisposable
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private readonly string _root;
    private readonly AppDb _db;
    private readonly BatchPlanRepository _plans;
    private readonly DocumentRepository _documents;
    private readonly FakeHistoryRecorder _recorder;
    private readonly FakeReindexTrigger _reindex;
    private readonly BatchApplyEngine _engine;
    private readonly SetMarginsOperation _setMargins;
    private readonly SetPaperSizeOperation _setPaperSize;
    private readonly SetOrientationOperation _setOrientation;
    private readonly SetAlignmentOperation _setAlignment;

    public PageLayoutOperationsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "page-layout-ops-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _db = new AppDb(Path.Combine(_root, "state.db"));
        _db.InitializeAsync().GetAwaiter().GetResult();
        _plans = new BatchPlanRepository(_db);
        _documents = new DocumentRepository(_db);
        new WorkspaceRepository(_db).CreateAsync(new Workspace { Id = "ws", Name = "page-layout-ops" }).GetAwaiter().GetResult();
        _recorder = new FakeHistoryRecorder();
        _reindex = new FakeReindexTrigger();
        _engine = new BatchApplyEngine(
            _plans, _documents,
            new RecoverySnapshotService(new FakeHistoryStorage(_root)),
            new WordEditStateProbe(), new AtomicFileReplacer(), _recorder, _reindex);
        _setMargins = new SetMarginsOperation(new StrictPageLayoutMutator());
        _setPaperSize = new SetPaperSizeOperation(new StrictPageLayoutMutator());
        _setOrientation = new SetOrientationOperation(new StrictPageLayoutMutator());
        _setAlignment = new SetAlignmentOperation(new StrictPageLayoutMutator());
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_root, true);
    }

    [Fact]
    public async Task Apply_A4PaperSizeDoesNotAlterMargins()
    {
        // Sprint required test #1 (mandated first RED): changing the reviewed
        // section's paper size to A4 must not alter margins, run formatting, or
        // any other package part.
        var package = DocxTestFixtureBuilder.CreateValidDocxPackage(
            documentXml: LayoutFixtureDocument,
            extraEntries: new[] { ("word/styles.xml", Encoding.UTF8.GetBytes(StylesXml)) });
        var (planId, path) = await SeedAsync("doc", "a4.docx", package);

        var parameters = new SetPaperSizeParameters
        {
            WidthTwips = PaperSizeTwips.A4Width,
            HeightTwips = PaperSizeTwips.A4Height
        };
        var occurrences = await _setPaperSize.BuildOccurrencesAsync(path, parameters);
        var occurrence = Assert.Single(occurrences);
        Assert.Equal(12240, occurrence.CurrentWidthTwips);
        Assert.Equal(15840, occurrence.CurrentHeightTwips);
        Assert.Equal(PaperSizeTwips.A4Width, occurrence.NewWidthTwips);
        Assert.Equal(PaperSizeTwips.A4Height, occurrence.NewHeightTwips);
        await SelectAsync(planId, "doc", new[] { occurrence.Id });

        var mutate = _setPaperSize.CreateMutate(parameters, await CreateTargetsAsync(planId));
        var result = await _engine.ApplyAsync(planId, mutate);

        var success = Assert.Single(result.Files);
        Assert.Equal(BatchApplyFileOutcome.Success, success.Outcome);
        Assert.Null(success.ReasonCode);

        // Page size changed to A4; margins, orientation and run formatting are intact.
        var doc = LoadPart(path, "word/document.xml");
        var pgSz = doc.Descendants(W + "pgSz").Single();
        Assert.Equal("11906", (string?)pgSz.Attribute(W + "w"));
        Assert.Equal("16838", (string?)pgSz.Attribute(W + "h"));
        Assert.Null(pgSz.Attribute(W + "orient"));
        var pgMar = doc.Descendants(W + "pgMar").Single();
        Assert.Equal("1440", (string?)pgMar.Attribute(W + "top"));
        Assert.Equal("1800", (string?)pgMar.Attribute(W + "right"));
        Assert.Equal("1440", (string?)pgMar.Attribute(W + "bottom"));
        Assert.Equal("1800", (string?)pgMar.Attribute(W + "left"));
        var rFonts = doc.Descendants(W + "rFonts").Single();
        Assert.Equal("Arial", (string?)rFonts.Attribute(W + "ascii"));
        Assert.Equal("22", (string?)doc.Descendants(W + "sz").Single().Attribute(W + "val"));
        Assert.Equal("240", (string?)doc.Descendants(W + "spacing").Single().Attribute(W + "after"));

        // Acceptance "no normalize-to-template": only the targeted part changed.
        AssertOnlyPartChanged(package, path, "word/document.xml");
        Assert.Equal(Encoding.UTF8.GetBytes(StylesXml), ReadEntryBytes(path, "word/styles.xml"));
    }

    [Fact]
    public async Task Apply_MarginsChangeDoesNotAlterFont()
    {
        // Sprint required test #2: changing the reviewed section's margins must not
        // alter run formatting, paragraph spacing, page size, or any other part.
        var package = DocxTestFixtureBuilder.CreateValidDocxPackage(
            documentXml: LayoutFixtureDocument,
            extraEntries: new[] { ("word/styles.xml", Encoding.UTF8.GetBytes(StylesXml)) });
        var (planId, path) = await SeedAsync("doc", "margins.docx", package);

        var parameters = new SetMarginsParameters
        {
            TopTwips = 2880,
            RightTwips = 1440,
            BottomTwips = 2880,
            LeftTwips = 1440
        };
        var occurrences = await _setMargins.BuildOccurrencesAsync(path, parameters);
        var occurrence = Assert.Single(occurrences);
        Assert.Equal(0, occurrence.SectionIndex);
        Assert.Equal(1440, occurrence.CurrentTopTwips);
        Assert.Equal(1800, occurrence.CurrentRightTwips);
        Assert.Equal(1440, occurrence.CurrentBottomTwips);
        Assert.Equal(1800, occurrence.CurrentLeftTwips);
        Assert.Equal(2880, occurrence.NewTopTwips);
        Assert.Equal(1440, occurrence.NewRightTwips);
        await SelectAsync(planId, "doc", new[] { occurrence.Id });

        var mutate = _setMargins.CreateMutate(parameters, await CreateTargetsAsync(planId));
        var result = await _engine.ApplyAsync(planId, mutate);

        var success = Assert.Single(result.Files);
        Assert.Equal(BatchApplyFileOutcome.Success, success.Outcome);
        Assert.Null(success.ReasonCode);

        // Margins changed; the font, size, spacing, alignment and page size are intact.
        var doc = LoadPart(path, "word/document.xml");
        var pgMar = doc.Descendants(W + "pgMar").Single();
        Assert.Equal("2880", (string?)pgMar.Attribute(W + "top"));
        Assert.Equal("1440", (string?)pgMar.Attribute(W + "right"));
        Assert.Equal("2880", (string?)pgMar.Attribute(W + "bottom"));
        Assert.Equal("1440", (string?)pgMar.Attribute(W + "left"));
        var pgSz = doc.Descendants(W + "pgSz").Single();
        Assert.Equal("12240", (string?)pgSz.Attribute(W + "w"));
        Assert.Equal("15840", (string?)pgSz.Attribute(W + "h"));
        var rFonts = doc.Descendants(W + "rFonts").Single();
        Assert.Equal("Arial", (string?)rFonts.Attribute(W + "ascii"));
        Assert.Equal("Arial", (string?)rFonts.Attribute(W + "hAnsi"));
        Assert.Equal("22", (string?)doc.Descendants(W + "sz").Single().Attribute(W + "val"));
        Assert.Equal("240", (string?)doc.Descendants(W + "spacing").Single().Attribute(W + "after"));
        Assert.Equal("left", (string?)doc.Descendants(W + "jc").Single().Attribute(W + "val"));
        Assert.Equal("Alpha body", doc.Descendants(W + "t").Single().Value);

        AssertOnlyPartChanged(package, path, "word/document.xml");
        Assert.Equal(Encoding.UTF8.GetBytes(StylesXml), ReadEntryBytes(path, "word/styles.xml"));
    }

    [Fact]
    public async Task Build_MarginsPreviewShowsPerSectionOldToNewAndSkipsUnchangedSections()
    {
        // Sprint step 5: preview lists each affected section with its old→new
        // margins; a section already at the requested margins is unchanged and not
        // listed (spec 07 §19 "Section 2: unchanged").
        var package = DocxTestFixtureBuilder.CreateValidDocxPackage(documentXml: TwoSectionFixtureDocument);
        var path = Path.Combine(_root, "two-sections.docx");
        await File.WriteAllBytesAsync(path, package);

        var parameters = new SetMarginsParameters
        {
            TopTwips = 2880,
            RightTwips = 1440,
            BottomTwips = 2880,
            LeftTwips = 1440
        };
        var occurrences = await _setMargins.BuildOccurrencesAsync(path, parameters);

        var occurrence = Assert.Single(occurrences);
        Assert.Equal(0, occurrence.SectionIndex);
        Assert.Equal("Section 1", occurrence.Context);
        Assert.Equal(1440, occurrence.CurrentTopTwips);
        Assert.Equal(1800, occurrence.CurrentRightTwips);
        Assert.Equal(2880, occurrence.NewTopTwips);
        Assert.Equal(1440, occurrence.NewLeftTwips);
        Assert.DoesNotContain(occurrences, o => o.SectionIndex == 1);

        // Preview never mutates the source.
        Assert.Equal(package, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task Apply_OrientationChangeSetsOrientationAndSwapsDimensions()
    {
        // Sprint step 3: a portrait→landscape change sets w:orient and swaps the
        // stored dimensions so the physical page size is preserved; margins, font
        // and every other property stay untouched.
        var package = DocxTestFixtureBuilder.CreateValidDocxPackage(
            documentXml: LayoutFixtureDocument,
            extraEntries: new[] { ("word/styles.xml", Encoding.UTF8.GetBytes(StylesXml)) });
        var (planId, path) = await SeedAsync("doc", "orientation.docx", package);

        var parameters = new SetOrientationParameters { Orientation = PageOrientationKind.Landscape };
        var occurrences = await _setOrientation.BuildOccurrencesAsync(path, parameters);
        var occurrence = Assert.Single(occurrences);
        Assert.Equal("Section 1", occurrence.Context);
        Assert.Equal("portrait", occurrence.CurrentOrientation);
        Assert.Equal("landscape", occurrence.NewOrientation);
        Assert.Equal(12240, occurrence.CurrentWidthTwips);
        Assert.Equal(15840, occurrence.CurrentHeightTwips);
        Assert.Equal(15840, occurrence.NewWidthTwips);
        Assert.Equal(12240, occurrence.NewHeightTwips);
        await SelectAsync(planId, "doc", new[] { occurrence.Id });

        var mutate = _setOrientation.CreateMutate(parameters, await CreateTargetsAsync(planId));
        var result = await _engine.ApplyAsync(planId, mutate);

        var success = Assert.Single(result.Files);
        Assert.Equal(BatchApplyFileOutcome.Success, success.Outcome);
        Assert.Null(success.ReasonCode);

        var doc = LoadPart(path, "word/document.xml");
        var pgSz = doc.Descendants(W + "pgSz").Single();
        Assert.Equal("15840", (string?)pgSz.Attribute(W + "w"));
        Assert.Equal("12240", (string?)pgSz.Attribute(W + "h"));
        Assert.Equal("landscape", (string?)pgSz.Attribute(W + "orient"));
        var pgMar = doc.Descendants(W + "pgMar").Single();
        Assert.Equal("1440", (string?)pgMar.Attribute(W + "top"));
        Assert.Equal("1800", (string?)pgMar.Attribute(W + "right"));
        Assert.Equal("Arial", (string?)doc.Descendants(W + "rFonts").Single().Attribute(W + "ascii"));
        Assert.Equal("22", (string?)doc.Descendants(W + "sz").Single().Attribute(W + "val"));

        AssertOnlyPartChanged(package, path, "word/document.xml");
        Assert.Equal(Encoding.UTF8.GetBytes(StylesXml), ReadEntryBytes(path, "word/styles.xml"));
    }

    [Fact]
    public async Task Build_OrientationAlreadyAtTargetIsNotAffected()
    {
        // Sprint step 3 no-op discipline: a section already in the requested
        // orientation must not be listed or rewritten; the reverse request lists
        // it with the exact inverse swap.
        var package = DocxTestFixtureBuilder.CreateValidDocxPackage(documentXml: LandscapeFixtureDocument);
        var path = Path.Combine(_root, "landscape.docx");
        await File.WriteAllBytesAsync(path, package);

        var landscapeRequest = new SetOrientationParameters { Orientation = PageOrientationKind.Landscape };
        Assert.Empty(await _setOrientation.BuildOccurrencesAsync(path, landscapeRequest));

        var portraitRequest = new SetOrientationParameters { Orientation = PageOrientationKind.Portrait };
        var occurrence = Assert.Single(await _setOrientation.BuildOccurrencesAsync(path, portraitRequest));
        Assert.Equal("landscape", occurrence.CurrentOrientation);
        Assert.Equal("portrait", occurrence.NewOrientation);
        Assert.Equal(15840, occurrence.CurrentWidthTwips);
        Assert.Equal(12240, occurrence.CurrentHeightTwips);
        Assert.Equal(12240, occurrence.NewWidthTwips);
        Assert.Equal(15840, occurrence.NewHeightTwips);

        // Preview never mutates the source.
        Assert.Equal(package, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task Apply_AlignmentChangeDoesNotAlterSectionProperties()
    {
        // Sprint required test #3: changing the reviewed paragraph's alignment must
        // not alter section properties (margins, page size, orientation), font
        // formatting, or any other package part.
        var package = DocxTestFixtureBuilder.CreateValidDocxPackage(
            documentXml: LayoutFixtureDocument,
            extraEntries: new[] { ("word/styles.xml", Encoding.UTF8.GetBytes(StylesXml)) });
        var (planId, path) = await SeedAsync("doc", "alignment.docx", package);
        string sectionsBefore = SectionSignature(package);

        var parameters = new SetAlignmentParameters { Alignment = ParagraphAlignmentKind.Center };
        var occurrences = await _setAlignment.BuildOccurrencesAsync(path, parameters);
        var occurrence = Assert.Single(occurrences);
        Assert.Equal("Body", occurrence.Origin);
        Assert.Equal("left", occurrence.CurrentAlignment);
        Assert.Equal("center", occurrence.NewAlignment);
        Assert.Contains("Alpha body", occurrence.Context);
        await SelectAsync(planId, "doc", new[] { occurrence.Id });

        var mutate = _setAlignment.CreateMutate(parameters, await CreateTargetsAsync(planId));
        var result = await _engine.ApplyAsync(planId, mutate);

        var success = Assert.Single(result.Files);
        Assert.Equal(BatchApplyFileOutcome.Success, success.Outcome);
        Assert.Null(success.ReasonCode);

        // Alignment changed; section properties and run formatting are intact.
        var doc = LoadPart(path, "word/document.xml");
        Assert.Equal("center", (string?)doc.Descendants(W + "jc").Single().Attribute(W + "val"));
        Assert.Equal("240", (string?)doc.Descendants(W + "spacing").Single().Attribute(W + "after"));
        Assert.Equal("Arial", (string?)doc.Descendants(W + "rFonts").Single().Attribute(W + "ascii"));
        Assert.Equal("22", (string?)doc.Descendants(W + "sz").Single().Attribute(W + "val"));
        Assert.Equal(sectionsBefore, SectionSignature(await File.ReadAllBytesAsync(path)));

        AssertOnlyPartChanged(package, path, "word/document.xml");
        Assert.Equal(Encoding.UTF8.GetBytes(StylesXml), ReadEntryBytes(path, "word/styles.xml"));
    }

    [Fact]
    public async Task Build_AlignmentPreviewTargetsOnlyDirectJustification()
    {
        // Sprint step 4 + 5: only paragraphs with a direct w:jc are targeted; the
        // preview identifies exactly the affected paragraphs (current→new), skips
        // style-inherited and already-at-target paragraphs, and reports
        // body/table/header origins.
        var package = DocxTestFixtureBuilder.CreateValidDocxPackage(
            documentXml: AlignmentFixtureDocument,
            extraEntries: new[] { ("word/header1.xml", Encoding.UTF8.GetBytes(AlignmentHeaderXml)) });
        var path = Path.Combine(_root, "alignment-preview.docx");
        await File.WriteAllBytesAsync(path, package);

        var occurrences = await _setAlignment.BuildOccurrencesAsync(
            path, new SetAlignmentParameters { Alignment = ParagraphAlignmentKind.Center });

        Assert.Equal(new[]
        {
            "align_word/document.xml_Body_p0",
            "align_word/document.xml_Body_p1",
            "align_word/document.xml_Table_p4",
            "align_word/header1.xml_Header_p0"
        }, occurrences.Select(o => o.Id));
        Assert.Equal(new[] { "Body", "Body", "Table", "Header" }, occurrences.Select(o => o.Origin));
        Assert.Equal(new[] { "left", "both", "left", "left" }, occurrences.Select(o => o.CurrentAlignment));
        Assert.All(occurrences, o => Assert.Equal("center", o.NewAlignment));
        Assert.Contains("Alpha left", occurrences[0].Context);
        Assert.Contains("Beta justified", occurrences[1].Context);
        Assert.Contains("Table left", occurrences[2].Context);
        Assert.Contains("Header left", occurrences[3].Context);
        // The style-inherited paragraph (p2) and the already-centered paragraph (p3) are not affected.
        Assert.DoesNotContain(occurrences, o => o.Id.EndsWith("_p2", StringComparison.Ordinal) || o.Id.EndsWith("_p3", StringComparison.Ordinal));

        // Canonical w:jc mapping: Justify is Word's "both" (p1 already justified is
        // excluded), Right is "right".
        var justifyOccurrences = await _setAlignment.BuildOccurrencesAsync(
            path, new SetAlignmentParameters { Alignment = ParagraphAlignmentKind.Justify });
        Assert.All(justifyOccurrences, o => Assert.Equal("both", o.NewAlignment));
        Assert.DoesNotContain(justifyOccurrences, o => o.Id.EndsWith("_p1", StringComparison.Ordinal));

        var rightOccurrences = await _setAlignment.BuildOccurrencesAsync(
            path, new SetAlignmentParameters { Alignment = ParagraphAlignmentKind.Right });
        Assert.All(rightOccurrences, o => Assert.Equal("right", o.NewAlignment));

        // Preview never mutates the source.
        Assert.Equal(package, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task Apply_AlignmentChangesOnlyTargetedParagraphs()
    {
        // Two directly left-aligned paragraphs: only the reviewed, selected
        // paragraph may change; the deselected sibling stays left-aligned.
        var package = DocxTestFixtureBuilder.CreateValidDocxPackage(documentXml: TwoParagraphFixtureDocument);
        var (planId, path) = await SeedAsync("doc", "align-deselect.docx", package);

        var parameters = new SetAlignmentParameters { Alignment = ParagraphAlignmentKind.Center };
        var occurrences = await _setAlignment.BuildOccurrencesAsync(path, parameters);
        Assert.Equal(2, occurrences.Count);

        await SelectAsync(planId, "doc", new[] { occurrences[1].Id });

        var mutate = _setAlignment.CreateMutate(parameters, await CreateTargetsAsync(planId));
        var result = await _engine.ApplyAsync(planId, mutate);

        var success = Assert.Single(result.Files);
        Assert.Equal(BatchApplyFileOutcome.Success, success.Outcome);

        var doc = LoadPart(path, "word/document.xml");
        var justifications = doc.Descendants(W + "jc").ToList();
        Assert.Equal("left", (string?)justifications[0].Attribute(W + "val"));
        Assert.Equal("center", (string?)justifications[1].Attribute(W + "val"));
        Assert.Equal("First left", doc.Descendants(W + "t").First().Value);
        Assert.Equal("Second left", doc.Descendants(W + "t").Last().Value);
    }

    [Fact]
    public async Task Build_PaperSizePreviewShowsPerSectionOldToNewValues()
    {
        // Sprint step 5: the A4 preview lists each affected section's old→new
        // dimensions and skips a section that already has the requested size.
        var package = DocxTestFixtureBuilder.CreateValidDocxPackage(documentXml: TwoSectionFixtureDocument);
        var path = Path.Combine(_root, "papersize-preview.docx");
        await File.WriteAllBytesAsync(path, package);

        var parameters = new SetPaperSizeParameters
        {
            WidthTwips = PaperSizeTwips.A4Width,
            HeightTwips = PaperSizeTwips.A4Height
        };
        var occurrences = await _setPaperSize.BuildOccurrencesAsync(path, parameters);

        var occurrence = Assert.Single(occurrences);
        Assert.Equal(0, occurrence.SectionIndex);
        Assert.Equal("Section 1", occurrence.Context);
        Assert.Equal(12240, occurrence.CurrentWidthTwips);
        Assert.Equal(15840, occurrence.CurrentHeightTwips);
        Assert.Equal(PaperSizeTwips.A4Width, occurrence.NewWidthTwips);
        Assert.Equal(PaperSizeTwips.A4Height, occurrence.NewHeightTwips);
        Assert.DoesNotContain(occurrences, o => o.SectionIndex == 1);

        Assert.Equal(package, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task Apply_PaperSizeChangesOnlySelectedSection()
    {
        // Two directly reviewed sections: only the selected section's page size may
        // change; the deselected section and both sections' margins stay intact.
        var package = DocxTestFixtureBuilder.CreateValidDocxPackage(documentXml: """
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:body>
                <w:p>
                  <w:pPr>
                    <w:sectPr><w:pgSz w:w="12240" w:h="15840"/><w:pgMar w:top="1440" w:right="1800" w:bottom="1440" w:left="1800"/></w:sectPr>
                  </w:pPr>
                  <w:r><w:t>Section one</w:t></w:r>
                </w:p>
                <w:p><w:r><w:t>Section two</w:t></w:r></w:p>
                <w:sectPr><w:pgSz w:w="12240" w:h="15840"/><w:pgMar w:top="2880" w:right="1440" w:bottom="2880" w:left="1440"/></w:sectPr>
              </w:body>
            </w:document>
            """);
        var (planId, path) = await SeedAsync("doc", "papersize-select.docx", package);

        var parameters = new SetPaperSizeParameters
        {
            WidthTwips = PaperSizeTwips.A4Width,
            HeightTwips = PaperSizeTwips.A4Height
        };
        var occurrences = await _setPaperSize.BuildOccurrencesAsync(path, parameters);
        Assert.Equal(2, occurrences.Count);

        await SelectAsync(planId, "doc", new[] { occurrences[1].Id });

        var mutate = _setPaperSize.CreateMutate(parameters, await CreateTargetsAsync(planId));
        var result = await _engine.ApplyAsync(planId, mutate);

        var success = Assert.Single(result.Files);
        Assert.Equal(BatchApplyFileOutcome.Success, success.Outcome);

        var doc = LoadPart(path, "word/document.xml");
        var pageSizes = doc.Descendants(W + "pgSz").ToList();
        // Section 1 (paragraph-level sectPr) stays letter; section 2 becomes A4.
        Assert.Equal("12240", (string?)pageSizes[0].Attribute(W + "w"));
        Assert.Equal("15840", (string?)pageSizes[0].Attribute(W + "h"));
        Assert.Equal("11906", (string?)pageSizes[1].Attribute(W + "w"));
        Assert.Equal("16838", (string?)pageSizes[1].Attribute(W + "h"));
        // Margins are untouched in both sections.
        var margins = doc.Descendants(W + "pgMar").ToList();
        Assert.Equal("1440", (string?)margins[0].Attribute(W + "top"));
        Assert.Equal("1800", (string?)margins[0].Attribute(W + "left"));
        Assert.Equal("2880", (string?)margins[1].Attribute(W + "top"));
        Assert.Equal("1440", (string?)margins[1].Attribute(W + "left"));
    }

    [Fact]
    public async Task Mutator_FailsClosedOnMismatchedOrMissingTargetsAndLeavesFileUnchanged()
    {
        // The strict mutator must verify every reviewed target against the file it
        // is about to change and must not write anything when any target fails.
        var package = DocxTestFixtureBuilder.CreateValidDocxPackage(documentXml: LayoutFixtureDocument);
        var path = Path.Combine(_root, "fail-closed.docx");
        await File.WriteAllBytesAsync(path, package);
        var mutator = new StrictPageLayoutMutator();

        // Reviewed margins say top 9999; the file says 1440 → mismatch blocks.
        var marginMismatch = Assert.Throws<InvalidOperationException>(() => mutator.SetMargins(
            path,
            new[] { new DocxMarginsTarget("word/document.xml", 0, 9999, 1800, 1440, 1800) },
            2880, 2880, 2880, 2880));
        Assert.Contains("PageLayoutTargetMismatch", marginMismatch.Message, StringComparison.Ordinal);
        Assert.Equal(package, await File.ReadAllBytesAsync(path));

        // Missing section identity → typed not-found block.
        var missingSection = Assert.Throws<InvalidOperationException>(() => mutator.SetPaperSize(
            path,
            new[] { new DocxPaperSizeTarget("word/document.xml", 9, 12240, 15840) },
            PaperSizeTwips.A4Width, PaperSizeTwips.A4Height));
        Assert.Contains("PageLayoutTargetNotFound", missingSection.Message, StringComparison.Ordinal);
        Assert.Equal(package, await File.ReadAllBytesAsync(path));

        // Reviewed paper size says A4; the file says letter → mismatch blocks.
        var paperMismatch = Assert.Throws<InvalidOperationException>(() => mutator.SetPaperSize(
            path,
            new[] { new DocxPaperSizeTarget("word/document.xml", 0, PaperSizeTwips.A4Width, PaperSizeTwips.A4Height) },
            PaperSizeTwips.LetterWidth, PaperSizeTwips.LetterHeight));
        Assert.Contains("PageLayoutTargetMismatch", paperMismatch.Message, StringComparison.Ordinal);
        Assert.Equal(package, await File.ReadAllBytesAsync(path));

        // Reviewed orientation says landscape; the file says portrait → mismatch blocks.
        var orientationMismatch = Assert.Throws<InvalidOperationException>(() => mutator.SetOrientation(
            path,
            new[] { new DocxOrientationTarget("word/document.xml", 0, "landscape", 15840, 12240) },
            PageOrientationKind.Landscape));
        Assert.Contains("PageLayoutTargetMismatch", orientationMismatch.Message, StringComparison.Ordinal);
        Assert.Equal(package, await File.ReadAllBytesAsync(path));

        // Missing paragraph identity and mismatched justification → typed blocks.
        var missingParagraph = Assert.Throws<InvalidOperationException>(() => mutator.SetParagraphAlignment(
            path,
            new[] { new DocxAlignmentTarget("word/document.xml", 9, "left") },
            ParagraphAlignmentKind.Center));
        Assert.Contains("PageLayoutTargetNotFound", missingParagraph.Message, StringComparison.Ordinal);
        Assert.Equal(package, await File.ReadAllBytesAsync(path));

        var alignmentMismatch = Assert.Throws<InvalidOperationException>(() => mutator.SetParagraphAlignment(
            path,
            new[] { new DocxAlignmentTarget("word/document.xml", 0, "right") },
            ParagraphAlignmentKind.Center));
        Assert.Contains("PageLayoutTargetMismatch", alignmentMismatch.Message, StringComparison.Ordinal);
        Assert.Equal(package, await File.ReadAllBytesAsync(path));

        // Missing part identity → typed not-found block.
        var missingPart = Assert.Throws<InvalidOperationException>(() => mutator.SetParagraphAlignment(
            path,
            new[] { new DocxAlignmentTarget("word/header9.xml", 0, "left") },
            ParagraphAlignmentKind.Center));
        Assert.Contains("PageLayoutTargetNotFound", missingPart.Message, StringComparison.Ordinal);
        Assert.Equal(package, await File.ReadAllBytesAsync(path));

        // A section without explicit geometry is never fabricated into a target.
        var noGeometry = DocxTestFixtureBuilder.CreateValidDocxPackage(documentXml: """
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:body><w:p><w:r><w:t>No geometry</w:t></w:r></w:p><w:sectPr/></w:body>
            </w:document>
            """);
        await File.WriteAllBytesAsync(path, noGeometry);
        var noMargins = Assert.Throws<InvalidOperationException>(() => mutator.SetMargins(
            path,
            new[] { new DocxMarginsTarget("word/document.xml", 0, 1440, 1440, 1440, 1440) },
            2880, 2880, 2880, 2880));
        Assert.Contains("PageLayoutTargetNotFound", noMargins.Message, StringComparison.Ordinal);
        var noPageSize = Assert.Throws<InvalidOperationException>(() => mutator.SetPaperSize(
            path,
            new[] { new DocxPaperSizeTarget("word/document.xml", 0, 12240, 15840) },
            PaperSizeTwips.A4Width, PaperSizeTwips.A4Height));
        Assert.Contains("PageLayoutTargetNotFound", noPageSize.Message, StringComparison.Ordinal);
        Assert.Equal(noGeometry, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task Parameters_RejectInvalidValuesBeforeAnyFileAccess()
    {
        // Validation must fire before any file access; "missing.docx" never exists.
        var missingPath = Path.Combine(_root, "missing.docx");

        foreach (var invalid in new[]
        {
            new SetMarginsParameters { TopTwips = -1 },
            new SetMarginsParameters { RightTwips = DocxPageLayoutScan.MaxTwips + 1 },
            new SetMarginsParameters { BottomTwips = int.MaxValue }
        })
        {
            var ex = await Assert.ThrowsAsync<ArgumentException>(() => _setMargins.BuildOccurrencesAsync(missingPath, invalid));
            Assert.Contains("InvalidParameters", ex.Message, StringComparison.Ordinal);
            Assert.Throws<ArgumentException>(() => _setMargins.CreateMutate(invalid, Array.Empty<PageLayoutChangeTarget>()));
        }

        foreach (var invalid in new[]
        {
            new SetPaperSizeParameters { WidthTwips = 0, HeightTwips = 100 },
            new SetPaperSizeParameters { WidthTwips = 100, HeightTwips = DocxPageLayoutScan.MaxTwips + 1 }
        })
        {
            var ex = await Assert.ThrowsAsync<ArgumentException>(() => _setPaperSize.BuildOccurrencesAsync(missingPath, invalid));
            Assert.Contains("InvalidParameters", ex.Message, StringComparison.Ordinal);
            Assert.Throws<ArgumentException>(() => _setPaperSize.CreateMutate(invalid, Array.Empty<PageLayoutChangeTarget>()));
        }

        var invalidOrientation = new SetOrientationParameters { Orientation = (PageOrientationKind)42 };
        var orientationEx = await Assert.ThrowsAsync<ArgumentException>(() => _setOrientation.BuildOccurrencesAsync(missingPath, invalidOrientation));
        Assert.Contains("InvalidParameters", orientationEx.Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => _setOrientation.CreateMutate(invalidOrientation, Array.Empty<PageLayoutChangeTarget>()));

        var invalidAlignment = new SetAlignmentParameters { Alignment = (ParagraphAlignmentKind)42 };
        var alignmentEx = await Assert.ThrowsAsync<ArgumentException>(() => _setAlignment.BuildOccurrencesAsync(missingPath, invalidAlignment));
        Assert.Contains("InvalidParameters", alignmentEx.Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => _setAlignment.CreateMutate(invalidAlignment, Array.Empty<PageLayoutChangeTarget>()));
    }

    [Fact]
    public async Task Mutator_NoOpTargetsDoNotRewritePackageBytes()
    {
        // No-op discipline (spec 06 §19): when the requested value already equals
        // the reviewed value, the mutator must not rewrite the package.
        var package = DocxTestFixtureBuilder.CreateValidDocxPackage(documentXml: LayoutFixtureDocument);
        var path = Path.Combine(_root, "noop.docx");
        await File.WriteAllBytesAsync(path, package);
        var mutator = new StrictPageLayoutMutator();

        mutator.SetMargins(
            path,
            new[] { new DocxMarginsTarget("word/document.xml", 0, 1440, 1800, 1440, 1800) },
            1440, 1800, 1440, 1800);
        Assert.Equal(package, await File.ReadAllBytesAsync(path));

        mutator.SetPaperSize(
            path,
            new[] { new DocxPaperSizeTarget("word/document.xml", 0, 12240, 15840) },
            12240, 15840);
        Assert.Equal(package, await File.ReadAllBytesAsync(path));

        mutator.SetOrientation(
            path,
            new[] { new DocxOrientationTarget("word/document.xml", 0, "portrait", 12240, 15840) },
            PageOrientationKind.Portrait);
        Assert.Equal(package, await File.ReadAllBytesAsync(path));

        mutator.SetParagraphAlignment(
            path,
            new[] { new DocxAlignmentTarget("word/document.xml", 0, "left") },
            ParagraphAlignmentKind.Left);
        Assert.Equal(package, await File.ReadAllBytesAsync(path));

        // An empty reviewed target list is also a no-op.
        mutator.SetMargins(path, Array.Empty<DocxMarginsTarget>(), 2880, 2880, 2880, 2880);
        Assert.Equal(package, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task Apply_ChangedFileAfterPreviewIsSkippedAsStale()
    {
        var package = DocxTestFixtureBuilder.CreateValidDocxPackage(documentXml: LayoutFixtureDocument);
        var (planId, path) = await SeedAsync("doc", "stale-layout.docx", package);

        var parameters = new SetPaperSizeParameters
        {
            WidthTwips = PaperSizeTwips.A4Width,
            HeightTwips = PaperSizeTwips.A4Height
        };
        var occurrences = await _setPaperSize.BuildOccurrencesAsync(path, parameters);
        await SelectAsync(planId, "doc", occurrences.Select(o => o.Id).ToList());

        // The file changes after preview: the reviewed fingerprint no longer matches.
        var modified = DocxTestFixtureBuilder.CreateValidDocxPackage(documentXml:
            LayoutFixtureDocument.Replace("Alpha body", "Alpha body changed after preview"));
        await File.WriteAllBytesAsync(path, modified);

        var mutate = _setPaperSize.CreateMutate(parameters, await CreateTargetsAsync(planId));
        var result = await _engine.ApplyAsync(planId, mutate);

        var skipped = Assert.Single(result.Files);
        Assert.Equal(BatchApplyFileOutcome.Skipped, skipped.Outcome);
        Assert.Equal("Stale", skipped.ReasonCode);
        Assert.Equal(modified, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task Apply_OneFileFailureYieldsTypedPerFileOutcomes()
    {
        var goodPackage = DocxTestFixtureBuilder.CreateValidDocxPackage(documentXml: LayoutFixtureDocument);
        // Not a DOCX package: its reviewed mutation fails at apply time while
        // the other file is valid (sprint edge case "one file fails").
        var badBytes = Encoding.UTF8.GetBytes("not a docx package");
        var (planId, goodPath) = await SeedAsync("aaa", "good-layout.docx", goodPackage);
        var (_, badPath) = await SeedAsync("bbb", "bad-layout.docx", badBytes, planId);

        var parameters = new SetMarginsParameters
        {
            TopTwips = 2880,
            RightTwips = 2880,
            BottomTwips = 2880,
            LeftTwips = 2880
        };
        var goodOccurrences = await _setMargins.BuildOccurrencesAsync(goodPath, parameters);
        await SelectAsync(planId, "aaa", goodOccurrences.Select(o => o.Id).ToList());
        // The reviewed plan believed this package contained the section; the
        // mutation failure must surface as a typed per-file outcome, not a silent skip.
        await SelectAsync(planId, "bbb", new[] { "margin_word/document.xml_s0" });

        var mutate = _setMargins.CreateMutate(parameters, await CreateTargetsAsync(planId));
        var result = await _engine.ApplyAsync(planId, mutate);

        Assert.Equal(BatchApplyResultStatus.Completed, result.Status);
        Assert.Equal(2, result.Files.Count);

        var good = result.Files.Single(f => f.DocumentId == "aaa");
        Assert.Equal(BatchApplyFileOutcome.Success, good.Outcome);
        var goodDoc = LoadPart(goodPath, "word/document.xml");
        Assert.Equal("2880", (string?)goodDoc.Descendants(W + "pgMar").Single().Attribute(W + "top"));

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
    public async Task Build_NonDocxFilesHaveNoPageLayoutOccurrences()
    {
        // Page-layout operations target DOCX packages only; other file types are
        // never touched and preview as unaffected (zero occurrences), not failed.
        var mdPath = Path.Combine(_root, "notes.md");
        await File.WriteAllBytesAsync(mdPath, Encoding.UTF8.GetBytes("# Notes\n\nSome text."));

        Assert.Empty(await _setMargins.BuildOccurrencesAsync(mdPath, new SetMarginsParameters { TopTwips = 2880, RightTwips = 2880, BottomTwips = 2880, LeftTwips = 2880 }));
        Assert.Empty(await _setPaperSize.BuildOccurrencesAsync(mdPath, new SetPaperSizeParameters { WidthTwips = PaperSizeTwips.A4Width, HeightTwips = PaperSizeTwips.A4Height }));
        Assert.Empty(await _setOrientation.BuildOccurrencesAsync(mdPath, new SetOrientationParameters { Orientation = PageOrientationKind.Landscape }));
        Assert.Empty(await _setAlignment.BuildOccurrencesAsync(mdPath, new SetAlignmentParameters { Alignment = ParagraphAlignmentKind.Center }));
        Assert.Equal("# Notes\n\nSome text.", await File.ReadAllTextAsync(mdPath));
    }

    private const string LayoutFixtureDocument = """
        <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
          <w:body>
            <w:p>
              <w:pPr><w:jc w:val="left"/><w:spacing w:after="240"/></w:pPr>
              <w:r><w:rPr><w:rFonts w:ascii="Arial" w:hAnsi="Arial"/><w:sz w:val="22"/></w:rPr><w:t>Alpha body</w:t></w:r>
            </w:p>
            <w:sectPr><w:pgSz w:w="12240" w:h="15840"/><w:pgMar w:top="1440" w:right="1800" w:bottom="1440" w:left="1800"/></w:sectPr>
          </w:body>
        </w:document>
        """;

    private const string TwoSectionFixtureDocument = """
        <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
          <w:body>
            <w:p>
              <w:pPr>
                <w:sectPr><w:pgSz w:w="12240" w:h="15840"/><w:pgMar w:top="1440" w:right="1800" w:bottom="1440" w:left="1800"/></w:sectPr>
              </w:pPr>
              <w:r><w:rPr><w:rFonts w:ascii="Arial" w:hAnsi="Arial"/><w:sz w:val="22"/></w:rPr><w:t>Section one</w:t></w:r>
            </w:p>
            <w:p>
              <w:r><w:t>Section two</w:t></w:r>
            </w:p>
            <w:sectPr><w:pgSz w:w="11906" w:h="16838"/><w:pgMar w:top="2880" w:right="1440" w:bottom="2880" w:left="1440"/></w:sectPr>
          </w:body>
        </w:document>
        """;

    private const string LandscapeFixtureDocument = """
        <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
          <w:body>
            <w:p>
              <w:r><w:rPr><w:rFonts w:ascii="Arial" w:hAnsi="Arial"/><w:sz w:val="22"/></w:rPr><w:t>Landscape body</w:t></w:r>
            </w:p>
            <w:sectPr><w:pgSz w:w="15840" w:h="12240" w:orient="landscape"/><w:pgMar w:top="1440" w:right="1800" w:bottom="1440" w:left="1800"/></w:sectPr>
          </w:body>
        </w:document>
        """;

    private const string AlignmentFixtureDocument = """
        <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
          <w:body>
            <w:p>
              <w:pPr><w:jc w:val="left"/><w:spacing w:after="120"/></w:pPr>
              <w:r><w:rPr><w:rFonts w:ascii="Arial" w:hAnsi="Arial"/><w:sz w:val="22"/></w:rPr><w:t>Alpha left</w:t></w:r>
            </w:p>
            <w:p>
              <w:pPr><w:jc w:val="both"/></w:pPr>
              <w:r><w:rPr><w:rFonts w:ascii="Calibri" w:hAnsi="Calibri"/><w:sz w:val="20"/></w:rPr><w:t>Beta justified</w:t></w:r>
            </w:p>
            <w:p><w:r><w:t>Gamma inherited</w:t></w:r></w:p>
            <w:p>
              <w:pPr><w:jc w:val="center"/></w:pPr>
              <w:r><w:t>Delta already centered</w:t></w:r>
            </w:p>
            <w:tbl><w:tr><w:tc><w:p>
              <w:pPr><w:jc w:val="left"/></w:pPr>
              <w:r><w:t>Table left</w:t></w:r>
            </w:p></w:tc></w:tr></w:tbl>
            <w:sectPr><w:pgSz w:w="12240" w:h="15840"/><w:pgMar w:top="1440" w:right="1800" w:bottom="1440" w:left="1800"/></w:sectPr>
          </w:body>
        </w:document>
        """;

    private const string AlignmentHeaderXml = """
        <w:hdr xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
          <w:p><w:pPr><w:jc w:val="left"/></w:pPr><w:r><w:t>Header left</w:t></w:r></w:p>
        </w:hdr>
        """;

    private const string TwoParagraphFixtureDocument = """
        <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
          <w:body>
            <w:p><w:pPr><w:jc w:val="left"/></w:pPr><w:r><w:t>First left</w:t></w:r></w:p>
            <w:p><w:pPr><w:jc w:val="left"/></w:pPr><w:r><w:t>Second left</w:t></w:r></w:p>
            <w:sectPr><w:pgSz w:w="12240" w:h="15840"/><w:pgMar w:top="1440" w:right="1800" w:bottom="1440" w:left="1800"/></w:sectPr>
          </w:body>
        </w:document>
        """;

    private const string StylesXml = """
        <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
          <w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii="Arial" w:hAnsi="Arial"/><w:sz w:val="22"/></w:rPr></w:rPrDefault></w:docDefaults>
        </w:styles>
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

    /// <summary>Canonical form of the document's section properties (required test #3).</summary>
    private static string SectionSignature(byte[] packageBytes)
    {
        using var archive = new ZipArchive(new MemoryStream(packageBytes), ZipArchiveMode.Read);
        using var stream = archive.GetEntry("word/document.xml")!.Open();
        var doc = XDocument.Load(stream);
        return string.Join('\u001f', doc.Descendants(W + "sectPr").Select(e => e.ToString(SaveOptions.DisableFormatting)));
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

    private async Task<IReadOnlyList<PageLayoutChangeTarget>> CreateTargetsAsync(string planId)
    {
        var plan = (await _plans.GetByIdAsync(planId))!;
        var targets = new List<PageLayoutChangeTarget>();
        foreach (var file in plan.Files)
        {
            var document = await _documents.GetByIdAsync(file.DocumentId);
            targets.Add(new PageLayoutChangeTarget(document!.CurrentPath, file.ExpectedFingerprint, file.SelectedChangeIds));
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
