using System.Threading.Tasks;

namespace DocumentIDE.Core.Domain.Repositories;

public interface IDocumentRepository
{
    Task<Document?> GetByIdAsync(string id);
    Task<Document?> GetByPathAsync(string currentPath);
    Task<Document?> GetByVolumeAndObjectIdAsync(string volumeId, string objectId);
    Task<System.Collections.Generic.List<Document>> GetDocumentsByFingerprintAsync(string fingerprint);
    Task CreateAsync(Document document);
    Task UpdateAsync(Document document);
    Task DeleteAsync(string id);
}
