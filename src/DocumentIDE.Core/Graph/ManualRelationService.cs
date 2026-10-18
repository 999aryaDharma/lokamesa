using System;
using System.Threading.Tasks;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Domain.Repositories;

namespace DocumentIDE.Core.Graph;

/// <summary>
/// Controlled vocabulary for manual relation types (spec §12).
/// </summary>
public static class ManualRelationType
{
    /// <summary>General explicit reference.</summary>
    public const string References = RelationType.ManualRelation;

    /// <summary>Source document is related to target.</summary>
    public const string RelatedTo = "RelatedTo";

    /// <summary>Source document depends on target.</summary>
    public const string DependsOn = "DependsOn";

    /// <summary>All recognised manual relation types.</summary>
    public static readonly string[] All = { References, RelatedTo, DependsOn };
}

/// <summary>
/// Service contract for GR-04 — Manual Relations.
/// </summary>
public interface IManualRelationService
{
    /// <summary>
    /// Create a new manual relation. Returns the new relation ID.
    /// Does NOT modify any Markdown file (spec §12).
    /// </summary>
    Task<string> CreateAsync(string workspaceId, string sourceDocumentId, string targetDocumentId, string relationType);

    /// <summary>
    /// Change the type of an existing manual relation.
    /// </summary>
    Task UpdateTypeAsync(string workspaceId, string relationId, string newRelationType);

    /// <summary>
    /// Delete a manual relation by ID.
    /// Does NOT modify any Markdown file (spec §12).
    /// </summary>
    Task DeleteAsync(string workspaceId, string relationId);
}

/// <summary>
/// Implementation of <see cref="IManualRelationService"/> for GR-04.
/// </summary>
public class ManualRelationService : IManualRelationService
{
    private readonly IRelationRepository _relationRepository;

    /// <param name="relationRepository">Relation store (no filesystem/Markdown dependency).</param>
    public ManualRelationService(IRelationRepository relationRepository)
    {
        _relationRepository = relationRepository ?? throw new ArgumentNullException(nameof(relationRepository));
    }

    /// <inheritdoc/>
    public async Task<string> CreateAsync(
        string workspaceId,
        string sourceDocumentId,
        string targetDocumentId,
        string relationType)
    {
        if (string.IsNullOrWhiteSpace(workspaceId))
            throw new ArgumentException("Workspace ID cannot be null or empty.", nameof(workspaceId));
        if (string.IsNullOrWhiteSpace(sourceDocumentId))
            throw new ArgumentException("Source document ID cannot be null or empty.", nameof(sourceDocumentId));
        if (string.IsNullOrWhiteSpace(targetDocumentId))
            throw new ArgumentException("Target document ID cannot be null or empty.", nameof(targetDocumentId));
        if (string.IsNullOrWhiteSpace(relationType))
            throw new ArgumentException("Relation type cannot be null or empty.", nameof(relationType));

        var relation = new Relation
        {
            Id = Guid.NewGuid().ToString(),
            WorkspaceId = workspaceId,
            SourceDocumentId = sourceDocumentId,
            TargetDocumentId = targetDocumentId,
            // Manual relations use RelationType from the controlled vocabulary
            RelationType = relationType,
            RelationOrigin = RelationOrigin.Manual,
            Confidence = 1.0,
            CreatedAt = DateTime.UtcNow.ToString("O"),
            UpdatedAt = DateTime.UtcNow.ToString("O"),
        };

        await _relationRepository.AddAsync(relation);
        return relation.Id;
    }

    /// <inheritdoc/>
    public async Task UpdateTypeAsync(string workspaceId, string relationId, string newRelationType)
    {
        if (string.IsNullOrWhiteSpace(workspaceId))
            throw new ArgumentException("Workspace ID cannot be null or empty.", nameof(workspaceId));
        if (string.IsNullOrWhiteSpace(relationId))
            throw new ArgumentException("Relation ID cannot be null or empty.", nameof(relationId));
        if (string.IsNullOrWhiteSpace(newRelationType))
            throw new ArgumentException("Relation type cannot be null or empty.", nameof(newRelationType));

        var existing = await _relationRepository.GetByIdAsync(relationId);
        if (existing == null)
            throw new InvalidOperationException($"Relation '{relationId}' not found.");
        if (existing.RelationOrigin != RelationOrigin.Manual)
            throw new InvalidOperationException($"Relation '{relationId}' is not a manual relation.");

        existing.RelationType = newRelationType;
        existing.UpdatedAt = DateTime.UtcNow.ToString("O");

        // Replace via delete + re-add (repository does not expose Update yet)
        await _relationRepository.DeleteAsync(relationId);
        await _relationRepository.AddAsync(existing);
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(string workspaceId, string relationId)
    {
        if (string.IsNullOrWhiteSpace(workspaceId))
            throw new ArgumentException("Workspace ID cannot be null or empty.", nameof(workspaceId));
        if (string.IsNullOrWhiteSpace(relationId))
            throw new ArgumentException("Relation ID cannot be null or empty.", nameof(relationId));

        // Verify it's a manual relation before deletion (defensive)
        var existing = await _relationRepository.GetByIdAsync(relationId);
        if (existing != null && existing.RelationOrigin != RelationOrigin.Manual)
            throw new InvalidOperationException($"Relation '{relationId}' is not a manual relation and cannot be deleted via ManualRelationService.");

        // No Markdown file mutation; only repository interaction.
        await _relationRepository.DeleteAsync(relationId);
    }
}
