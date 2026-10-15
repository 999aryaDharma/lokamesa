using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Filesystem;
using DocumentIDE.Infrastructure.Filesystem;
using DocumentIDE.Infrastructure.Persistence;
using DocumentIDE.Infrastructure.Persistence.Repositories;
using Xunit;

namespace DocumentIDE.IntegrationTests.Filesystem;

/// <summary>
/// FS-06 — Missing File &amp; Folder Recovery.
/// Red-first coverage: deleted file shows missing (persisted, metadata kept).
/// </summary>
public class MissingResourceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly AppDb _appDb;
    private readonly FolderSourceRepository _folderRepo;
    private readonly DocumentRepository _documentRepo;
    private readonly WindowsFileIdentityProvider _identityProvider;
    private readonly DocumentIdentityService _identityService;
    private readonly MissingResourceService _missing;
    private readonly string _testWorkspaceId;

    public MissingResourceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"fs06_test_{Guid.NewGuid():N}.db");
        _appDb = new AppDb(_dbPath);
        _appDb.InitializeAsync().GetAwaiter().GetResult();

        _folderRepo = new FolderSourceRepository(_appDb);
        _documentRepo = new DocumentRepository(_appDb);
        _identityProvider = new WindowsFileIdentityProvider();
        _identityService = new DocumentIdentityService(_documentRepo, _identityProvider);
        _missing = new MissingResourceService(_documentRepo, _folderRepo, _identityProvider);
        _testWorkspaceId = Guid.NewGuid().ToString();

        using var conn = _appDb.CreateConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO workspaces (id, name, active_view, created_at, updated_at)
            VALUES (@id, 'Test Workspace', 'canvas', @now, @now);";
        cmd.Parameters.AddWithValue("@id", _testWorkspaceId);
        cmd.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            var p = _dbPath + suffix;
            if (File.Exists(p))
            {
                try { File.Delete(p); } catch { }
            }
        }
    }

    [Fact]
    public async Task Deleted_File_Shows_Missing()
    {
        string tempDir = NewTempDir("fs06_deleted");
        string file = Path.Combine(tempDir, "report.md");
        File.WriteAllText(file, "# Report Content");

        try
        {
            await CreateSourceAsync(tempDir, "DeletedFolder");
            var doc = await _identityService.ResolveDocumentAsync(file);
            string documentId = doc.Id;

            File.Delete(file);

            var probe = await _missing.ProbeDocumentAsync(documentId);

            Assert.Equal(ResourceProbeStatus.Missing, probe.Status);

            // Workspace metadata is persisted, never deleted.
            var preserved = await _documentRepo.GetByIdAsync(documentId);
            Assert.NotNull(preserved);
            Assert.Equal("missing", preserved.AvailabilityStatus);
            Assert.Equal(file, preserved.LastKnownPath ?? preserved.CurrentPath);
            Assert.Equal("# Report Content".Length > 0, preserved.SizeBytes > 0);
        }
        finally
        {
            DeleteDir(tempDir);
        }
    }

    [Fact]
    public async Task Locate_Same_File_Rebinds_Prior_DocumentId()
    {
        string tempDir = NewTempDir("fs06_locate");
        string file = Path.Combine(tempDir, "notes.md");
        File.WriteAllText(file, "# Locate me");
        string moved = Path.Combine(tempDir, "notes-moved.md");

        try
        {
            await CreateSourceAsync(tempDir, "LocateFolder");
            var doc = await _identityService.ResolveDocumentAsync(file);
            string documentId = doc.Id;

            File.Move(file, moved);

            var probe = await _missing.ProbeDocumentAsync(documentId);
            Assert.Equal(ResourceProbeStatus.Missing, probe.Status);

            var locate = await _missing.LocateAsync(documentId, moved);

            Assert.True(locate.Success);
            Assert.Equal(documentId, locate.DocumentId);
            Assert.False(locate.RequiresConfirmation);

            var rebound = await _documentRepo.GetByIdAsync(documentId);
            Assert.NotNull(rebound);
            Assert.Equal("available", rebound.AvailabilityStatus);
            Assert.Equal(Path.GetFullPath(moved), rebound.CurrentPath);
        }
        finally
        {
            DeleteDir(tempDir);
        }
    }

    [Fact]
    public async Task Locate_Restores_Prior_Canvas_State()
    {
        string tempDir = NewTempDir("fs06_canvas");
        string file = Path.Combine(tempDir, "canvas.md");
        File.WriteAllText(file, "# Canvas doc");
        string moved = Path.Combine(tempDir, "canvas-moved.md");

        try
        {
            await CreateSourceAsync(tempDir, "CanvasFolder");
            var doc = await _identityService.ResolveDocumentAsync(file);

            InsertCanvasRow(_testWorkspaceId, doc.Id, canvasX: 123.5, canvasY: 456.25, pinned: true);

            File.Move(file, moved);
            var locate = await _missing.LocateAsync(doc.Id, moved);
            Assert.True(locate.Success);

            var row = ReadCanvasRow(_testWorkspaceId, doc.Id);
            Assert.NotNull(row);
            Assert.Equal(123.5, row.Value.x);
            Assert.Equal(456.25, row.Value.y);
            Assert.True(row.Value.pinned);
        }
        finally
        {
            DeleteDir(tempDir);
        }
    }

    [Fact]
    public async Task Locate_Uncertain_Candidate_Requires_Confirmation()
    {
        string tempDir = NewTempDir("fs06_uncertain");
        string file = Path.Combine(tempDir, "original.md");
        File.WriteAllText(file, "# Original content alpha");
        string other = Path.Combine(tempDir, "totally-different.md");
        File.WriteAllText(other, "# Something else entirely, different size and words");

        try
        {
            await CreateSourceAsync(tempDir, "UncertainFolder");
            var doc = await _identityService.ResolveDocumentAsync(file);

            File.Delete(file);

            var probe = await _missing.ProbeDocumentAsync(doc.Id);
            Assert.Equal(ResourceProbeStatus.Missing, probe.Status);

            var unconfirmed = await _missing.LocateAsync(doc.Id, other, confirmed: false);
            Assert.False(unconfirmed.Success);
            Assert.True(unconfirmed.RequiresConfirmation);

            // Nothing rebound without confirmation.
            var untouched = await _documentRepo.GetByIdAsync(doc.Id);
            Assert.NotNull(untouched);
            Assert.Equal("missing", untouched.AvailabilityStatus);

            var confirmed = await _missing.LocateAsync(doc.Id, other, confirmed: true);
            Assert.True(confirmed.Success);
            Assert.Equal(doc.Id, confirmed.DocumentId);
        }
        finally
        {
            DeleteDir(tempDir);
        }
    }

    [Fact]
    public async Task Remove_Reference_Never_Deletes_Physical_File()
    {
        string tempDir = NewTempDir("fs06_remove");
        string file = Path.Combine(tempDir, "keep.md");
        const string content = "# Do not delete me";
        File.WriteAllText(file, content);
        string hashBefore = ComputeSha256(file);

        try
        {
            await CreateSourceAsync(tempDir, "RemoveFolder");
            var doc = await _identityService.ResolveDocumentAsync(file);

            var result = await _missing.RemoveReferenceAsync(doc.Id);

            Assert.True(result.Success);
            Assert.Null(await _documentRepo.GetByIdAsync(doc.Id));

            // Physical file untouched: still exists, byte-identical.
            Assert.True(File.Exists(file));
            Assert.Equal(hashBefore, ComputeSha256(file));
        }
        finally
        {
            DeleteDir(tempDir);
        }
    }

    [Fact]
    public void Permission_Denied_Is_Distinct_From_Missing()
    {
        Assert.Equal(
            ResourceProbeStatus.PermissionDenied,
            MissingResourceService.ClassifyProbeFailure(new UnauthorizedAccessException("denied")));
        Assert.Equal(
            ResourceProbeStatus.Missing,
            MissingResourceService.ClassifyProbeFailure(new FileNotFoundException("gone")));
        Assert.Equal(
            ResourceProbeStatus.Missing,
            MissingResourceService.ClassifyProbeFailure(new DirectoryNotFoundException("gone")));
        Assert.Equal(
            ResourceProbeStatus.Locked,
            MissingResourceService.ClassifyProbeFailure(new IOException("sharing violation")));
    }

    [Fact]
    public async Task Unavailable_Drive_Preserves_Layout()
    {
        // Simulate a disconnected drive: a source path that does not exist.
        string ghostRoot = Path.Combine(Path.GetTempPath(), $"fs06_ghost_{Guid.NewGuid():N}");
        string ghostFile = Path.Combine(ghostRoot, "on_drive.md");

        var source = new FolderSource
        {
            Id = Guid.NewGuid().ToString(),
            WorkspaceId = _testWorkspaceId,
            CanonicalPath = ghostRoot,
            DisplayName = "GhostDrive",
            AvailabilityStatus = "available"
        };
        await _folderRepo.CreateAsync(source);

        var doc = new Document
        {
            Id = Guid.NewGuid().ToString(),
            CurrentPath = ghostFile,
            Filename = "on_drive.md",
            Extension = ".md",
            DocumentType = "markdown",
            SizeBytes = 42,
            ContentFingerprint = "abc123",
            AvailabilityStatus = "available"
        };
        await _documentRepo.CreateAsync(doc);
        InsertCanvasRow(_testWorkspaceId, doc.Id, canvasX: 10, canvasY: 20, pinned: false);

        var probe = await _missing.ProbeSourceAsync(_testWorkspaceId, source.Id);

        Assert.True(probe.Status is SourceProbeStatus.UnavailableDrive or SourceProbeStatus.MissingPath);

        // Layout preserved: source row, document row, and canvas row all survive.
        var sources = await _folderRepo.GetByWorkspaceAsync(_testWorkspaceId);
        Assert.Contains(sources, s => s.Id == source.Id);
        var preservedDoc = await _documentRepo.GetByIdAsync(doc.Id);
        Assert.NotNull(preservedDoc);
        Assert.Equal(ghostFile, preservedDoc.LastKnownPath ?? preservedDoc.CurrentPath);
        Assert.NotNull(ReadCanvasRow(_testWorkspaceId, doc.Id));
    }

    private async Task CreateSourceAsync(string path, string displayName)
    {
        var source = new FolderSource
        {
            Id = Guid.NewGuid().ToString(),
            WorkspaceId = _testWorkspaceId,
            CanonicalPath = Path.GetFullPath(path),
            DisplayName = displayName,
            AvailabilityStatus = "available"
        };
        await _folderRepo.CreateAsync(source);
    }

    private void InsertCanvasRow(string workspaceId, string documentId, double canvasX, double canvasY, bool pinned)
    {
        using var conn = _appDb.CreateConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO workspace_documents (workspace_id, document_id, canvas_x, canvas_y, position_mode, pinned, created_at)
            VALUES (@ws, @doc, @x, @y, 'manual', @pinned, @now);";
        cmd.Parameters.AddWithValue("@ws", workspaceId);
        cmd.Parameters.AddWithValue("@doc", documentId);
        cmd.Parameters.AddWithValue("@x", canvasX);
        cmd.Parameters.AddWithValue("@y", canvasY);
        cmd.Parameters.AddWithValue("@pinned", pinned ? 1 : 0);
        cmd.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    private (double x, double y, bool pinned)? ReadCanvasRow(string workspaceId, string documentId)
    {
        using var conn = _appDb.CreateConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT canvas_x, canvas_y, pinned FROM workspace_documents WHERE workspace_id = @ws AND document_id = @doc;";
        cmd.Parameters.AddWithValue("@ws", workspaceId);
        cmd.Parameters.AddWithValue("@doc", documentId);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return (reader.GetDouble(0), reader.GetDouble(1), reader.GetInt32(2) != 0);
    }

    private static string NewTempDir(string prefix)
    {
        string dir = Path.Combine(Path.GetTempPath(), $"{prefix}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void DeleteDir(string dir)
    {
        if (Directory.Exists(dir))
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    private static string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream));
    }
}
