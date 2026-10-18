using System;
using System.Globalization;
using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DocumentIDE.ShareHost.Security
{
    /// <summary>
    /// Server-side rate-limit budgets for the public ShareHost. V1 defaults from the master
    /// specification §101.10; all values are configurable per deployment.
    /// </summary>
    public sealed class ShareRateLimitOptions
    {
        /// <summary>General share reads allowed per client IP per minute. Default 120.</summary>
        public int GeneralReadsPerMinutePerIp { get; set; } = 120;

        /// <summary>Share search requests allowed per client IP per minute. Default 30.</summary>
        public int SearchesPerMinutePerIp { get; set; } = 30;

        /// <summary>Download requests allowed per client IP per minute. Default 10.</summary>
        public int DownloadsPerMinutePerIp { get; set; } = 10;

        /// <summary>Concurrent downloads allowed per client IP. Default 2.</summary>
        public int ConcurrentDownloadsPerIp { get; set; } = 2;

        /// <summary>
        /// Process-wide cap on concurrent expensive public work (artifact/preview serving and
        /// downloads) across all client IPs, so the laptop cannot be swamped from many sources.
        /// </summary>
        public int GlobalExpensiveWorkConcurrency { get; set; } = 8;
    }

    /// <summary>
    /// Enforces the public ShareHost rate limits entirely server-side using the ASP.NET Core
    /// rate-limiting middleware. Rejections return <c>429 Too Many Requests</c> with
    /// <c>Retry-After</c> header and JSON retry information.
    /// </summary>
    /// <remarks>
    /// Client identity: the ShareHost binds loopback-only and is exposed through the ngrok
    /// tunnel, so the immediate peer is the local/tunnel process. For such peers the right-most
    /// <c>X-Forwarded-For</c> entry (the closest hop, appended by the tunnel) is used as the
    /// client IP; otherwise the socket remote address is used. Invalid/oversized forwarded
    /// values fall back to the socket address.
    /// </remarks>
    public static class ShareRateLimits
    {
        public const string ForwardedForHeaderName = "X-Forwarded-For";

        private const string UnknownClientKey = "unknown-client";
        private const string NoLimiterKey = "no-limiter";
        private const int MaxForwardedForLength = 128;
        private static readonly TimeSpan WindowLength = TimeSpan.FromMinutes(1);

        /// <summary>Registers the ShareHost rate-limit options and the global rate limiter.</summary>
        public static IServiceCollection AddShareRateLimits(this IServiceCollection services)
        {
            services.TryAddSingleton(new ShareRateLimitOptions());

            services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

                // Chained global limiter: every public request passes through the per-IP window
                // for its category, then (for downloads) the per-IP concurrency cap, then the
                // process-wide expensive-work cap. The first limiter that rejects produces the
                // 429; its lease carries the Retry-After metadata.
                options.GlobalLimiter = PartitionedRateLimiter.CreateChained(
                    CreatePerClientWindowLimiter(),
                    CreateConcurrentDownloadLimiter(),
                    CreateGlobalExpensiveWorkLimiter());

                options.OnRejected = async (context, cancellationToken) =>
                {
                    var response = context.HttpContext.Response;
                    response.StatusCode = StatusCodes.Status429TooManyRequests;

                    var retryAfterSeconds = 1;
                    if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter) &&
                        retryAfter > TimeSpan.Zero)
                    {
                        retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
                    }

                    response.Headers.RetryAfter = retryAfterSeconds.ToString(CultureInfo.InvariantCulture);
                    await response.WriteAsJsonAsync(
                        new { error = "rate_limit_exceeded", retryAfterSeconds },
                        cancellationToken);
                };
            });

            return services;
        }

        /// <summary>Resolves the rate-limit partition key for the request's client.</summary>
        internal static string GetClientKey(HttpContext context)
        {
            var remote = context.Connection.RemoteIpAddress;
            if (remote is null || IPAddress.IsLoopback(remote))
            {
                if (TryGetForwardedClientIp(context.Request.Headers, out var forwardedIp))
                {
                    return forwardedIp;
                }
            }

            return remote?.ToString() ?? UnknownClientKey;
        }

        /// <summary><c>GET /share/{token}/search</c>.</summary>
        internal static bool IsSearchRequest(PathString path)
        {
            var segments = GetSharePathSegments(path);
            return segments.Length == 3 &&
                   IsShareSegment(segments, 0) &&
                   segments[2].Equals("search", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary><c>GET /share/{token}/documents/{id}/download-original|download-pdf</c>.</summary>
        internal static bool IsDownloadRequest(PathString path)
        {
            var segments = GetSharePathSegments(path);
            return segments.Length == 5 &&
                   IsShareSegment(segments, 0) &&
                   segments[2].Equals("documents", StringComparison.OrdinalIgnoreCase) &&
                   (segments[4].Equals("download-original", StringComparison.OrdinalIgnoreCase) ||
                    segments[4].Equals("download-pdf", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Downloads, artifact/preview serving and document previews are the expensive public
        /// work (spec §23 "concurrent expensive preview limits").
        /// </summary>
        internal static bool IsExpensiveRequest(PathString path)
        {
            if (IsDownloadRequest(path))
            {
                return true;
            }

            var segments = GetSharePathSegments(path);
            if (segments.Length < 3 || !IsShareSegment(segments, 0))
            {
                return false;
            }

            if (segments[2].Equals("artifacts", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // GET /share/{token}/documents/{id}/preview
            return segments.Length == 5 &&
                   segments[2].Equals("documents", StringComparison.OrdinalIgnoreCase) &&
                   segments[4].Equals("preview", StringComparison.OrdinalIgnoreCase);
        }

        private static PartitionedRateLimiter<HttpContext> CreatePerClientWindowLimiter()
        {
            return PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                var options = context.RequestServices.GetRequiredService<ShareRateLimitOptions>();
                var clientKey = GetClientKey(context);
                var path = context.Request.Path;

                if (IsSearchRequest(path))
                {
                    return RateLimitPartition.GetFixedWindowLimiter(
                        $"search|{clientKey}",
                        _ => CreateWindowOptions(options.SearchesPerMinutePerIp));
                }

                if (IsDownloadRequest(path))
                {
                    return RateLimitPartition.GetFixedWindowLimiter(
                        $"download|{clientKey}",
                        _ => CreateWindowOptions(options.DownloadsPerMinutePerIp));
                }

                return RateLimitPartition.GetFixedWindowLimiter(
                    $"general|{clientKey}",
                    _ => CreateWindowOptions(options.GeneralReadsPerMinutePerIp));
            });
        }

        private static PartitionedRateLimiter<HttpContext> CreateConcurrentDownloadLimiter()
        {
            return PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                if (!IsDownloadRequest(context.Request.Path))
                {
                    return RateLimitPartition.GetNoLimiter(NoLimiterKey);
                }

                var options = context.RequestServices.GetRequiredService<ShareRateLimitOptions>();
                return RateLimitPartition.GetConcurrencyLimiter(
                    $"download-concurrency|{GetClientKey(context)}",
                    _ => new ConcurrencyLimiterOptions
                    {
                        PermitLimit = options.ConcurrentDownloadsPerIp,
                        QueueLimit = 0
                    });
            });
        }

        private static PartitionedRateLimiter<HttpContext> CreateGlobalExpensiveWorkLimiter()
        {
            return PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                if (!IsExpensiveRequest(context.Request.Path))
                {
                    return RateLimitPartition.GetNoLimiter(NoLimiterKey);
                }

                var options = context.RequestServices.GetRequiredService<ShareRateLimitOptions>();
                return RateLimitPartition.GetConcurrencyLimiter(
                    "global-expensive-work",
                    _ => new ConcurrencyLimiterOptions
                    {
                        PermitLimit = options.GlobalExpensiveWorkConcurrency,
                        QueueLimit = 0
                    });
            });
        }

        private static FixedWindowRateLimiterOptions CreateWindowOptions(int permitLimitPerMinute) => new()
        {
            PermitLimit = permitLimitPerMinute,
            Window = WindowLength,
            QueueLimit = 0,
            AutoReplenishment = true
        };

        private static bool TryGetForwardedClientIp(IHeaderDictionary headers, out string clientIp)
        {
            clientIp = string.Empty;

            var raw = headers[ForwardedForHeaderName].ToString();
            if (string.IsNullOrWhiteSpace(raw) || raw.Length > MaxForwardedForLength)
            {
                return false;
            }

            // Right-most entry: added by the closest hop (the local tunnel); earlier entries are
            // client-supplied and untrusted.
            var candidate = raw.Split(',')[^1].Trim();
            if (candidate.Length == 0 ||
                candidate.Length > MaxForwardedForLength ||
                !IPAddress.TryParse(candidate, out var parsed))
            {
                return false;
            }

            clientIp = parsed.ToString();
            return true;
        }

        private static string[] GetSharePathSegments(PathString path)
        {
            var value = path.Value;
            if (string.IsNullOrEmpty(value) ||
                !value.StartsWith("/share/", StringComparison.OrdinalIgnoreCase))
            {
                return Array.Empty<string>();
            }

            return value.Split('/', StringSplitOptions.RemoveEmptyEntries);
        }

        private static bool IsShareSegment(string[] segments, int index) =>
            segments[index].Equals("share", StringComparison.OrdinalIgnoreCase);
    }
}
