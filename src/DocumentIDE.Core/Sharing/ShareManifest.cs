using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Domain.Sharing;

namespace DocumentIDE.Core.Sharing;

public class ShareManifest
{
    public string ShareId { get; set; } = string.Empty;
    public string Mode { get; set; } = ShareMode.Snapshot.ToString();
    public string AccessMode { get; set; } = DocumentIDE.Core.Domain.Sharing.AccessMode.SecretToken.ToString();
    public bool AllowOriginalDownload { get; set; }
    public bool AllowPdfDownload { get; set; }
    public string CreatedAt { get; set; } = DateTime.UtcNow.ToString("O");
    public List<SanitizedDocumentMetadata> Documents { get; set; } = new List<SanitizedDocumentMetadata>();

    public static ShareManifest Create(Share share, IEnumerable<Document> candidateDocuments)
    {
        if (share == null) throw new ArgumentNullException(nameof(share));
        if (candidateDocuments == null) throw new ArgumentNullException(nameof(candidateDocuments));

        var memberMap = (share.Members ?? new List<ShareMember>())
            .Where(m => m != null && !string.IsNullOrWhiteSpace(m.DocumentId))
            .GroupBy(m => m.DocumentId!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().DisplayPath, StringComparer.Ordinal);

        var manifest = new ShareManifest
        {
            ShareId = share.Id,
            Mode = share.Mode.ToString(),
            AccessMode = share.AccessMode.ToString(),
            AllowOriginalDownload = share.AllowOriginalDownload,
            AllowPdfDownload = share.AllowPdfDownload,
            CreatedAt = share.CreatedAt
        };

        foreach (var doc in candidateDocuments)
        {
            // Explicit membership check: Live or Snapshot mode DOES NOT auto-add non-members.
            if (!memberMap.TryGetValue(doc.Id, out var displayPath))
            {
                continue;
            }

            var sanitizedDisplayName = SanitizeDisplayPath(displayPath, doc.Filename ?? "unnamed_document");

            var ext = Path.GetExtension(sanitizedDisplayName);
            if (string.IsNullOrWhiteSpace(ext) && !string.IsNullOrWhiteSpace(doc.Extension))
            {
                ext = Path.GetExtension(doc.Extension.Trim());
            }

            manifest.Documents.Add(new SanitizedDocumentMetadata
            {
                DocumentId = doc.Id,
                DisplayName = sanitizedDisplayName,
                Extension = ext ?? string.Empty,
                DocumentType = doc.DocumentType,
                SizeBytes = doc.SizeBytes,
                ModifiedAt = doc.ModifiedAt,
                ContentFingerprint = doc.ContentFingerprint
            });
        }

        return manifest;
    }

    private static string SanitizeDisplayPath(string? displayPath, string defaultFilename)
    {
        var sanitized = ExtractFileName(displayPath);
        if (string.IsNullOrWhiteSpace(sanitized) || sanitized == "." || sanitized == "..")
        {
            sanitized = ExtractFileName(defaultFilename);
        }
        if (string.IsNullOrWhiteSpace(sanitized) || sanitized == "." || sanitized == "..")
        {
            sanitized = "unnamed_document";
        }
        return sanitized;
    }

    private static string? ExtractFileName(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        var trimmed = input.Trim();

        // Strip drive letter or colon specifier (e.g. C:, D:, stream:)
        var lastColon = trimmed.LastIndexOf(':');
        if (lastColon >= 0)
        {
            trimmed = trimmed.Substring(lastColon + 1).TrimStart('\\', '/');
        }

        // Normalize directory separators for cross-platform Path.GetFileName
        var normalized = trimmed.Replace('\\', '/');

        // Extract strictly the filename component to strip all directory paths, UNC prefixes, and relative traversals
        var fileName = Path.GetFileName(normalized).Trim();

        return fileName;
    }
}
