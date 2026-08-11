using System;
using System.IO;
using DocumentIDE.Core.Sharing;
using Xunit;

namespace DocumentIDE.Core.Tests.Sharing;

public class ShareTokenServiceTests
{
    private readonly ShareTokenService _tokenService;

    public ShareTokenServiceTests()
    {
        _tokenService = new ShareTokenService();
    }

    [Fact]
    public void GenerateToken_Returns256BitHighEntropyString()
    {
        var token1 = _tokenService.GenerateToken();
        var token2 = _tokenService.GenerateToken();

        Assert.NotNull(token1);
        Assert.NotNull(token2);
        Assert.NotEqual(token1, token2);
        // 256 bits = 32 bytes. In Hex representation, 32 bytes is 64 hex characters.
        // Base64url representation for 32 bytes is 43 characters.
        Assert.True(token1.Length >= 43, $"Token length {token1.Length} should be at least 43 chars for 256 bits");
    }

    [Fact]
    public void ComputeHash_ReturnsSha256HexHash()
    {
        var token = "test-secret-token-1234567890";
        var hash1 = _tokenService.ComputeHash(token);
        var hash2 = _tokenService.ComputeHash(token);

        Assert.Equal(hash1, hash2);
        // SHA-256 hex string is 64 characters long
        Assert.Equal(64, hash1.Length);
    }

    [Fact]
    public void VerifyToken_ConstantTimeComparison_ValidatesCorrectly()
    {
        var token = _tokenService.GenerateToken();
        var hash = _tokenService.ComputeHash(token);

        Assert.True(_tokenService.VerifyToken(token, hash));
        Assert.False(_tokenService.VerifyToken("invalid-token-12345", hash));
    }

    [Fact]
    public void GetShareRootPath_ReturnsPathUnderAppDataShares()
    {
        var shareId = "share-12345";
        var path = _tokenService.GetShareRootPath(shareId);

        var expectedBase = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DocumentIDE", "shares");
        Assert.StartsWith(expectedBase, path, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(shareId, path, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetArtifactPath_PreventsPathTraversal()
    {
        var shareId = "share-safe-123";

        // Normal relative path should succeed
        var safePath = _tokenService.GetArtifactPath(shareId, "subfolder/doc1.html");
        Assert.NotNull(safePath);

        // Path traversal attempt should throw ArgumentException or InvalidOperationException
        Assert.ThrowsAny<Exception>(() => _tokenService.GetArtifactPath(shareId, "../../../Windows/System32/calc.exe"));
        Assert.ThrowsAny<Exception>(() => _tokenService.GetArtifactPath(shareId, @"..\..\..\Secret\passwords.txt"));
        Assert.ThrowsAny<Exception>(() => _tokenService.GetArtifactPath(shareId, @"C:\Windows\System32\calc.exe"));
    }
}
