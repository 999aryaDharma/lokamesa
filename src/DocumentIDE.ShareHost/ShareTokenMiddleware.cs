using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using DocumentIDE.Core.Sharing;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;

namespace DocumentIDE.ShareHost
{
    /// <summary>
    /// Minimal middleware placeholder that validates a share token if present.
    /// Real security logic can be added later; for now it simply forwards the request.
    /// </summary>
    public class ShareTokenMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ShareTokenService? _service;
        // Primary constructor used by ASP.NET Core pipeline (DI will supply RequestDelegate)
        public ShareTokenMiddleware(RequestDelegate next) => _next = next;
        // Overload for unit tests that want to inject the service directly
        public ShareTokenMiddleware(RequestDelegate next, ShareTokenService service)
        {
            _next = next;
            _service = service;
        }

        public async Task InvokeAsync(HttpContext context, ShareTokenService? tokenService = null)
        {
            // Prefer the DI-provided service, fallback to the injected field for tests
            var service = tokenService ?? _service;
            if (service == null)
            {
                // If no token service is available, treat as unauthorized
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }
            // Extract shareId from route values
            var shareId = context.Request.RouteValues["shareId"]?.ToString();
            // Extract token from header
            var token = context.Request.Headers["X-Share-Token"].FirstOrDefault();

            if (string.IsNullOrEmpty(shareId) || string.IsNullOrEmpty(token))
            {
                // Missing shareId or token - proceed without validation
                await _next(context);
                return;
            }

            if (!service.VerifyTokenForShare(shareId, token))
            {
                // Invalid token -> forbidden
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            // Token valid -> proceed
            await _next(context);
        }
    }
}
