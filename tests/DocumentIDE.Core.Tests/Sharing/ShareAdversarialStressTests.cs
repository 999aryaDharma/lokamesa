using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Domain.Sharing;
using DocumentIDE.Core.Sharing;
using Xunit;

namespace DocumentIDE.Core.Tests.Sharing;

public class ShareAdversarialStressTests
{
    private readonly ShareTokenService _tokenService;

    public ShareAdversarialStressTests()
    {
        _tokenService = new ShareTokenService();
    }

    [Theory]
    [InlineData("../../../Windows/System32/calc.exe")]
    [InlineData("..\\..\\..\\Windows\\System32\\cmd.exe")]
    [InlineData("/etc/passwd")]
    [InlineData("/var/log/syslog")]
    [InlineData(@"C:\Windows\System32\drivers\etc\hosts")]
    [InlineData(@"D:\document-ide\secrets.env")]
    [InlineData(@"\\127.0.0.1\c$\Windows\System32")]
    [InlineData(@"..\..\..\..\..\..\..\..\..\..\..\..\Windows\System32\cmd.exe")]
    [InlineData(@"subfolder\..\..\..\secret.txt")]
    [InlineData("sub/../../secret.txt")]
    public void GetArtifactPath_RejectsAllPathTraversalAttempts(string maliciousRelativePath)
    {
        string shareId = "valid-share-id-123";

        Assert.Throws<InvalidOperationException>(() =>
            _tokenService.GetArtifactPath(shareId, maliciousRelativePath));
    }

    [Theory]
    [InlineData("../share-2")]
    [InlineData("..\\share-2")]
    [InlineData("share/123")]
    [InlineData("share\\123")]
    [InlineData("../../etc")]
    [InlineData(@"C:\Windows")]
    public void GetShareRootPath_RejectsInvalidOrTraversableShareIds(string maliciousShareId)
    {
        Assert.Throws<InvalidOperationException>(() =>
            _tokenService.GetShareRootPath(maliciousShareId));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GetShareRootPath_RejectsNullOrWhitespaceShareIds(string? emptyShareId)
    {
        Assert.Throws<ArgumentException>(() =>
            _tokenService.GetShareRootPath(emptyShareId!));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void ShareManifest_NeverExposesPhysicalPaths_UnderAdversarialInputs()
    {
        var share = new Share
        {
            Id = "share-stress-01",
            Mode = ShareMode.Snapshot,
            AccessMode = AccessMode.SecretToken,
            TokenHash = "00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff",
            CreatedAt = DateTime.UtcNow.ToString("O")
        };

        var adversarialDocs = new List<Document>
        {
            new Document
            {
                Id = "doc-1",
                CurrentPath = @"C:\Users\Administrator\Desktop\TopSecret.docx",
                Filename = @"C:\Users\Administrator\Desktop\TopSecret.docx",
                Extension = ".docx",
                DocumentType = "docx",
                SizeBytes = 2048
            },
            new Document
            {
                Id = "doc-2",
                CurrentPath = @"D:\document-ide\secrets\private_key.pem",
                Filename = "private_key.pem",
                Extension = ".pem",
                DocumentType = "markdown",
                SizeBytes = 4096
            },
            new Document
            {
                Id = "doc-3",
                CurrentPath = @"/home/user/vault/passwords.txt",
                Filename = "/home/user/vault/passwords.txt",
                Extension = "/home/user/vault/passwords.txt",
                DocumentType = "markdown",
                SizeBytes = 512
            },
            new Document
            {
                Id = "doc-4",
                CurrentPath = @"\\192.168.1.100\shared\confidential.pdf",
                Filename = @"\\192.168.1.100\shared\confidential.pdf",
                Extension = ".pdf",
                DocumentType = "pdf",
                SizeBytes = 8192
            }
        };

        share.Members.Add(new ShareMember { ShareId = share.Id, DocumentId = "doc-1", DisplayPath = @"C:\Users\Administrator\Desktop\TopSecret.docx" });
        share.Members.Add(new ShareMember { ShareId = share.Id, DocumentId = "doc-2", DisplayPath = @"D:\document-ide\secrets\private_key.pem" });
        share.Members.Add(new ShareMember { ShareId = share.Id, DocumentId = "doc-3", DisplayPath = @"/home/user/vault/passwords.txt" });
        share.Members.Add(new ShareMember { ShareId = share.Id, DocumentId = "doc-4", DisplayPath = @"\\192.168.1.100\shared\confidential.pdf" });

        var manifest = ShareManifest.Create(share, adversarialDocs);
        var json = JsonSerializer.Serialize(manifest);

        // Verification: absolute host drives and directories MUST NOT appear in serialized manifest JSON
        Assert.DoesNotContain(@"C:\Users\", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(@"C:\", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(@"D:\document-ide", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(@"/home/user", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(@"\\192.168.1.100", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(@"CurrentPath", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ShareToken_EntropyAndFormatVerification()
    {
        var tokens = new HashSet<string>();
        for (int i = 0; i < 100; i++)
        {
            var token = _tokenService.GenerateToken();
            Assert.Equal(64, token.Length); // 32 bytes = 64 hex chars
            Assert.Matches("^[0-9a-f]{64}$", token); // strictly lowercase hex
            Assert.True(tokens.Add(token), "Token generator produced a collision!");
        }
    }

    [Fact]
    public void TokenVerification_ConstantTimeHashComparison()
    {
        var token = _tokenService.GenerateToken();
        var hash = _tokenService.ComputeHash(token);

        Assert.True(_tokenService.VerifyToken(token, hash));
        Assert.False(_tokenService.VerifyToken(token + "a", hash));
        Assert.False(_tokenService.VerifyToken(token.Substring(0, token.Length - 1), hash));
        Assert.False(_tokenService.VerifyToken("", hash));
        Assert.False(_tokenService.VerifyToken(token, ""));
        Assert.False(_tokenService.VerifyToken(null!, hash));
        Assert.False(_tokenService.VerifyToken(token, null!));
    }
}
