using System;

namespace DocumentIDE.Core.Filesystem;

public enum PhysicalOperationType
{
    Rename,
    Move,
    Duplicate,
    CreateDirectory,
    CreateFile,
    Delete
}

public record PhysicalOperationResultDto(
    bool Success,
    string Operation,
    string SourcePath,
    string TargetPath,
    string? ErrorMessage = null,
    string? ErrorCode = null
);

public record RenamePhysicalItemRequest(
    string CurrentPath,
    string NewBaseName
);

public record MovePhysicalItemRequest(
    string SourcePath,
    string DestinationDirectoryPath,
    string? NewBaseName = null
);

public record DuplicatePhysicalItemRequest(
    string SourceFilePath,
    string? CustomNewBaseName = null
);

public record CreateDirectoryRequest(
    string ParentDirectoryPath,
    string NewDirectoryName
);

public record CreateFileRequest(
    string ParentDirectoryPath,
    string NewFileName,
    string FileType = "markdown",
    string? InitialContent = null
);

public record DeletePhysicalItemRequest(
    string TargetPath
);
