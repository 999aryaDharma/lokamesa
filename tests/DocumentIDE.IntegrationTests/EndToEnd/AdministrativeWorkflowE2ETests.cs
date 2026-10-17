using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Search;
using DocumentIDE.Desktop.WebView;
using DocumentIDE.Infrastructure.Persistence;
using DocumentIDE.Infrastructure.Persistence.Repositories;
using DocumentIDE.Infrastructure.Search;

namespace DocumentIDE.IntegrationTests.EndToEnd;

/// <summary>
/// G9 — automated equivalent of the testing-strategy §20 end-to-end
/// administrative scenario, driven through the shipped privileged host
/// (<see cref="WebAppHost"/>) behind the FND-02/QA-03 guard:
///
/// create workspace → attach folder → index → search → preview recursive
/// Search &amp; Replace → deselect one occurrence → apply → inspect history →
/// finalize (Word / PDF surfaces) → undo. It also re-proves the §26 release-gate
/// behavior that a plan whose file changed after preview cannot mutate it.
///
/// Word open/save requires a licensed Word-capable runner (WORD-01..03) and the
/// file watcher is covered by its own suites, so those two steps are asserted at
/// the route/validation level here rather than through COM.
/// </summary>
public sealed class AdministrativeWorkflowE2ETests : IDisposable
{
    private const string AllowedOrigin = "https://appassets.html2sav";
    private const string FindToken = "NUSA-DUA-1";
    private const string ReplaceToken = "NUSA-DUA-2";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "admin-e2e-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task AdministrativeScenario_FromWorkspaceThroughApplyHistoryFinalizationAndUndo()
    {
        var db = await SeedWorkspaceAsync();
        await using var host = await WebAppHost.StartAsync(database: db);
        using var client = AuthorizedClient(host);

        var baFolder = Path.Combine(_root, "BA");
        var notesPath = Path.Combine(baFolder, "notes.md");
        var otherPath = Path.Combine(baFolder, "other.md");
        var notesBody = $"{FindToken} alpha {FindToken}";
        var otherBody = $"{FindToken} only";
        Assert.Equal(notesBody, await File.ReadAllTextAsync(notesPath));
        Assert.Equal(otherBody, await File.ReadAllTextAsync(otherPath));

        // 1. Attach the BA folder through the privileged endpoint.
        using (var attach = await client.PostAsJsonAsync("/api/workspaces/ws/folder-sources", new { folderPath = _root }))
        {
            Assert.Equal(HttpStatusCode.OK, attach.StatusCode);
        }

        // 2. Search finds the indexed member.
        using (var search = await client.GetAsync($"/api/search?q={FindToken}"))
        {
            Assert.Equal(HttpStatusCode.OK, search.StatusCode);
            using var json = JsonDocument.Parse(await search.Content.ReadAsStringAsync());
            var ids = json.RootElement.GetProperty("results").EnumerateArray()
                .Select(r => r.GetProperty("documentId").GetString())
                .ToHashSet();
            Assert.Contains("notes", ids);
            Assert.Contains("other", ids);
        }

        // 3. Preview a recursive Search & Replace over the workspace scope.
        var planId = await PreviewAsync(client, new[] { "notes", "other" });

        // 4. Deselect one of the two occurrences in notes.md (keep only the first).
        string firstNotesOccurrence;
        string otherOccurrence;
        using (var preview = await client.GetAsync($"/api/batch-operations/{planId}"))
        {
            using var json = JsonDocument.Parse(await preview.Content.ReadAsStringAsync());
            var files = json.RootElement.GetProperty("files").EnumerateArray().ToList();
            var notesFile = files.Single(f => f.GetProperty("documentId").GetString() == "notes");
            var notesOccurrences = notesFile.GetProperty("occurrences").EnumerateArray().ToList();
            Assert.Equal(2, notesOccurrences.Count);
            firstNotesOccurrence = notesOccurrences[0].GetProperty("id").GetString()!;

            var otherFile = files.Single(f => f.GetProperty("documentId").GetString() == "other");
            otherOccurrence = Assert.Single(otherFile.GetProperty("occurrences").EnumerateArray()).GetProperty("id").GetString()!;
        }

        using (var selection = await client.PutAsJsonAsync($"/api/batch-operations/{planId}/selection", new
        {
            files = new object[]
            {
                new { documentId = "notes", selected = true, selectedOccurrenceIds = new[] { firstNotesOccurrence } },
                new { documentId = "other", selected = true, selectedOccurrenceIds = new[] { otherOccurrence } },
            },
        }))
        {
            Assert.Equal(HttpStatusCode.OK, selection.StatusCode);
        }

        // 5. Apply the reviewed selection.
        using (var apply = await client.PostAsJsonAsync($"/api/batch-operations/{planId}/apply", new { }))
        {
            Assert.Equal(HttpStatusCode.OK, apply.StatusCode);
            using var json = JsonDocument.Parse(await apply.Content.ReadAsStringAsync());
            Assert.Equal("Completed", json.RootElement.GetProperty("status").GetString());
            Assert.All(
                json.RootElement.GetProperty("files").EnumerateArray(),
                file => Assert.Equal("Success", file.GetProperty("outcome").GetString()));
        }

        // Only the selected occurrence of notes.md changed; other.md no longer
        // contains the find token.
        Assert.Equal($"{ReplaceToken} alpha {FindToken}", await File.ReadAllTextAsync(notesPath));
        Assert.DoesNotContain(FindToken, await File.ReadAllTextAsync(otherPath));

        // 6. History records the apply (and its recovery snapshot).
        using (var history = await client.GetAsync($"/api/batch-operations/{planId}/history"))
        {
            Assert.Equal(HttpStatusCode.OK, history.StatusCode);
            using var json = JsonDocument.Parse(await history.Content.ReadAsStringAsync());
            var applyRecords = json.RootElement.GetProperty("apply").EnumerateArray().ToList();
            Assert.Equal(2, applyRecords.Count);
            Assert.All(applyRecords, record => Assert.Equal("Success", record.GetProperty("outcome").GetString()));
            Assert.All(applyRecords, record => Assert.False(string.IsNullOrEmpty(record.GetProperty("snapshotId").GetString())));
        }

        // 7. Word finalization surface is reachable (real open/save needs COM).
        using (var word = await client.PostAsJsonAsync("/api/word/open", new { }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, word.StatusCode);
            using var json = JsonDocument.Parse(await word.Content.ReadAsStringAsync());
            Assert.True(json.RootElement.TryGetProperty("status", out _));
        }

        // 8. PDF finalization surface is reachable and reports a typed status.
        using (var pdf = await client.PostAsJsonAsync("/api/pdf/export", new { sourceDocxPath = Path.Combine(_root, "absent.docx") }))
        {
            Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
            using var json = JsonDocument.Parse(await pdf.Content.ReadAsStringAsync());
            Assert.Equal("SourceNotFound", json.RootElement.GetProperty("status").GetString());
        }

        // 9. Undo the batch and confirm the sources are restored.
        using (var undo = await client.PostAsJsonAsync($"/api/batch-operations/{planId}/undo", new { }))
        {
            Assert.Equal(HttpStatusCode.OK, undo.StatusCode);
            using var json = JsonDocument.Parse(await undo.Content.ReadAsStringAsync());
            Assert.Equal("Completed", json.RootElement.GetProperty("status").GetString());
            Assert.All(
                json.RootElement.GetProperty("files").EnumerateArray(),
                file => Assert.Equal("Restored", file.GetProperty("outcome").GetString()));
        }
        Assert.Equal(notesBody, await File.ReadAllTextAsync(notesPath));
        Assert.Equal(otherBody, await File.ReadAllTextAsync(otherPath));
    }

    [Fact]
    public async Task ReleaseGate_PlanChangedAfterPreview_CannotMutateTheFile()
    {
        var db = await SeedWorkspaceAsync();
        await using var host = await WebAppHost.StartAsync(database: db);
        using var client = AuthorizedClient(host);

        var otherPath = Path.Combine(_root, "BA", "other.md");
        var originalBody = await File.ReadAllTextAsync(otherPath);
        var planId = await PreviewAsync(client, new[] { "other" });

        // The file changes after preview: the reviewed fingerprint no longer matches.
        await File.WriteAllTextAsync(otherPath, "unexpected external edit");

        using var apply = await client.PostAsJsonAsync($"/api/batch-operations/{planId}/apply", new { });
        Assert.Equal(HttpStatusCode.OK, apply.StatusCode);
        using var json = JsonDocument.Parse(await apply.Content.ReadAsStringAsync());
        var file = Assert.Single(json.RootElement.GetProperty("files").EnumerateArray());
        Assert.Equal("Skipped", file.GetProperty("outcome").GetString());
        Assert.Equal("Stale", file.GetProperty("reasonCode").GetString());
        Assert.Equal("unexpected external edit", await File.ReadAllTextAsync(otherPath));
    }

    private async Task<AppDb> SeedWorkspaceAsync()
    {
        var baFolder = Path.Combine(_root, "BA");
        Directory.CreateDirectory(baFolder);
        var db = new AppDb(Path.Combine(_root, "state.db"));
        await db.InitializeAsync();
        await new WorkspaceRepository(db).CreateAsync(new Workspace { Id = "ws", Name = "Administrasi" });
        await new FolderSourceRepository(db).CreateAsync(new FolderSource { Id = "folder", WorkspaceId = "ws", CanonicalPath = _root, DisplayName = "BA" });

        await SeedDocumentAsync(db, "notes", Path.Combine(baFolder, "notes.md"), notesBody: $"{FindToken} alpha {FindToken}");
        await SeedDocumentAsync(db, "other", Path.Combine(baFolder, "other.md"), otherBody: $"{FindToken} only");
        return db;
    }

    private static async Task SeedDocumentAsync(AppDb db, string id, string path, string? notesBody = null, string? otherBody = null)
    {
        var body = notesBody ?? otherBody ?? string.Empty;
        await File.WriteAllTextAsync(path, body);
        await new DocumentRepository(db).CreateAsync(new Document
        {
            Id = id,
            CurrentPath = path,
            Filename = Path.GetFileName(path),
            Extension = ".md",
            DocumentType = "markdown",
        });
        await new FtsIndex(db).IndexDocumentAsync(new FtsIndexEntry(id, Path.GetFileName(path), body));
    }

    private static async Task<string> PreviewAsync(HttpClient client, IReadOnlyList<string> documentIds)
    {
        using var created = await client.PostAsJsonAsync("/api/batch-operations/preview", new
        {
            scope = new
            {
                workspaceId = "ws",
                type = 0,
                documentIds,
                includeSubfolders = true,
                excludedBranches = Array.Empty<string>(),
            },
            find = FindToken,
            replacement = ReplaceToken,
        });
        Assert.Equal(HttpStatusCode.Accepted, created.StatusCode);
        using var creationJson = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var planId = creationJson.RootElement.GetProperty("planId").GetString()!;

        for (var attempt = 0; attempt < 200; attempt++)
        {
            using var response = await client.GetAsync($"/api/batch-operations/{planId}");
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var status = json.RootElement.GetProperty("status").GetString();
            if (status is not ("Pending" or "Running")) return planId;
            await Task.Delay(25);
        }

        throw new Xunit.Sdk.XunitException("Preview did not complete in time.");
    }

    private static HttpClient AuthorizedClient(WebAppHost host)
    {
        var client = new HttpClient { BaseAddress = new Uri(host.ApiUrl) };
        client.DefaultRequestHeaders.Add("X-DocumentIDE-Session-Token", host.SessionToken);
        client.DefaultRequestHeaders.Add("Origin", AllowedOrigin);
        client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "cross-site");
        return client;
    }
}
