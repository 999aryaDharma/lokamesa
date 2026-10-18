using System.Net;
using System.Text;
using System.Text.Json;
using DocumentIDE.Core.Domain;
using DocumentIDE.Desktop.WebView;
using DocumentIDE.Infrastructure.Persistence;
using DocumentIDE.Infrastructure.Persistence.Repositories;
using DocumentIDE.IntegrationTests.Docx;

namespace DocumentIDE.IntegrationTests.Desktop;

/// <summary>
/// UI-02 — the read-only DOCX preview content route must be served by the shipped
/// desktop host (<c>WebAppHost</c>) behind the FND-02/QA-03 privileged guard.
///
/// Identity is the FS-02 <c>documentId</c>, never a filesystem path: the route never
/// accepts a path as input and never returns one. Lines are the document's own words
/// (real body text from the DOCX parse), bounded so a huge document cannot become an
/// unbounded dump. A non-<c>.docx</c> document stays on its own authority (Markdown
/// keeps <c>GET /api/documents/{id}/markdown</c>).
/// </summary>
public sealed class DocumentPreviewHostTests : IDisposable
{
    private const string AllowedOrigin = "https://appassets.html2sav";
    private const int MaxPayloadBytes = 8 * 1024;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "doc-preview-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task RealDocxDocument_Returns200_WithLinesFromRealContent()
    {
        var db = await SeedAsync();
        await SeedDocxAsync(
            db,
            "docx-doc",
            "letter.docx",
            DocxXml(
                P("Quarterly NUSA-DUA summary", "Heading1"),
                P("REAL-DOCX-CONTENT-TOKEN-42 appears in the body of the letter."),
                P("A second paragraph carries more of the document's own words.")));
        await using var host = await WebAppHost.StartAsync(database: db);
        using var client = AuthorizedClient(host);

        using var response = await client.GetAsync("/api/documents/docx-doc/preview");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("docx-doc", json.RootElement.GetProperty("documentId").GetString());
        Assert.Equal("docx", json.RootElement.GetProperty("kind").GetString());
        Assert.Equal("letter.docx", json.RootElement.GetProperty("title").GetString());

        var lines = json.RootElement.GetProperty("lines").EnumerateArray()
            .Select(entry => entry.GetString()!)
            .ToList();
        Assert.NotEmpty(lines);
        // The lines are the document's real words, never synthesized filler.
        Assert.Contains(lines, line => line.Contains("REAL-DOCX-CONTENT-TOKEN-42"));
        Assert.Contains(lines, line => line.Contains("Quarterly NUSA-DUA summary"));
    }

    [Fact]
    public async Task UnknownDocumentId_Returns404_UnknownDocument()
    {
        var db = await SeedAsync();
        await using var host = await WebAppHost.StartAsync(database: db);
        using var client = AuthorizedClient(host);

        using var response = await client.GetAsync("/api/documents/does-not-exist/preview");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("UnknownDocument", json.RootElement.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task MarkdownDocument_Returns409_NotPreviewableDocument()
    {
        var db = await SeedAsync();
        await SeedMarkdownAsync(db, "md-doc", "notes.md", "# Notes\nbody");
        await using var host = await WebAppHost.StartAsync(database: db);
        using var client = AuthorizedClient(host);

        using var response = await client.GetAsync("/api/documents/md-doc/preview");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("NotPreviewableDocument", json.RootElement.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task DocxMissingOnDisk_Returns409_DocumentUnavailable()
    {
        var db = await SeedAsync();
        Directory.CreateDirectory(_root);
        var missingPath = Path.Combine(_root, "gone.docx");
        await new DocumentRepository(db).CreateAsync(new Document
        {
            Id = "missing-docx",
            CurrentPath = missingPath,
            Filename = "gone.docx",
            Extension = ".docx",
            DocumentType = "docx",
        });
        await using var host = await WebAppHost.StartAsync(database: db);
        using var client = AuthorizedClient(host);

        using var response = await client.GetAsync("/api/documents/missing-docx/preview");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("DocumentUnavailable", json.RootElement.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task Response_NeverContainsFilesystemPath()
    {
        var db = await SeedAsync();
        var seededPath = await SeedDocxAsync(
            db,
            "docx-doc",
            "secret-location.docx",
            DocxXml(P("The body mentions only its own words.")));
        await using var host = await WebAppHost.StartAsync(database: db);
        using var client = AuthorizedClient(host);

        using var response = await client.GetAsync("/api/documents/docx-doc/preview");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(seededPath, body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Path.GetDirectoryName(seededPath)!, body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LongDocument_IsBounded_To60Lines160CharsAnd8KB()
    {
        var db = await SeedAsync();
        var paragraphs = Enumerable.Range(0, 100)
            .Select(index => P($"Line {index:D3} " + new string('x', 300)))
            .ToArray();
        await SeedDocxAsync(db, "big-doc", "big.docx", DocxXml(paragraphs));
        await using var host = await WebAppHost.StartAsync(database: db);
        using var client = AuthorizedClient(host);

        using var response = await client.GetAsync("/api/documents/big-doc/preview");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(body);
        var lines = json.RootElement.GetProperty("lines").EnumerateArray()
            .Select(entry => entry.GetString()!)
            .ToList();

        Assert.InRange(lines.Count, 1, 60);
        Assert.All(lines, line => Assert.True(line.Length <= 160, $"Line exceeded 160 chars: {line.Length}"));
        Assert.True(Encoding.UTF8.GetByteCount(body) <= MaxPayloadBytes, $"Payload was {Encoding.UTF8.GetByteCount(body)} bytes");
    }

    [Fact]
    public async Task PrivilegedGuard_GatesTheRoute_AuthorizedServed_UnauthorizedRejected()
    {
        var db = await SeedAsync();
        await SeedDocxAsync(db, "docx-doc", "guarded.docx", DocxXml(P("Guarded content")));
        await using var host = await WebAppHost.StartAsync(database: db);

        using (var authorized = AuthorizedClient(host))
        using (var ok = await authorized.GetAsync("/api/documents/docx-doc/preview"))
        {
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        }

        using (var unauthorized = UnauthorizedClient(host))
        using (var rejected = await unauthorized.GetAsync("/api/documents/docx-doc/preview"))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
        }
    }

    private async Task<AppDb> SeedAsync()
    {
        Directory.CreateDirectory(_root);
        var db = new AppDb(Path.Combine(_root, "state.db"));
        await db.InitializeAsync();
        await new WorkspaceRepository(db).CreateAsync(new Workspace { Id = "ws", Name = "Preview" });
        await new FolderSourceRepository(db).CreateAsync(new FolderSource { Id = "folder", WorkspaceId = "ws", CanonicalPath = _root, DisplayName = "Source" });
        return db;
    }

    private async Task<string> SeedDocxAsync(AppDb db, string id, string fileName, string documentXml)
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, fileName);
        await File.WriteAllBytesAsync(path, DocxTestFixtureBuilder.CreateValidDocxPackage(documentXml: documentXml));
        await new DocumentRepository(db).CreateAsync(new Document
        {
            Id = id,
            CurrentPath = path,
            Filename = fileName,
            Extension = ".docx",
            DocumentType = "docx",
        });
        return path;
    }

    private static async Task SeedMarkdownAsync(AppDb db, string id, string fileName, string body)
    {
        var path = Path.Combine(Path.GetDirectoryName(db.DatabasePath)!, fileName);
        await File.WriteAllTextAsync(path, body);
        await new DocumentRepository(db).CreateAsync(new Document
        {
            Id = id,
            CurrentPath = path,
            Filename = fileName,
            Extension = ".md",
            DocumentType = "markdown",
        });
    }

    private static string DocxXml(params string[] paragraphXml) => $"""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
          <w:body>
            {string.Join("\n", paragraphXml)}
            <w:sectPr/>
          </w:body>
        </w:document>
        """;

    private static string P(string text, string? style = null)
    {
        var properties = style is null ? string.Empty : $"<w:pPr><w:pStyle w:val=\"{style}\"/></w:pPr>";
        return $"<w:p>{properties}<w:r><w:t xml:space=\"preserve\">{text}</w:t></w:r></w:p>";
    }

    private static HttpClient AuthorizedClient(WebAppHost host)
    {
        var client = new HttpClient { BaseAddress = new Uri(host.ApiUrl) };
        client.DefaultRequestHeaders.Add("X-DocumentIDE-Session-Token", host.SessionToken);
        client.DefaultRequestHeaders.Add("Origin", AllowedOrigin);
        client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "cross-site");
        return client;
    }

    private static HttpClient UnauthorizedClient(WebAppHost host)
    {
        var client = new HttpClient { BaseAddress = new Uri(host.ApiUrl) };
        client.DefaultRequestHeaders.Add("Origin", AllowedOrigin);
        client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "cross-site");
        return client;
    }
}
