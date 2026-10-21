using System.Threading.Tasks;
using DocumentIDE.Core.Activity;
using DocumentIDE.Core.Word;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace DocumentIDE.Api.Controllers;

public record OpenWordDocumentRequest(string DocumentId);

[ApiController]
public class WordController : ControllerBase
{
    private readonly IWordDesktopLauncher _wordLauncher;
    private readonly ActivityGate _activity;

    public WordController(IWordDesktopLauncher wordLauncher, ActivityGate activity)
    {
        _wordLauncher = wordLauncher;
        _activity = activity;
    }

    [HttpPost("api/word/open")]
    public async Task<IActionResult> OpenWordDocument([FromBody] OpenWordDocumentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request?.DocumentId))
        {
            return BadRequest(WordLaunchResult.Failed(WordLaunchStatus.DocumentNotFound, "DocumentId is required."));
        }

        return await HandleOpenResultAsync(request.DocumentId);
    }

    [HttpPost("api/documents/{id}/open-in-word")]
    public async Task<IActionResult> OpenDocumentInWord(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return BadRequest(WordLaunchResult.Failed(WordLaunchStatus.DocumentNotFound, "Document ID is required."));
        }

        return await HandleOpenResultAsync(id);
    }

    private async Task<IActionResult> HandleOpenResultAsync(string documentId)
    {
        using var lease = _activity.Enter("word-open");
        var result = await _wordLauncher.OpenDocumentAsync(documentId);

        if (result.Success)
        {
            return Ok(result);
        }

        return result.Status switch
        {
            WordLaunchStatus.WordNotInstalled => StatusCode(StatusCodes.Status503ServiceUnavailable, result),
            WordLaunchStatus.DocumentNotFound => NotFound(result),
            WordLaunchStatus.InvalidFileType => BadRequest(result),
            _ => StatusCode(StatusCodes.Status500InternalServerError, result)
        };
    }
}
