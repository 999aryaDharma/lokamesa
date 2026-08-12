using System.Threading;
using System.Threading.Tasks;

namespace DocumentIDE.Infrastructure.Sharing
{
    public interface INgrokProcessRunner
    {
        string BaseUrl { get; set; }
        string LoggedOutput { get; }
        Task StartAsync(string localUrl, string authToken, CancellationToken cancellationToken = default);
        Task StopAsync(CancellationToken cancellationToken = default);
    }
}
