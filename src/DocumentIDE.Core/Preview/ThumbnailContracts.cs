using System;
using System.Threading;
using System.Threading.Tasks;

namespace DocumentIDE.Core.Preview;

/// <summary>
/// Status of a cached document thumbnail preview shell.
/// </summary>
public enum ThumbnailStatus
{
    Loading,
    Ready,
    Stale,
    Missing,
    Failed
}

/// <summary>
/// Supported thumbnail render formats.
/// </summary>
public enum ThumbnailFormat
{
    Png,
    Jpeg,
    Svg,
    TextSkeleton
}

/// <summary>
/// Metadata for a cached document paper object thumbnail.
/// Thumbnail is derived/cached data only.
/// </summary>
public record ThumbnailMetadata(
    string DocumentId,
    string? ContentHash,
    ThumbnailStatus Status,
    int Width,
    int Height,
    ThumbnailFormat Format,
    string? DataUrl,
    DateTimeOffset GeneratedAt,
    string? ErrorMessage = null
);

/// <summary>
/// Request parameters for thumbnail generation/retrieval.
/// </summary>
public record ThumbnailRequest(
    string DocumentId,
    string FilePath,
    string DocumentType,
    bool ForceRefresh = false
);

/// <summary>
/// API contract for managing the thumbnail cache.
/// </summary>
public interface IThumbnailCache
{
    Task<ThumbnailMetadata?> GetAsync(string documentId, CancellationToken cancellationToken = default);
    Task SaveAsync(ThumbnailMetadata thumbnail, CancellationToken cancellationToken = default);
    Task InvalidateAsync(string documentId, CancellationToken cancellationToken = default);
}
