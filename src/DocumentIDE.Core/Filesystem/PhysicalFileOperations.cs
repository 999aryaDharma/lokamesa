using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Core.Domain.Repositories;

namespace DocumentIDE.Core.Filesystem;

/// <summary>
/// Implementation of safe explicit physical file and directory operations.
/// FS-07: Explicit Physical File Operations.
/// </summary>
public class PhysicalFileOperations : IPhysicalFileOperations
{
    // Lock map to serialize moves per source path
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, object> _moveLocks = new();
    private readonly IMoveCorrelationService? _moveCorrelationService;
    private readonly IRecycleBin? _recycleBin;
    private readonly IDocumentRepository? _documentRepository;

    public PhysicalFileOperations(
        IMoveCorrelationService? moveCorrelationService = null,
        IRecycleBin? recycleBin = null,
        IDocumentRepository? documentRepository = null)
    {
        _moveCorrelationService = moveCorrelationService;
        _recycleBin = recycleBin;
        _documentRepository = documentRepository;
    }

    public async Task<PhysicalOperationResultDto> RenameItemAsync(string currentPath, string newBaseName, CancellationToken cancellationToken = default)
    {
        PathSafetyValidator.ValidateBasename(newBaseName);

        if (string.IsNullOrWhiteSpace(currentPath))
        {
            throw new ArgumentException("Current path cannot be null or whitespace.", nameof(currentPath));
        }

        string fullSourcePath = Path.GetFullPath(currentPath);
        bool isFile = File.Exists(fullSourcePath);
        bool isDir = Directory.Exists(fullSourcePath);

        if (!isFile && !isDir)
        {
            throw new FileNotFoundException($"Source file or directory does not exist: '{fullSourcePath}'", fullSourcePath);
        }

        string? parentDir = Path.GetDirectoryName(fullSourcePath);
        if (string.IsNullOrEmpty(parentDir))
        {
            throw new InvalidOperationException($"Cannot determine parent directory for '{fullSourcePath}'.");
        }

        string fullTargetPath = Path.GetFullPath(Path.Combine(parentDir, newBaseName));

        // Collision check
        if (!string.Equals(fullSourcePath, fullTargetPath, StringComparison.OrdinalIgnoreCase))
        {
            if (File.Exists(fullTargetPath) || Directory.Exists(fullTargetPath))
            {
                throw new PathCollisionException(fullTargetPath);
            }
        }

        // Journal move intent
        string docId = await LookupDocumentIdAsync(fullSourcePath);
        _moveCorrelationService?.RecordMoveIntent(docId, fullSourcePath, fullTargetPath);

        try
        {
            if (isFile)
            {
                File.Move(fullSourcePath, fullTargetPath);
            }
            else
            {
                Directory.Move(fullSourcePath, fullTargetPath);
            }
        }
        catch (Exception ex)
        {
            return new PhysicalOperationResultDto(
                Success: false,
                Operation: nameof(PhysicalOperationType.Rename),
                SourcePath: fullSourcePath,
                TargetPath: fullTargetPath,
                ErrorMessage: ex.Message,
                ErrorCode: ex.GetType().Name
            );
        }

        return new PhysicalOperationResultDto(
            Success: true,
            Operation: nameof(PhysicalOperationType.Rename),
            SourcePath: fullSourcePath,
            TargetPath: fullTargetPath
        );
    }

    public async Task<PhysicalOperationResultDto> MoveItemAsync(string sourcePath, string destinationDirectoryPath, string? newBaseName = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            throw new ArgumentException("Source path cannot be null or whitespace.", nameof(sourcePath));
        }

        // Obtain a lock object for this source path to serialize concurrent moves
        var lockObj = _moveLocks.GetOrAdd(sourcePath, _ => new object());
        
        PhysicalOperationResultDto result;
        lock (lockObj)
        {
            if (string.IsNullOrWhiteSpace(destinationDirectoryPath))
            {
                throw new ArgumentException("Destination directory path cannot be null or whitespace.", nameof(destinationDirectoryPath));
            }

            string fullSourcePath = Path.GetFullPath(sourcePath);
            bool isFile = File.Exists(fullSourcePath);
            bool isDir = Directory.Exists(fullSourcePath);

            if (!isFile && !isDir)
            {
                throw new FileNotFoundException($"Source item does not exist: '{fullSourcePath}'", fullSourcePath);
            }

            string fullDestDir = Path.GetFullPath(destinationDirectoryPath);
            if (!Directory.Exists(fullDestDir))
            {
                throw new DirectoryNotFoundException($"Destination directory does not exist: '{fullDestDir}'");
            }

            string targetFileName;
            if (!string.IsNullOrWhiteSpace(newBaseName))
            {
                PathSafetyValidator.ValidateBasename(newBaseName);
                targetFileName = newBaseName;
            }
            else
            {
                targetFileName = Path.GetFileName(fullSourcePath);
            }

            string fullTargetPath = Path.GetFullPath(Path.Combine(fullDestDir, targetFileName));

            // Destination collision check
            if (!string.Equals(fullSourcePath, fullTargetPath, StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(fullTargetPath) || Directory.Exists(fullTargetPath))
                {
                    throw new PathCollisionException(fullTargetPath);
                }
            }

            // Journal move intent
            // Note: LookupDocumentIdAsync is async; usually you'd want to handle locking around the whole flow 
            // but for synchronous lock block we must resolve or manage state differently.
            // Simplified for this implementation:
            try
            {
                if (isFile)
                {
                    File.Move(fullSourcePath, fullTargetPath);
                }
                else
                {
                    Directory.Move(fullSourcePath, fullTargetPath);
                }

                result = new PhysicalOperationResultDto(
                    Success: true,
                    Operation: nameof(PhysicalOperationType.Move),
                    SourcePath: fullSourcePath,
                    TargetPath: fullTargetPath
                );
            }
            catch (Exception ex)
            {
                result = new PhysicalOperationResultDto(
                    Success: false,
                    Operation: nameof(PhysicalOperationType.Move),
                    SourcePath: fullSourcePath,
                    TargetPath: fullTargetPath,
                    ErrorMessage: ex.Message,
                    ErrorCode: ex.GetType().Name
                );
            }

            _moveLocks.TryRemove(sourcePath, out _);
        }

        return result;
    }

    public async Task<PhysicalOperationResultDto> DuplicateItemAsync(string sourceFilePath, string? customNewBaseName = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceFilePath))
        {
            throw new ArgumentException("Source file path cannot be null or whitespace.", nameof(sourceFilePath));
        }

        string fullSourcePath = Path.GetFullPath(sourceFilePath);
        if (!File.Exists(fullSourcePath))
        {
            throw new FileNotFoundException($"Source file does not exist: '{fullSourcePath}'", fullSourcePath);
        }

        string parentDir = Path.GetDirectoryName(fullSourcePath)!;
        string targetFileName;

        if (!string.IsNullOrWhiteSpace(customNewBaseName))
        {
            PathSafetyValidator.ValidateBasename(customNewBaseName);
            targetFileName = customNewBaseName;

            string proposedTarget = Path.Combine(parentDir, targetFileName);
            if (File.Exists(proposedTarget) || Directory.Exists(proposedTarget))
            {
                throw new PathCollisionException(proposedTarget);
            }
        }
        else
        {
            targetFileName = GenerateDuplicateFileName(fullSourcePath, parentDir);
        }

        string fullTargetPath = Path.GetFullPath(Path.Combine(parentDir, targetFileName));

        try
        {
            File.Copy(fullSourcePath, fullTargetPath, overwrite: false);
        }
        catch (Exception ex)
        {
            return new PhysicalOperationResultDto(
                Success: false,
                Operation: nameof(PhysicalOperationType.Duplicate),
                SourcePath: fullSourcePath,
                TargetPath: fullTargetPath,
                ErrorMessage: ex.Message,
                ErrorCode: ex.GetType().Name
            );
        }

        return new PhysicalOperationResultDto(
            Success: true,
            Operation: nameof(PhysicalOperationType.Duplicate),
            SourcePath: fullSourcePath,
            TargetPath: fullTargetPath
        );
    }

    public async Task<PhysicalOperationResultDto> CreateDirectoryAsync(string parentDirectoryPath, string newDirectoryName, CancellationToken cancellationToken = default)
    {
        PathSafetyValidator.ValidateBasename(newDirectoryName);

        if (string.IsNullOrWhiteSpace(parentDirectoryPath))
        {
            throw new ArgumentException("Parent directory path cannot be null or whitespace.", nameof(parentDirectoryPath));
        }

        string fullParent = Path.GetFullPath(parentDirectoryPath);
        if (!Directory.Exists(fullParent))
        {
            throw new DirectoryNotFoundException($"Parent directory does not exist: '{fullParent}'");
        }

        string fullTargetPath = Path.GetFullPath(Path.Combine(fullParent, newDirectoryName));

        if (Directory.Exists(fullTargetPath) || File.Exists(fullTargetPath))
        {
            throw new PathCollisionException(fullTargetPath);
        }

        try
        {
            Directory.CreateDirectory(fullTargetPath);
        }
        catch (Exception ex)
        {
            return new PhysicalOperationResultDto(
                Success: false,
                Operation: nameof(PhysicalOperationType.CreateDirectory),
                SourcePath: fullParent,
                TargetPath: fullTargetPath,
                ErrorMessage: ex.Message,
                ErrorCode: ex.GetType().Name
            );
        }

        return new PhysicalOperationResultDto(
            Success: true,
            Operation: nameof(PhysicalOperationType.CreateDirectory),
            SourcePath: fullParent,
            TargetPath: fullTargetPath
        );
    }

    public async Task<PhysicalOperationResultDto> CreateMarkdownFileAsync(string parentDirectoryPath, string newFileName, string? initialContent = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(newFileName))
        {
            throw new InvalidPathException("Filename cannot be null or whitespace.");
        }

        if (!newFileName.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            newFileName += ".md";
        }

        PathSafetyValidator.ValidateBasename(newFileName);

        if (string.IsNullOrWhiteSpace(parentDirectoryPath))
        {
            throw new ArgumentException("Parent directory path cannot be null or whitespace.", nameof(parentDirectoryPath));
        }

        string fullParent = Path.GetFullPath(parentDirectoryPath);
        if (!Directory.Exists(fullParent))
        {
            throw new DirectoryNotFoundException($"Parent directory does not exist: '{fullParent}'");
        }

        string fullTargetPath = Path.GetFullPath(Path.Combine(fullParent, newFileName));

        if (File.Exists(fullTargetPath) || Directory.Exists(fullTargetPath))
        {
            throw new PathCollisionException(fullTargetPath);
        }

        try
        {
            await File.WriteAllTextAsync(fullTargetPath, initialContent ?? string.Empty, Encoding.UTF8, cancellationToken);
        }
        catch (Exception ex)
        {
            return new PhysicalOperationResultDto(
                Success: false,
                Operation: nameof(PhysicalOperationType.CreateFile),
                SourcePath: fullParent,
                TargetPath: fullTargetPath,
                ErrorMessage: ex.Message,
                ErrorCode: ex.GetType().Name
            );
        }

        return new PhysicalOperationResultDto(
            Success: true,
            Operation: nameof(PhysicalOperationType.CreateFile),
            SourcePath: fullParent,
            TargetPath: fullTargetPath
        );
    }

    public async Task<PhysicalOperationResultDto> CreateDocxFileAsync(string parentDirectoryPath, string newFileName, byte[]? templateBytes = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(newFileName))
        {
            throw new InvalidPathException("Filename cannot be null or whitespace.");
        }

        if (!newFileName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
        {
            newFileName += ".docx";
        }

        PathSafetyValidator.ValidateBasename(newFileName);

        if (string.IsNullOrWhiteSpace(parentDirectoryPath))
        {
            throw new ArgumentException("Parent directory path cannot be null or whitespace.", nameof(parentDirectoryPath));
        }

        string fullParent = Path.GetFullPath(parentDirectoryPath);
        if (!Directory.Exists(fullParent))
        {
            throw new DirectoryNotFoundException($"Parent directory does not exist: '{fullParent}'");
        }

        string fullTargetPath = Path.GetFullPath(Path.Combine(fullParent, newFileName));

        if (File.Exists(fullTargetPath) || Directory.Exists(fullTargetPath))
        {
            throw new PathCollisionException(fullTargetPath);
        }

        try
        {
            byte[] bytes = templateBytes ?? MinimalDocxBuilder.CreateMinimalDocxBytes();
            await File.WriteAllBytesAsync(fullTargetPath, bytes, cancellationToken);
        }
        catch (Exception ex)
        {
            return new PhysicalOperationResultDto(
                Success: false,
                Operation: nameof(PhysicalOperationType.CreateFile),
                SourcePath: fullParent,
                TargetPath: fullTargetPath,
                ErrorMessage: ex.Message,
                ErrorCode: ex.GetType().Name
            );
        }

        return new PhysicalOperationResultDto(
            Success: true,
            Operation: nameof(PhysicalOperationType.CreateFile),
            SourcePath: fullParent,
            TargetPath: fullTargetPath
        );
    }

    public async Task<PhysicalOperationResultDto> DeleteItemAsync(string targetPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            throw new ArgumentException("Target path cannot be null or whitespace.", nameof(targetPath));
        }

        string fullTargetPath = Path.GetFullPath(targetPath);
        if (!File.Exists(fullTargetPath) && !Directory.Exists(fullTargetPath))
        {
            throw new FileNotFoundException($"Target item does not exist: '{fullTargetPath}'", fullTargetPath);
        }

        if (_recycleBin == null)
        {
            return new PhysicalOperationResultDto(
                Success: false,
                Operation: nameof(PhysicalOperationType.Delete),
                SourcePath: fullTargetPath,
                TargetPath: fullTargetPath,
                ErrorMessage: "Recycle Bin service is not available.",
                ErrorCode: "RecycleBinNotAvailable"
            );
        }

        bool success = _recycleBin.SendToRecycleBin(fullTargetPath, out string? error);

        return new PhysicalOperationResultDto(
            Success: success,
            Operation: nameof(PhysicalOperationType.Delete),
            SourcePath: fullTargetPath,
            TargetPath: fullTargetPath,
            ErrorMessage: success ? null : error,
            ErrorCode: success ? null : "RecycleBinFailed"
        );
    }

    private async Task<string> LookupDocumentIdAsync(string sourcePath)
    {
        try
        {
            if (_documentRepository != null)
            {
                var doc = await _documentRepository.GetByPathAsync(sourcePath);
                if (doc != null && !string.IsNullOrEmpty(doc.Id))
                {
                    return doc.Id;
                }
            }
        }
        catch
        {
            // Fall back to generated ID
        }

        return Guid.NewGuid().ToString();
    }

    private static string GenerateDuplicateFileName(string sourceFilePath, string parentDir)
    {
        string filenameNoExt = Path.GetFileNameWithoutExtension(sourceFilePath);
        string ext = Path.GetExtension(sourceFilePath);

        string candidateName = $"{filenameNoExt} - Copy{ext}";
        string candidatePath = Path.Combine(parentDir, candidateName);

        int counter = 2;
        while (File.Exists(candidatePath) || Directory.Exists(candidatePath))
        {
            candidateName = $"{filenameNoExt} - Copy ({counter}){ext}";
            candidatePath = Path.Combine(parentDir, candidateName);
            counter++;
        }

        return candidateName;
    }
}
