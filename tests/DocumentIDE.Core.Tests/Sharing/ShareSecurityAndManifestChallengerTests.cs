using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Domain.Sharing;
using DocumentIDE.Core.Sharing;
using Xunit;

namespace DocumentIDE.Core.Tests.Sharing;

public class ShareSecurityAndManifestChallengerTests
{
    private readonly ShareTokenService _tokenService = new ShareTokenService();

    [Theory]
    [InlineData(@"\\server\share\secret.docx")]
    [InlineData(@"\\?\C:\Windows\System32\cmd.exe")]
    [InlineData(@"../../etc/passwd")]
    [InlineData(@"..\..\Windows\System32\config\SAM")]
    [InlineData(@"C:\Users\Admin\Desktop\secrets.txt")]
    [InlineData(@"D:relative\drive\path.md")]
    [InlineData(@"  E:\Whitespace\Path.txt  ")]
    [InlineData(@"  ..\..\relative.txt  ")]
    [InlineData(@"C:\dir:stream")]
    public void SanitizeDisplayPath_Strips_All_Drive_Letters_UNC_And_Traversals(string inputPath)
    {
        var share = new Share
        {
            Id = Guid.NewGuid().ToString(),
            Mode = ShareMode.Snapshot,
            AccessMode = AccessMode.SecretToken
        };

        var doc = new Document
        {
            Id = "doc-1",
            CurrentPath = inputPath,
            Filename = "fallback.md"
        };

        share.Members.Add(new ShareMember
        {
            Id = Guid.NewGuid().ToString(),
            ShareId = share.Id,
            DocumentId = doc.Id,
            DisplayPath = inputPath
        });

        var manifest = ShareManifest.Create(share, new[] { doc });
        var json = JsonSerializer.Serialize(manifest);

        var sanitized = manifest.Documents[0].DisplayName;

        // None of drive letters (C:, D:, E:), UNC prefix (\\), or colons should remain
        Assert.DoesNotContain(":", sanitized);
        Assert.DoesNotContain(@"\\", json);
        Assert.DoesNotContain(@"C:\", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(@"D:\", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(@"E:\", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(@"/home/", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Maintainability", "CA1508:Avoid dead code", Justification = "Adversarial test verification")]
    public void Token_Service_Generates_High_Entropy_256bit_Tokens()
    {
        var tokens = new HashSet<string>();
        for (int i = 0; i < 1000; i++)
        {
            string token = _tokenService.GenerateToken();
            Assert.NotNull(token);
            Assert.Equal(64, token.Length); // 32 bytes = 64 hex characters
            Assert.True(tokens.Add(token), "Token generator produced duplicate token!");
        }
    }

    [Fact]
    public void Token_ComputeHash_Is_Deterministic_SHA256()
    {
        string token = "test_token_1234567890_abcdef";
        string hash1 = _tokenService.ComputeHash(token);
        string hash2 = _tokenService.ComputeHash(token);

        Assert.Equal(64, hash1.Length);
        Assert.Equal(hash1, hash2);

        string differentToken = "test_token_1234567890_abcdeg";
        string hash3 = _tokenService.ComputeHash(differentToken);
        Assert.NotEqual(hash1, hash3);
    }

    [Fact]
    public void Token_VerifyToken_Validates_Correctly_And_Handles_Invalid_Inputs()
    {
        string token = _tokenService.GenerateToken();
        string hash = _tokenService.ComputeHash(token);

        Assert.True(_tokenService.VerifyToken(token, hash));
        Assert.False(_tokenService.VerifyToken("wrong_token", hash));
        Assert.False(_tokenService.VerifyToken(token, "wrong_hash"));
        Assert.False(_tokenService.VerifyToken("", hash));
        Assert.False(_tokenService.VerifyToken(token, ""));
    }

    [Theory]
    [InlineData("../")]
    [InlineData("..\\")]
    [InlineData("share/../123")]
    [InlineData("share\\..\\123")]
    [InlineData("/etc/passwd")]
    [InlineData("C:\\Windows")]
    public void TokenService_GetShareRootPath_Rejects_Traversal_And_Invalid_ShareIds(string invalidShareId)
    {
        Assert.ThrowsAny<Exception>(() => _tokenService.GetShareRootPath(invalidShareId));
    }

    [Theory]
    [InlineData("../secret.txt")]
    [InlineData("..\\secret.txt")]
    [InlineData("subfolder/../../secret.txt")]
    [InlineData(@"subfolder\..\..\secret.txt")]
    [InlineData(@"C:\Windows\System32\cmd.exe")]
    [InlineData(@"/etc/shadow")]
    public void TokenService_GetArtifactPath_Rejects_Path_Traversal(string invalidRelativePath)
    {
        string shareId = "valid-share-id-123";
        Assert.Throws<InvalidOperationException>(() => _tokenService.GetArtifactPath(shareId, invalidRelativePath));
    }

    [Fact]
    public void Manifest_Creation_Filters_Out_Non_Members_Regardless_Of_ShareMode()
    {
        foreach (ShareMode mode in Enum.GetValues(typeof(ShareMode)))
        {
            var share = new Share
            {
                Id = Guid.NewGuid().ToString(),
                Mode = mode
            };

            var docMember = new Document { Id = "doc-member", CurrentPath = @"C:\doc1.md", Filename = "doc1.md" };
            var docNonMember = new Document { Id = "doc-non-member", CurrentPath = @"C:\doc2.md", Filename = "doc2.md" };

            share.Members.Add(new ShareMember
            {
                Id = Guid.NewGuid().ToString(),
                ShareId = share.Id,
                DocumentId = docMember.Id,
                DisplayPath = "doc1.md"
            });

            var manifest = ShareManifest.Create(share, new[] { docMember, docNonMember });

            Assert.Single(manifest.Documents);
            Assert.Equal("doc-member", manifest.Documents[0].DocumentId);
        }
    }
}
