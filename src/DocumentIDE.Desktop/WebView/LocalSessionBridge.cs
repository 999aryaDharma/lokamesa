using System;
using Microsoft.Web.WebView2.Core;

namespace DocumentIDE.Desktop.WebView;

public class LocalSessionBridge
{
    public string SessionToken { get; }

    public LocalSessionBridge(string sessionToken)
    {
        SessionToken = sessionToken ?? throw new ArgumentNullException(nameof(sessionToken));
    }

    /// <summary>
    /// Injects session token header on outgoing WebView2 requests to loopback endpoints.
    /// </summary>
    public void AttachToWebView(CoreWebView2 webView2)
    {
        if (webView2 == null) throw new ArgumentNullException(nameof(webView2));

        // Inject session token into window.__DOCUMENTIDE_SESSION_TOKEN__ for frontend fetch requests
        webView2.AddScriptToExecuteOnDocumentCreatedAsync(
            $"window.__DOCUMENTIDE_SESSION_TOKEN__ = '{SessionToken}';"
        );

        // Intercept requests to loopback API and attach session token header
        webView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        webView2.WebResourceRequested += (sender, args) =>
        {
            if (Uri.TryCreate(args.Request.Uri, UriKind.Absolute, out var uri))
            {
                if (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(uri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(uri.Host, "[::1]", StringComparison.OrdinalIgnoreCase))
                {
                    args.Request.Headers.SetHeader("X-DocumentIDE-Session-Token", SessionToken);
                }
            }
        };
    }
}
