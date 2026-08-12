using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Semantic;
using DocumentIDE.Infrastructure.Persistence;
using DocumentIDE.Infrastructure.Persistence.Repositories;
using DocumentIDE.Infrastructure.Semantic;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DocumentIDE.IntegrationTests.Semantic;

public class LocalEmbeddingProviderTests : IDisposable
{
    private readonly string _dbPath;
    private readonly AppDb _appDb;
    private readonly SqliteEmbeddingStore _store;

    public LocalEmbeddingProviderTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"docide_local_embedding_test_{Guid.NewGuid():N}.db");
        _appDb = new AppDb(_dbPath);
        _appDb.InitializeAsync().GetAwaiter().GetResult();
        _store = new SqliteEmbeddingStore(_appDb);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch { }
        }
        var walPath = _dbPath + "-wal";
        if (File.Exists(walPath))
        {
            try { File.Delete(walPath); } catch { }
        }
        var shmPath = _dbPath + "-shm";
        if (File.Exists(shmPath))
        {
            try { File.Delete(shmPath); } catch { }
        }
    }

    [Fact]
    public async Task Requirement1_EmbeddingGenerationWorksOffline()
    {
        // Arrange
        var provider = new LocalEmbeddingProvider(dimensions: 128);

        // Assert contract properties
        Assert.False(provider.IsExternal, "Local provider must not be marked as external.");
        Assert.Equal("local", provider.ProviderId);
        Assert.Equal("local-lightweight-v1", provider.ModelId);
        Assert.Equal(128, provider.Dimensions);

        bool isAvailable = await provider.IsAvailableAsync();
        Assert.True(isAvailable, "Local embedding provider should be available offline.");

        // Act
        var inputs = new List<string>
        {
            "Document IDE local search and indexing specification.",
            "Offline vector embedding provider implementation using C#."
        };

        var vectors = await provider.GenerateEmbeddingsAsync(inputs);

        // Assert
        Assert.NotNull(vectors);
        Assert.Equal(2, vectors.Count);
        Assert.Equal(128, vectors[0].Length);
        Assert.Equal(128, vectors[1].Length);

        // Verify vectors are non-zero normalized float arrays
        float norm0 = 0f;
        foreach (var val in vectors[0])
        {
            norm0 += val * val;
        }
        Assert.True(Math.Abs(Math.Sqrt(norm0) - 1.0) < 1e-3, "Vector length should be L2 normalized.");
    }

    [Fact]
    public async Task Requirement2_IndexRebuildsFromDocuments()
    {
        // Arrange
        var provider = new LocalEmbeddingProvider(dimensions: 128);
        var coordinator = new EmbeddingCoordinator(provider, _store);

        var doc1 = ("doc-1", "First document content for local embedding test.", "fp-v1");
        var doc2 = ("doc-2", "Second document content for vector search indexing.", "fp-v2");

        // Seed store with an old/stale embedding
        var staleEmbedding = new DocumentEmbedding
        {
            DocumentId = "doc-1",
            ProviderId = "old-provider",
            ModelId = "old-model",
            ContentFingerprint = "fp-old",
            Vector = new float[128],
            CreatedAt = DateTime.UtcNow.ToString("O")
        };
        await _store.SaveEmbeddingAsync(staleEmbedding);

        // Act: Rebuild index from current documents
        var documentsToRebuild = new List<(string DocumentId, string Text, string ContentFingerprint)>
        {
            doc1,
            doc2
        };

        var rebuildResults = await provider.RebuildIndexAsync(coordinator, _store, documentsToRebuild);

        // Assert
        Assert.Equal(2, rebuildResults.Count);
        Assert.All(rebuildResults, r => Assert.True(r.IsSuccess));

        // Verify fresh embeddings are stored in SQLite
        var stored1 = await coordinator.GetValidEmbeddingAsync("doc-1", "fp-v1");
        var stored2 = await coordinator.GetValidEmbeddingAsync("doc-2", "fp-v2");

        Assert.NotNull(stored1);
        Assert.NotNull(stored2);
        Assert.Equal("local", stored1.ProviderId);
        Assert.Equal("local-lightweight-v1", stored1.ModelId);
        Assert.Equal("fp-v1", stored1.ContentFingerprint);
        Assert.Equal("fp-v2", stored2.ContentFingerprint);
    }

    [Fact]
    public async Task Requirement3_ResourceUseIsBoundedDuringBatchEmbedding()
    {
        // Arrange: Bounded local provider with max concurrency = 2
        var provider = new LocalEmbeddingProvider(dimensions: 128, maxConcurrency: 2);

        var items = Enumerable.Range(1, 50).Select(i => $"Batch document item content #{i} with varied words for hashing.").ToList();

        // Act: Run batch embedding with cancellation token support
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var vectors = await provider.GenerateEmbeddingsAsync(items, cts.Token);

        // Assert
        Assert.Equal(50, vectors.Count);
        Assert.All(vectors, v => Assert.Equal(128, v.Length));

        // Test cancellation behavior
        using var canceledCts = new CancellationTokenSource();
        canceledCts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await provider.GenerateEmbeddingsAsync(items, canceledCts.Token);
        });
    }

    [Fact]
    public async Task EdgeCase_ModelUnavailable_ReturnsFalseAndEmptyOrFailure()
    {
        // Arrange
        var provider = new LocalEmbeddingProvider(dimensions: 128);
        provider.SetAvailable(false);

        // Act
        bool isAvailable = await provider.IsAvailableAsync();

        // Assert
        Assert.False(isAvailable, "IsAvailableAsync must return false when provider is disabled/unavailable.");

        // Act & Assert exception/failure
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await provider.GenerateEmbeddingsAsync(new[] { "Test text" });
        });
    }

    [Fact]
    public async Task EdgeCase_EmbeddingIsStaleRelativeToContent_InvalidatesInStore()
    {
        // Arrange
        var provider = new LocalEmbeddingProvider(dimensions: 128);
        var coordinator = new EmbeddingCoordinator(provider, _store);

        string docId = "doc-stale-test";
        string oldFingerprint = "fp-old";
        string newFingerprint = "fp-new";

        await coordinator.GenerateAndStoreEmbeddingAsync(docId, "Original text", oldFingerprint);

        // Verify valid with old fingerprint
        var validOld = await coordinator.GetValidEmbeddingAsync(docId, oldFingerprint);
        Assert.NotNull(validOld);

        // Act: Query with new fingerprint
        var validNew = await coordinator.GetValidEmbeddingAsync(docId, newFingerprint);

        // Assert
        Assert.Null(validNew);
        var inStore = await _store.GetEmbeddingAsync(docId);
        Assert.Null(inStore); // Verified deleted from SQLite store
    }
}
