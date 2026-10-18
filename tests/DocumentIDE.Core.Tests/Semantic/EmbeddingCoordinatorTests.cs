using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Core.Security;
using DocumentIDE.Core.Semantic;
using DocumentIDE.Core.Settings;
using Xunit;

namespace DocumentIDE.Core.Tests.Semantic;

public class EmbeddingCoordinatorTests
{
    private readonly InMemoryEmbeddingStore _store;
    private MockEmbeddingProvider _provider;
    private EmbeddingCoordinator _coordinator;

    public EmbeddingCoordinatorTests()
    {
        _store = new InMemoryEmbeddingStore();
        _provider = new MockEmbeddingProvider("mock-provider", "mock-model-v1", dimensions: 4, isExternal: false);
        _coordinator = new EmbeddingCoordinator(_provider, _store);
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_WithMockProvider_PerformsRoundTripAndStoresEmbedding()
    {
        // Arrange
        string docId = "doc-1";
        string rawText = "  Hello   World!\r\n\r\nThis is   a test document.  ";
        string contentFingerprint = "hash-123";

        // Act
        var result = await _coordinator.GenerateAndStoreEmbeddingAsync(docId, rawText, contentFingerprint);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Embedding);
        Assert.Equal(docId, result.Embedding.DocumentId);
        Assert.Equal("mock-provider", result.Embedding.ProviderId);
        Assert.Equal("mock-model-v1", result.Embedding.ModelId);
        Assert.Equal(contentFingerprint, result.Embedding.ContentFingerprint);
        Assert.Equal(4, result.Embedding.Vector.Length);

        // Verify stored in repository
        var stored = await _coordinator.GetValidEmbeddingAsync(docId, contentFingerprint);
        Assert.NotNull(stored);
        Assert.Equal(result.Embedding.Vector, stored.Vector);
    }

    [Fact]
    public void NormalizeInput_PreparesTextCorrectly()
    {
        // Arrange
        string rawText = "Line 1\r\n  Line 2\t\tLine 3   \n   Line 4  ";

        // Act
        string normalized = EmbeddingCoordinator.NormalizeInput(rawText, maxCharacters: 100);

        // Assert
        Assert.Equal("Line 1 Line 2 Line 3 Line 4", normalized);
    }

    [Fact]
    public async Task GetValidEmbeddingAsync_WhenContentFingerprintChanges_InvalidatesEmbeddingAndReturnsNull()
    {
        // Arrange
        string docId = "doc-1";
        string rawText = "Initial content";
        string initialFingerprint = "hash-v1";
        string updatedFingerprint = "hash-v2";

        await _coordinator.GenerateAndStoreEmbeddingAsync(docId, rawText, initialFingerprint);

        // Act
        var validBefore = await _coordinator.GetValidEmbeddingAsync(docId, initialFingerprint);
        var validAfter = await _coordinator.GetValidEmbeddingAsync(docId, updatedFingerprint);
        var storedAfter = await _store.GetEmbeddingAsync(docId);

        // Assert
        Assert.NotNull(validBefore);
        Assert.Null(validAfter);
        Assert.Null(storedAfter); // Verified that stale embedding was deleted from store
    }

    [Fact]
    public async Task GetValidEmbeddingAsync_WhenModelOrProviderIdChanges_InvalidatesEmbeddingAndReturnsNull()
    {
        // Arrange
        string docId = "doc-2";
        string rawText = "Test content";
        string fingerprint = "hash-100";

        await _coordinator.GenerateAndStoreEmbeddingAsync(docId, rawText, fingerprint);

        // Change coordinator's active provider model
        var newProvider = new MockEmbeddingProvider("mock-provider", "mock-model-v2", dimensions: 4, isExternal: false);
        var newCoordinator = new EmbeddingCoordinator(newProvider, _store);

        // Act
        var validAfterModelChange = await newCoordinator.GetValidEmbeddingAsync(docId, fingerprint);

        // Assert
        Assert.Null(validAfterModelChange);

        // Now test provider change
        var diffProvider = new MockEmbeddingProvider("other-provider", "mock-model-v1", dimensions: 4, isExternal: false);
        var diffCoordinator = new EmbeddingCoordinator(diffProvider, _store);

        await _coordinator.GenerateAndStoreEmbeddingAsync(docId, rawText, fingerprint);
        var validAfterProviderChange = await diffCoordinator.GetValidEmbeddingAsync(docId, fingerprint);

        Assert.Null(validAfterProviderChange);
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_WhenNoProviderConfigured_ReturnsDisabledWithoutThrowing()
    {
        // Arrange - No provider
        var unconfiguredCoordinator = new EmbeddingCoordinator(provider: null, store: _store);

        // Act
        var result = await unconfiguredCoordinator.GenerateAndStoreEmbeddingAsync("doc-no-provider", "Some text", "fp-1");
        var validEmbedding = await unconfiguredCoordinator.GetValidEmbeddingAsync("doc-no-provider", "fp-1");

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("Disabled", result.FailureReason);
        Assert.Null(validEmbedding);
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_WhenExternalProviderWithoutOptIn_ReturnsDisabled()
    {
        // Arrange - External provider, settings default (EnableExternalEmbeddingProvider = false)
        var externalProvider = new MockEmbeddingProvider("openai", "text-embedding-3-small", 1536, isExternal: true);
        var mockSettingsService = new MockSettingsService(new AppSettings { EnableExternalEmbeddingProvider = false });
        var externalCoordinator = new EmbeddingCoordinator(externalProvider, _store, mockSettingsService);

        // Act
        var result = await externalCoordinator.GenerateAndStoreEmbeddingAsync("doc-ext", "Text", "fp-ext");

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("Disabled", result.FailureReason);
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_WhenExternalProviderWithOptInEnabled_GeneratesEmbeddingSuccessfully()
    {
        // Arrange - External provider, opt-in enabled
        var externalProvider = new MockEmbeddingProvider("openai", "text-embedding-3-small", 1536, isExternal: true);
        var mockSettingsService = new MockSettingsService(new AppSettings { EnableExternalEmbeddingProvider = true });
        var externalCoordinator = new EmbeddingCoordinator(externalProvider, _store, mockSettingsService);

        // Act
        var result = await externalCoordinator.GenerateAndStoreEmbeddingAsync("doc-ext-opt", "Text", "fp-ext-opt");

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Embedding);
        Assert.Equal(1536, result.Embedding.Vector.Length);
    }

    [Fact]
    public async Task GenerateEmbeddingAsync_WhenProviderUnavailable_ReturnsUnavailableOutcome()
    {
        // Arrange
        _provider.Available = false;

        // Act
        var result = await _coordinator.GenerateAndStoreEmbeddingAsync("doc-unavail", "Text", "fp-u");

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("Unavailable", result.FailureReason);
    }

    [Fact]
    public async Task BatchGenerateEmbeddingsAsync_ProcessesInBoundedBatches_AndRespectsCancellation()
    {
        // Arrange
        var items = Enumerable.Range(1, 25).Select(i => (
            DocumentId: $"doc-{i}",
            Text: $"Sample document content number {i}",
            ContentFingerprint: $"fp-{i}"
        )).ToList();

        // Act
        var results = await _coordinator.BatchGenerateEmbeddingsAsync(items, batchSize: 10);

        // Assert
        Assert.Equal(25, results.Count);
        Assert.All(results, r => Assert.True(r.IsSuccess));
        Assert.Equal(25, (await _store.GetAllEmbeddingsAsync()).Count);

        // Verify provider was called in bounded batch sizes (max 10)
        Assert.Equal(25, _provider.CapturedInputs.Count);

        // Verify Cancellation
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await _coordinator.BatchGenerateEmbeddingsAsync(items, batchSize: 5, cancellationToken: cts.Token);
        });
    }

    private class MockEmbeddingProvider : IEmbeddingProvider
    {
        public string ProviderId { get; }
        public string ModelId { get; }
        public int Dimensions { get; }
        public bool IsExternal { get; }
        public bool Available { get; set; } = true;
        public List<string> CapturedInputs { get; } = new();

        public MockEmbeddingProvider(string providerId, string modelId, int dimensions = 4, bool isExternal = false)
        {
            ProviderId = providerId;
            ModelId = modelId;
            Dimensions = dimensions;
            IsExternal = isExternal;
        }

        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Available);
        }

        public Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(IReadOnlyList<string> inputs, CancellationToken cancellationToken = default)
        {
            CapturedInputs.AddRange(inputs);
            var results = new List<float[]>();
            foreach (var input in inputs)
            {
                var vector = new float[Dimensions];
                for (int i = 0; i < Dimensions; i++)
                {
                    vector[i] = (float)(input.Length + i);
                }
                results.Add(vector);
            }
            return Task.FromResult<IReadOnlyList<float[]>>(results);
        }
    }

    private class MockSettingsService : ISettingsService
    {
        private readonly AppSettings _settings;

        public MockSettingsService(AppSettings settings)
        {
            _settings = settings;
        }

        public Task<AppSettings> GetSettingsAsync()
        {
            return Task.FromResult(_settings);
        }

        public Task SaveSettingsAsync(AppSettings settings)
        {
            return Task.CompletedTask;
        }
    }
}
