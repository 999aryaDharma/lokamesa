using System;

namespace DocumentIDE.Core.Sharing;

/// <summary>
/// B2/B3 — the share package's own access policy. Public sharing has no
/// account/login: security comes from an unguessable token, so the token
/// <em>hash</em> (never the plaintext) plus the share's expiry are stored next
/// to <c>manifest.json</c> as <c>access.json</c>.
///
/// This is the only cross-process channel between the privileged owner app and
/// the separate <c>DocumentIDE.ShareHost</c> process, which cannot read the
/// owner database. It lets ShareHost authenticate every request and enforce
/// stop (<see cref="ExpiresAt"/> in the past) and revoke (empty
/// <see cref="TokenHash"/>) without any privileged dependency.
/// </summary>
public sealed class ShareAccessPolicy
{
    /// <summary>Share identifier (matches the route segment and manifest).</summary>
    public string ShareId { get; set; } = string.Empty;

    /// <summary>
    /// SHA-256 hex hash of the share secret token. Empty means revoked: no token
    /// can verify, ever.
    /// </summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>
    /// ISO-8601 expiry (UTC). Null means no expiry. A value in the past means the
    /// share was stopped.
    /// </summary>
    public string? ExpiresAt { get; set; }

    /// <summary>
    /// The moment the policy was written. Informational only; verification is
    /// driven by <see cref="TokenHash"/> and <see cref="ExpiresAt"/>.
    /// </summary>
    public string UpdatedAt { get; set; } = DateTime.UtcNow.ToString("O");
}
