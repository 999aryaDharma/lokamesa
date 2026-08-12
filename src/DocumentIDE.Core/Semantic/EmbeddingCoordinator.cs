using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Core.Security;
using DocumentIDE.Core.Settings;

namespace DocumentIDE.Core.Semantic;

public class EmbeddingCoordinator
{
    private readonly IEmbeddingProvider? _provider;
    private readonly IEmbeddingStore _store;
    private readonly ISettingsService? _settingsService;
    private readonly ISecretStore? _secretStore;

    public EmbeddingCoordinator(
        IEmbeddingProvider? provider = null,
        IEmbeddingStore? store = null,
        ISettingsService? settingsService = null,
        ISecretStore? secretStore = null)
    {
        _provider = provider;
        _store = store ?? new InMemoryEmbeddingStore();
        _settingsService = settingsService;
        _secretStore = secretStore;
    }

    public static string NormalizeInput(string rawText, int maxCharacters = 4000)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return string.Empty;
        }

        string normalized = Regex.Replace(rawText, @"\s+", " ").Trim();

        if (normalized.Length > maxCharacters)
        {
            normalized = normalized.Substring(0, maxCharacters).Trim();
        }

        return normalized;
    }

    public async Task<EmbeddingResult> GenerateAndStoreEmbeddingAsync(
        string documentId,
        string rawText,
        string contentFingerprint,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(documentId)) throw new ArgumentNullException(nameof(documentId));
        if (string.IsNullOrEmpty(contentFingerprint)) throw new ArgumentNullException(nameof(contentFingerprint));

        if (_provider == null)
        {
            return EmbeddingResult.Disabled();
        }

        if (_settingsService != null)
        {
            var settings = await _settingsService.GetSettingsAsync();
            if (_provider.IsExternal && !settings.EnableExternalEmbeddingProvider)
            {
                return EmbeddingResult.Disabled();
            }
        }
        else if (_provider.IsExternal)
        {
            return EmbeddingResult.Disabled();
        }

        bool available = await _provider.IsAvailableAsync(cancellationToken);
        if (!available)
        {
            return EmbeddingResult.Unavailable();
        }

        try
        {
            string normalizedText = NormalizeInput(rawText);
            var vectors = await _provider.GenerateEmbeddingsAsync(new[] { normalizedText }, cancellationToken);

            if (vectors == null || vectors.Count == 0 || vectors[0] == null)
            {
                return EmbeddingResult.Failure("EmptyOutput", "Embedding provider returned no vectors.");
            }

            var embedding = new DocumentEmbedding
            {
                DocumentId = documentId,
                ProviderId = _provider.ProviderId,
                ModelId = _provider.ModelId,
                ContentFingerprint = contentFingerprint,
                Vector = vectors[0],
                CreatedAt = DateTime.UtcNow.ToString("O")
            };

            await _store.SaveEmbeddingAsync(embedding, cancellationToken);
            return EmbeddingResult.Success(embedding);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return EmbeddingResult.Failure("ProviderError", ex.Message);
        }
    }

    public async Task<IReadOnlyList<EmbeddingResult>> BatchGenerateEmbeddingsAsync(
        IReadOnlyList<(string DocumentId, string Text, string ContentFingerprint)> items,
        int batchSize = 10,
        CancellationToken cancellationToken = default)
    {
        if (items == null) throw new ArgumentNullException(nameof(items));
        if (batchSize <= 0) throw new ArgumentOutOfRangeException(nameof(batchSize), "Batch size must be greater than zero.");

        var results = new List<EmbeddingResult>();

        if (_provider == null)
        {
            foreach (var _ in items)
            {
                results.Add(EmbeddingResult.Disabled());
            }
            return results;
        }

        if (_settingsService != null)
        {
            var settings = await _settingsService.GetSettingsAsync();
            if (_provider.IsExternal && !settings.EnableExternalEmbeddingProvider)
            {
                foreach (var _ in items)
                {
                    results.Add(EmbeddingResult.Disabled());
                }
                return results;
            }
        }
        else if (_provider.IsExternal)
        {
            foreach (var _ in items)
            {
                results.Add(EmbeddingResult.Disabled());
            }
            return results;
        }

        bool available = await _provider.IsAvailableAsync(cancellationToken);
        if (!available)
        {
            foreach (var _ in items)
            {
                results.Add(EmbeddingResult.Unavailable());
            }
            return results;
        }

        for (int i = 0; i < items.Count; i += batchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var chunk = items.Skip(i).Take(batchSize).ToList();
            var normalizedTexts = chunk.Select(c => NormalizeInput(c.Text)).ToList();

            try
            {
                var vectors = await _provider.GenerateEmbeddingsAsync(normalizedTexts, cancellationToken);

                for (int j = 0; j < chunk.Count; j++)
                {
                    var item = chunk[j];
                    if (vectors != null && j < vectors.Count && vectors[j] != null)
                    {
                        var embedding = new DocumentEmbedding
                        {
                            DocumentId = item.DocumentId,
                            ProviderId = _provider.ProviderId,
                            ModelId = _provider.ModelId,
                            ContentFingerprint = item.ContentFingerprint,
                            Vector = vectors[j],
                            CreatedAt = DateTime.UtcNow.ToString("O")
                        };

                        await _store.SaveEmbeddingAsync(embedding, cancellationToken);
                        results.Add(EmbeddingResult.Success(embedding));
                    }
                    else
                    {
                        results.Add(EmbeddingResult.Failure("EmptyOutput", "Embedding vector was missing."));
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                foreach (var _ in chunk)
                {
                    results.Add(EmbeddingResult.Failure("ProviderError", ex.Message));
                }
            }
        }

        return results;
    }

    public async Task<DocumentEmbedding?> GetValidEmbeddingAsync(
        string documentId,
        string currentContentFingerprint,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(documentId)) return null;

        var stored = await _store.GetEmbeddingAsync(documentId, cancellationToken);
        if (stored == null) return null;

        if (_provider == null)
        {
            if (!string.Equals(stored.ContentFingerprint, currentContentFingerprint, StringComparison.Ordinal))
            {
                await _store.DeleteEmbeddingAsync(documentId, cancellationToken);
                return null;
            }
            return stored;
        }

        if (!string.Equals(stored.ContentFingerprint, currentContentFingerprint, StringComparison.Ordinal) ||
            !string.Equals(stored.ProviderId, _provider.ProviderId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(stored.ModelId, _provider.ModelId, StringComparison.OrdinalIgnoreCase))
        {
            await _store.DeleteEmbeddingAsync(documentId, cancellationToken);
            return null;
        }

        return stored;
    }
}
