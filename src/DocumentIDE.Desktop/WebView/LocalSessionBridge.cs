using System;
using Microsoft.Web.WebView2.Core;

namespace DocumentIDE.Desktop.WebView;

public class LocalSessionBridge
{
    public string SessionToken { get; }
    public int ApiPort { get; set; }

    public LocalSessionBridge(string sessionToken, int apiPort = 0)
    {
        SessionToken = sessionToken ?? throw new ArgumentNullException(nameof(sessionToken));
        ApiPort = apiPort;
    }

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

        webView2.AddScriptToExecuteOnDocumentCreatedAsync(
            $"window.__DOCUMENTIDE_API_URL__ = '{apiUrl}'; window.__DOCUMENTIDE_SESSION_TOKEN__ = '{SessionToken}';"
        );

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
