using System;
using System.IO;
using System.Threading.Tasks;
using DocumentIDE.Core.Filesystem;
using Microsoft.AspNetCore.Mvc;

namespace DocumentIDE.Api.Controllers;

[ApiController]
[Route("api/filesystem")]
public class PhysicalFileOperationsController : ControllerBase
{
    private readonly IPhysicalFileOperations _operations;

    public PhysicalFileOperationsController(IPhysicalFileOperations operations)
    {
        _operations = operations;
    }

    [HttpPost("rename")]
    public async Task<IActionResult> Rename([FromBody] RenamePhysicalItemRequest request)
    {
        if (request == null) return BadRequest(new { error = "Request body is required." });

        try
        {
            var result = await _operations.RenameItemAsync(request.CurrentPath, request.NewBaseName);
            if (!result.Success)
            {
                return BadRequest(new { error = result.ErrorMessage, code = result.ErrorCode });
            }
            return Ok(result);
        }
        catch (InvalidPathException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (PathCollisionException ex)
        {
            return Conflict(new { error = ex.Message, targetPath = ex.TargetPath });
        }
        catch (FileNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPost("move")]
    public async Task<IActionResult> Move([FromBody] MovePhysicalItemRequest request)
    {
        if (request == null) return BadRequest(new { error = "Request body is required." });

        try
        {
            var result = await _operations.MoveItemAsync(request.SourcePath, request.DestinationDirectoryPath, request.NewBaseName);
            if (!result.Success)
            {
                return BadRequest(new { error = result.ErrorMessage, code = result.ErrorCode });
            }
            return Ok(result);
        }
        catch (InvalidPathException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (PathCollisionException ex)
        {
            return Conflict(new { error = ex.Message, targetPath = ex.TargetPath });
        }
        catch (FileNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (DirectoryNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPost("duplicate")]
    public async Task<IActionResult> Duplicate([FromBody] DuplicatePhysicalItemRequest request)
    {
        if (request == null) return BadRequest(new { error = "Request body is required." });

        try
        {
            var result = await _operations.DuplicateItemAsync(request.SourceFilePath, request.CustomNewBaseName);
            if (!result.Success)
            {
                return BadRequest(new { error = result.ErrorMessage, code = result.ErrorCode });
            }
            return Ok(result);
        }
        catch (InvalidPathException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (PathCollisionException ex)
        {
            return Conflict(new { error = ex.Message, targetPath = ex.TargetPath });
        }
        catch (FileNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPost("create-directory")]
    public async Task<IActionResult> CreateDirectory([FromBody] CreateDirectoryRequest request)
    {
        if (request == null) return BadRequest(new { error = "Request body is required." });

        try
        {
            var result = await _operations.CreateDirectoryAsync(request.ParentDirectoryPath, request.NewDirectoryName);
            if (!result.Success)
            {
                return BadRequest(new { error = result.ErrorMessage, code = result.ErrorCode });
            }
            return Ok(result);
        }
        catch (InvalidPathException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (PathCollisionException ex)
        {
            return Conflict(new { error = ex.Message, targetPath = ex.TargetPath });
        }
        catch (DirectoryNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPost("create-file")]
    public async Task<IActionResult> CreateFile([FromBody] CreateFileRequest request)
    {
        if (request == null) return BadRequest(new { error = "Request body is required." });

        try
        {
            PhysicalOperationResultDto result;
            if (string.Equals(request.FileType, "docx", StringComparison.OrdinalIgnoreCase))
            {
                result = await _operations.CreateDocxFileAsync(request.ParentDirectoryPath, request.NewFileName);
            }
            else
            {
                result = await _operations.CreateMarkdownFileAsync(request.ParentDirectoryPath, request.NewFileName, request.InitialContent);
            }

            if (!result.Success)
            {
                return BadRequest(new { error = result.ErrorMessage, code = result.ErrorCode });
            }
            return Ok(result);
        }
        catch (InvalidPathException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (PathCollisionException ex)
        {
            return Conflict(new { error = ex.Message, targetPath = ex.TargetPath });
        }
        catch (DirectoryNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPost("delete")]
    public async Task<IActionResult> Delete([FromBody] DeletePhysicalItemRequest request)
    {
        if (request == null) return BadRequest(new { error = "Request body is required." });

        try
        {
            var result = await _operations.DeleteItemAsync(request.TargetPath);
            if (!result.Success)
            {
                return BadRequest(new { error = result.ErrorMessage, code = result.ErrorCode });
            }
            return Ok(result);
        }
        catch (FileNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }
}
