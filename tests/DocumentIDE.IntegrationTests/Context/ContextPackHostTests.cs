using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Search;
using DocumentIDE.Desktop.WebView;
using DocumentIDE.Infrastructure.Persistence;
using DocumentIDE.Infrastructure.Persistence.Repositories;
using DocumentIDE.Infrastructure.Search;

namespace DocumentIDE.IntegrationTests.Context;

/// <summary>
/// UI-05 A — the shipped desktop host must serve <c>POST /api/context/pack</c>
/// over an explicit ordered document list, behind the privileged guard.
///
/// The route is read-only. Markdown contributes its real source; DOCX
/// contributes only the text the DOCX parser already indexed
/// (<c>SearchableBody</c>), explicitly labelled as extracted text. A DOCX with
/// no indexed text is a typed refusal, never a silently empty item. Every cap
/// (per document, whole pack, token budget) is a typed refusal.
/// </summary>
public sealed class ContextPackHostTests : IDisposable
{
    private const string AllowedOrigin = "https://appassets.html2sav";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ctx-pack-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task TwoMarkdownDocuments_ReturnTheirRealTextAndARealTokenEstimate()
    {
        const string first = "# Architecture\nALPHA-BODY-TOKEN one two three";
        const string second = "# API\nBETA-BODY-TOKEN four five six seven";
        var db = await SeedAsync();
        await SeedMarkdownAsync(db, "md-a", "architecture.md", first);
        await SeedMarkdownAsync(db, "md-b", "api.md", second);
        await using var host = await WebAppHost.StartAsync(database: db);
        using var client = AuthorizedClient(host);

        using var response = await client.PostAsJsonAsync("/api/context/pack", new
        {
            documentIds = new[] { "md-a", "md-b" },
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Ok", json.RootElement.GetProperty("status").GetString());

        var documents = json.RootElement.GetProperty("documents").EnumerateArray().ToList();
        Assert.Equal(2, documents.Count);
        Assert.Equal("md-a", documents[0].GetProperty("documentId").GetString());
        Assert.Equal(first, documents[0].GetProperty("content").GetString());
        Assert.Equal("markdown-source", documents[0].GetProperty("textSource").GetString());
        Assert.Equal("md-b", documents[1].GetProperty("documentId").GetString());
        Assert.Equal(second, documents[1].GetProperty("content").GetString());

        var markdown = json.RootElement.GetProperty("markdown").GetString()!;
        Assert.Equal(first + "\n\n" + second, markdown);

        // The estimate is the ContextExportService estimate over the same text —
        // never a fabricated number.
        var estimate = json.RootElement.GetProperty("tokenEstimate");
        var expectedTokens =
            Math.Max(1, (int)Math.Ceiling(first.Length / 4.0)) +
            Math.Max(1, (int)Math.Ceiling(second.Length / 4.0));
        Assert.Equal(expectedTokens, estimate.GetProperty("totalEstimatedTokens").GetInt32());
        Assert.Equal(first.Length + second.Length, estimate.GetProperty("totalCharacterCount").GetInt32());
        Assert.Contains("Generic Tokenizer", estimate.GetProperty("modelLabel").GetString());
        Assert.False(estimate.GetProperty("exceedsBudget").GetBoolean());
    }

    [Fact]
    public async Task DocxItem_ContributesItsIndexedText_AndIsLabelledAsExtracted()
    {
        const string indexedBody = "EXTRACTED-DOCX-BODY-TOKEN from the indexed parser output.";
        var db = await SeedAsync();
        var path = await SeedDocxAsync(db, "docx-a", "Report.docx");
        await new FtsIndex(db).IndexDocumentAsync(new FtsIndexEntry("docx-a", "Report.docx", indexedBody));
        await using var host = await WebAppHost.StartAsync(database: db);
        using var client = AuthorizedClient(host);

        using var response = await client.PostAsJsonAsync("/api/context/pack", new
        {
            documentIds = new[] { "docx-a" },
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var document = Assert.Single(json.RootElement.GetProperty("documents").EnumerateArray());
        Assert.Equal("docx-a", document.GetProperty("documentId").GetString());
        Assert.Equal("extracted-docx-text", document.GetProperty("textSource").GetString());
        Assert.Contains("Extracted DOCX text", document.GetProperty("contentLabel").GetString());

        var content = document.GetProperty("content").GetString()!;
        Assert.Contains(indexedBody, content);
        // The label is part of the emitted Markdown, so the copy cannot pass the
        // extracted index off as the document's own bytes.
        Assert.Contains("Extracted DOCX text", content);
        Assert.DoesNotContain(path, content, StringComparison.OrdinalIgnoreCase);

        var markdown = json.RootElement.GetProperty("markdown").GetString()!;
        Assert.Contains(indexedBody, markdown);
        Assert.Contains("Extracted DOCX text", markdown);
    }

    [Fact]
    public async Task DocxWithoutIndexedText_IsTypedUnavailable_NotEmpty()
    {
        var db = await SeedAsync();
        await SeedDocxAsync(db, "docx-empty", "Empty.docx");
        await using var host = await WebAppHost.StartAsync(database: db);
        using var client = AuthorizedClient(host);

        using var response = await client.PostAsJsonAsync("/api/context/pack", new
        {
            documentIds = new[] { "docx-empty" },
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("NoTextAvailable", json.RootElement.GetProperty("errorCode").GetString());
        Assert.Equal("docx-empty", json.RootElement.GetProperty("documentId").GetString());
        Assert.False(json.RootElement.TryGetProperty("documents", out _));
    }

    [Fact]
    public async Task UnknownDocumentId_IsTyped()
    {
        var db = await SeedAsync();
        await using var host = await WebAppHost.StartAsync(database: db);
        using var client = AuthorizedClient(host);

        using var response = await client.PostAsJsonAsync("/api/context/pack", new
        {
            documentIds = new[] { "does-not-exist" },
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("UnknownDocument", json.RootElement.GetProperty("errorCode").GetString());
        Assert.Equal("does-not-exist", json.RootElement.GetProperty("documentId").GetString());
    }

    [Fact]
    public async Task EmptySelection_IsTyped()
    {
        var db = await SeedAsync();
        await using var host = await WebAppHost.StartAsync(database: db);
        using var client = AuthorizedClient(host);

        using var response = await client.PostAsJsonAsync("/api/context/pack", new
        {
            documentIds = Array.Empty<string>(),
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("EmptySelection", json.RootElement.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task OverPerDocumentCap_IsTyped()
    {
        var db = await SeedAsync();
        await SeedMarkdownAsync(db, "md-big", "big.md", new string('x', 140 * 1024));
        await using var host = await WebAppHost.StartAsync(database: db);
        using var client = AuthorizedClient(host);

        using var response = await client.PostAsJsonAsync("/api/context/pack", new
        {
            documentIds = new[] { "md-big" },
        });

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("DocumentTooLarge", json.RootElement.GetProperty("errorCode").GetString());
        Assert.Equal("md-big", json.RootElement.GetProperty("documentId").GetString());
    }

    [Fact]
    public async Task OverTotalCap_IsTyped()
    {
        var db = await SeedAsync();
        var ids = new List<string>();
        for (var index = 0; index < 5; index++)
        {
            var id = $"md-pack-{index}";
            ids.Add(id);
            await SeedMarkdownAsync(db, id, $"pack-{index}.md", new string((char)('a' + index), 120 * 1024));
        }

        await using var host = await WebAppHost.StartAsync(database: db);
        using var client = AuthorizedClient(host);

        using var response = await client.PostAsJsonAsync("/api/context/pack", new
        {
            documentIds = ids,
        });

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("PackTooLarge", json.RootElement.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task OverTokenBudget_IsTyped()
    {
        var db = await SeedAsync();
        await SeedMarkdownAsync(db, "md-a", "architecture.md", new string('x', 400));
        await using var host = await WebAppHost.StartAsync(database: db);
        using var client = AuthorizedClient(host);

        using var response = await client.PostAsJsonAsync("/api/context/pack", new
        {
            documentIds = new[] { "md-a" },
            tokenBudget = 1,
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("BudgetExceeded", json.RootElement.GetProperty("errorCode").GetString());
        Assert.False(json.RootElement.TryGetProperty("markdown", out _));
    }

    [Fact]
    public async Task PackRoute_NeverMutatesTheSourceFiles()
    {
        const string body = "# Keep me\nread-only context";
        var db = await SeedAsync();
        var path = await SeedMarkdownAsync(db, "md-a", "keep.md", body);
        var hashBefore = ComputeSha256(path);
        await using var host = await WebAppHost.StartAsync(database: db);
        using var client = AuthorizedClient(host);

        using var response = await client.PostAsJsonAsync("/api/context/pack", new
        {
            documentIds = new[] { "md-a" },
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(hashBefore, ComputeSha256(path));
        Assert.Equal(body, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task Guards_401WithoutToken_403HostileOrigin_403HostileHost()
    {
        var db = await SeedAsync();
        await SeedMarkdownAsync(db, "md-a", "architecture.md", "# Guarded");
        await using var host = await WebAppHost.StartAsync(database: db);

        using (var unauthorized = Client(host, token: null, AllowedOrigin))
        using (var response = await unauthorized.PostAsJsonAsync("/api/context/pack", new { documentIds = new[] { "md-a" } }))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        using (var hostileOrigin = Client(host, host.SessionToken, "http://malicious.example"))
        using (var response = await hostileOrigin.PostAsJsonAsync("/api/context/pack", new { documentIds = new[] { "md-a" } }))
        {
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        using (var hostileHost = Client(host, host.SessionToken, AllowedOrigin))
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/context/pack")
            {
                Content = JsonContent.Create(new { documentIds = new[] { "md-a" } }),
            };
            request.Headers.Host = "attacker.example";
            using var response = await hostileHost.SendAsync(request);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    private async Task<AppDb> SeedAsync()
    {
        Directory.CreateDirectory(_root);
        var db = new AppDb(Path.Combine(_root, "state.db"));
        await db.InitializeAsync();
        await new WorkspaceRepository(db).CreateAsync(new Workspace { Id = "ws", Name = "Context pack" });
        await new FolderSourceRepository(db).CreateAsync(new FolderSource
        {
            Id = "folder",
            WorkspaceId = "ws",
            CanonicalPath = _root,
            DisplayName = "Source",
        });
        return db;
    }

    private async Task<string> SeedMarkdownAsync(AppDb db, string id, string fileName, string body)
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, fileName);
        await File.WriteAllTextAsync(path, body, new UTF8Encoding(false));
        await new DocumentRepository(db).CreateAsync(new Document
        {
            Id = id,
            CurrentPath = path,
            Filename = fileName,
            Extension = ".md",
            DocumentType = "markdown",
        });
        return path;
    }

    private async Task<string> SeedDocxAsync(AppDb db, string id, string fileName)
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, fileName);
        // The route must read the index, never the package bytes. A placeholder
        // file proves that: only the indexed body can reach the response.
        await File.WriteAllBytesAsync(path, new byte[] { 1, 2, 3, 4 });
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

    private static HttpClient Client(WebAppHost host, string? token, string origin)
    {
        var client = new HttpClient { BaseAddress = new Uri(host.ApiUrl) };
        if (token != null)
        {
            client.DefaultRequestHeaders.Add("X-DocumentIDE-Session-Token", token);
        }
        client.DefaultRequestHeaders.Add("Origin", origin);
        client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "cross-site");
        return client;
    }

    private static HttpClient AuthorizedClient(WebAppHost host) => Client(host, host.SessionToken, AllowedOrigin);

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha256 = SHA256.Create();
        return Convert.ToHexString(sha256.ComputeHash(stream));
    }
}
