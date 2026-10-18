using DocumentIDE.Core.Domain.Sharing;
using DocumentIDE.Infrastructure.Sharing;
using Microsoft.AspNetCore.Mvc;

namespace DocumentIDE.Api.Controllers;

/// <summary>
/// G5 — privileged owner-side share lifecycle (create Snapshot/Live, stop,
/// revoke, regenerate, membership). Served only by the privileged hosts behind
/// the FND-02/QA-03 <c>PrivilegedRequestGuard</c>; the public ShareHost never
/// exposes these routes and never receives an arbitrary local path.
///
/// The response shape mirrors the owner-side <c>ShareManagementState</c> the web
/// client consumes. Raw tokens are returned only by create/regenerate; reads
/// return an empty <c>shareToken</c> because only the hash is persisted.
/// </summary>
[ApiController]
[Route("api/shares")]
public sealed class ShareController(ShareLifecycleService lifecycle) : ControllerBase
{
    public sealed record CreateShareRequest(
        string Mode,
        IReadOnlyList<string>? MemberDocumentIds,
        bool AllowOriginalDownload,
        bool AllowPdfDownload);

    public sealed record AddMemberRequest(string DocumentId);

    [HttpPost]
    public async Task<IActionResult> Create(CreateShareRequest request)
    {
        if (request == null || !Enum.TryParse<ShareMode>(request.Mode, ignoreCase: true, out var mode))
        {
            return BadRequest(new { errorCode = "InvalidMode" });
        }

        try
        {
            var result = await lifecycle.CreateAsync(new ShareCreateRequest(
                mode,
                request.MemberDocumentIds ?? Array.Empty<string>(),
                request.AllowOriginalDownload,
                request.AllowPdfDownload));
            return Created($"/api/shares/{result.Share.Id}", Project(result.Share, result.RawToken));
        }
        catch (ShareLifecycleException ex)
        {
            return ProblemFor(ex);
        }
    }

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var all = await lifecycle.ListAsync();
        // The app shell consumes a bare ShareManagementState[] here
        // (shareApi.listShares), not a wrapped object.
        return Ok(all.Select(share => Project(share)).ToArray());
    }

    [HttpGet("{shareId}")]
    public async Task<IActionResult> Get(string shareId)
    {
        var share = await lifecycle.GetAsync(shareId);
        return share == null ? NotFound() : Ok(Project(share));
    }

    [HttpPost("{shareId}/stop")]
    public async Task<IActionResult> Stop(string shareId)
    {
        var share = await lifecycle.StopAsync(shareId);
        return share == null ? NotFound() : Ok(Project(share));
    }

    [HttpPost("{shareId}/revoke")]
    public async Task<IActionResult> Revoke(string shareId)
    {
        var share = await lifecycle.RevokeAsync(shareId);
        return share == null ? NotFound() : Ok(Project(share));
    }

    [HttpPost("{shareId}/regenerate")]
    public async Task<IActionResult> Regenerate(string shareId)
    {
        var result = await lifecycle.RegenerateAsync(shareId);
        return result == null ? NotFound() : Ok(Project(result.Share, result.RawToken));
    }

    [HttpPost("{shareId}/members")]
    public async Task<IActionResult> AddMember(string shareId, AddMemberRequest request)
    {
        try
        {
            var share = await lifecycle.AddMemberAsync(shareId, request?.DocumentId ?? string.Empty);
            return share == null ? NotFound() : Ok(Project(share));
        }
        catch (ShareLifecycleException ex)
        {
            return ProblemFor(ex);
        }
    }

    [HttpDelete("{shareId}/members/{documentId}")]
    public async Task<IActionResult> RemoveMember(string shareId, string documentId)
    {
        try
        {
            var share = await lifecycle.RemoveMemberAsync(shareId, documentId);
            return share == null ? NotFound() : Ok(Project(share));
        }
        catch (ShareLifecycleException ex)
        {
            return ProblemFor(ex);
        }
    }

    private IActionResult ProblemFor(ShareLifecycleException ex) => ex.Code switch
    {
        "NoMembers" or "UnknownDocument" or "UnsupportedMemberType" or "DocumentUnavailable" =>
            BadRequest(new { errorCode = ex.Code }),
        _ =>
            // SnapshotMembershipFrozen / DocxPreviewUnavailable / PdfArtifactUnavailable / NotAMember
            Conflict(new { errorCode = ex.Code }),
    };

    private static object Project(Share share, string? rawToken = null) => new
    {
        shareId = share.Id,
        mode = share.Mode.ToString(),
        status = DeriveStatus(share),
        shareToken = rawToken ?? string.Empty,
        shareUrl = BuildUrl(share, rawToken),
        revoked = string.IsNullOrEmpty(share.TokenHash),
        share.AllowOriginalDownload,
        share.AllowPdfDownload,
        members = share.Members.Select(member => new
        {
            documentId = member.DocumentId,
            displayName = member.DisplayPath,
        }).ToArray(),
        tunnelError = (string?)null,
    };

    private static string DeriveStatus(Share share)
    {
        if (string.IsNullOrEmpty(share.TokenHash)) return "stopped";
        if (!string.IsNullOrEmpty(share.ExpiresAt)
            && DateTimeOffset.TryParse(share.ExpiresAt, out var expiresAt)
            && expiresAt <= DateTimeOffset.UtcNow)
        {
            return "stopped";
        }
        return "active";
    }

    private static string BuildUrl(Share share, string? rawToken) =>
        string.IsNullOrEmpty(rawToken)
            ? $"/share/{share.Id}"
            : $"/share/{share.Id}?token={rawToken}";
}
