using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace DocumentIDE.Api.Security;

public class PrivilegedRequestGuard
{
    private readonly RequestDelegate _next;
    private static readonly HashSet<string> AllowedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "localhost",
        "127.0.0.1",
        "[::1]"
    };

    public PrivilegedRequestGuard(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ISessionTokenProvider sessionTokenProvider)
    {
        // 1. Host Header Validation (DNS Rebinding Check)
        var rawHost = context.Request.Host.Value;
        if (string.IsNullOrEmpty(rawHost))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsync("Forbidden: Host header missing.");
            return;
        }

        string hostWithoutPort = ExtractHostWithoutPort(rawHost);
        if (string.IsNullOrEmpty(hostWithoutPort) || !AllowedHosts.Contains(hostWithoutPort))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsync("Forbidden: Host header mismatch.");
            return;
        }

        // 2. Fetch-Metadata Defense-in-Depth Check
        if (context.Request.Headers.TryGetValue("Sec-Fetch-Site", out var secFetchSite))
        {
            if (string.Equals(secFetchSite, "cross-site", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsync("Forbidden: Cross-site requests rejected.");
                return;
            }
        }

        // 3. Origin Header Validation
        if (context.Request.Headers.TryGetValue("Origin", out var originHeader))
        {
            string origin = originHeader.ToString();
            if (string.Equals(origin, "null", StringComparison.OrdinalIgnoreCase) || !IsAllowedOrigin(origin))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsync("Forbidden: Origin is rejected.");
                return;
            }

            // Set CORS headers for valid origin
            context.Response.Headers["Access-Control-Allow-Origin"] = origin;
            context.Response.Headers["Access-Control-Allow-Headers"] = "X-DocumentIDE-Session-Token, Content-Type, Authorization";
            context.Response.Headers["Access-Control-Allow-Methods"] = "GET, POST, PUT, DELETE, OPTIONS";
            context.Response.Headers["Vary"] = "Origin";
        }

        // Handle preflight OPTIONS requests
        if (HttpMethods.IsOptions(context.Request.Method))
        {
            context.Response.StatusCode = StatusCodes.Status204NoContent;
            return;
        }

        // 4. Session Token Authorization Header Check
        string? token = context.Request.Headers["X-DocumentIDE-Session-Token"].FirstOrDefault();
        if (string.IsNullOrEmpty(token))
        {
            var authHeader = context.Request.Headers["Authorization"].FirstOrDefault();
            if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                token = authHeader.Substring("Bearer ".Length).Trim();
            }
        }

        if (!sessionTokenProvider.ValidateToken(token))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Unauthorized: Missing or invalid session token.");
            return;
        }

        await _next(context);
    }

    private static string ExtractHostWithoutPort(string rawHost)
    {
        if (string.IsNullOrWhiteSpace(rawHost))
            return string.Empty;

        // If IPv6 address with brackets like [::1] or [::1]:5000 or [::1].attacker.com
        if (rawHost.StartsWith("["))
        {
            int closingBracket = rawHost.IndexOf(']');
            if (closingBracket < 0)
                return string.Empty;

            if (closingBracket == rawHost.Length - 1)
            {
                return rawHost;
            }

            if (rawHost[closingBracket + 1] == ':')
            {
                return rawHost.Substring(0, closingBracket + 1);
            }

            return rawHost;
        }

        int colonIdx = rawHost.LastIndexOf(':');
        if (colonIdx > 0)
        {
            return rawHost.Substring(0, colonIdx);
        }

        return rawHost;
    }

    private static bool IsAllowedOrigin(string origin)
    {
        if (string.IsNullOrWhiteSpace(origin))
        {
            return false;
        }

        // Check for IPv6 bracket spoofing (e.g. http://[::1].attacker.com)
        int closeBracket = origin.IndexOf(']');
        if (closeBracket >= 0 && closeBracket + 1 < origin.Length)
        {
            char nextChar = origin[closeBracket + 1];
            if (nextChar != ':' && nextChar != '/' && nextChar != '?' && nextChar != '#')
            {
                return false;
            }
        }

        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(uri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(uri.Host, "[::1]", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase) &&
            (string.Equals(uri.Host, "appassets.html2sav", StringComparison.OrdinalIgnoreCase) ||
             uri.Host.EndsWith(".appassets.html2sav", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return false;
    }
}
