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
///
/// B5 — create/regenerate also start the public path (<see cref="ShareLinkService"/>:
/// ShareHost process + ngrok transport) and return an absolute viewer URL. When
/// the tunnel is unavailable the owner gets a local URL plus a real
/// <c>tunnelError</c> instead of the previous hardcoded <c>null</c>.
/// </summary>
[ApiController]
[Route("api/shares")]
public sealed class ShareController(ShareLifecycleService lifecycle, ShareLinkService links) : ControllerBase
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
            var link = await links.ActivateAsync(result.Share.Id);
            return Created($"/api/shares/{result.Share.Id}", Project(result.Share, result.RawToken, link));
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
        return Ok(all.Select(share => Project(share, null, links.Snapshot())).ToArray());
    }

    [HttpGet("{shareId}")]
    public async Task<IActionResult> Get(string shareId)
    {
        var share = await lifecycle.GetAsync(shareId);
        return share == null ? NotFound() : Ok(Project(share, null, links.Snapshot()));
    }

    [HttpPost("{shareId}/stop")]
    public async Task<IActionResult> Stop(string shareId)
    {
        var share = await lifecycle.StopAsync(shareId);
        if (share == null) return NotFound();
        await links.DeactivateAsync(shareId);
        return Ok(Project(share, null, links.Snapshot()));
    }

    [HttpPost("{shareId}/revoke")]
    public async Task<IActionResult> Revoke(string shareId)
    {
        var share = await lifecycle.RevokeAsync(shareId);
        if (share == null) return NotFound();
        await links.DeactivateAsync(shareId);
        return Ok(Project(share, null, links.Snapshot()));
    }

    [HttpPost("{shareId}/regenerate")]
    public async Task<IActionResult> Regenerate(string shareId)
    {
        var result = await lifecycle.RegenerateAsync(shareId);
        if (result == null) return NotFound();
        var link = await links.ActivateAsync(shareId);
        return Ok(Project(result.Share, result.RawToken, link));
    }

    [HttpPost("{shareId}/members")]
    public async Task<IActionResult> AddMember(string shareId, AddMemberRequest request)
    {
        try
        {
            var share = await lifecycle.AddMemberAsync(shareId, request?.DocumentId ?? string.Empty);
            return share == null ? NotFound() : Ok(Project(share, null, links.Snapshot()));
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
            return share == null ? NotFound() : Ok(Project(share, null, links.Snapshot()));
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

    private static object Project(Share share, string? rawToken, ShareLinkState link) => new
    {
        shareId = share.Id,
        mode = share.Mode.ToString(),
        status = DeriveStatus(share, link),
        shareToken = rawToken ?? string.Empty,
        shareUrl = ShareLinkService.BuildUrl(share.Id, rawToken, link.BaseUrl),
        revoked = string.IsNullOrEmpty(share.TokenHash),
        share.AllowOriginalDownload,
        share.AllowPdfDownload,
        members = share.Members.Select(member => new
        {
            documentId = member.DocumentId,
            displayName = member.DisplayPath,
        }).ToArray(),
        tunnelError = link.TunnelError,
    };

    /// <summary>
    /// Maps owner-side share state to the UI contract
    /// (<c>active | stopped | tunnel-error</c>). A stopped/revoked share is
    /// "stopped" regardless of the tunnel; an otherwise-active share whose
    /// public tunnel failed is "tunnel-error" so the owner UI shows the real
    /// reason (spec §27) instead of a live-looking link.
    /// </summary>
    private static string DeriveStatus(Share share, ShareLinkState link)
    {
        if (string.IsNullOrEmpty(share.TokenHash)) return "stopped";
        if (!string.IsNullOrEmpty(share.ExpiresAt)
            && DateTimeOffset.TryParse(share.ExpiresAt, out var expiresAt)
            && expiresAt <= DateTimeOffset.UtcNow)
        {
            return "stopped";
        }
        return string.IsNullOrWhiteSpace(link.TunnelError) ? "active" : "tunnel-error";
    }
}
