using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Infrastructure.Sharing;

namespace DocumentIDE.IntegrationTests.Sharing;

public class MockNgrokProcessRunner : INgrokProcessRunner
{
    public MockNgrokProcessRunner(string baseUrl)
    {
        BaseUrl = baseUrl;
    }

    public string BaseUrl { get; set; }

    // LoggedOutput is used in tests to ensure secret token never appears.
    public string LoggedOutput => BaseUrl; // simple log containing only the public base URL

    public Task StartAsync(string localUrl, string authToken, CancellationToken cancellationToken = default)
    {
        // In a mock we ignore the authToken entirely to avoid logging it.
        // The BaseUrl is already set via constructor.
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        // No real process to stop in the mock.
        return Task.CompletedTask;
    }
}
