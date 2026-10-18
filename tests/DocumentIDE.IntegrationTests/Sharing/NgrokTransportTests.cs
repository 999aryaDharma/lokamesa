using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Core.Security;
using DocumentIDE.Core.Sharing;
using DocumentIDE.Infrastructure.Security;
using DocumentIDE.Infrastructure.Sharing;
using Xunit;

namespace DocumentIDE.IntegrationTests.Sharing;

public class NgrokTransportTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly WindowsSecretStore _secretStore;

    public NgrokTransportTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "DocumentIDE_NgrokTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        _secretStore = new WindowsSecretStore(_tempDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            try
            {
                Directory.Delete(_tempDirectory, true);
            }
            catch
            {
                // Ignore cleanup errors in tests
            }
        }
    }

    [Fact]
    public async Task Start_ReturnsPublicEndpoint()
    {
        const string mockAuthToken = "2abc123def456_ngrok_token_sample";
        await _secretStore.SetSecretAsync(NgrokTransport.SecretKey, new SecretValue(mockAuthToken));

        var mockRunner = new MockNgrokProcessRunner("https://test-subdomain.ngrok-free.app");
        var transport = new NgrokTransport(_secretStore, mockRunner);

        const string localUrl = "http://127.0.0.1:5050";
        const string secretToken = "token_abc123xyz";

        var endpoint = await transport.StartAsync(localUrl, secretToken);

        Assert.NotNull(endpoint);
        Assert.Equal("https://test-subdomain.ngrok-free.app", endpoint.BaseUrl);
        Assert.Equal("share/token_abc123xyz", endpoint.SecretPath);
        Assert.Equal("https://test-subdomain.ngrok-free.app/share/token_abc123xyz", endpoint.FullUrl);
        Assert.Equal("https://test-subdomain.ngrok-free.app/share/token_abc123xyz", endpoint.PublicUrl);

        var status = transport.GetStatus();
        Assert.Equal(TransportStatusState.Running, status.State);
        Assert.Equal("https://test-subdomain.ngrok-free.app/share/token_abc123xyz", status.PublicUrl);
        Assert.Null(status.ErrorMessage);
    }

    [Fact]
    public async Task Stop_InvalidatesActiveTransport()
    {
        const string mockAuthToken = "2abc123def456_ngrok_token_sample";
        await _secretStore.SetSecretAsync(NgrokTransport.SecretKey, new SecretValue(mockAuthToken));

        var mockRunner = new MockNgrokProcessRunner("https://test-subdomain.ngrok-free.app");
        var transport = new NgrokTransport(_secretStore, mockRunner);

        await transport.StartAsync("http://127.0.0.1:5050", "token_abc123xyz");
        Assert.Equal(TransportStatusState.Running, transport.GetStatus().State);

        await transport.StopAsync();

        var status = transport.GetStatus();
        Assert.Equal(TransportStatusState.Stopped, status.State);
        Assert.Null(status.PublicUrl);
        Assert.Null(status.ErrorMessage);
    }

    [Fact]
    public async Task Credential_NeverAppearsInLogs()
    {
        const string sensitiveToken = "super_secret_ngrok_authtoken_9999999999";
        await _secretStore.SetSecretAsync(NgrokTransport.SecretKey, new SecretValue(sensitiveToken));

        var mockRunner = new MockNgrokProcessRunner("https://test-subdomain.ngrok-free.app");
        var transport = new NgrokTransport(_secretStore, mockRunner);

        await transport.StartAsync("http://127.0.0.1:5050", "token_abc123xyz");

        var status = transport.GetStatus();
        var capturedLogs = mockRunner.LoggedOutput + " " + (status.ErrorMessage ?? "") + " " + transport.ToString();

        Assert.DoesNotContain(sensitiveToken, capturedLogs);
    }

    [Fact]
    public async Task Start_ThrowsException_WhenCredentialMissing()
    {
        var mockRunner = new MockNgrokProcessRunner("https://test-subdomain.ngrok-free.app");
        var transport = new NgrokTransport(_secretStore, mockRunner);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            transport.StartAsync("http://127.0.0.1:5050", "token_abc123xyz"));

        Assert.Contains("authtoken", ex.Message, StringComparison.OrdinalIgnoreCase);

        var status = transport.GetStatus();
        Assert.Equal(TransportStatusState.Failed, status.State);
        Assert.NotNull(status.ErrorMessage);
    }

    [Fact]
    public async Task Start_HandlesTunnelFailureAndRestartCleanly()
    {
        const string mockAuthToken = "2abc123def456_ngrok_token_sample";
        await _secretStore.SetSecretAsync(NgrokTransport.SecretKey, new SecretValue(mockAuthToken));

        var mockRunner = new MockNgrokProcessRunner("https://test-subdomain.ngrok-free.app");
        var transport = new NgrokTransport(_secretStore, mockRunner);

        // First start
        var ep1 = await transport.StartAsync("http://127.0.0.1:5050", "token_1");
        Assert.Equal(TransportStatusState.Running, transport.GetStatus().State);

        // Stop
        await transport.StopAsync();
        Assert.Equal(TransportStatusState.Stopped, transport.GetStatus().State);

        // Restart
        mockRunner.BaseUrl = "https://new-subdomain.ngrok-free.app";
        var ep2 = await transport.StartAsync("http://127.0.0.1:5050", "token_2");

        Assert.Equal("https://new-subdomain.ngrok-free.app/share/token_2", ep2.FullUrl);
        Assert.Equal(TransportStatusState.Running, transport.GetStatus().State);
    }
}
