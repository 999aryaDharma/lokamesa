using DocumentIDE.Core.Domain.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace DocumentIDE.Api.Controllers;

/// <summary>
/// GR-03/GR-05 — privileged workspace graph data source for the owner graph view.
///
/// Serves exactly the envelope the app shell consumes
/// (<c>GET /api/workspaces/{workspaceId}/graph</c>):
/// <c>{ nodes: [{ documentId, displayName, documentType }],
///     edges: [{ sourceDocumentId, targetDocumentId, relationType,
///               relationOrigin, confidence }] }</c>.
///
/// Truthfulness (master §27.2, spec 08 §14, AI-04): relation origins are passed
/// through unchanged — <c>Explicit</c>/<c>Manual</c> stay factual (solid) and
/// <c>Inferred</c>/<c>semantic</c> stay suggestions (dashed). The client
/// downgrades any unknown origin to inferred, so a relationship is never
/// falsified. Only ids, display names and relation labels are served: no local
/// filesystem path ever leaves the host.
/// </summary>
[ApiController]
[Route("api/workspaces/{workspaceId}")]
public sealed class GraphController(
    IDocumentRepository documents,
    IRelationRepository relations) : ControllerBase
{
    [HttpGet("graph")]
    public async Task<IActionResult> GetGraph(string workspaceId)
    {
        var workspaceDocuments = await documents.GetByWorkspaceAsync(workspaceId);
        var nodes = workspaceDocuments
            .Select(document => new
            {
                documentId = document.Id,
                displayName = document.Filename,
                documentType = document.DocumentType,
            })
            .ToArray();

        var edges = (await relations.GetRelationsAsync(workspaceId))
            .Select(relation => new
            {
                sourceDocumentId = relation.SourceDocumentId,
                targetDocumentId = relation.TargetDocumentId,
                relationType = relation.RelationType,
                relationOrigin = relation.RelationOrigin,
                confidence = relation.Confidence,
            })
            .ToArray();

        return Ok(new { nodes, edges });
    }
}
