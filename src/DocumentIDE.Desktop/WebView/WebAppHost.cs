using System;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using DocumentIDE.Api.Security;
using DocumentIDE.Api;
using DocumentIDE.Api.Controllers;
using DocumentIDE.Core.Domain.Repositories;
using DocumentIDE.Core.Filesystem;
using DocumentIDE.Infrastructure.Persistence;
using DocumentIDE.Infrastructure.Persistence.Repositories;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DocumentIDE.Desktop.WebView;

public class WebAppHost : IAsyncDisposable, IDisposable
{
    private IHost? _host;

    public string ApiUrl { get; private set; } = string.Empty;
    public int Port { get; private set; }
    public string SessionToken { get; private set; } = string.Empty;
    public bool IsRunning => _host != null;

    public static async Task<WebAppHost> StartAsync(
        int preferredPort = 0,
        AppDb? database = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var appHost = new WebAppHost();
        await appHost.InitializeAndStartAsync(preferredPort, database, configureServices);
        return appHost;
    }

    public async Task InitializeAndStartAsync(
        int preferredPort = 0,
        AppDb? database = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var builder = WebApplication.CreateBuilder();

        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(IPAddress.Loopback, preferredPort);
        });

        builder.Services.AddSingleton<ISessionTokenProvider, SessionTokenProvider>();
        if (database != null) builder.Services.AddSingleton(database);
        // Test/embedding seam: lets a caller override a single service (e.g. an
        // isolated ShareTokenService root) before the shared registrations below,
        // which use TryAdd and therefore keep the caller's choice.
        configureServices?.Invoke(builder.Services);
        builder.Services.AddBatchPreviewServices();
        // G4: serve the required privileged surfaces (Word open, folder sources,
        // rebind, batch apply/undo, search, PDF) through the same FND-02/QA-03
        // guard. The controller allow-list below still restricts the surface.
        builder.Services.AddPrivilegedSurfaces();
        builder.Services.AddScoped<IWorkspaceRepository, WorkspaceRepository>();
        builder.Services.AddScoped<IFolderSourceRepository, FolderSourceRepository>();
        builder.Services.AddScoped<StartupRecoveryService>();
        builder.Services.AddControllers()
            .AddApplicationPart(typeof(BatchPreviewController).Assembly)
            .ConfigureApplicationPartManager(manager =>
            {
                foreach (var provider in manager.FeatureProviders.OfType<ControllerFeatureProvider>().ToArray())
                    manager.FeatureProviders.Remove(provider);
                manager.FeatureProviders.Add(new BatchPreviewControllerProvider());
            });

        var app = builder.Build();
        await app.Services.GetRequiredService<AppDb>().InitializeAsync();

        // QA-07: repair state a crashed previous process left behind (plans stuck in
        // Executing, orphan ~apply-*.tmp artifacts) before serving any request. Safe
        // no-op on an empty database.
        using (var scope = app.Services.CreateScope())
        {
            var recovery = scope.ServiceProvider.GetRequiredService<StartupRecoveryService>();
            var recoveryReport = await recovery.RecoverAsync();
            StartupRecoveryLogging.LogReport(
                scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("StartupRecovery"),
                recoveryReport);
        }

        app.UseMiddleware<PrivilegedRequestGuard>();
        app.MapControllers();

        app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));
        app.MapGet("/api/status", () => Results.Ok(new { status = "running" }));

        await app.StartAsync();

        _host = app;

        var server = app.Services.GetRequiredService<IServer>();
        var addressesFeature = server.Features.Get<IServerAddressesFeature>();
        var address = addressesFeature?.Addresses.FirstOrDefault();

        if (address != null && Uri.TryCreate(address, UriKind.Absolute, out var uri))
        {
            Port = uri.Port;
            ApiUrl = $"http://127.0.0.1:{Port}";
        }
        else
        {
            Port = preferredPort > 0 ? preferredPort : 5000;
            ApiUrl = $"http://127.0.0.1:{Port}";
        }

        var tokenProvider = app.Services.GetRequiredService<ISessionTokenProvider>();
        SessionToken = tokenProvider.SessionToken;
    }

    public async Task StopAsync()
    {
        if (_host != null)
        {
            var hostToStop = _host;
            _host = null;
            try
            {
                await hostToStop.StopAsync();
            }
            catch { }

            if (hostToStop is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
    }

    private sealed class BatchPreviewControllerProvider : ControllerFeatureProvider
    {
        // G4: the shipped desktop host deliberately allow-lists its privileged
        // controller surface. Security is enforced per request by
        // PrivilegedRequestGuard (Origin/Host/session token), not by omission.
        private static readonly Type[] AllowedControllers =
        {
            typeof(BatchPreviewController),
            typeof(WordController),
            typeof(FolderSourcesController),
            typeof(MoveCorrelationController),
            typeof(SearchController),
            typeof(PdfController),
            typeof(ShareController),
            // App-shell bootstrap + navigation surfaces the shipped web client
            // mounts: cached workspace documents, folder sources, the workspace
            // graph envelope, and Markdown source load/save.
            typeof(ReconciliationController),
            typeof(GraphController),
            typeof(MarkdownDocumentController),
        };

        protected override bool IsController(TypeInfo typeInfo) => Array.IndexOf(AllowedControllers, typeInfo.AsType()) >= 0;
    }
}
