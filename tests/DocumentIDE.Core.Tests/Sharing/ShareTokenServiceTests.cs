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

    [Fact]
    public void EnsureStorageDirectoryCreated_CreatesApplicationOwnedShareRoot()
    {
        var tempBase = Path.Combine(Path.GetTempPath(), $"docide_shares_test_{Guid.NewGuid():N}");
        try
        {
            var service = new ShareTokenService(tempBase);
            var rootDir = service.EnsureStorageDirectoryCreated();

            Assert.True(Directory.Exists(rootDir));
            Assert.Equal(Path.GetFullPath(tempBase), Path.GetFullPath(rootDir));
        }
        finally
        {
            if (Directory.Exists(tempBase))
            {
                try { Directory.Delete(tempBase, true); } catch { }
            }
        }
    }

    [Fact]
    public void EnsureShareRootExists_CreatesSpecificShareFolder()
    {
        var tempBase = Path.Combine(Path.GetTempPath(), $"docide_shares_test_{Guid.NewGuid():N}");
        var shareId = "test-share-folder-001";
        try
        {
            var service = new ShareTokenService(tempBase);
            var shareFolder = service.EnsureShareRootExists(shareId);

            Assert.True(Directory.Exists(shareFolder));
            Assert.Equal(Path.GetFullPath(Path.Combine(tempBase, shareId)), Path.GetFullPath(shareFolder));
        }
        finally
        {
            if (Directory.Exists(tempBase))
            {
                try { Directory.Delete(tempBase, true); } catch { }
            }
        }
    }

    // B2/B3: the share package carries the token hash + expiry so the separate
    // ShareHost process can authenticate requests and enforce stop/revoke
    // without any privileged DB access. Verification is fail-closed.

    [Fact]
    public void WriteAccessPolicy_ThenReadAccessPolicy_RoundTrips()
    {
        var tempBase = NewTempBase();
        try
        {
            var service = new ShareTokenService(tempBase);
            var policy = new ShareAccessPolicy
            {
                ShareId = "share-roundtrip",
                TokenHash = service.ComputeHash("token-abc"),
            };

            service.WriteAccessPolicy(policy);
            var read = service.ReadAccessPolicy("share-roundtrip");

            Assert.NotNull(read);
            Assert.Equal("share-roundtrip", read!.ShareId);
            Assert.Equal(policy.TokenHash, read.TokenHash);
            Assert.Null(read.ExpiresAt);
        }
        finally { Cleanup(tempBase); }
    }

    [Fact]
    public void IsAccessGranted_WithMatchingToken_IsTrue()
    {
        var tempBase = NewTempBase();
        try
        {
            var service = new ShareTokenService(tempBase);
            var token = "matching-token-123";
            service.WriteAccessPolicy(new ShareAccessPolicy
            {
                ShareId = "share-ok",
                TokenHash = service.ComputeHash(token),
            });

            Assert.True(service.IsAccessGranted("share-ok", token));
        }
        finally { Cleanup(tempBase); }
    }

    [Fact]
    public void IsAccessGranted_WithoutPolicy_FailsClosed()
    {
        var tempBase = NewTempBase();
        try
        {
            var service = new ShareTokenService(tempBase);
            // No access.json was written for this share: deny even a guessable token.
            Assert.False(service.IsAccessGranted("share-no-policy", "any-token"));
        }
        finally { Cleanup(tempBase); }
    }

    [Fact]
    public void IsAccessGranted_WithWrongOrMissingToken_IsFalse()
    {
        var tempBase = NewTempBase();
        try
        {
            var service = new ShareTokenService(tempBase);
            service.WriteAccessPolicy(new ShareAccessPolicy
            {
                ShareId = "share-wrong",
                TokenHash = service.ComputeHash("the-right-token"),
            });

            Assert.False(service.IsAccessGranted("share-wrong", "the-wrong-token"));
            Assert.False(service.IsAccessGranted("share-wrong", null));
            Assert.False(service.IsAccessGranted("share-wrong", string.Empty));
        }
        finally { Cleanup(tempBase); }
    }

    [Fact]
    public void IsAccessGranted_AfterStop_PastExpiry_IsFalse()
    {
        var tempBase = NewTempBase();
        try
        {
            var service = new ShareTokenService(tempBase);
            var token = "stopped-token";
            service.WriteAccessPolicy(new ShareAccessPolicy
            {
                ShareId = "share-stop",
                TokenHash = service.ComputeHash(token),
                ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1).ToString("O"),
            });

            Assert.False(service.IsAccessGranted("share-stop", token));
        }
        finally { Cleanup(tempBase); }
    }

    [Fact]
    public void IsAccessGranted_AfterRevoke_EmptyHash_IsFalse()
    {
        var tempBase = NewTempBase();
        try
        {
            var service = new ShareTokenService(tempBase);
            service.WriteAccessPolicy(new ShareAccessPolicy
            {
                ShareId = "share-revoke",
                TokenHash = string.Empty,
                ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1).ToString("O"),
            });

            // Even the original secret must not verify once the hash is cleared.
            Assert.False(service.IsAccessGranted("share-revoke", "original-token"));
        }
        finally { Cleanup(tempBase); }
    }

    private static string NewTempBase() =>
        Path.Combine(Path.GetTempPath(), $"docide_shares_test_{Guid.NewGuid():N}");

    private static void Cleanup(string tempBase)
    {
        if (Directory.Exists(tempBase))
        {
            try { Directory.Delete(tempBase, true); } catch { }
        }
    }
}
