using DocumentIDE.Api;
using DocumentIDE.Core.Sharing;
using DocumentIDE.Infrastructure.Sharing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DocumentIDE.IntegrationTests.Sharing;

/// <summary>
/// B5 — the privileged surface must register the production public-path
/// implementations. Before B5 <see cref="IShareTransport"/> had no registration,
/// <see cref="INgrokProcessRunner"/> had no product implementation, and no
/// ShareHost launcher or secret store existed, so the share controller could not
/// start ngrok or the viewer host at all.
/// </summary>
public sealed class PublicSharePathRegistrationTests
{
    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPrivilegedSurfaces();
        return services.BuildServiceProvider();
    }

    [Fact]
    public void PrivilegedSurfaces_RegisterThePublicSharePath()
    {
        using var provider = BuildProvider();

        Assert.IsType<NgrokProcessRunner>(provider.GetRequiredService<INgrokProcessRunner>());
        Assert.IsType<NgrokTransport>(provider.GetRequiredService<IShareTransport>());
        Assert.IsType<ShareHostProcess>(provider.GetRequiredService<IShareHostProcess>());
        Assert.NotNull(provider.GetRequiredService<DocumentIDE.Core.Security.ISecretStore>());

        var linkService = provider.GetRequiredService<ShareLinkService>();
        Assert.NotNull(linkService);
    }
}
