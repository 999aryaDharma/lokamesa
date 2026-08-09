using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using DocumentIDE.Api.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DocumentIDE.Desktop.WebView;

public class WebAppHost : IAsyncDisposable, IDisposable
{
    private IHost? _host;

    public string ApiUrl { get; private set; } = string.Empty;
    public int Port { get; private set; }
    public string SessionToken { get; private set; } = string.Empty;
    public bool IsRunning => _host != null;

    public static async Task<WebAppHost> StartAsync(int preferredPort = 0)
    {
        var appHost = new WebAppHost();
        await appHost.InitializeAndStartAsync(preferredPort);
        return appHost;
    }

    public async Task InitializeAndStartAsync(int preferredPort = 0)
    {
        var builder = WebApplication.CreateBuilder();

        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(IPAddress.Loopback, preferredPort);
        });

        builder.Services.AddSingleton<ISessionTokenProvider, SessionTokenProvider>();

        var app = builder.Build();

        app.UseMiddleware<PrivilegedRequestGuard>();

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
}
