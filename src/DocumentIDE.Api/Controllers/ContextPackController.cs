using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Core.Context;
using Microsoft.AspNetCore.Mvc;

namespace DocumentIDE.Api.Controllers;

/// <summary>
/// UI-05 A — <c>POST /api/context/pack</c>.
///
/// Builds a read-only context pack over an explicit ordered list of document
/// ids and returns the combined Markdown plus the existing
/// <see cref="ContextExportService"/> token estimate. It has no export
/// destination and writes no file; the UI's copy action is the only output in
/// this build.
///
/// Every refusal is typed (<c>errorCode</c>) and carries no partial content:
/// empty selection, unknown id, no text available, per-document cap, whole-pack
/// cap and token budget. The route is privileged-guarded by the host middleware
/// (Origin/Host/session token) like every other controller.
/// </summary>
[ApiController]
[Route("api/context")]
public sealed class ContextPackController(ContextPackBuilder builder) : ControllerBase
{
    public sealed record PackRequest(string[]? DocumentIds, int? TokenBudget);

    [HttpPost("pack")]
    public async Task<IActionResult> Pack([FromBody] PackRequest request, CancellationToken cancellationToken)
    {
        var result = await builder.BuildAsync(request?.DocumentIds, request?.TokenBudget, cancellationToken);

        if (!result.IsSuccess)
        {
            var statusCode = result.Status switch
            {
                ContextPackStatus.EmptySelection => StatusCodes.Status400BadRequest,
                ContextPackStatus.InvalidRequest => StatusCodes.Status400BadRequest,
                ContextPackStatus.UnknownDocument => StatusCodes.Status404NotFound,
                ContextPackStatus.NoTextAvailable => StatusCodes.Status409Conflict,
                ContextPackStatus.DocumentTooLarge => StatusCodes.Status413PayloadTooLarge,
                ContextPackStatus.PackTooLarge => StatusCodes.Status413PayloadTooLarge,
                ContextPackStatus.BudgetExceeded => StatusCodes.Status422UnprocessableEntity,
                _ => StatusCodes.Status500InternalServerError,
            };

            return StatusCode(statusCode, new
            {
                status = result.Status.ToString(),
                errorCode = result.Status.ToString(),
                documentId = result.DocumentId,
                detail = result.Detail,
                tokenEstimate = result.TokenEstimate,
            });
        }

        return Ok(new
        {
            status = result.Status.ToString(),
            documents = result.Documents.Select(document => new
            {
                documentId = document.DocumentId,
                title = document.Title,
                textSource = document.TextSource,
                contentLabel = document.ContentLabel,
                content = document.Content,
                characterCount = document.CharacterCount,
                byteCount = document.ByteCount,
            }),
            markdown = result.Markdown,
            tokenEstimate = result.TokenEstimate,
        });
    }
}
