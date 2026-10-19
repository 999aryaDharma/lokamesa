using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Core.Sharing;

namespace DocumentIDE.Infrastructure.Sharing;

/// <summary>
/// B5 — production <see cref="IShareHostProcess"/>. The privileged app launches
/// the separate <c>DocumentIDE.ShareHost</c> executable on loopback and hands the
/// transport its base URL (spec §16: "start restricted server → start ngrok").
///
/// The child binds the port this owner reserves
/// (<c>DOCUMENTIDE_SHAREHOST_PORT</c>) so the owner knows the URL, and the owner
/// waits for the host's health route before reporting success. When the
/// executable is not deployed the start fails fast with a typed error instead of
/// pretending a public link exists.
/// </summary>
public sealed class ShareHostProcess : IShareHostProcess, IDisposable
{
    public const string ExecutableEnvironmentVariable = "DOCUMENTIDE_SHAREHOST_PATH";
    public const string PortEnvironmentVariable = "DOCUMENTIDE_SHAREHOST_PORT";
    public const string ExecutableFileName = "DocumentIDE.ShareHost.exe";

    private readonly string? _explicitPath;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly TimeSpan _startupTimeout;
    private readonly object _sync = new();
    private Process? _process;

    public ShareHostProcess(
        string? executablePath = null,
        HttpClient? httpClient = null,
        TimeSpan? startupTimeout = null)
    {
        _explicitPath = executablePath;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        _ownsHttpClient = httpClient == null;
        _startupTimeout = startupTimeout ?? TimeSpan.FromSeconds(30);
    }

    public string? LocalBaseUrl { get; private set; }

    public async Task<string> EnsureStartedAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (_process is { HasExited: false } && !string.IsNullOrWhiteSpace(LocalBaseUrl))
            {
                return LocalBaseUrl!;
            }
        }

        var executable = ResolveExecutable()
            ?? throw new InvalidOperationException(
                $"ShareHost executable not found. Expected '{ExecutableFileName}' next to the app or {ExecutableEnvironmentVariable}.");

        var port = ReserveFreeLoopbackPort();
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.Environment[PortEnvironmentVariable] = port.ToString();

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start the ShareHost process.");

        var baseUrl = $"http://127.0.0.1:{port}";
        try
        {
            await WaitForHealthAsync(baseUrl, cancellationToken);
        }
        catch
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            process.Dispose();
            throw;
        }

        lock (_sync)
        {
            _process = process;
            LocalBaseUrl = baseUrl;
        }
        return baseUrl;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        Process? process;
        lock (_sync)
        {
            process = _process;
            _process = null;
            LocalBaseUrl = null;
        }

        if (process != null)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception)
            {
                // Teardown is best-effort.
            }
            finally
            {
                process.Dispose();
            }
        }

        return Task.CompletedTask;
    }

    private async Task WaitForHealthAsync(string baseUrl, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + _startupTimeout;
        Exception? lastError = null;

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var response = await _httpClient.GetAsync(baseUrl + "/", cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (Exception ex)
            {
                lastError = ex;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
        }

        throw new InvalidOperationException(
            "ShareHost did not become healthy in time." +
            (lastError == null ? string.Empty : " " + lastError.Message));
    }

    private string? ResolveExecutable()
    {
        // An explicitly supplied path is authoritative (used by the owner to
        // point at a deployed exe); a missing explicit path fails deterministically.
        if (!string.IsNullOrWhiteSpace(_explicitPath))
        {
            return File.Exists(_explicitPath) ? _explicitPath : null;
        }

        var configured = Environment.GetEnvironmentVariable(ExecutableEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
        {
            return configured;
        }

        var local = Path.Combine(AppContext.BaseDirectory, ExecutableFileName);
        return File.Exists(local) ? local : null;
    }

    private static int ReserveFreeLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}
