using System.Threading;
using System.Threading.Tasks;

namespace DocumentIDE.Core.Filesystem;

/// <summary>
/// Core contract for explicit physical file and directory operations.
/// FS-07: Explicit Physical File Operations.
/// </summary>
public interface IPhysicalFileOperations
{
    Task<PhysicalOperationResultDto> RenameItemAsync(string currentPath, string newBaseName, CancellationToken cancellationToken = default);
    Task<PhysicalOperationResultDto> MoveItemAsync(string sourcePath, string destinationDirectoryPath, string? newBaseName = null, CancellationToken cancellationToken = default);
    Task<PhysicalOperationResultDto> DuplicateItemAsync(string sourceFilePath, string? customNewBaseName = null, CancellationToken cancellationToken = default);
    Task<PhysicalOperationResultDto> CreateDirectoryAsync(string parentDirectoryPath, string newDirectoryName, CancellationToken cancellationToken = default);
    Task<PhysicalOperationResultDto> CreateMarkdownFileAsync(string parentDirectoryPath, string newFileName, string? initialContent = null, CancellationToken cancellationToken = default);
    Task<PhysicalOperationResultDto> CreateDocxFileAsync(string parentDirectoryPath, string newFileName, byte[]? templateBytes = null, CancellationToken cancellationToken = default);
    Task<PhysicalOperationResultDto> DeleteItemAsync(string targetPath, CancellationToken cancellationToken = default);
}
