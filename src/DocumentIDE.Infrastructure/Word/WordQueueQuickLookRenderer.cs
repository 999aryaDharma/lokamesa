using System;
using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Core.Preview;

namespace DocumentIDE.Infrastructure.Word;

/// <summary>
/// WORD-04 lazy render adapter over the WORD-03 dedicated STA queue.
///
/// The export operation itself runs on the single Word automation worker
/// (concurrency exactly 1); Quick Look page renders are therefore serialized
/// with PDF export and every other COM call. Word/COM failure states are
/// mapped to typed <see cref="WordRenderException"/> kinds so
/// <see cref="QuickLookService"/> can return inspectable outcomes.
///
/// The actual Word COM export body stays behind the injectable
/// <c>exportOperation</c>; the default reports Word as required/unavailable
/// until a real COM export lands (PDF-01 scope), which keeps this adapter
/// honest: it never pretends a render succeeded.
/// </summary>
public sealed class WordQueueQuickLookRenderer : IQuickLookRenderer
{
    private readonly WordAutomationQueue _queue;
    private readonly Func<QuickLookRenderCommand, QuickLookRenderOutput> _exportOperation;
    private readonly Func<bool> _isWordAvailable;

    public WordQueueQuickLookRenderer(
        WordAutomationQueue queue,
        Func<QuickLookRenderCommand, QuickLookRenderOutput>? exportOperation = null,
        Func<bool>? isWordAvailable = null)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _exportOperation = exportOperation ?? DefaultExport;
        _isWordAvailable = isWordAvailable ?? (() => false);
    }

    /// <summary>
    /// Renders one page lazily through the dedicated STA queue.
    /// Throws typed <see cref="WordRenderException"/> for Word
    /// unavailable/busy/modal/COM states; cancellation propagates.
    /// </summary>
    public Task<QuickLookRenderOutput> RenderPageAsync(
        QuickLookRenderCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!_isWordAvailable())
        {
            throw new WordRenderException(
                WordRenderFailureKind.Unavailable,
                "Microsoft Word is required for this operation.");
        }

        return RenderThroughQueueAsync(command, cancellationToken);
    }

    private async Task<QuickLookRenderOutput> RenderThroughQueueAsync(
        QuickLookRenderCommand command, CancellationToken cancellationToken)
    {
        try
        {
            return await _queue.EnqueueAsync(() => _exportOperation(command), cancellationToken);
        }
        catch (WordRenderException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            throw new WordRenderException(
                WordRenderFailureKind.ComFailure,
                $"Word COM call failed during Quick Look render: {ex.Message}");
        }
        catch (InvalidOperationException ex) when (IsBusyMessage(ex.Message))
        {
            throw new WordRenderException(
                WordRenderFailureKind.Busy,
                $"Word is busy; Quick Look render deferred: {ex.Message}");
        }
        catch (InvalidOperationException ex) when (IsModalMessage(ex.Message))
        {
            throw new WordRenderException(
                WordRenderFailureKind.Modal,
                $"Word is showing a modal dialog; Quick Look render deferred: {ex.Message}");
        }
        catch (Exception ex)
        {
            throw new WordRenderException(
                WordRenderFailureKind.ComFailure,
                $"Quick Look render failed: {ex.Message}");
        }
    }

    private static bool IsBusyMessage(string message) =>
        message.Contains("busy", StringComparison.OrdinalIgnoreCase);

    private static bool IsModalMessage(string message) =>
        message.Contains("modal", StringComparison.OrdinalIgnoreCase);

    private static QuickLookRenderOutput DefaultExport(QuickLookRenderCommand command) =>
        throw new WordRenderException(
            WordRenderFailureKind.Unavailable,
            "Microsoft Word is required for this operation.");
}
