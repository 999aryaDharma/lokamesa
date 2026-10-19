using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DocumentIDE.Core.Sharing;

public class ShareTokenService
{
    /// <summary>
    /// File name (under the share root) that carries the token hash + expiry for
    /// the separate ShareHost process. Never served by any public route.
    /// </summary>
    public const string AccessFileName = "access.json";

    private static readonly JsonSerializerOptions AccessJsonOptions = new(JsonSerializerDefaults.Web);

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

        /// <summary>Absolute path of the share's <c>access.json</c> policy file.</summary>
        public string GetAccessPolicyPath(string shareId)
        {
            return Path.Combine(GetShareRootPath(shareId), AccessFileName);
        }

        /// <summary>
        /// Atomically writes the share's access policy (temp file + move) so a
        /// crash can never leave a half-written policy behind.
        /// </summary>
        public void WriteAccessPolicy(ShareAccessPolicy policy)
        {
            if (policy == null) throw new ArgumentNullException(nameof(policy));
            if (string.IsNullOrWhiteSpace(policy.ShareId))
                throw new ArgumentException("Access policy must carry a share ID.", nameof(policy));

            EnsureShareRootExists(policy.ShareId);
            var path = GetAccessPolicyPath(policy.ShareId);
            var json = JsonSerializer.Serialize(policy, AccessJsonOptions);
            var temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(temp, json, Encoding.UTF8);
                File.Move(temp, path, overwrite: true);
            }
            finally
            {
                if (File.Exists(temp))
                {
                    try { File.Delete(temp); } catch { }
                }
            }
        }

        /// <summary>
        /// Reads the share's access policy, or <c>null</c> when it is missing or
        /// unreadable. Never throws for an unknown/malformed share ID.
        /// </summary>
        public ShareAccessPolicy? ReadAccessPolicy(string shareId)
        {
            try
            {
                var path = GetAccessPolicyPath(shareId);
                if (!File.Exists(path)) return null;
                return JsonSerializer.Deserialize<ShareAccessPolicy>(
                    File.ReadAllText(path), AccessJsonOptions);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// B2/B3 — fail-closed authorization for a public share request.
        ///
        /// Access is granted only when a policy exists, it is not expired
        /// (stop = past <c>ExpiresAt</c>), it still carries a non-empty hash
        /// (revoke = cleared hash), and the presented token hash matches in
        /// constant time. Anything else — a missing policy for a legacy/unmanaged
        /// package, a missing token, or a mismatch — denies.
        /// </summary>
        public bool IsAccessGranted(string shareId, string? providedToken)
        {
            if (string.IsNullOrWhiteSpace(shareId) || string.IsNullOrWhiteSpace(providedToken))
                return false;

            var policy = ReadAccessPolicy(shareId);
            if (policy == null)
                return false;

            if (string.IsNullOrWhiteSpace(policy.TokenHash))
                return false;

            if (!string.IsNullOrWhiteSpace(policy.ExpiresAt)
                && DateTimeOffset.TryParse(policy.ExpiresAt, out var expiresAt)
                && expiresAt <= DateTimeOffset.UtcNow)
            {
                return false;
            }

            return VerifyToken(providedToken, policy.TokenHash);
        }

        /// <summary>
        /// Builds a policy from owner-side share state. Used by the publishers and
        /// the lifecycle service so the on-disk policy always mirrors the DB.
        /// </summary>
        public static ShareAccessPolicy PolicyFor(string shareId, string? tokenHash, string? expiresAt) =>
            new()
            {
                ShareId = shareId,
                TokenHash = tokenHash ?? string.Empty,
                ExpiresAt = string.IsNullOrWhiteSpace(expiresAt) ? null : expiresAt,
                UpdatedAt = DateTime.UtcNow.ToString("O"),
            };
    }
