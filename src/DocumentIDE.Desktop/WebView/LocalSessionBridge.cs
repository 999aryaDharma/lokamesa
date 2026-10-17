using System;
using Microsoft.Web.WebView2.Core;

namespace DocumentIDE.Desktop.WebView;

public class LocalSessionBridge
{
    public string SessionToken { get; }
    public int ApiPort { get; set; }

    /// <summary>
    /// Workspace the shipped app shell bootstraps (spec 03 §2 / master §83).
    /// The web app shell reads <c>window.__DOCUMENTIDE_WORKSPACE_ID__</c> and
    /// falls back to <c>default</c> when it is absent, so the same value is used
    /// here as the default.
    /// </summary>
    public string WorkspaceId { get; set; } = "default";

    public LocalSessionBridge(string sessionToken, int apiPort = 0)
    {
        SessionToken = sessionToken ?? throw new ArgumentNullException(nameof(sessionToken));
        ApiPort = apiPort;
    }

    /// <summary>
    /// Builds the pre-load script that hands the app shell the privileged API
    /// base URL, session token and workspace id. Kept as a pure string so the
    /// injected contract is unit-testable without a WebView2 runtime.
    /// </summary>
    public string BuildInjectionScript(string apiUrl) =>
        $"window.__DOCUMENTIDE_API_URL__ = '{apiUrl}'; " +
        $"window.__DOCUMENTIDE_SESSION_TOKEN__ = '{SessionToken}'; " +
        $"window.__DOCUMENTIDE_WORKSPACE_ID__ = '{WorkspaceId}';";

    public bool ShouldInjectHeader(Uri targetUri, int apiPort = 0)
    {
        if (targetUri == null) return false;

        var portToCheck = apiPort > 0 ? apiPort : ApiPort;

        bool isLoopbackHost = string.Equals(targetUri.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(targetUri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(targetUri.Host, "[::1]", StringComparison.OrdinalIgnoreCase);

        if (!isLoopbackHost)
        {
            return false;
        }

        if (portToCheck > 0 && targetUri.Port != portToCheck)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Injects session token header on outgoing WebView2 requests targeting privileged loopback endpoints.
    /// </summary>
    public void AttachToWebView(CoreWebView2 webView2, string apiUrl)
    {
        if (webView2 == null) throw new ArgumentNullException(nameof(webView2));

        webView2.AddScriptToExecuteOnDocumentCreatedAsync(BuildInjectionScript(apiUrl));

        webView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        webView2.WebResourceRequested += (sender, args) =>
        {
            if (Uri.TryCreate(args.Request.Uri, UriKind.Absolute, out var uri))
            {
                if (ShouldInjectHeader(uri, ApiPort))
                {
                    args.Request.Headers.SetHeader("X-DocumentIDE-Session-Token", SessionToken);
                }
            }
        };
    }
}
