using System.Net.Http;
using DocumentIDE.Core.Domain.Sharing;
using DocumentIDE.Core.Sharing;

namespace DocumentIDE.IntegrationTests.Sharing;

/// <summary>
/// B2 — helpers for the fail-closed ShareHost token gate.
///
/// Since B2 the public ShareHost authorizes every share-scoped request against
/// the package's on-disk access policy (<c>access.json</c>) and denies 401 when
/// it is missing or does not match. Integration lanes that build a share package
/// directly must therefore register a policy (as the owner process does) and
/// present the token, exactly like a real viewer.
/// </summary>
internal static class ShareHostTestSupport
{
    /// <summary>Header the owner "Copy link" flow presents on the token-bearing URL.</summary>
    public const string TokenHeader = "X-Share-Token";

    /// <summary>
    /// Writes an access policy for a share that has no owner-side hash (hand-written
    /// packages) and returns the raw token a viewer would present.
    /// </summary>
    public static string IssueToken(ShareTokenService tokens, string shareId)
    {
        var token = tokens.GenerateToken();
        tokens.WriteAccessPolicy(
            ShareTokenService.PolicyFor(shareId, tokens.ComputeHash(token), expiresAt: null));
        return token;
    }

    /// <summary>
    /// Stamps a freshly issued token hash onto the share so the publisher writes a
    /// matching policy, and returns the raw token a viewer would present.
    /// </summary>
    public static string IssueTokenFor(ShareTokenService tokens, Share share)
    {
        var token = tokens.GenerateToken();
        share.TokenHash = tokens.ComputeHash(token);
        return token;
    }

    /// <summary>Attaches the share token to every request the client sends.</summary>
    public static HttpClient WithShareToken(this HttpClient client, string token)
    {
        client.DefaultRequestHeaders.TryAddWithoutValidation(TokenHeader, token);
        return client;
    }
}
