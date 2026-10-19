using System;
using System.IO;
using System.Threading.Tasks;
using DocumentIDE.Infrastructure.Sharing;
using Xunit;

namespace DocumentIDE.IntegrationTests.Sharing;

/// <summary>
/// B5 — safety of the production public-path launcher.
///
/// <see cref="NgrokProcessRunner"/> and <see cref="ShareHostProcess"/> are the
/// two production implementations that B5 added; before that no concrete
/// <c>INgrokProcessRunner</c> or ShareHost launcher existed. These lanes pin the
/// fail-closed behaviour: a missing executable is a typed error (never a silent
/// pretend-success) and the ngrok authtoken can never reach the logs.
/// </summary>
public sealed class PublicPathLauncherTests
{
    private static string MissingExe(string name) =>
        Path.Combine(Path.GetTempPath(), "documentide-missing-" + Guid.NewGuid().ToString("N"), name);

    [Fact]
    public async Task NgrokRunner_WhenExecutableMissing_ThrowsTypedError()
    {
        using var runner = new NgrokProcessRunner(ngrokPath: MissingExe("ngrok.exe"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => runner.StartAsync("http://127.0.0.1:5050", "auth-token-value"));

        Assert.Contains("ngrok", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(string.Empty, runner.BaseUrl);
    }

    [Fact]
    public void NgrokRunner_Redact_RemovesAuthtokenFromLogLines()
    {
        const string authtoken = "2abc123def456_ngrok_token_sample";
        var line = $"ngrok: using authtoken {authtoken} for tunnel";

        var redacted = NgrokProcessRunner.Redact(line, authtoken);

        Assert.DoesNotContain(authtoken, redacted, StringComparison.Ordinal);
        Assert.Contains("***", redacted);
    }

    [Fact]
    public async Task ShareHostProcess_WhenExecutableMissing_ThrowsTypedError()
    {
        using var process = new ShareHostProcess(executablePath: MissingExe("DocumentIDE.ShareHost.exe"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => process.EnsureStartedAsync());

        Assert.Contains("ShareHost", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(process.LocalBaseUrl);
    }
}
