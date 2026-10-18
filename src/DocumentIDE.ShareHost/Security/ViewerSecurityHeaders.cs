using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace DocumentIDE.ShareHost.Security
{
    /// <summary>
    /// Applies the public viewer hardening headers to every ShareHost response:
    /// strict Content-Security-Policy, no-sniff, referrer, frame and form policies.
    /// Headers are set before the rest of the pipeline runs so they are also present on
    /// error and rate-limit (429) responses.
    /// </summary>
    /// <remarks>
    /// CSP baseline from the master spec §101.11. <c>style-src 'self'</c> is added to the
    /// baseline because view stylesheets must be bundled locally ('self'); it stays free of
    /// <c>unsafe-inline</c>. <c>referrer-policy: no-referrer</c> prevents a share URL (which
    /// carries the secret token) from leaking to third parties via the Referer header.
    /// </remarks>
    public static class ViewerSecurityHeaders
    {
        public const string ContentSecurityPolicyValue =
            "default-src 'none'; " +
            "script-src 'self'; " +
            "style-src 'self'; " +
            "connect-src 'self'; " +
            "img-src 'self' data:; " +
            "font-src 'self'; " +
            "object-src 'none'; " +
            "base-uri 'none'; " +
            "frame-ancestors 'none'; " +
            "form-action 'none'";

        public const string ContentTypeOptionsValue = "nosniff";
        public const string ReferrerPolicyValue = "no-referrer";
        public const string FrameOptionsValue = "DENY";

        /// <summary>Sets the viewer hardening headers on a response.</summary>
        public static void Apply(IHeaderDictionary headers)
        {
            headers["Content-Security-Policy"] = ContentSecurityPolicyValue;
            headers["X-Content-Type-Options"] = ContentTypeOptionsValue;
            headers["Referrer-Policy"] = ReferrerPolicyValue;
            headers["X-Frame-Options"] = FrameOptionsValue;
        }

        /// <summary>Adds the viewer security header middleware to the public pipeline.</summary>
        public static IApplicationBuilder UseViewerSecurityHeaders(this IApplicationBuilder app)
        {
            return app.Use(async (context, next) =>
            {
                Apply(context.Response.Headers);
                await next(context);
            });
        }
    }
}
