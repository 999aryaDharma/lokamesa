using System;
using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Core.Pdf;

namespace DocumentIDE.Infrastructure.Word;

/// <summary>
/// PDF-01 — Core queue abstraction over the WORD-03 dedicated STA
/// <see cref="WordAutomationQueue"/>. Reuses WORD-03 COM threading as-is;
/// no COM threading is reimplemented here.
/// </summary>
public sealed class WordAutomationQueueAdapter : IWordAutomationQueue
{
    private readonly WordAutomationQueue _inner;

    public WordAutomationQueueAdapter(WordAutomationQueue inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public Task<T> EnqueueAsync<T>(Func<T> operation, CancellationToken cancellationToken = default)
    {
        return _inner.EnqueueAsync(operation, cancellationToken);
    }
}
