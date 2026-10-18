using DocumentIDE.Infrastructure.Word;
using DocumentIDE.Api;
using DocumentIDE.Api.Security;
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
// serializes index/worktree mutations per worktree (spec 13 §19); read-only
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
// the API response always reflects the real index (spec 13 §10/§19).
builder.Services.AddSingleton(sp => new GitStageService(
    sp.GetRequiredService<IGitProcessRunner>(),
    sp.GetRequiredService<GitRepositoryService>()));

// SCM-07: commit application service. Commit and Commit All run through the
// same controlled runner/mutation queue; hooks stay enabled, the plain commit
// never stages anything itself, and there is no automatic push
// (spec 13 §11/§19/§23).
builder.Services.AddSingleton(sp => new GitCommitService(
    sp.GetRequiredService<IGitProcessRunner>(),
    sp.GetRequiredService<GitRepositoryService>(),
    sp.GetRequiredService<GitStageService>()));

// SCM-09: Document IDE History ↔ Git correlation. Correlation is derived
// metadata stored in the application database (spec 13 §13/§24): the session
// service is Git-free by construction, so no correlation operation can create a
// commit, and dropping correlation data never touches the repository.
builder.Services.AddScoped<IWorkSessionRepository, WorkSessionRepository>();
builder.Services.AddScoped<IWorkSessionService>(sp => new WorkSessionService(
    sp.GetRequiredService<IWorkSessionRepository>()));

// SCM-11: remote-state read and explicit Fetch. Reads upstream/tracking state
// (a branch with no configured upstream is a first-class typed state, never a
// bogus 0/0) and runs `git fetch` through the same controlled runner/mutation
// queue against the resolved worktree root, so concurrent fetch/stage/commit on
// one worktree serialize (spec 13 §14.1/§14.2/§19). Fetch never changes document
// contents or the index; the runner keeps GIT_TERMINAL_PROMPT=0 so an auth
// prompt fails fast typed instead of hanging (spec 13 §18/§20).
builder.Services.AddSingleton(sp => new GitRemoteService(
    sp.GetRequiredService<IGitProcessRunner>(),
    sp.GetRequiredService<GitRepositoryService>()));

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
