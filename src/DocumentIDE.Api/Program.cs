using DocumentIDE.Infrastructure.Word;
using DocumentIDE.Api.Security;
using DocumentIDE.Core.Domain.Repositories;
using DocumentIDE.Core.Filesystem;
using DocumentIDE.Core.Word;
using DocumentIDE.Infrastructure.Filesystem;
using DocumentIDE.Infrastructure.Persistence;
using DocumentIDE.Infrastructure.Persistence.Repositories;
using DocumentIDE.Infrastructure.Diagnostics;
using DocumentIDE.Core.Search;
using DocumentIDE.Infrastructure.Search;
using DocumentIDE.Core.Indexing;
using DocumentIDE.Infrastructure.Docx;

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
builder.Services.AddScoped<IFolderSourceRepository, FolderSourceRepository>();
builder.Services.AddScoped<IFolderSourceService, FolderSourceService>();
builder.Services.AddScoped<IDocumentRepository, DocumentRepository>();
builder.Services.AddScoped<IFileIdentityProvider, WindowsFileIdentityProvider>();
builder.Services.AddScoped<DocumentIdentityService>();
builder.Services.AddScoped<IMoveCorrelationService, MoveCorrelationService>();
builder.Services.AddScoped<IStartupReconciler, StartupReconciler>();
builder.Services.AddScoped<IWordDesktopLauncher, WordDesktopLauncher>();
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

var app = builder.Build();

// Ensure DB migrations run on startup
using (var scope = app.Services.CreateScope())
{
    var appDb = scope.ServiceProvider.GetRequiredService<AppDb>();
    await appDb.InitializeAsync();
}

// Step 3 & 4: Privileged Request Security Guard Middleware
app.UseMiddleware<PrivilegedRequestGuard>();

app.MapControllers();

app.MapGet("/", () => "Hello World!");

app.Run();

public partial class Program { }
