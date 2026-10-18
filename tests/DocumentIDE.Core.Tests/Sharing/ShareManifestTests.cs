using System;
using System.Collections.Generic;
using System.Text.Json;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Domain.Sharing;
using DocumentIDE.Core.Sharing;
using Xunit;

namespace DocumentIDE.Core.Tests.Sharing;

public class ShareManifestTests
{
    [Fact]
    public void Manifest_Payload_Has_No_Physical_Path()
    {
        var share = new Share
        {
            Id = Guid.NewGuid().ToString(),
            Mode = ShareMode.Snapshot,
            AccessMode = AccessMode.SecretToken,
            TokenHash = "abc123hash",
            AllowOriginalDownload = false,
            AllowPdfDownload = false,
            CreatedAt = DateTime.UtcNow.ToString("O")
        };

        var doc1 = new Document
        {
            Id = Guid.NewGuid().ToString(),
            CurrentPath = @"D:\SensitiveUserFolder\SecretDocs\ProjectSpec.md",
            Filename = "ProjectSpec.md",
            Extension = ".md",
            DocumentType = "markdown",
            SizeBytes = 1024,
            ContentFingerprint = "fingerprint123"
        };

        var member1 = new ShareMember
        {
            Id = Guid.NewGuid().ToString(),
            ShareId = share.Id,
            DocumentId = doc1.Id,
            DisplayPath = "ProjectSpec.md",
            AddedAt = DateTime.UtcNow.ToString("O")
        };
        share.Members.Add(member1);

        var manifest = ShareManifest.Create(share, new[] { doc1 });
        var json = JsonSerializer.Serialize(manifest);

        // Physical path "D:\SensitiveUserFolder\SecretDocs\ProjectSpec.md" or "D:" must NOT appear anywhere in the manifest JSON
        Assert.DoesNotContain(@"D:\SensitiveUserFolder", json);
        Assert.DoesNotContain(@"D:\", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(@"C:\", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(@"/home/", json, StringComparison.OrdinalIgnoreCase);

        Assert.Single(manifest.Documents);
        Assert.Equal(doc1.Id, manifest.Documents[0].DocumentId);
        Assert.Equal("ProjectSpec.md", manifest.Documents[0].DisplayName);
    }

    [Fact]
    public void Manifest_Contains_Only_Explicit_Member_IDs()
    {
        var share = new Share
        {
            Id = Guid.NewGuid().ToString(),
            Mode = ShareMode.Live,
            AccessMode = AccessMode.Public
        };

        var docInShare = new Document
        {
            Id = "doc-explicit-1",
            CurrentPath = @"D:\Workspace\doc1.md",
            Filename = "doc1.md"
        };
        var docOutsideShare = new Document
        {
            Id = "doc-implicit-2",
            CurrentPath = @"D:\Workspace\doc2.md",
            Filename = "doc2.md"
        };

        share.Members.Add(new ShareMember
        {
            Id = Guid.NewGuid().ToString(),
            ShareId = share.Id,
            DocumentId = docInShare.Id,
            DisplayPath = "doc1.md"
        });

        // Pass both docInShare and docOutsideShare to generator
        var manifest = ShareManifest.Create(share, new[] { docInShare, docOutsideShare });

        Assert.Single(manifest.Documents);
        Assert.Equal("doc-explicit-1", manifest.Documents[0].DocumentId);
    }

    [Fact]
    public void New_Collection_Member_Does_Not_Auto_Enter_Existing_Share()
    {
        var share = new Share
        {
            Id = Guid.NewGuid().ToString(),
            Mode = ShareMode.Live,
            AccessMode = AccessMode.SecretToken
        };

        var initialDoc = new Document
        {
            Id = "initial-doc-id",
            CurrentPath = @"C:\Folder\doc1.md",
            Filename = "doc1.md"
        };

        share.Members.Add(new ShareMember
        {
            ShareId = share.Id,
            DocumentId = initialDoc.Id,
            DisplayPath = "doc1.md"
        });

        // Workspace or collection has a new document added later
        var newCollectionDoc = new Document
        {
            Id = "newly-added-doc-id",
            CurrentPath = @"C:\Folder\doc2.md",
            Filename = "doc2.md"
        };

        // All workspace docs currently in collection
        var allCollectionDocs = new[] { initialDoc, newCollectionDoc };

        var manifest = ShareManifest.Create(share, allCollectionDocs);

        // Only initialDoc should be present in the manifest
        Assert.Single(manifest.Documents);
        Assert.Equal("initial-doc-id", manifest.Documents[0].DocumentId);
    }

    [Fact]
    public void Live_Mode_Does_Not_Imply_Live_Membership()
    {
        var share = new Share
        {
            Id = Guid.NewGuid().ToString(),
            Mode = ShareMode.Live
        };

        var doc1 = new Document { Id = "member-1", CurrentPath = @"D:\docs\a.md", Filename = "a.md" };
        var doc2 = new Document { Id = "non-member-2", CurrentPath = @"D:\docs\b.md", Filename = "b.md" };

        share.Members.Add(new ShareMember { ShareId = share.Id, DocumentId = doc1.Id, DisplayPath = "a.md" });

        var manifest = ShareManifest.Create(share, new[] { doc1, doc2 });

        Assert.Single(manifest.Documents);
        Assert.Equal("member-1", manifest.Documents[0].DocumentId);
    }

    [Fact]
    public void Manifest_Sanitizes_Extension_When_Extension_Contains_Path()
    {
        var share = new Share { Id = Guid.NewGuid().ToString() };
        var doc = new Document
        {
            Id = "doc-ext-test",
            CurrentPath = @"D:\secret\file.txt",
            Filename = "file.txt",
            Extension = @"/home/user/vault/passwords.txt"
        };
        share.Members.Add(new ShareMember { ShareId = share.Id, DocumentId = doc.Id, DisplayPath = "file.txt" });

        var manifest = ShareManifest.Create(share, new[] { doc });

        Assert.Single(manifest.Documents);
        Assert.Equal(".txt", manifest.Documents[0].Extension);
        Assert.DoesNotContain("/home/user", JsonSerializer.Serialize(manifest));
    }

    [Fact]
    public void Manifest_Handles_Duplicate_Member_IDs_Without_Throwing()
    {
        var share = new Share { Id = Guid.NewGuid().ToString() };
        var doc = new Document { Id = "doc-dup", Filename = "dup.md" };

        share.Members.Add(new ShareMember { ShareId = share.Id, DocumentId = doc.Id, DisplayPath = "dup1.md" });
        share.Members.Add(new ShareMember { ShareId = share.Id, DocumentId = doc.Id, DisplayPath = "dup2.md" });

        var manifest = ShareManifest.Create(share, new[] { doc });

        Assert.Single(manifest.Documents);
        Assert.Equal("dup1.md", manifest.Documents[0].DisplayName);
    }

    [Theory]
    [InlineData(@"\\server\share\secret.txt", "secret.txt")]
    [InlineData(@"\\server\share", "fallback.md")]
    [InlineData(@"\\server\share\", "fallback.md")]
    [InlineData(@"\\?\C:\secret.txt", "secret.txt")]
    [InlineData(@"\\.\pipe\my-pipe", "my-pipe")]
    public void Manifest_Sanitizes_UNC_Paths(string inputPath, string expectedDisplayName)
    {
        var share = new Share { Id = Guid.NewGuid().ToString() };
        var doc = new Document { Id = "doc-unc", Filename = "fallback.md" };
        share.Members.Add(new ShareMember { ShareId = share.Id, DocumentId = doc.Id, DisplayPath = inputPath });

        var manifest = ShareManifest.Create(share, new[] { doc });

        Assert.Single(manifest.Documents);
        Assert.Equal(expectedDisplayName, manifest.Documents[0].DisplayName);
        Assert.DoesNotContain(@"\", manifest.Documents[0].DisplayName);
        Assert.DoesNotContain(@"/", manifest.Documents[0].DisplayName);
    }

    [Theory]
    [InlineData(@"C:\Windows\System32\cmd.exe", "cmd.exe")]
    [InlineData(@"D:file.txt", "file.txt")]
    [InlineData(@"C:", "fallback.md")]
    [InlineData(@"C:\", "fallback.md")]
    [InlineData(@"C:..\..\secret.txt", "secret.txt")]
    public void Manifest_Sanitizes_Drive_Letters(string inputPath, string expectedDisplayName)
    {
        var share = new Share { Id = Guid.NewGuid().ToString() };
        var doc = new Document { Id = "doc-drive", Filename = "fallback.md" };
        share.Members.Add(new ShareMember { ShareId = share.Id, DocumentId = doc.Id, DisplayPath = inputPath });

        var manifest = ShareManifest.Create(share, new[] { doc });

        Assert.Single(manifest.Documents);
        Assert.Equal(expectedDisplayName, manifest.Documents[0].DisplayName);
        Assert.DoesNotContain(":", manifest.Documents[0].DisplayName);
    }

    [Theory]
    [InlineData(@"..\..\..\etc\passwd", "passwd")]
    [InlineData(@"././foo.md", "foo.md")]
    [InlineData(@"..", "fallback.md")]
    [InlineData(@".", "fallback.md")]
    [InlineData(@"dir/..", "fallback.md")]
    [InlineData(@"....\....\secret.txt", "secret.txt")]
    public void Manifest_Sanitizes_Path_Traversals(string inputPath, string expectedDisplayName)
    {
        var share = new Share { Id = Guid.NewGuid().ToString() };
        var doc = new Document { Id = "doc-traverse", Filename = "fallback.md" };
        share.Members.Add(new ShareMember { ShareId = share.Id, DocumentId = doc.Id, DisplayPath = inputPath });

        var manifest = ShareManifest.Create(share, new[] { doc });

        Assert.Single(manifest.Documents);
        Assert.Equal(expectedDisplayName, manifest.Documents[0].DisplayName);
        Assert.DoesNotContain("/", manifest.Documents[0].DisplayName);
        Assert.DoesNotContain(@"\", manifest.Documents[0].DisplayName);
    }

    [Theory]
    [InlineData(@"  C:\path\file.txt  ", "file.txt")]
    [InlineData(@"   ", "fallback.md")]
    [InlineData(@"  ..  ", "fallback.md")]
    [InlineData(@"   file.txt   ", "file.txt")]
    [InlineData(@"file.txt   ", "file.txt")]
    [InlineData(@"  dir / file.txt  ", "file.txt")]
    public void Manifest_Sanitizes_Whitespace_Padding(string inputPath, string expectedDisplayName)
    {
        var share = new Share { Id = Guid.NewGuid().ToString() };
        var doc = new Document { Id = "doc-space", Filename = "fallback.md" };
        share.Members.Add(new ShareMember { ShareId = share.Id, DocumentId = doc.Id, DisplayPath = inputPath });

        var manifest = ShareManifest.Create(share, new[] { doc });

        Assert.Single(manifest.Documents);
        Assert.Equal(expectedDisplayName, manifest.Documents[0].DisplayName);
    }

    [Theory]
    [InlineData(@"C:/Windows/foo\bar.txt", "bar.txt")]
    [InlineData(@"foo\bar/baz\test.md", "test.md")]
    public void Manifest_Sanitizes_Mixed_Slashes(string inputPath, string expectedDisplayName)
    {
        var share = new Share { Id = Guid.NewGuid().ToString() };
        var doc = new Document { Id = "doc-slash", Filename = "fallback.md" };
        share.Members.Add(new ShareMember { ShareId = share.Id, DocumentId = doc.Id, DisplayPath = inputPath });

        var manifest = ShareManifest.Create(share, new[] { doc });

        Assert.Single(manifest.Documents);
        Assert.Equal(expectedDisplayName, manifest.Documents[0].DisplayName);
    }

    [Theory]
    [InlineData(@"foo.md\bar.txt", "bar.txt", ".txt")]
    [InlineData(@"C:\path\ext.docx", "ext.docx", ".docx")]
    public void Manifest_Sanitizes_Extension_Injection(string inputPath, string expectedDisplayName, string expectedExt)
    {
        var share = new Share { Id = Guid.NewGuid().ToString() };
        var doc = new Document { Id = "doc-ext-inj", Filename = "fallback.md" };
        share.Members.Add(new ShareMember { ShareId = share.Id, DocumentId = doc.Id, DisplayPath = inputPath });

        var manifest = ShareManifest.Create(share, new[] { doc });

        Assert.Single(manifest.Documents);
        Assert.Equal(expectedDisplayName, manifest.Documents[0].DisplayName);
        Assert.Equal(expectedExt, manifest.Documents[0].Extension);
    }

    [Fact]
    public void Manifest_Handles_Null_Member_DocumentId_Or_Null_Member_Gracefully()
    {
        var share = new Share { Id = Guid.NewGuid().ToString() };
        var doc = new Document { Id = "valid-doc-id", Filename = "doc.md" };

        share.Members.Add(null!);
        share.Members.Add(new ShareMember { ShareId = share.Id, DocumentId = null!, DisplayPath = "bad1.md" });
        share.Members.Add(new ShareMember { ShareId = share.Id, DocumentId = "   ", DisplayPath = "bad2.md" });
        share.Members.Add(new ShareMember { ShareId = share.Id, DocumentId = doc.Id, DisplayPath = "doc.md" });

        var manifest = ShareManifest.Create(share, new[] { doc });

        Assert.NotNull(manifest);
        Assert.Single(manifest.Documents);
        Assert.Equal(doc.Id, manifest.Documents[0].DocumentId);
    }

    [Fact]
    public void SnapshotMembersFromCollection_Freezes_Membership_AtCreation()
    {
        var doc1 = new Document { Id = "doc-col-1", CurrentPath = @"D:\Collection\Doc1.md", Filename = "Doc1.md" };
        var doc2 = new Document { Id = "doc-col-2", CurrentPath = @"D:\Collection\Doc2.md", Filename = "Doc2.md" };

        var initialCollectionDocs = new[] { doc1, doc2 };

        var share = new Share
        {
            Id = Guid.NewGuid().ToString(),
            Mode = ShareMode.Live
        };

        // Snapshot explicit membership at creation time from collection
        share.SnapshotMembersFromCollection(initialCollectionDocs);

        Assert.Equal(2, share.Members.Count);
        Assert.Contains(share.Members, m => m.DocumentId == "doc-col-1");
        Assert.Contains(share.Members, m => m.DocumentId == "doc-col-2");

        // Later, a new document doc3 enters the collection
        var doc3 = new Document { Id = "doc-col-3", CurrentPath = @"D:\Collection\Doc3.md", Filename = "Doc3.md" };
        var updatedCollectionDocs = new[] { doc1, doc2, doc3 };

        // Manifest generated for this share must NOT include doc3
        var manifest = ShareManifest.Create(share, updatedCollectionDocs);

        Assert.Equal(2, manifest.Documents.Count);
        Assert.DoesNotContain(manifest.Documents, d => d.DocumentId == "doc-col-3");
    }
}



