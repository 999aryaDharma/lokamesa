using System.Text.Json;
using DocumentIDE.Api.Lifecycle;

namespace DocumentIDE.Desktop.WebView;

/// <summary>
/// Desktop side of the DEV-UPD restart handshake. The API host calls
/// <see cref="PrepareRestartAsync"/> before the watcher swaps the install
/// directory; the UI bridge saves dirty buffers and reports whether that
/// succeeded.
///
/// <para>
/// WebView2's <c>ExecuteScriptAsync</c> returns the JSON value of the script it
/// ran. A Promise serializes as <c>{}</c> rather than its resolved value, so the
/// injected script stores its result in
/// <c>window.__DOCUMENTIDE_RESTART_RESULT__</c> and <see cref="PollScript"/>
/// reads it until the flush settles.
/// </para>
/// </summary>
public sealed class RestartCoordinator : IRestartFlushBridge
{
    public Func<CancellationToken, Task<RestartFlushResult>>? FlushAsync { get; set; }

    public Task<RestartFlushResult> PrepareRestartAsync(CancellationToken cancellationToken) =>
        FlushAsync is null
            ? Task.FromResult(new RestartFlushResult(true, null))
            : FlushAsync(cancellationToken);

    /// <summary>
    /// Starts the page-side flush. A page without the bridge (for example one
    /// that loaded before the SPA registered it) succeeds immediately: there is
    /// no editor buffer to lose.
    /// </summary>
    public const string StartScript = """
        (function () {
          window.__DOCUMENTIDE_RESTART_RESULT__ = null;
          if (!window.__DOCUMENTIDE_PREPARE_RESTART__) {
            window.__DOCUMENTIDE_RESTART_RESULT__ = { ok: true };
            return true;
          }
          Promise.resolve()
            .then(function () { return window.__DOCUMENTIDE_PREPARE_RESTART__(); })
            .then(function (result) {
              window.__DOCUMENTIDE_RESTART_RESULT__ = result || { ok: true };
            })
            .catch(function (error) {
              window.__DOCUMENTIDE_RESTART_RESULT__ = { ok: false, reason: String(error) };
            });
          return true;
        })()
        """;

    /// <summary>Returns the settled result as a JSON string, or <c>null</c>.</summary>
    public const string PollScript =
        "window.__DOCUMENTIDE_RESTART_RESULT__ ? JSON.stringify(window.__DOCUMENTIDE_RESTART_RESULT__) : null";

    /// <summary>
    /// Parses the <c>ExecuteScriptAsync</c> result of <see cref="PollScript"/>.
    /// Returns <c>null</c> while the page has not settled yet; malformed values
    /// become a failure so a restart never proceeds on an unknown state.
    /// </summary>
    public static RestartFlushResult? ParsePollResult(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || string.Equals(raw, "null", StringComparison.Ordinal))
        {
            return null;
        }

        string? payload;
        try
        {
            // ExecuteScriptAsync JSON-encodes the string returned by the script.
            payload = JsonSerializer.Deserialize<string?>(raw);
        }
        catch (JsonException)
        {
            return new RestartFlushResult(false, "invalid-flush-result");
        }

        if (payload is null)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            bool ok = root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("ok", out var okElement)
                && okElement.ValueKind == JsonValueKind.True;
            string? reason = root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("reason", out var reasonElement)
                && reasonElement.ValueKind == JsonValueKind.String
                    ? reasonElement.GetString()
                    : null;

            return new RestartFlushResult(ok, ok ? null : reason ?? "flush-failed");
        }
        catch (JsonException)
        {
            return new RestartFlushResult(false, "invalid-flush-result");
        }
    }
}
