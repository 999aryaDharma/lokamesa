using System;
using System.Threading;
using System.Threading.Tasks;

namespace DocumentIDE.Core.Pdf;

/// <summary>
/// PDF-01 — Core abstraction over the WORD-03 dedicated STA Word automation
/// queue. This is the ONLY path through which Word COM automation may run
/// (concurrency exactly 1); the PDF export service has no other way to
/// reach Word.
/// </summary>
public interface IWordAutomationQueue
{
    /// <summary>
    /// Enqueues a Word COM operation to run on the dedicated STA worker.
    /// Cancellation applies between queued items, never mid-COM-call.
    /// </summary>
    Task<T> EnqueueAsync<T>(Func<T> operation, CancellationToken cancellationToken = default);
}
