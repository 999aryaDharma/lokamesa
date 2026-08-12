using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DocumentIDE.Core.Semantic;

public interface IEmbeddingStore
{
    Task SaveEmbeddingAsync(DocumentEmbedding embedding, CancellationToken cancellationToken = default);
    Task<DocumentEmbedding?> GetEmbeddingAsync(string documentId, CancellationToken cancellationToken = default);
    Task DeleteEmbeddingAsync(string documentId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DocumentEmbedding>> GetAllEmbeddingsAsync(CancellationToken cancellationToken = default);
}
