using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DocumentIDE.Core.Semantic;

public interface IEmbeddingProvider
{
    string ProviderId { get; }
    string ModelId { get; }
    int Dimensions { get; }
    bool IsExternal { get; }

    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(
        IReadOnlyList<string> inputs,
        CancellationToken cancellationToken = default);
}
