using DocumentIDE.Infrastructure.Word;
using DocumentIDE.Api;
using DocumentIDE.Api.Security;
using DocumentIDE.Core.Batch;
using DocumentIDE.Core.Domain.Repositories;
using DocumentIDE.Core.Filesystem;
using DocumentIDE.Core.History;
using DocumentIDE.Core.Word;
using DocumentIDE.Infrastructure.Filesystem;
using DocumentIDE.Infrastructure.Persistence;
using DocumentIDE.Infrastructure.Persistence.Repositories;
using DocumentIDE.Infrastructure.Diagnostics;
using DocumentIDE.Core.Search;
using DocumentIDE.Infrastructure.Search;
using DocumentIDE.Core.Indexing;
using DocumentIDE.Core.Pdf;
using DocumentIDE.Infrastructure.Docx;
using DocumentIDE.Core.SourceControl;
using DocumentIDE.Infrastructure.SourceControl;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseDiagnostics();

// Step 1: Configure Kestrel host to bind ONLY to loopback interfaces
builder.WebHost.ConfigureKestrel(options =>
{
    options.Listen(System.Net.IPAddress.Loopback, 0);
    options.Listen(System.Net.IPAddress.IPv6Loopback, 0);
});

// Step 2: Register 256-bit in-memory session token provider as Singleton
builder.Services.AddSingleton<ISessionTokenProvider, SessionTokenProvider>();

// Register Persistence & Services
builder.Services.AddSingleton<AppDb>();
builder.Services.AddScoped<IWorkspaceRepository, WorkspaceRepository>();
builder.Services.AddScoped<IFolderSourceRepository, FolderSourceRepository>();
builder.Services.AddScoped<StartupRecoveryService>();
builder.Services.AddScoped<IFolderSourceService, FolderSourceService>();
builder.Services.AddScoped<IDocumentRepository, DocumentRepository>();
builder.Services.AddScoped<IFileIdentityProvider, WindowsFileIdentityProvider>();
builder.Services.AddScoped<DocumentIdentityService>();
builder.Services.AddScoped<IMoveCorrelationService, MoveCorrelationService>();
builder.Services.AddScoped<IStartupReconciler, StartupReconciler>();
builder.Services.AddScoped<IWordDesktopLauncher, WordDesktopLauncher>();
builder.Services.AddSingleton<DocumentIDE.Infrastructure.Word.WordAutomationQueue>();
builder.Services.AddSingleton<IWordAutomationQueue, WordAutomationQueueAdapter>();
builder.Services.AddSingleton<IWordPdfRenderer, WordPdfRenderer>();

// SCM-01: controlled Git process runner. The per-worktree mutation queue
// serializes index/worktree mutations per worktree (spec 13 ┬º19); read-only
// history/status commands bypass it via the runner's deterministic
// classifier. Process policy (structured args, no shell, prompts/pagers
// disabled, LC_ALL=C) lives in GitProcessRunnerOptions.
builder.Services.AddSingleton(GitProcessRunnerOptions.Default);
builder.Services.AddSingleton<GitWorktreeMutationQueue>();
builder.Services.AddSingleton<IGitProcessRunner, GitProcessRunner>();

// SCM-03: repository/worktree/status service consumed by the Source Control
// changes pane. Singleton so a watched-worktree context can outlive a request
// (SCM-02 debounce seam). The FS-03 FileChangeCoordinator is the optional
// debounce input and is not registered in this host yet (its dependencies are
// scoped), so the pane currently re-derives status on every explicit call; a
// host that registers the coordinator must keep this factory's requirement
// (singleton-safe coordinator) in mind.
builder.Services.AddSingleton(sp => new GitRepositoryService(
    sp.GetRequiredService<IGitProcessRunner>(),
    sp.GetService<DocumentIDE.Core.Filesystem.FileChangeCoordinator>()));

// SCM-06: stage/unstage application service. Mutations run through the same
// controlled runner/mutation queue and re-read status from Git afterwards, so
// the API response always reflects the real index (spec 13 ┬º10/┬º19).
builder.Services.AddSingleton(sp => new GitStageService(
    sp.GetRequiredService<IGitProcessRunner>(),
    sp.GetRequiredService<GitRepositoryService>()));

// SCM-07: commit application service. Commit and Commit All run through the
// same controlled runner/mutation queue; hooks stay enabled, the plain commit
// never stages anything itself, and there is no automatic push
// (spec 13 ┬º11/┬º19/┬º23).
builder.Services.AddSingleton(sp => new GitCommitService(
    sp.GetRequiredService<IGitProcessRunner>(),
    sp.GetRequiredService<GitRepositoryService>(),
    sp.GetRequiredService<GitStageService>()));

// SCM-09: Document IDE History Γåö Git correlation. Correlation is derived
// metadata stored in the application database (spec 13 ┬º13/┬º24): the session
// service is Git-free by construction, so no correlation operation can create a
// commit, and dropping correlation data never touches the repository.
builder.Services.AddScoped<IWorkSessionRepository, WorkSessionRepository>();
builder.Services.AddScoped<IWorkSessionService>(sp => new WorkSessionService(
    sp.GetRequiredService<IWorkSessionRepository>()));

// SCM-11: remote-state read and explicit Fetch. Reads upstream/tracking state
// (a branch with no configured upstream is a first-class typed state, never a
// bogus 0/0) and runs `git fetch` through the same controlled runner/mutation
// queue against the resolved worktree root, so concurrent fetch/stage/commit on
// one worktree serialize (spec 13 ┬º14.1/┬º14.2/┬º19). Fetch never changes document
// contents or the index; the runner keeps GIT_TERMINAL_PROMPT=0 so an auth
// prompt fails fast typed instead of hanging (spec 13 ┬º18/┬º20).
builder.Services.AddSingleton(sp => new GitRemoteService(
    sp.GetRequiredService<IGitProcessRunner>(),
    sp.GetRequiredService<GitRepositoryService>()));

// SCM-13: explicit Push. Runs `git push` through the same controlled
// runner/mutation queue against the resolved true worktree root, so a push
// serializes with concurrent stage/commit/fetch on one worktree and is scoped to
// the current branch's configured upstream: a branch with no upstream is a typed
// state that runs no Git at all, never a push to a guessed default remote
// (spec 13 ┬º14.2/┬º14.4/┬º19). Push is only ever user initiated ΓÇö no commit, save
// or other flow calls it ΓÇö and the runner keeps interactive credential prompts
// disabled (GIT_TERMINAL_PROMPT=0, emptied askpass, GCM_INTERACTIVE=never), so an
// authenticated remote fails fast typed instead of hanging (spec 13 ┬º14.4/┬º18).
builder.Services.AddSingleton(sp => new GitPushService(
    sp.GetRequiredService<IGitProcessRunner>(),
    sp.GetRequiredService<GitRepositoryService>(),
    sp.GetRequiredService<GitRemoteService>()));

// SCM-12: conservative fast-forward Pull. Runs `git pull --ff-only --no-rebase`
// through the same controlled runner/mutation queue against the resolved true
// worktree root, so a pull serializes with concurrent stage/commit/fetch on one
// worktree. A dirty/conflicted worktree is refused before Git runs, divergence is
// a typed blocking state (never a merge commit or rebase), and a successful pull
// re-reads status, reindexes the changed documents and records the operation in
// the same Document IDE History store (spec 13 ┬º14.3/┬º19/┬º20/┬º23).
builder.Services.AddScoped(sp => new GitPullService(
    sp.GetRequiredService<IGitProcessRunner>(),
    sp.GetRequiredService<GitRepositoryService>(),
    sp.GetRequiredService<GitRemoteService>(),
    sp.GetRequiredService<IDocumentRepository>(),
    sp.GetRequiredService<IBatchApplyReindexTrigger>(),
    sp.GetRequiredService<IGitPullHistoryRecorder>()));

// SCM-08: read-only Git history service (commit history, file history and
// document-version inspection). Every read goes through the same controlled
// runner and `log`/`show` are in its read-only vocabulary, so nothing here
// joins the per-worktree mutation queue and no read mutates the repository
// (spec 13 ┬º9.4/┬º12/┬º19/┬º20).
builder.Services.AddSingleton(sp => new GitHistoryService(
    sp.GetRequiredService<IGitProcessRunner>(),
    sp.GetRequiredService<GitRepositoryService>()));

// SCM-10: safe discard/restore. Scoped because the HIST-01 recovery snapshot
// service, the document repository and the reindex seam are scoped. Every
// mutation is file-scoped, requires the reviewed confirmation token, writes a
// recovery snapshot before Git runs, refuses conflicted/untracked paths, and
// records the operation in Document IDE History (spec 13 ┬º15/┬º19/┬º21/┬º23).
builder.Services.AddScoped(sp => new GitRestoreService(
    sp.GetRequiredService<IGitProcessRunner>(),
    sp.GetRequiredService<GitRepositoryService>(),
    sp.GetRequiredService<RecoverySnapshotService>(),
    sp.GetRequiredService<IGitRestoreHistoryRecorder>(),
    sp.GetRequiredService<IBatchApplyReindexTrigger>(),
    sp.GetRequiredService<IDocumentRepository>()));
// SCM-18: worktree API surface. Exposes the SCM-15 discovery/open service and
// the SCM-16 create/remove/prune service over the privileged API as a thin
// typed layer (spec 13 ┬º16/┬º18): no Git behaviour is added, no branch is
// created or switched, removal is never forced, and opening a
// registered-but-unattached worktree only reports RequiresAttachmentFlow
// instead of attaching a folder source. Scoped because both services consume
// the scoped FS-01 folder-source service.
builder.Services.AddScoped<IGitWorktreeService>(sp => new GitWorktreeService(
    sp.GetRequiredService<IGitProcessRunner>(),
    sp.GetRequiredService<IFolderSourceService>()));
builder.Services.AddScoped<IGitWorktreeMutationService>(sp => new GitWorktreeMutationService(
    sp.GetRequiredService<IGitProcessRunner>(),
    sp.GetRequiredService<IGitWorktreeService>()));

// SCM-14: Git conflict state and resolution shell. Read-only: it derives the
// real unmerged state from the SCM-02 status snapshot, reports the unsafe
// actions that are refused typed, and offers the existing document-open seam
// for registered documents. It never merges, parses or writes conflicted
// content; a DOCX conflict is resolved by a human through the guarded Word host
// (spec 13 ┬º20/┬º21/┬º23). Scoped because the document repository is scoped.
builder.Services.AddScoped(sp => new GitConflictService(
    sp.GetRequiredService<GitRepositoryService>(),
    sp.GetRequiredService<IDocumentRepository>()));

builder.Services.AddScoped<PdfExportService>();
builder.Services.AddScoped<BatchPdfExportService>();
builder.Services.AddScoped<PdfMergeService>();
builder.Services.AddScoped<IRecycleBin, WindowsRecycleBin>();
builder.Services.AddScoped<IPhysicalFileOperations, PhysicalFileOperations>();
builder.Services.AddScoped<IDocumentMetadataRepository, DocumentMetadataRepository>();
builder.Services.AddScoped<IFtsIndex, FtsIndex>();
builder.Services.AddScoped<DocumentIDE.Core.Markdown.IMarkdownStructureParser, DocumentIDE.Core.Markdown.MarkdownStructureParser>();
builder.Services.AddScoped<DocumentIDE.Core.Indexing.IDocxStructureParser, DocxStructureParser>();
builder.Services.AddScoped<IncrementalIndexCoordinator>();
builder.Services.AddScoped<DocumentIDE.Core.Semantic.IEmbeddingStore, DocumentIDE.Infrastructure.Semantic.SqliteEmbeddingStore>();
builder.Services.AddScoped<DocumentIDE.Core.Semantic.IEmbeddingProvider, DocumentIDE.Infrastructure.Semantic.LocalEmbeddingProvider>();
builder.Services.AddScoped<ISemanticSearchService, SemanticSearchService>();
builder.Services.AddScoped<IDocumentSearchService, DocumentSearchService>();

builder.Services.AddControllers();
DocumentIDE.Api.BatchPreviewRegistration.AddBatchPreviewServices(builder.Services);

// G2/G3/G8: wire the reviewed batch apply/undo/history engines, the realtime
// search service and the PDF finalization engines behind the privileged API.
builder.Services.AddPrivilegedSurfaces();

var app = builder.Build();

// Ensure DB migrations run on startup
using (var scope = app.Services.CreateScope())
{
    var appDb = scope.ServiceProvider.GetRequiredService<AppDb>();
    await appDb.InitializeAsync();

    // QA-07: repair state a crashed previous process left behind (plans stuck in
    // Executing, orphan ~apply-*.tmp artifacts) before serving any request. Safe
    // no-op on an empty database.
    var recovery = scope.ServiceProvider.GetRequiredService<StartupRecoveryService>();
    var recoveryReport = await recovery.RecoverAsync();
    StartupRecoveryLogging.LogReport(
        scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("StartupRecovery"),
        recoveryReport);
}

// Step 3 & 4: Privileged Request Security Guard Middleware
app.UseMiddleware<PrivilegedRequestGuard>();

app.MapControllers();

app.MapGet("/", () => "Hello World!");

app.Run();

public partial class Program { }
