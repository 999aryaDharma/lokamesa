using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Core.Semantic;

namespace DocumentIDE.Infrastructure.Semantic;

public class LocalEmbeddingProvider : IEmbeddingProvider
{
    private bool _isAvailable = true;
    private readonly SemaphoreSlim _concurrencySemaphore;
    private readonly int _maxCharactersPerInput;

    public string ProviderId => "local";
    public string ModelId => "local-lightweight-v1";
    public int Dimensions { get; }
    public bool IsExternal => false;

    public LocalEmbeddingProvider(int dimensions = 128, int maxConcurrency = 4, int maxCharactersPerInput = 4000)
    {
        if (dimensions <= 0) throw new ArgumentOutOfRangeException(nameof(dimensions), "Dimensions must be greater than zero.");
        if (maxConcurrency <= 0) throw new ArgumentOutOfRangeException(nameof(maxConcurrency), "Max concurrency must be greater than zero.");

        Dimensions = dimensions;
        _concurrencySemaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        _maxCharactersPerInput = maxCharactersPerInput;
    }

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_isAvailable);
    }

    public void SetAvailable(bool available)
    {
        _isAvailable = available;
    }

    public async Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(
        IReadOnlyList<string> inputs,
        CancellationToken cancellationToken = default)
    {
        if (inputs == null) throw new ArgumentNullException(nameof(inputs));
        if (!_isAvailable)
        {
            throw new InvalidOperationException("Local embedding provider is currently unavailable.");
        }

        var results = new float[inputs.Count][];

        for (int i = 0; i < inputs.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await _concurrencySemaphore.WaitAsync(cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                results[i] = ComputeLocalVector(inputs[i]);
            }
            finally
            {
                _concurrencySemaphore.Release();
            }
        }

        return results;
    }

    public async Task<IReadOnlyList<EmbeddingResult>> RebuildIndexAsync(
        EmbeddingCoordinator coordinator,
        IEmbeddingStore store,
        IReadOnlyList<(string DocumentId, string Text, string ContentFingerprint)> documents,
        CancellationToken cancellationToken = default)
    {
        if (coordinator == null) throw new ArgumentNullException(nameof(coordinator));
        if (store == null) throw new ArgumentNullException(nameof(store));
        if (documents == null) throw new ArgumentNullException(nameof(documents));

        // Generate and store fresh embeddings in bounded batches
        return await coordinator.BatchGenerateEmbeddingsAsync(documents, batchSize: 10, cancellationToken: cancellationToken);
    }

    private float[] ComputeLocalVector(string rawText)
    {
        var vector = new float[Dimensions];
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return vector;
        }

        string normalized = Regex.Replace(rawText, @"\s+", " ").Trim();
        if (normalized.Length > _maxCharactersPerInput)
        {
            normalized = normalized.Substring(0, _maxCharactersPerInput);
        }

        var words = normalized.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return vector;
        }

        // Hashing vectorizer + word & n-gram feature hashing
        foreach (var word in words)
        {
            uint hash = ComputeFnvalHash(word);
            int idx = (int)(hash % (uint)Dimensions);
            vector[idx] += 1.0f;

            // Character n-grams (trigrams) for subword feature capture
            if (word.Length >= 3)
            {
                for (int j = 0; j <= word.Length - 3; j++)
                {
                    string sub = word.Substring(j, 3);
                    uint subHash = ComputeFnvalHash(sub);
                    int subIdx = (int)(subHash % (uint)Dimensions);
                    vector[subIdx] += 0.5f;
                }
            }
        }

        // Sublinear term frequency scaling
        for (int i = 0; i < Dimensions; i++)
        {
            if (vector[i] > 0f)
            {
                vector[i] = 1.0f + (float)Math.Log(vector[i]);
            }
        }

        // L2 Normalization
        double sumSq = 0;
        for (int i = 0; i < Dimensions; i++)
        {
            sumSq += vector[i] * vector[i];
        }

        if (sumSq > 1e-12)
        {
            float norm = (float)Math.Sqrt(sumSq);
            for (int i = 0; i < Dimensions; i++)
            {
                vector[i] /= norm;
            }
        }

        return vector;
    }

    private static uint ComputeFnvalHash(string str)
    {
        uint hash = 2166136261;
        foreach (char c in str)
        {
            hash = (hash ^ c) * 16777619;
        }
        return hash;
    }
}
