using System;
using System.Threading.Tasks;
using DocumentIDE.Core.Filesystem;
using Microsoft.AspNetCore.Mvc;

namespace DocumentIDE.Api.Controllers;

[ApiController]
[Route("api/documents/{documentId}")]
public class MoveCorrelationController : ControllerBase
{
    private readonly IMoveCorrelationService _correlationService;

    public MoveCorrelationController(IMoveCorrelationService correlationService)
    {
        _correlationService = correlationService;
    }

    [HttpGet("rebind-candidates")]
    [HttpGet("candidates")]
    public async Task<IActionResult> GetRebindCandidates(string documentId, [FromQuery] string? searchDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(documentId))
        {
            return BadRequest(new { error = "DocumentId is required." });
        }

        var result = await _correlationService.GetRebindCandidatesAsync(documentId, searchDirectory);
        return Ok(result);
    }

    [HttpPost("rebind")]
    public async Task<IActionResult> ConfirmRebind(string documentId, [FromBody] ConfirmRebindRequest request)
    {
        if (string.IsNullOrWhiteSpace(documentId))
        {
            return BadRequest(new { error = "DocumentId is required." });
        }
        if (string.IsNullOrWhiteSpace(request?.TargetPath))
        {
            return BadRequest(new { error = "TargetPath is required." });
        }

        try
        {
            var result = await _correlationService.RebindDocumentAsync(documentId, request.TargetPath, request.ForceRebind);
            if (!result.Success)
            {
                return BadRequest(new { error = result.ErrorMessage });
            }
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message, isAmbiguous = true });
        }
    }

    [HttpPost("move")]
    public async Task<IActionResult> MoveDocument(string documentId, [FromBody] MoveDocumentRequest request)
    {
        if (string.IsNullOrWhiteSpace(documentId))
        {
            return BadRequest(new { error = "DocumentId is required." });
        }
        if (string.IsNullOrWhiteSpace(request?.DestinationPath))
        {
            return BadRequest(new { error = "DestinationPath is required." });
        }

        try
        {
            var result = await _correlationService.RebindDocumentAsync(documentId, request.DestinationPath, forceRebind: true);
            return Ok(new MoveDocumentResultDto(documentId, result.PreviousPath, request.DestinationPath, result.Success, result.ErrorMessage));
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
