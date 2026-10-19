using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using DocumentIDE.Core.Sharing;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;

namespace DocumentIDE.ShareHost
{
    /// <summary>
    /// B2 — fail-closed token gate for the public ShareHost.
    ///
    /// Every request that carries a <c>shareId</c> route value must present the
    /// share token (header <c>X-Share-Token</c> or query <c>?token=</c>) and it is
    /// validated against the share package's on-disk access policy
    /// (<c>access.json</c>) written by the owner process. A missing policy, a
    /// cleared hash (revoked link), a past expiry (stopped link), a missing token,
    /// or any mismatch denies with 401 — a share package that the owner never
    /// registered a policy for is never served.
    ///
    /// Requests without a <c>shareId</c> route value (e.g. the health route) pass
    /// through untouched.
    /// </summary>
    public class ShareTokenMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ShareTokenService? _service;

        // Primary constructor used by the ASP.NET Core pipeline (DI supplies RequestDelegate)
        public ShareTokenMiddleware(RequestDelegate next) => _next = next;

        // Overload for unit tests that want to inject the service directly
        public ShareTokenMiddleware(RequestDelegate next, ShareTokenService service)
        {
            _next = next;
            _service = service;
        }

        public async Task InvokeAsync(HttpContext context, ShareTokenService? tokenService = null)
        {
            var shareId = context.Request.RouteValues["shareId"]?.ToString();
            if (string.IsNullOrEmpty(shareId))
            {
                // Not a share-scoped route (health, etc.): nothing to authorize.
                await _next(context);
                return;
            }

            // Prefer the DI-provided service, fall back to the injected field for tests.
            var service = tokenService ?? _service;
            if (service == null)
            {
                // No token service means we cannot prove authorization: deny.
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            var token = ExtractToken(context);
            if (!service.IsAccessGranted(shareId, token))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            await _next(context);
        }

        private static string? ExtractToken(HttpContext context)
        {
            var header = context.Request.Headers["X-Share-Token"].FirstOrDefault();
            if (!string.IsNullOrEmpty(header))
            {
                return header;
            }

            // Owner "Copy link" shape: /share/{id}?token=...
            return context.Request.Query["token"].FirstOrDefault();
        }
    }
}
