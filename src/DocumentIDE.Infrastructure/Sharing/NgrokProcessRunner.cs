using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DocumentIDE.Infrastructure.Sharing;

/// <summary>
/// B5 — production <see cref="INgrokProcessRunner"/>. Before B5 the interface
/// existed but had no implementation anywhere in the product (only a test mock),
/// so <see cref="NgrokTransport"/> could never actually expose anything.
///
/// It launches <c>ngrok http &lt;localUrl&gt;</c> with the user's protected
/// authtoken and reads the public URL from ngrok's local API
/// (<c>http://127.0.0.1:4040/api/tunnels</c>). The authtoken is never written to
/// <see cref="LoggedOutput"/>: every captured line and error message is redacted.
/// </summary>
public sealed class NgrokProcessRunner : INgrokProcessRunner, IDisposable
{
    public const string DefaultLocalApiUrl = "http://127.0.0.1:4040/api/tunnels";
    public const string PathEnvironmentVariable = "DOCUMENTIDE_NGROK_PATH";

    private readonly string? _explicitPath;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly TimeSpan _probeTimeout;
    private readonly StringBuilder _log = new();
    private Process? _process;

    public NgrokProcessRunner(
        string? ngrokPath = null,
        HttpClient? httpClient = null,
        TimeSpan? probeTimeout = null)
    {
        _explicitPath = ngrokPath;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        _ownsHttpClient = httpClient == null;
        _probeTimeout = probeTimeout ?? TimeSpan.FromSeconds(20);
    }

    public string BaseUrl { get; set; } = string.Empty;

    public string LoggedOutput => _log.ToString();

    public async Task StartAsync(string localUrl, string authToken, CancellationToken cancellationToken = default)
    {
        var executable = ResolveExecutable()
            ?? throw new InvalidOperationException(
                $"ngrok executable not found. Set {PathEnvironmentVariable} or install ngrok on PATH.");

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("http");
        startInfo.ArgumentList.Add(localUrl);
        startInfo.ArgumentList.Add("--authtoken=" + authToken);
        startInfo.ArgumentList.Add("--log=stdout");

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => AppendRedacted(e.Data, authToken);
        process.ErrorDataReceived += (_, e) => AppendRedacted(e.Data, authToken);

        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException("Failed to start the ngrok process.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        _process = process;

        BaseUrl = await ProbePublicUrlAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        var process = _process;
        _process = null;
        BaseUrl = string.Empty;

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

    private async Task<string> ProbePublicUrlAsync(CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + _probeTimeout;
        Exception? lastError = null;

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var json = await _httpClient.GetStringAsync(DefaultLocalApiUrl, cancellationToken);
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.TryGetProperty("tunnels", out var tunnels) &&
                    tunnels.ValueKind == JsonValueKind.Array)
                {
                    foreach (var tunnel in tunnels.EnumerateArray())
                    {
                        if (tunnel.TryGetProperty("public_url", out var publicUrl) &&
                            publicUrl.ValueKind == JsonValueKind.String)
                        {
                            var value = publicUrl.GetString();
                            if (!string.IsNullOrWhiteSpace(value))
                            {
                                return value!.TrimEnd('/');
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                lastError = ex;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
        }

        throw new InvalidOperationException(
            "ngrok tunnel did not report a public URL in time." +
            (lastError == null ? string.Empty : " " + lastError.Message));
    }

    private string? ResolveExecutable()
    {
        // An explicitly supplied path is authoritative: when it is missing we
        // fail rather than silently falling back to an unrelated ngrok on PATH.
        if (!string.IsNullOrWhiteSpace(_explicitPath))
        {
            return File.Exists(_explicitPath) ? _explicitPath : null;
        }

        var configured = Environment.GetEnvironmentVariable(PathEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
        {
            return configured;
        }

        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var name in new[] { "ngrok.exe", "ngrok" })
            {
                try
                {
                    var candidate = Path.Combine(directory.Trim(), name);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
                catch (Exception)
                {
                    // A malformed PATH entry simply does not match.
                }
            }
        }

        return null;
    }

    private void AppendRedacted(string? line, string authToken)
    {
        if (string.IsNullOrEmpty(line))
        {
            return;
        }

        var safe = Redact(line, authToken);
        lock (_log)
        {
            _log.AppendLine(safe);
        }
    }

    /// <summary>Removes the ngrok authtoken from any captured line before it is logged.</summary>
    public static string Redact(string? line, string authToken)
    {
        if (string.IsNullOrEmpty(line))
        {
            return string.Empty;
        }

        return string.IsNullOrEmpty(authToken)
            ? line
            : line.Replace(authToken, "***", StringComparison.Ordinal);
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
