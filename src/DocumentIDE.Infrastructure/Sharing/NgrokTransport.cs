using System;
using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Core.Security;
using DocumentIDE.Core.Sharing;
using DocumentIDE.Infrastructure.Sharing;

namespace DocumentIDE.Infrastructure.Sharing;

public class NgrokTransport : IShareTransport
{
    public const string SecretKey = "ngrok_authtoken";

    private readonly ISecretStore _secretStore;
    private readonly INgrokProcessRunner _runner;
    private TransportStatus _status = new TransportStatus { State = TransportStatusState.Stopped };

    public NgrokTransport(ISecretStore secretStore, INgrokProcessRunner runner)
    {
        _secretStore = secretStore ?? throw new ArgumentNullException(nameof(secretStore));
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    }

    public async Task<PublicEndpoint> StartAsync(string localUrl, string secretToken, CancellationToken cancellationToken = default)
    {
        // Retrieve the ngrok authtoken
        var secret = await _secretStore.GetSecretAsync(SecretKey);
        if (secret == null || string.IsNullOrWhiteSpace(secret.Value))
        {
            _status = new TransportStatus { State = TransportStatusState.Failed, ErrorMessage = "ngrok authtoken missing" };
            throw new InvalidOperationException("ngrok authtoken missing");
        }

        // Start the ngrok tunnel via the runner (runner must not log the secret)
        await _runner.StartAsync(localUrl, secret.Value, cancellationToken);

        var endpoint = new PublicEndpoint
        {
            BaseUrl = _runner.BaseUrl,
            SecretPath = $"share/{secretToken}"
        };

        _status = new TransportStatus { State = TransportStatusState.Running, PublicUrl = endpoint.FullUrl };
        return endpoint;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _runner.StopAsync(cancellationToken);
        _status = new TransportStatus { State = TransportStatusState.Stopped };
    }

    public TransportStatus GetStatus() => _status;

    public override string ToString()
    {
        // Never expose the authtoken; only show the public URL if available
        return $"NgrokTransport(Status={_status.State}, PublicUrl={_status.PublicUrl ?? "<none>"})";
    }
}
