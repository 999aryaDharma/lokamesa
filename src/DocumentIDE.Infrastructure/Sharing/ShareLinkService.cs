using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Core.Sharing;

namespace DocumentIDE.Infrastructure.Sharing;

/// <summary>
/// Owner-side view of where a share can be reached from.
/// <see cref="BaseUrl"/> is always absolute when non-null; <see cref="TunnelError"/>
/// carries the transport failure when the public tunnel is down so the owner UI
/// can show "ngrok unavailable / credentials missing" (spec §27) instead of a
/// silent hardcoded null.
/// </summary>
public sealed record ShareLinkState(string? BaseUrl, string? TunnelError)
{
    public static readonly ShareLinkState None = new(null, null);
}

/// <summary>
/// B5 — composes the public viewer URL for a share (spec §16 step "create final
/// URL with /share/&lt;secret&gt;").
///
/// It owns two seams that were previously never wired: the separate ShareHost
/// process (<see cref="IShareHostProcess"/>) and the ngrok transport
/// (<see cref="IShareTransport"/>). On the first active share it starts the
/// loopback viewer host and the tunnel; the public URL is
/// <c>{base}/share/{shareId}?token={rawToken}</c>, which the ShareHost viewer
/// route and the B2 token gate both understand. When the tunnel cannot be
/// started it falls back to the local viewer URL and records the failure rather
/// than pretending the share is public.
/// </summary>
public sealed class ShareLinkService(IShareHostProcess shareHost, IShareTransport transport)
{
    private readonly object _sync = new();
    private readonly HashSet<string> _activeShares = new(StringComparer.Ordinal);
    private string? _baseUrl;
    private string? _tunnelError;

    /// <summary>Current base URL/error without starting anything (reads only).</summary>
    public ShareLinkState Snapshot()
    {
        lock (_sync)
        {
            return new ShareLinkState(_baseUrl, _tunnelError);
        }
    }

    /// <summary>
    /// Registers an active share, ensuring the viewer host + tunnel are running,
    /// and returns the absolute URL the owner should copy.
    /// </summary>
    public async Task<ShareLinkState> ActivateAsync(string shareId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(shareId))
        {
            return ShareLinkState.None;
        }

        string? localBase;
        try
        {
            localBase = await shareHost.EnsureStartedAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            lock (_sync)
            {
                _activeShares.Add(shareId);
                _baseUrl = null;
                _tunnelError = ex.Message;
                return new ShareLinkState(_baseUrl, _tunnelError);
            }
        }

        try
        {
            var endpoint = await transport.StartAsync(localBase, shareId, cancellationToken);
            lock (_sync)
            {
                _activeShares.Add(shareId);
                _baseUrl = string.IsNullOrWhiteSpace(endpoint?.BaseUrl) ? localBase : endpoint!.BaseUrl;
                _tunnelError = null;
                return new ShareLinkState(_baseUrl, _tunnelError);
            }
        }
        catch (Exception ex)
        {
            // No public tunnel: keep the local viewer reachable and surface why.
            lock (_sync)
            {
                _activeShares.Add(shareId);
                _baseUrl = localBase;
                _tunnelError = ex.Message;
                return new ShareLinkState(_baseUrl, _tunnelError);
            }
        }
    }

    /// <summary>
    /// Releases a stopped/revoked share. When no active share remains the tunnel
    /// is stopped (spec §17: "stop transport if no other active share needs it").
    /// </summary>
    public async Task DeactivateAsync(string shareId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(shareId))
        {
            return;
        }

        bool noneRemain;
        lock (_sync)
        {
            _activeShares.Remove(shareId);
            noneRemain = _activeShares.Count == 0;
        }

        if (!noneRemain)
        {
            return;
        }

        try
        {
            await transport.StopAsync(cancellationToken);
        }
        catch (Exception)
        {
            // Transport teardown is best-effort; the on-disk policy still denies.
        }

        lock (_sync)
        {
            _baseUrl = null;
            _tunnelError = null;
        }
    }

    /// <summary>Builds the absolute viewer URL for a share, or a relative path when unknown.</summary>
    public static string BuildUrl(string shareId, string? rawToken, string? baseUrl)
    {
        var path = string.IsNullOrEmpty(rawToken)
            ? $"/share/{shareId}"
            : $"/share/{shareId}?token={rawToken}";

        return string.IsNullOrWhiteSpace(baseUrl)
            ? path
            : $"{baseUrl.TrimEnd('/')}{path}";
    }
}
