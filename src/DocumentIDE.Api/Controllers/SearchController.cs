using DocumentIDE.Core.Search;
using Microsoft.AspNetCore.Mvc;

namespace DocumentIDE.Api.Controllers;

/// <summary>
/// SRCH-05 / G3 — privileged realtime search surface.
///
/// Serves the exact contract the web client's <c>searchStore</c> fetches
/// (<c>GET /api/search?q=…</c>): <c>{ results, totalCount, executionTimeMs,
/// activeFilterChips? }</c> where every result carries
/// <c>documentId, filename, score, matchSources, snippets, metadataSummary?,
/// path?</c>. Query parsing/matching/ranking stays owned by
/// <see cref="IDocumentSearchService"/>; this controller adds no matching
/// semantics of its own (no fuzzy/semantic fallback beyond the parsed query).
/// </summary>
[ApiController]
public sealed class SearchController(IDocumentSearchService search) : ControllerBase
{
    [HttpGet("api/search")]
    public async Task<IActionResult> Search(
        [FromQuery] string? q,
        [FromQuery] string? workspaceId,
        [FromQuery] int? limit,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            var empty = new SearchQuery(
                Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<QueryFilter>());
            return Ok(new SearchExecutionResult(
                Array.Empty<SearchResult>(), 0, 0, empty, Array.Empty<FilterChipInfo>()));
        }

        try
        {
            var options = new DocumentSearchOptions(
                WorkspaceId: string.IsNullOrWhiteSpace(workspaceId) ? null : workspaceId,
                Limit: limit is > 0 and <= 500 ? limit.Value : 50);
            var result = await search.SearchAsync(q, options, cancellationToken);
            return Ok(result);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            // The client maps 400 to "Malformed query syntax" (searchStore.ts).
            return BadRequest(new { errorCode = "MalformedQuery" });
        }
    }
}
