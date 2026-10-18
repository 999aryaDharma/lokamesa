using DocumentIDE.Core.Batch;
using DocumentIDE.Core.Batch.Operations;
using DocumentIDE.Core.Domain.Repositories;
using DocumentIDE.Core.Filesystem;
using DocumentIDE.Core.History;
using DocumentIDE.Core.Indexing;
using DocumentIDE.Core.Markdown;
using DocumentIDE.Core.Pdf;
using DocumentIDE.Core.Search;
using DocumentIDE.Core.Sharing;
using DocumentIDE.Core.SourceControl;
using DocumentIDE.Core.Word;
using DocumentIDE.Infrastructure.Batch;
using DocumentIDE.Infrastructure.Docx;
using DocumentIDE.Infrastructure.Filesystem;
using DocumentIDE.Infrastructure.History;
using DocumentIDE.Infrastructure.Markdown;
using DocumentIDE.Infrastructure.Persistence.Repositories;
using DocumentIDE.Infrastructure.Search;
using DocumentIDE.Infrastructure.Sharing;
using DocumentIDE.Infrastructure.Word;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DocumentIDE.Api;

/// <summary>
/// G4 — the privileged controller surface shared by the development API host
/// (<c>DocumentIDE.Api</c>) and the shipped desktop host (<c>WebAppHost</c>).
///
/// Registering this superset here keeps both hosts honest: the FND-02/QA-03
/// <see cref="Security.PrivilegedRequestGuard"/> still gates every request by
/// Origin/Host/session token, and the desktop host still allow-lists exactly the
/// controllers it serves. This method adds only service *implementations*; it
/// never loosens the security middleware.
/// </summary>
public static class PrivilegedSurfaceRegistration
{
    public static IServiceCollection AddPrivilegedSurfaces(this IServiceCollection services)
    {
        // Persistence repositories consumed by the privileged controllers. The
        // desktop host passes a single AppDb instance; TryAdd keeps the API host's
        // existing registrations authoritative.
        services.TryAddScoped<IWorkspaceRepository, WorkspaceRepository>();
        services.TryAddScoped<IFolderSourceRepository, FolderSourceRepository>();
        services.TryAddScoped<IDocumentRepository, DocumentRepository>();
        services.TryAddScoped<IBatchPlanRepository, BatchPlanRepository>();
        services.TryAddScoped<IRelationRepository, RelationRepository>();

        // Folders / identity / move correlation (rebind).
        services.TryAddScoped<IFolderSourceService, FolderSourceService>();
        services.TryAddScoped<IFileIdentityProvider, WindowsFileIdentityProvider>();
        services.TryAddScoped<DocumentIdentityService>();
        services.TryAddScoped<IMoveCorrelationService, MoveCorrelationService>();
        services.TryAddScoped<IStartupReconciler, StartupReconciler>();

        // Word (open in Word / PDF render seam), COM concurrency exactly 1.
        services.TryAddScoped<IWordDesktopLauncher, WordDesktopLauncher>();
        services.TryAddSingleton<WordAutomationQueue>();
        services.TryAddSingleton<IWordAutomationQueue, WordAutomationQueueAdapter>();
        services.TryAddSingleton<IWordPdfRenderer, WordPdfRenderer>();

        // PDF finalization engines (PDF-01/02/03).
        services.TryAddScoped<PdfExportService>();
        services.TryAddScoped<BatchPdfExportService>();
        services.TryAddScoped<PdfMergeService>();

        // Indexing + search (SRCH-05 realtime endpoint).
        services.TryAddScoped<IDocumentMetadataRepository, DocumentMetadataRepository>();
        services.TryAddScoped<IFtsIndex, FtsIndex>();
        services.TryAddScoped<IMarkdownStructureParser, MarkdownStructureParser>();
        services.TryAddScoped<IDocxStructureParser, DocxStructureParser>();
        services.TryAddScoped<IncrementalIndexCoordinator>();
        services.TryAddScoped<IFuzzySearchService, FuzzySearchService>();
        services.TryAddScoped<IDocumentSearchService, DocumentSearchService>();

        // Batch apply / undo / history (BAT-04, HIST-02, BAT-11).
        services.TryAddSingleton<IHistoryStorage>(_ => new HistoryStorage());
        services.TryAddSingleton<JsonlHistoryStore>();
        services.TryAddSingleton<IBatchApplyHistoryRecorder>(sp => sp.GetRequiredService<JsonlHistoryStore>());
        services.TryAddSingleton<IUndoHistoryRecorder>(sp => sp.GetRequiredService<JsonlHistoryStore>());
        // SCM-10 — discard/restore operations are recorded in the same Document
        // IDE History store (spec 13 §15.2 step 8), not a parallel one.
        services.TryAddSingleton<IGitRestoreHistoryRecorder>(sp => sp.GetRequiredService<JsonlHistoryStore>());
        // SCM-12 — conservative pull operations are recorded in the same store.
        services.TryAddSingleton<IGitPullHistoryRecorder>(sp => sp.GetRequiredService<JsonlHistoryStore>());
        services.TryAddScoped<RecoverySnapshotService>();
        services.TryAddScoped<IExternalEditStateProbe, WordEditStateProbe>();
        services.TryAddScoped<IAtomicFileReplacer, AtomicFileReplacer>();
        services.TryAddScoped<IBatchApplyReindexTrigger, IndexCoordinatorReindexTrigger>();
        services.TryAddScoped<IDocxOccurrenceMutator, StrictTextMutator>();
        services.TryAddScoped<SearchReplaceOperation>();
        services.TryAddScoped<BatchApplyEngine>();
        services.TryAddScoped<UndoService>();
        services.TryAddScoped<BatchApplyCoordinator>();

        // Share lifecycle (G5 / SHR-11): owner-side create Snapshot/Live, stop,
        // revoke, regenerate and explicit membership through the privileged host.
        // Stops/revokes persist on the Share aggregate; the public ShareHost side
        // is unchanged and still enforces token/CSP/download policy per request.
        services.TryAddSingleton<ShareTokenService>();
        services.TryAddSingleton<MarkdownPreviewService>();
        services.TryAddSingleton<ISnapshotMarkdownRenderer, SnapshotMarkdownRendererAdapter>();
        services.TryAddScoped<IShareRepository, ShareRepository>();
        services.TryAddScoped<ShareLifecycleService>();

        return services;
    }
}
