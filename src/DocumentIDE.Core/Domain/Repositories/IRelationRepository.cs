using System.Collections.Generic;
using System.Threading.Tasks;

namespace DocumentIDE.Core.Domain.Repositories;

public interface IRelationRepository
{
    Task AddAsync(Relation relation);
    Task AddRangeAsync(IEnumerable<Relation> relations);
    Task<Relation?> GetByIdAsync(string id);
    Task<IReadOnlyList<Relation>> GetRelationsAsync(string workspaceId, string? origin = null, string? type = null);
    Task<IReadOnlyList<Relation>> GetBySourceDocumentAsync(string workspaceId, string sourceDocumentId);
    Task<IReadOnlyList<Relation>> GetByTargetDocumentAsync(string workspaceId, string targetDocumentId);
    Task<IReadOnlyList<Relation>> GetBacklinksAsync(string workspaceId, string targetDocumentId);
    Task ReplaceDerivedRelationsAsync(string workspaceId, string sourceDocumentId, IEnumerable<Relation> newDerivedRelations);
    Task DeleteAsync(string id);
    Task DeleteManualRelationAsync(string workspaceId, string sourceDocumentId, string targetDocumentId, string relationType);
}
