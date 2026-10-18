using DocumentIDE.Core.Batch;
using DocumentIDE.Core.Domain.Repositories;
using DocumentIDE.Core.Filesystem;
using DocumentIDE.Core.History;
using DocumentIDE.Core.SourceControl;
using DocumentIDE.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DocumentIDE.Infrastructure.SourceControl;

/// <summary>
/// Composition helper for the SCM-01…18 runtime (spec 13).
///
/// Why this lives here: the architecture boundary
/// (<c>SourceControlProcessBoundaryTests.ApiCompositionRoot_IsTheOnlyCSharpPlaceWiringTheGitRunner</c>)
/// only permits the concrete <see cref="GitProcessRunner"/> type to be named in
/// <c>src/DocumentIDE.Api/Program.cs</c> or under
/// <c>src/DocumentIDE.Infrastructure/SourceControl/</c>. The desktop WebView host
/// needs the same SCM services behind <c>SourceControlController</c>, so it calls
/// this extension instead of naming the runner itself.
///
/// Every registration uses <c>TryAdd*</c>: a caller (or the development API
/// host) may register its own instance first and this helper never overrides it.
///
/// KNOWN DUPLICATION: <c>DocumentIDE.Api/Program.cs</c> still wires the same
/// SCM-01…18 services inline. This extension is intentionally additive for
/// now; consolidating Program.cs onto it is a follow-up cleanup (recorded in the
/// UI-03 handoff as a residual).
/// </summary>
public static class SourceControlServiceRegistration
{
    public static IServiceCollection AddSourceControlServices(this IServiceCollection services)
    {
        services.TryAddSingleton<GitProcessRunnerOptions>(GitProcessRunnerOptions.Default);
        services.TryAddSingleton<GitWorktreeMutationQueue>();
        // The only place outside Program.cs that may name the concrete runner.
        services.TryAddSingleton<IGitProcessRunner, GitProcessRunner>();

        services.TryAddSingleton(sp => new GitRepositoryService(
            sp.GetRequiredService<IGitProcessRunner>(),
            sp.GetService<FileChangeCoordinator>()));
        services.TryAddSingleton(sp => new GitStageService(
            sp.GetRequiredService<IGitProcessRunner>(),
            sp.GetRequiredService<GitRepositoryService>()));
        services.TryAddSingleton(sp => new GitCommitService(
            sp.GetRequiredService<IGitProcessRunner>(),
            sp.GetRequiredService<GitRepositoryService>(),
            sp.GetRequiredService<GitStageService>()));

        services.TryAddScoped<IWorkSessionRepository, WorkSessionRepository>();
        services.TryAddScoped<IWorkSessionService>(sp => new WorkSessionService(
            sp.GetRequiredService<IWorkSessionRepository>()));

        services.TryAddSingleton(sp => new GitRemoteService(
            sp.GetRequiredService<IGitProcessRunner>(),
            sp.GetRequiredService<GitRepositoryService>()));
        services.TryAddSingleton(sp => new GitPushService(
            sp.GetRequiredService<IGitProcessRunner>(),
            sp.GetRequiredService<GitRepositoryService>(),
            sp.GetRequiredService<GitRemoteService>()));
        services.TryAddScoped(sp => new GitPullService(
            sp.GetRequiredService<IGitProcessRunner>(),
            sp.GetRequiredService<GitRepositoryService>(),
            sp.GetRequiredService<GitRemoteService>(),
            sp.GetRequiredService<IDocumentRepository>(),
            sp.GetRequiredService<IBatchApplyReindexTrigger>(),
            sp.GetRequiredService<IGitPullHistoryRecorder>()));
        services.TryAddSingleton(sp => new GitHistoryService(
            sp.GetRequiredService<IGitProcessRunner>(),
            sp.GetRequiredService<GitRepositoryService>()));
        services.TryAddScoped(sp => new GitRestoreService(
            sp.GetRequiredService<IGitProcessRunner>(),
            sp.GetRequiredService<GitRepositoryService>(),
            sp.GetRequiredService<RecoverySnapshotService>(),
            sp.GetRequiredService<IGitRestoreHistoryRecorder>(),
            sp.GetRequiredService<IBatchApplyReindexTrigger>(),
            sp.GetRequiredService<IDocumentRepository>()));
        services.TryAddScoped<IGitWorktreeService>(sp => new GitWorktreeService(
            sp.GetRequiredService<IGitProcessRunner>(),
            sp.GetRequiredService<IFolderSourceService>()));
        services.TryAddScoped<IGitWorktreeMutationService>(sp => new GitWorktreeMutationService(
            sp.GetRequiredService<IGitProcessRunner>(),
            sp.GetRequiredService<IGitWorktreeService>()));
        services.TryAddScoped(sp => new GitConflictService(
            sp.GetRequiredService<GitRepositoryService>(),
            sp.GetRequiredService<IDocumentRepository>()));

        return services;
    }
}
