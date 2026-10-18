using DocumentIDE.Core.Docx;

namespace DocumentIDE.GoldenTests;

/// <summary>
/// One strict replacement case for a golden fixture: the exact logical text to
/// replace, the replacement text, and how many logical occurrences the mutator
/// must report (QA-01 strict replacement invariant suite).
/// </summary>
public sealed record StrictReplacementCase(string Target, string Replacement, int ExpectedCount);

/// <summary>
/// A deterministic, programmatically generated valid DOCX fixture plus its
/// expected structural outcomes (spec 06 §26; spec 11 §4; master spec §54).
/// </summary>
public sealed record GoldenFixture(
    string Name,
    string Purpose,
    Func<byte[]> BuildPackage,
    IReadOnlyList<(string Text, DocxStructureOrigin Origin)> ExpectedTexts,
    IReadOnlyList<string> AbsentTexts,
    int ExpectedTables,
    int ExpectedMediaParts,
    int ExpectedSections,
    int ExpectedHeaderParts,
    int ExpectedFooterParts,
    StrictReplacementCase StrictReplacement);

/// <summary>How a hostile fixture is expected to fail, always without source mutation.</summary>
public enum HostileFailureMode
{
    /// <summary>The package preflight rejects the fixture with a typed error code.</summary>
    PreflightRejection,

    /// <summary>The package is structurally acceptable but deep parsing is bounded by the occurrence budget.</summary>
    SearchBudgetExceeded
}

/// <summary>
/// A hostile/resource-exhaustion fixture (spec 06 §28; spec 11 §28) and the
/// typed safe failure it must produce. <see cref="PreflightOptions"/> tightens
/// physical budgets where allocating the default-scale attack is impractical;
/// the deployed defaults themselves are covered by DocxResourceBudgetTests.
/// </summary>
public sealed record HostileFixture(
    string Name,
    string Purpose,
    Func<byte[]> BuildPackage,
    HostileFailureMode FailureMode,
    DocxPreflightErrorCode? ExpectedPreflightError,
    DocxPreflightOptions? PreflightOptions,
    string? SearchTarget,
    int SearchBudget);

/// <summary>
/// The canonical golden DOCX regression corpus. Fixtures are generated
/// programmatically and byte-deterministically at test time; no opaque
/// binaries are committed (see tests/fixtures/docx/README.md).
/// </summary>
public static class GoldenDocxFixtures
{
    public static IReadOnlyList<GoldenFixture> Valid { get; } = new GoldenFixture[]
    {
        new(
            "simple",
            "Minimal single-section document; baseline package shape and body search.",
            GoldenDocxFixtureBuilder.Simple,
            new (string, DocxStructureOrigin)[]
            {
                ("Hello Golden Corpus Document", DocxStructureOrigin.Body),
                ("Second paragraph stays untouched.", DocxStructureOrigin.Body)
            },
            Array.Empty<string>(),
            ExpectedTables: 0,
            ExpectedMediaParts: 0,
            ExpectedSections: 1,
            ExpectedHeaderParts: 0,
            ExpectedFooterParts: 0,
            StrictReplacement: new StrictReplacementCase("Golden Corpus", "Golden Archive", 1)),

        new(
            "split-runs",
            "NUSA-DUA-1 fragmented across differently formatted runs; logical search and strict replacement must cross the run boundary (spec 11 §6).",
            GoldenDocxFixtureBuilder.SplitRuns,
            new (string, DocxStructureOrigin)[]
            {
                ("NUSA-DUA-1", DocxStructureOrigin.Body),
                ("Project", DocxStructureOrigin.Body)
            },
            Array.Empty<string>(),
            ExpectedTables: 0,
            ExpectedMediaParts: 0,
            ExpectedSections: 1,
            ExpectedHeaderParts: 0,
            ExpectedFooterParts: 0,
            StrictReplacement: new StrictReplacementCase("NUSA-DUA-1", "NUSA DUA 1", 1)),

        new(
            "mixed-format",
            "One paragraph mixing bold/italic/underline/color/font/size runs; replacement spans italic + underlined runs while unrelated paragraph formatting must survive.",
            GoldenDocxFixtureBuilder.MixedFormat,
            new (string, DocxStructureOrigin)[]
            {
                ("Quarterly Report Summary", DocxStructureOrigin.Body),
                ("Unrelated paragraph with different formatting.", DocxStructureOrigin.Body)
            },
            Array.Empty<string>(),
            ExpectedTables: 0,
            ExpectedMediaParts: 0,
            ExpectedSections: 1,
            ExpectedHeaderParts: 0,
            ExpectedFooterParts: 0,
            StrictReplacement: new StrictReplacementCase("Report Summary", "Report Overview", 1)),

        new(
            "tables",
            "Table cell text participates in logical matching with Table origin; strict replacement inside a cell must preserve table/cell properties.",
            GoldenDocxFixtureBuilder.Tables,
            new (string, DocxStructureOrigin)[]
            {
                ("Alpha GOLDEN-TARGET cell", DocxStructureOrigin.Table),
                ("Gamma", DocxStructureOrigin.Table),
                ("Body paragraph after table.", DocxStructureOrigin.Body)
            },
            Array.Empty<string>(),
            ExpectedTables: 1,
            ExpectedMediaParts: 0,
            ExpectedSections: 1,
            ExpectedHeaderParts: 0,
            ExpectedFooterParts: 0,
            StrictReplacement: new StrictReplacementCase("GOLDEN-TARGET", "GOLDEN-REPLACED", 1)),

        new(
            "headers",
            "Default + even-page header parts; header text is searchable with Header origin and header mutation touches only the matched header part.",
            GoldenDocxFixtureBuilder.Headers,
            new (string, DocxStructureOrigin)[]
            {
                ("ACME GOLDEN Confidential", DocxStructureOrigin.Header),
                ("Even page header", DocxStructureOrigin.Header),
                ("Main body with no marker.", DocxStructureOrigin.Body)
            },
            Array.Empty<string>(),
            ExpectedTables: 0,
            ExpectedMediaParts: 0,
            ExpectedSections: 1,
            ExpectedHeaderParts: 2,
            ExpectedFooterParts: 0,
            StrictReplacement: new StrictReplacementCase("ACME GOLDEN", "ACME ARCHIVE", 1)),

        new(
            "footers",
            "Default + even-page footer parts; footer text is searchable with Footer origin and footer mutation touches only the matched footer part.",
            GoldenDocxFixtureBuilder.Footers,
            new (string, DocxStructureOrigin)[]
            {
                ("Golden Draft - internal", DocxStructureOrigin.Footer),
                ("Strictly private", DocxStructureOrigin.Footer),
                ("Main body with footer wiring.", DocxStructureOrigin.Body)
            },
            Array.Empty<string>(),
            ExpectedTables: 0,
            ExpectedMediaParts: 0,
            ExpectedSections: 1,
            ExpectedHeaderParts: 0,
            ExpectedFooterParts: 2,
            StrictReplacement: new StrictReplacementCase("Golden Draft", "Archive Draft", 1)),

        new(
            "sections",
            "Two sections with different margins and a landscape orientation change; section properties must survive strict replacement.",
            GoldenDocxFixtureBuilder.Sections,
            new (string, DocxStructureOrigin)[]
            {
                ("Section one content GOLDEN-ONE.", DocxStructureOrigin.Body),
                ("Section two content GOLDEN-TWO.", DocxStructureOrigin.Body)
            },
            Array.Empty<string>(),
            ExpectedTables: 0,
            ExpectedMediaParts: 0,
            ExpectedSections: 2,
            ExpectedHeaderParts: 0,
            ExpectedFooterParts: 0,
            StrictReplacement: new StrictReplacementCase("GOLDEN-TWO", "GOLDEN-SECOND", 1)),

        new(
            "images",
            "Inline drawing with a real PNG media part; the drawing subtree and media bytes must survive strict replacement elsewhere in the part.",
            GoldenDocxFixtureBuilder.Images,
            new (string, DocxStructureOrigin)[]
            {
                ("Figure caption GOLDEN image.", DocxStructureOrigin.Body)
            },
            Array.Empty<string>(),
            ExpectedTables: 0,
            ExpectedMediaParts: 1,
            ExpectedSections: 1,
            ExpectedHeaderParts: 0,
            ExpectedFooterParts: 0,
            StrictReplacement: new StrictReplacementCase("GOLDEN", "ARCHIVE", 1)),

        new(
            "hyperlinks",
            "External hyperlink relationship with styled hyperlink run; link part/relationship must survive strict replacement.",
            GoldenDocxFixtureBuilder.Hyperlinks,
            new (string, DocxStructureOrigin)[]
            {
                ("Example Site", DocxStructureOrigin.Body),
                ("GOLDEN link.", DocxStructureOrigin.Body)
            },
            Array.Empty<string>(),
            ExpectedTables: 0,
            ExpectedMediaParts: 0,
            ExpectedSections: 1,
            ExpectedHeaderParts: 0,
            ExpectedFooterParts: 0,
            StrictReplacement: new StrictReplacementCase("GOLDEN", "ARCHIVE", 1)),

        new(
            "numbering",
            "List paragraphs with numPr referencing a numbering part; numbering definitions and numPr must survive strict replacement.",
            GoldenDocxFixtureBuilder.Numbering,
            new (string, DocxStructureOrigin)[]
            {
                ("Item one GOLDEN.", DocxStructureOrigin.Body),
                ("Item two.", DocxStructureOrigin.Body)
            },
            Array.Empty<string>(),
            ExpectedTables: 0,
            ExpectedMediaParts: 0,
            ExpectedSections: 1,
            ExpectedHeaderParts: 0,
            ExpectedFooterParts: 0,
            StrictReplacement: new StrictReplacementCase("GOLDEN", "ARCHIVE", 1)),

        new(
            "custom-styles",
            "Paragraph/character custom styles (pStyle/rStyle) backed by a styles part; style references must survive strict replacement.",
            GoldenDocxFixtureBuilder.CustomStyles,
            new (string, DocxStructureOrigin)[]
            {
                ("Custom styled GOLDEN heading", DocxStructureOrigin.Body)
            },
            Array.Empty<string>(),
            ExpectedTables: 0,
            ExpectedMediaParts: 0,
            ExpectedSections: 1,
            ExpectedHeaderParts: 0,
            ExpectedFooterParts: 0,
            StrictReplacement: new StrictReplacementCase("GOLDEN", "ARCHIVE", 1)),

        new(
            "fields",
            "PAGE field with instrText instruction and cached result; field-code text must never be searchable/replaceable while result text is (spec 06 §24).",
            GoldenDocxFixtureBuilder.Fields,
            new (string, DocxStructureOrigin)[]
            {
                ("Page 7 of 9 GOLDEN field.", DocxStructureOrigin.Body)
            },
            new[] { "MERGEFORMAT", "PAGE" },
            ExpectedTables: 0,
            ExpectedMediaParts: 0,
            ExpectedSections: 1,
            ExpectedHeaderParts: 0,
            ExpectedFooterParts: 0,
            StrictReplacement: new StrictReplacementCase("GOLDEN", "ARCHIVE", 1)),

        new(
            "tracked-changes",
            "w:ins insertion and w:del deletion revisions; inserted text is searchable, deleted text is not, and both revision subtrees must survive strict replacement (spec 06 §25).",
            GoldenDocxFixtureBuilder.TrackedChanges,
            new (string, DocxStructureOrigin)[]
            {
                ("Final GOLDEN version.", DocxStructureOrigin.Body),
                ("Inserted sentence.", DocxStructureOrigin.Body)
            },
            new[] { "Deleted sentence." },
            ExpectedTables: 0,
            ExpectedMediaParts: 0,
            ExpectedSections: 1,
            ExpectedHeaderParts: 0,
            ExpectedFooterParts: 0,
            StrictReplacement: new StrictReplacementCase("GOLDEN", "ARCHIVE", 1))
    };

    public static IReadOnlyList<HostileFixture> Hostile { get; } = new HostileFixture[]
    {
        new(
            "hostile-entry-explosion",
            "ZIP entry explosion above the default 10,000-entry budget; must be skipped for safety without source mutation.",
            () => GoldenDocxFixtureBuilder.EntryExplosionPackage(10001),
            HostileFailureMode.PreflightRejection,
            DocxPreflightErrorCode.TooManyEntries,
            PreflightOptions: null,
            SearchTarget: null,
            SearchBudget: 0),

        new(
            "hostile-oversized-single-part",
            "Single package part exceeding the per-part uncompressed budget (tightened preflight budget; same code path as the 128 MiB default).",
            () => GoldenDocxFixtureBuilder.OversizedSinglePartPackage(2048),
            HostileFailureMode.PreflightRejection,
            DocxPreflightErrorCode.PartSizeExceeded,
            PreflightOptions: new DocxPreflightOptions { MaxPartUncompressedSizeBytes = 1024 },
            SearchTarget: null,
            SearchBudget: 0),

        new(
            "hostile-oversized-total",
            "Oversized uncompressed declaration across the package (tightened total budget; same code path as the 512 MiB default).",
            () => GoldenDocxFixtureBuilder.OversizedTotalPackage(partCount: 4, bytesPerPart: 1024),
            HostileFailureMode.PreflightRejection,
            DocxPreflightErrorCode.TotalUncompressedSizeExceeded,
            PreflightOptions: new DocxPreflightOptions
            {
                MaxPartUncompressedSizeBytes = 1024,
                MaxTotalUncompressedSizeBytes = 2048
            },
            SearchTarget: null,
            SearchBudget: 0),

        new(
            "hostile-high-compression",
            "Highly compressed XML (~1 MiB of repeated text); preflight accepts it, deep search must stay bounded by the occurrence budget.",
            () => GoldenDocxFixtureBuilder.HighlyCompressedPackage(1_000_000),
            HostileFailureMode.SearchBudgetExceeded,
            ExpectedPreflightError: null,
            PreflightOptions: null,
            SearchTarget: "a",
            SearchBudget: 64),

        new(
            "hostile-parser-budget",
            "Parser/occurrence budget exhaustion: 500 matches against a 50-match review budget must produce the typed OccurrenceBudgetExceeded failure.",
            () => GoldenDocxFixtureBuilder.ParserBudgetPackage(500),
            HostileFailureMode.SearchBudgetExceeded,
            ExpectedPreflightError: null,
            PreflightOptions: null,
            SearchTarget: "GOLDEN",
            SearchBudget: 50),

        new(
            "hostile-dtd-external-entity",
            "DTD/XXE declaration in document.xml; must be prohibited, never resolved.",
            GoldenDocxFixtureBuilder.DtdExternalEntityPackage,
            HostileFailureMode.PreflightRejection,
            DocxPreflightErrorCode.DtdProhibited,
            PreflightOptions: null,
            SearchTarget: null,
            SearchBudget: 0),

        new(
            "hostile-zip-slip",
            "ZIP entry path traversal attempt ('../escape.txt'); must be rejected before any extraction.",
            GoldenDocxFixtureBuilder.ZipSlipPackage,
            HostileFailureMode.PreflightRejection,
            DocxPreflightErrorCode.ZipSlipAttempt,
            PreflightOptions: null,
            SearchTarget: null,
            SearchBudget: 0),

        new(
            "hostile-not-a-zip",
            "Non-ZIP bytes masquerading as a DOCX; must be reported as corrupted, never parsed further.",
            GoldenDocxFixtureBuilder.NotAZipPackage,
            HostileFailureMode.PreflightRejection,
            DocxPreflightErrorCode.InvalidZipArchive,
            PreflightOptions: null,
            SearchTarget: null,
            SearchBudget: 0),

        new(
            "hostile-malformed-xml",
            "Structurally truncated document.xml; must be reported as malformed XML rather than silently tolerated.",
            GoldenDocxFixtureBuilder.MalformedXmlPackage,
            HostileFailureMode.PreflightRejection,
            DocxPreflightErrorCode.XmlMalformed,
            PreflightOptions: null,
            SearchTarget: null,
            SearchBudget: 0)
    };
}
