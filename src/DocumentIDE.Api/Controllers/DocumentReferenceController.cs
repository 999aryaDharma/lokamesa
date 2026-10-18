using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Core.Filesystem;
using Microsoft.AspNetCore.Mvc;

namespace DocumentIDE.Api.Controllers;

/// <summary>
/// UI-05 B — <c>DELETE /api/documents/{documentId}/reference</c>.
///
/// Removes only the workspace's database reference to a document (canvas /
/// workspace rows cascade by foreign key). The physical file is never touched:
/// <see cref="MissingResourceService.RemoveReferenceAsync"/> performs zero
/// filesystem writes, and this controller does not add any.
///
/// The route is privileged-guarded by the host middleware. An unknown id is a
/// typed <c>UnknownDocument</c>; nothing is deleted in that case.
/// </summary>
[ApiController]
[Route("api/documents/{documentId}")]
public sealed class DocumentReferenceController(MissingResourceService missingResources) : ControllerBase
{
    [HttpDelete("reference")]
    public async Task<IActionResult> RemoveReference(string documentId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(documentId))
        {
            return NotFound(new
            {
                status = "Refused",
                errorCode = "UnknownDocument",
                documentId,
                detail = "unknown-document",
            });
        }

        var result = await missingResources.RemoveReferenceAsync(documentId, cancellationToken);

        if (!result.Success)
        {
            var statusCode = string.Equals(result.ErrorMessage, "unknown-document", System.StringComparison.Ordinal)
                ? StatusCodes.Status404NotFound
                : StatusCodes.Status409Conflict;

            return StatusCode(statusCode, new
            {
                status = "Refused",
                errorCode = string.Equals(result.ErrorMessage, "unknown-document", System.StringComparison.Ordinal)
                    ? "UnknownDocument"
                    : "RemoveReferenceFailed",
                documentId = result.DocumentId,
                detail = result.ErrorMessage,
            });
        }

        return Ok(new
        {
            status = "Removed",
            documentId = result.DocumentId,
            removed = true,
        });
    }
}
