using System;
using System.Collections.Generic;

namespace DocumentIDE.Core.Domain.Sharing;

public class Share
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public ShareMode Mode { get; set; } = ShareMode.Snapshot;
    public AccessMode AccessMode { get; set; } = AccessMode.SecretToken;
    public string TokenHash { get; set; } = string.Empty;
    public bool AllowOriginalDownload { get; set; } = false;
    public bool AllowPdfDownload { get; set; } = false;
    public string CreatedAt { get; set; } = DateTime.UtcNow.ToString("O");
    public string UpdatedAt { get; set; } = DateTime.UtcNow.ToString("O");
    public string? ExpiresAt { get; set; }

    public List<ShareMember> Members { get; set; } = new List<ShareMember>();
    public List<ShareSnapshotArtifact> SnapshotArtifacts { get; set; } = new List<ShareSnapshotArtifact>();

    /// <summary>
    /// Snapshots explicit membership at creation from a collection of documents.
    /// New members added to the collection later do not automatically become share members.
    /// </summary>
    public void SnapshotMembersFromCollection(IEnumerable<Document> initialDocuments)
    {
        if (initialDocuments == null) throw new ArgumentNullException(nameof(initialDocuments));

        foreach (var doc in initialDocuments)
        {
            if (doc == null || string.IsNullOrWhiteSpace(doc.Id)) continue;

            if (Members.Exists(m => string.Equals(m.DocumentId, doc.Id, StringComparison.Ordinal)))
                continue;

            Members.Add(new ShareMember
            {
                Id = Guid.NewGuid().ToString(),
                ShareId = Id,
                DocumentId = doc.Id,
                DisplayPath = doc.Filename ?? "unnamed_document",
                AddedAt = DateTime.UtcNow.ToString("O")
            });
        }
    }
}
