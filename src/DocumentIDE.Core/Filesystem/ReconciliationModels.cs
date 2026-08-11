using System;

namespace DocumentIDE.Core.Filesystem;

public enum FileDifferenceType
{
    Unchanged,
    Modified,
    New,
    Renamed,
    Missing,
    Locked,
    PermissionDenied
}

public record FileReconciliationAction(
    string Path,
    FileDifferenceType DifferenceType,
    string? DocumentId,
    string FolderSourceId
);

public record ReconciliationProgress(
    string WorkspaceId,
    bool IsReconciling,
    int TotalFilesDiscovered,
    int ProcessedFiles,
    int ChangedFiles,
    int MissingFiles,
    string? CurrentFolderPath = null,
    string? ErrorMessage = null
);

public record WorkspaceReconciliationResult(
    string WorkspaceId,
    int TotalSources,
    int TotalFiles,
    int UnchangedCount,
    int ModifiedCount,
    int NewCount,
    int RenamedCount,
    int MissingCount,
    TimeSpan Elapsed
);
