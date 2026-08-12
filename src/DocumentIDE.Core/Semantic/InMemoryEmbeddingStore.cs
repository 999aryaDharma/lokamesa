using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DocumentIDE.Core.Semantic;

public class InMemoryEmbeddingStore : IEmbeddingStore
{
    private readonly ConcurrentDictionary<string, DocumentEmbedding> _embeddings = new(StringComparer.OrdinalIgnoreCase);

    public Task SaveEmbeddingAsync(DocumentEmbedding embedding, CancellationToken cancellationToken = default)
    {
        if (embedding == null) throw new ArgumentNullException(nameof(embedding));
        if (string.IsNullOrEmpty(embedding.DocumentId)) throw new ArgumentException("DocumentId cannot be null or empty.", nameof(embedding));

        _embeddings[embedding.DocumentId] = embedding;
        return Task.CompletedTask;
    }

    public Task<DocumentEmbedding?> GetEmbeddingAsync(string documentId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(documentId)) return Task.FromResult<DocumentEmbedding?>(null);

        if (_embeddings.TryGetValue(documentId, out var embedding))
        {
            return Task.FromResult<DocumentEmbedding?>(embedding);
        }

        return Task.FromResult<DocumentEmbedding?>(null);
    }

    public Task DeleteEmbeddingAsync(string documentId, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(documentId))
        {
            _embeddings.TryRemove(documentId, out _);
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DocumentEmbedding>> GetAllEmbeddingsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<DocumentEmbedding> list = _embeddings.Values.ToList();
        return Task.FromResult(list);
    }
}
