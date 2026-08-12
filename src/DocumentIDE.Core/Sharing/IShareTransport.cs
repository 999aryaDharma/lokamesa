using System;
using System.Threading;
using System.Threading.Tasks;

namespace DocumentIDE.Core.Sharing;

public enum TransportStatusState
{
    Stopped,
    Starting,
    Running,
    Failed
}

public class TransportStatus
{
    public TransportStatusState State { get; set; } = TransportStatusState.Stopped;
    public string? PublicUrl { get; set; }
    public string? ErrorMessage { get; set; }
}

public class PublicEndpoint
{
    public string BaseUrl { get; set; } = string.Empty;
    public string SecretPath { get; set; } = string.Empty;
    public string FullUrl => string.IsNullOrWhiteSpace(SecretPath)
        ? BaseUrl.TrimEnd('/')
        : $"{BaseUrl.TrimEnd('/')}/{SecretPath.TrimStart('/')}";

    public string PublicUrl => FullUrl;
}

public interface IShareTransport
{
    Task<PublicEndpoint> StartAsync(string localUrl, string secretToken, CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    TransportStatus GetStatus();
}
