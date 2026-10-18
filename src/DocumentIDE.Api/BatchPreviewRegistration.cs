using DocumentIDE.Core.Domain.Repositories;
using DocumentIDE.Infrastructure.Batch;
using DocumentIDE.Infrastructure.Jobs;
using DocumentIDE.Infrastructure.Persistence;
using DocumentIDE.Infrastructure.Persistence.Repositories;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DocumentIDE.Api;

public static class BatchPreviewRegistration
{
    public static IServiceCollection AddBatchPreviewServices(this IServiceCollection services)
    {
        services.TryAddSingleton<AppDb>();
        services.TryAddSingleton<IBatchPlanRepository, BatchPlanRepository>();
        services.TryAddSingleton<LocalJobScheduler>();
        services.TryAddSingleton<BatchPreviewService>();
        services.TryAddSingleton<BatchPreviewCoordinator>();
        return services;
    }
}
