using DocumentIDE.Core.Filesystem;
using Microsoft.AspNetCore.Mvc;

namespace DocumentIDE.Api.Controllers;

public record AttachFolderRequest(string FolderPath);

[ApiController]
[Route("api/workspaces/{workspaceId}/folder-sources")]
public class FolderSourcesController : ControllerBase
{
    private readonly IFolderSourceService _folderSourceService;

    public FolderSourcesController(IFolderSourceService folderSourceService)
    {
        _folderSourceService = folderSourceService;
    }

    [HttpPost]
    public async Task<IActionResult> AttachFolder(string workspaceId, [FromBody] AttachFolderRequest request)
    {
        if (string.IsNullOrWhiteSpace(request?.FolderPath))
        {
            return BadRequest(new { error = "FolderPath is required." });
        }

        try
        {
            var result = await _folderSourceService.AttachFolderAsync(workspaceId, request.FolderPath);
            return Ok(new
            {
                folderSource = result.FolderSource,
                supportedFileCount = result.SupportedFileCount,
                displayName = result.DisplayName
            });
        }
        catch (FolderNotFoundException ex)
        {
            return BadRequest(new { error = ex.Message, folderPath = ex.FolderPath });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetFolderSources(string workspaceId)
    {
        var sources = await _folderSourceService.GetFolderSourcesAsync(workspaceId);
        return Ok(sources);
    }
}
