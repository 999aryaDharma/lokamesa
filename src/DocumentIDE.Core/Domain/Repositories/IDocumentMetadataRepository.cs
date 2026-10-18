using System.Threading.Tasks;

namespace DocumentIDE.Core.Domain.Repositories;

public interface IDocumentMetadataRepository
{
    Task<DocumentMetadata?> GetDocxMetadataAsync(string documentId);
    Task SaveDocxMetadataAsync(DocumentMetadata metadata);

    Task<MarkdownDocumentMetadata?> GetMarkdownMetadataAsync(string documentId);
    Task SaveMarkdownMetadataAsync(MarkdownDocumentMetadata metadata);

    Task DeleteMetadataAsync(string documentId);
    Task DeleteAllMetadataAsync();

    Task MarkStatusAsync(string documentId, string status, string? lastError = null);
    Task<DocumentMetadataDto?> GetMetadataDtoAsync(string documentId, string currentParserVersion = "1.0.0");
}
