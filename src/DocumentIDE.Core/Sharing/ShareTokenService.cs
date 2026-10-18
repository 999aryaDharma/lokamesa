using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace DocumentIDE.Core.Sharing;

public class ShareTokenService
{
    private readonly string _sharesBaseDirectory;
    // In-memory map of shareId to token hash for authentication
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _shareTokenHashes = new System.Collections.Concurrent.ConcurrentDictionary<string, string>();

    public ShareTokenService(string? sharesBaseDirectory = null)
    {
        _sharesBaseDirectory = sharesBaseDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DocumentIDE",
            "shares"
        );
    }

    /// <summary>
    /// Generates a 256-bit (32 bytes) cryptographically random high-entropy token string.
    /// </summary>
    public string GenerateToken()
    {
        byte[] bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// Computes SHA-256 hash of secret token returning 64-character lowercase hex string.
    /// </summary>
    public string ComputeHash(string token)
    {
        if (token == null) throw new ArgumentNullException(nameof(token));

        byte[] tokenBytes = Encoding.UTF8.GetBytes(token);
        byte[] hashBytes = SHA256.HashData(tokenBytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>
    /// Verifies provided token against stored SHA-256 hash in constant time using FixedTimeEquals.
    /// </summary>
    public bool VerifyToken(string providedToken, string storedHash)
    {
        if (string.IsNullOrEmpty(providedToken) || string.IsNullOrEmpty(storedHash))
        {
            return false;
        }

        string computedHash = ComputeHash(providedToken);

        byte[] computedBytes = Encoding.UTF8.GetBytes(computedHash);
        byte[] storedBytes = Encoding.UTF8.GetBytes(storedHash);

        return CryptographicOperations.FixedTimeEquals(computedBytes, storedBytes);
    }

    /// <summary>
    /// Returns application-owned share root path (%LOCALAPPDATA%\DocumentIDE\shares\<shareId>).
    /// </summary>
    public string GetShareRootPath(string shareId)
    {
        if (string.IsNullOrWhiteSpace(shareId))
        {
            throw new ArgumentException("Share ID cannot be null or empty", nameof(shareId));
        }

        // Sanitize shareId itself against path traversal
        if (shareId.Contains("..") || shareId.Contains('/') || shareId.Contains('\\'))
        {
            throw new InvalidOperationException("Invalid share ID format");
        }

        string root = Path.GetFullPath(_sharesBaseDirectory);
        string sharePath = Path.GetFullPath(Path.Combine(root, shareId));

        if (!sharePath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Path traversal detected in share root resolution");
        }

        return sharePath;
    }

    /// <summary>
    /// Canonicalizes and resolves artifact path under application-owned share storage root,
    /// enforcing strict path traversal prevention.
    /// </summary>
    public string GetArtifactPath(string shareId, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("Relative path cannot be null or empty", nameof(relativePath));
        }

        string shareRoot = GetShareRootPath(shareId);
        string canonicalRoot = shareRoot.EndsWith(Path.DirectorySeparatorChar.ToString()) 
            ? shareRoot 
            : shareRoot + Path.DirectorySeparatorChar;

        string combinedPath = Path.GetFullPath(Path.Combine(shareRoot, relativePath));

        if (!combinedPath.StartsWith(canonicalRoot, StringComparison.OrdinalIgnoreCase) &&
            !combinedPath.Equals(shareRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Path traversal attempt detected outside share root");
        }

        return combinedPath;
    }

    /// <summary>
    /// Ensures the application-owned share artifact root directory (%LOCALAPPDATA%\DocumentIDE\shares) exists.
    /// </summary>
    public string EnsureStorageDirectoryCreated()
    {
        if (!Directory.Exists(_sharesBaseDirectory))
        {
            Directory.CreateDirectory(_sharesBaseDirectory);
        }
        return _sharesBaseDirectory;
    }

    /// <summary>
    /// Ensures the specific share root directory (%LOCALAPPDATA%\DocumentIDE\shares\<shareId>) exists.
    /// </summary>
    public string EnsureShareRootExists(string shareId)
    {
        string shareRoot = GetShareRootPath(shareId);
        if (!Directory.Exists(shareRoot))
        {
            Directory.CreateDirectory(shareRoot);
        }
        return shareRoot;
    }

        // Generates a token for a specific share and stores its hash.
        public string GenerateTokenForShare(string shareId)
        {
            var token = GenerateToken();
            var hash = ComputeHash(token);
            _shareTokenHashes[shareId] = hash;
            return token;
        }

        // Verifies a token for the given share using stored hash.
        public bool VerifyTokenForShare(string shareId, string token)
        {
            if (string.IsNullOrWhiteSpace(shareId) || string.IsNullOrWhiteSpace(token))
                return false;
            if (!_shareTokenHashes.TryGetValue(shareId, out var storedHash))
                return false;
            return VerifyToken(token, storedHash);
        }
    }
