using System;
using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Core.Domain;

namespace DocumentIDE.Core.Filesystem;

public interface IMoveCorrelationService
{
    void RecordMoveIntent(string documentId, string sourcePath, string destinationPath, TimeSpan? ttl = null);
    PhysicalMoveIntent? FindActiveIntentBySource(string sourcePath);
    PhysicalMoveIntent? FindActiveIntentByDestination(string destinationPath);
    void CompleteIntent(string intentId);

    Task<Document?> CorrelateSameVolumeMoveAsync(string newPath, CancellationToken cancellationToken = default);
    Task<RebindCandidateListDto> GetRebindCandidatesAsync(string documentId, string? searchDirectory = null, CancellationToken cancellationToken = default);
    Task<RebindResultDto> RebindDocumentAsync(string documentId, string targetPath, bool forceRebind = false, CancellationToken cancellationToken = default);
}
