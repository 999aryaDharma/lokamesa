using System;
using System.Collections.Generic;
using System.Linq;
using DocumentIDE.Core.Domain;

namespace DocumentIDE.Core.Workspaces;

/// <summary>
/// An occupied canvas rectangle used when resolving a restored position.
/// Carries identity + geometry only; NEVER a physical path.
/// </summary>
public sealed record CanvasRect(string DocumentId, double X, double Y, double Width, double Height);

/// <summary>
/// A restored canvas position: exact coordinates when free, otherwise the
/// nearest-safe fallback (<see cref="RestoredPosition.FellBack"/>).
/// </summary>
public sealed record RestoredPosition(string DocumentId, double X, double Y, bool FellBack);

/// <summary>An auto object shifted by a local reflow step.</summary>
public sealed record CanvasReflowMove(string DocumentId, double FromX, double FromY, double ToX, double ToY);

/// <summary>
/// Typed failure when a persisted position cannot be restored without
/// overlap: the caller must handle it instead of silently stacking cards.
/// Mirrors the CAN-05 <c>blocked</c> placement outcome.
/// </summary>
public sealed class CanvasPositionBlockedException : InvalidOperationException
{
    public CanvasPositionBlockedException(string documentId, IReadOnlyList<string> blockingIds)
        : base($"No free restore location for '{documentId}' near its persisted position (blocked by: {string.Join(", ", blockingIds)}).")
    {
        DocumentId = documentId;
        BlockingIds = blockingIds;
    }

    public string DocumentId { get; }

    public IReadOnlyList<string> BlockingIds { get; }
}

/// <summary>
/// CAN-06: Position Persistence &amp; Pinning — workspace canvas spatial memory.
///
/// Keyed by stable <c>DocumentId</c> (FS-02/FS-05 identity), never by
/// filesystem path, so rename and physical moves preserve positions.
/// Adopts the CAN-05 <c>auto</c>/<c>manual</c>/<c>pinned</c> vocabulary as the
/// persisted mode; <c>pinned</c> additionally sets <see cref="WorkspaceDocument.Pinned"/>
/// (spec 02 carries both fields — they are kept in sync here).
///
/// CRITICAL SAFETY CONTRACT (same as CAN-04 <see cref="CanvasDocumentService"/>):
/// pure in-memory workspace metadata. No filesystem, network, or DOM access.
/// Canvas drag never physically moves files.
/// </summary>
public sealed class CanvasPositionService
{
    /// <summary>System may reposition locally (spec 03 §9).</summary>
    public const string Auto = "auto";

    /// <summary>Set once the user drags the object; system strongly avoids moving it.</summary>
    public const string Manual = "manual";

    /// <summary>System MUST NOT move it automatically (spec 03 §9).</summary>
    public const string Pinned = "pinned";

    /// <summary>Maximum restore-fallback displacement in pixels (mirrors CAN-05 reflow radius discipline).</summary>
    public const double MaxRestoreDisplacement = 600;

    private static readonly IReadOnlySet<string> ValidModes =
        new HashSet<string>(StringComparer.Ordinal) { Auto, Manual, Pinned };

    private readonly Dictionary<(string WorkspaceId, string DocumentId), WorkspaceDocument> _store = new();

    /// <summary>Persist a position mode + coordinates for a document.</summary>
    public WorkspaceDocument UpsertPosition(string workspaceId, string documentId, double x, double y, string mode)
    {
        RequireIds(workspaceId, documentId);
        RequireValidMode(mode);

        var key = (workspaceId, documentId);
        if (!_store.TryGetValue(key, out var doc))
        {
            doc = new WorkspaceDocument { WorkspaceId = workspaceId, DocumentId = documentId };
            _store[key] = doc;
        }

        doc.CanvasX = x;
        doc.CanvasY = y;
        doc.PositionMode = mode;
        doc.Pinned = string.Equals(mode, Pinned, StringComparison.Ordinal);
        return Copy(doc);
    }

    /// <summary>
    /// Record a user drag: coordinates update and the mode switches to
    /// <c>manual</c> (spec 03 §9). Drag never moves a physical file.
    /// </summary>
    public WorkspaceDocument MoveTo(string workspaceId, string documentId, double x, double y)
    {
        return UpsertPosition(workspaceId, documentId, x, y, Manual);
    }

    /// <summary>Pin a document: mode becomes <c>pinned</c>, coordinates retained.</summary>
    public WorkspaceDocument Pin(string workspaceId, string documentId)
    {
        var doc = RequireExisting(workspaceId, documentId);
        doc.PositionMode = Pinned;
        doc.Pinned = true;
        return Copy(doc);
    }

    /// <summary>
    /// Unpin a document: mode returns to <c>manual</c> (spatial memory is
    /// retained and assisted placement still must not move it), coordinates retained.
    /// </summary>
    public WorkspaceDocument Unpin(string workspaceId, string documentId)
    {
        var doc = RequireExisting(workspaceId, documentId);
        doc.PositionMode = Manual;
        doc.Pinned = false;
        return Copy(doc);
    }

    /// <summary>Restore a single persisted position after restart/reopen.</summary>
    public WorkspaceDocument? Get(string workspaceId, string documentId)
    {
        RequireIds(workspaceId, documentId);
        return _store.TryGetValue((workspaceId, documentId), out var doc) ? Copy(doc) : null;
    }

    /// <summary>Restore all persisted positions for a workspace after restart/reopen.</summary>
    public IReadOnlyList<WorkspaceDocument> GetWorkspace(string workspaceId)
    {
        if (string.IsNullOrWhiteSpace(workspaceId))
        {
            throw new ArgumentException("Workspace ID cannot be null or empty.", nameof(workspaceId));
        }

        return _store.Values
            .Where(doc => string.Equals(doc.WorkspaceId, workspaceId, StringComparison.Ordinal))
            .Select(Copy)
            .ToList();
    }

    /// <summary>
    /// Export a detached snapshot of all positions (the durable side of
    /// restart persistence; the caller serializes it to SQLite/localStorage).
    /// </summary>
    public IReadOnlyList<WorkspaceDocument> ExportSnapshot()
    {
        return _store.Values.Select(Copy).ToList();
    }

    /// <summary>Hydrate positions from a snapshot after restart/reopen.</summary>
    public void LoadSnapshot(IEnumerable<WorkspaceDocument> snapshot)
    {
        if (snapshot is null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        _store.Clear();
        foreach (var doc in snapshot)
        {
            RequireIds(doc.WorkspaceId, doc.DocumentId);
            RequireValidMode(doc.PositionMode);
            _store[(doc.WorkspaceId, doc.DocumentId)] = Copy(doc);
        }
    }

    /// <summary>
    /// Resolve where a persisted position lands on reopen: exact coordinates
    /// when free, otherwise the nearest-safe fallback found by a short spiral
    /// probe (same 32px-step discipline as the CAN-05 nudge search).
    /// Pinned/manual occupants are never considered movable — the restored
    /// card moves around them. Throws <see cref="CanvasPositionBlockedException"/>
    /// when no free cell exists nearby instead of stacking cards silently.
    /// </summary>
    public RestoredPosition ResolveRestorePosition(
        string workspaceId,
        string documentId,
        double x,
        double y,
        double width,
        double height,
        IReadOnlyList<CanvasRect> occupied)
    {
        RequireIds(workspaceId, documentId);
        if (occupied is null)
        {
            throw new ArgumentNullException(nameof(occupied));
        }

        if (!OverlapsAny(x, y, width, height, occupied, excludeDocumentId: documentId))
        {
            return new RestoredPosition(documentId, x, y, FellBack: false);
        }

        const double step = 32;
        const int maxRings = 8;
        var offsets = new (double Dx, double Dy)[] { (1, 0), (0, 1), (-1, 0), (0, -1) };
        for (int ring = 1; ring <= maxRings; ring++)
        {
            foreach (var (dx, dy) in offsets)
            {
                double cx = x + (ring * step * dx);
                double cy = y + (ring * step * dy);
                if (Math.Sqrt(((cx - x) * (cx - x)) + ((cy - y) * (cy - y))) > MaxRestoreDisplacement)
                {
                    continue;
                }

                if (!OverlapsAny(cx, cy, width, height, occupied, excludeDocumentId: documentId))
                {
                    return new RestoredPosition(documentId, cx, cy, FellBack: true);
                }
            }
        }

        throw new CanvasPositionBlockedException(
            documentId,
            occupied.Select(rect => rect.DocumentId).ToList());
    }

    /// <summary>
    /// Apply a local reflow step: only <c>auto</c> objects move. Pinned and
    /// manual objects are never moved (spec 03 §§9-10) and never reported.
    /// </summary>
    public IReadOnlyList<CanvasReflowMove> ApplyLocalReflow(
        string workspaceId,
        IReadOnlyDictionary<string, (double Dx, double Dy)> nudges)
    {
        if (string.IsNullOrWhiteSpace(workspaceId))
        {
            throw new ArgumentException("Workspace ID cannot be null or empty.", nameof(workspaceId));
        }

        if (nudges is null)
        {
            throw new ArgumentNullException(nameof(nudges));
        }

        var moves = new List<CanvasReflowMove>();
        foreach (var (documentId, (dx, dy)) in nudges)
        {
            if (!_store.TryGetValue((workspaceId, documentId), out var doc))
            {
                continue;
            }

            if (!string.Equals(doc.PositionMode, Auto, StringComparison.Ordinal))
            {
                continue;
            }

            var move = new CanvasReflowMove(documentId, doc.CanvasX, doc.CanvasY, doc.CanvasX + dx, doc.CanvasY + dy);
            doc.CanvasX = move.ToX;
            doc.CanvasY = move.ToY;
            moves.Add(move);
        }

        return moves;
    }

    /// <summary>True when two axis-aligned rectangles overlap (edge-touching is free).</summary>
    public static bool RectsOverlap(
        double x1, double y1, double w1, double h1,
        double x2, double y2, double w2, double h2)
    {
        return x1 < x2 + w2 && x2 < x1 + w1 && y1 < y2 + h2 && y2 < y1 + h1;
    }

    private static bool OverlapsAny(
        double x, double y, double width, double height,
        IReadOnlyList<CanvasRect> occupied,
        string excludeDocumentId)
    {
        return occupied.Any(rect =>
            !string.Equals(rect.DocumentId, excludeDocumentId, StringComparison.Ordinal) &&
            RectsOverlap(x, y, width, height, rect.X, rect.Y, rect.Width, rect.Height));
    }

    private WorkspaceDocument RequireExisting(string workspaceId, string documentId)
    {
        RequireIds(workspaceId, documentId);
        if (!_store.TryGetValue((workspaceId, documentId), out var doc))
        {
            throw new InvalidOperationException(
                $"No canvas position for document '{documentId}' in workspace '{workspaceId}'.");
        }

        return doc;
    }

    private static void RequireIds(string workspaceId, string documentId)
    {
        if (string.IsNullOrWhiteSpace(workspaceId))
        {
            throw new ArgumentException("Workspace ID cannot be null or empty.", nameof(workspaceId));
        }

        if (string.IsNullOrWhiteSpace(documentId))
        {
            throw new ArgumentException("Document ID cannot be null or empty.", nameof(documentId));
        }
    }

    private static void RequireValidMode(string mode)
    {
        if (!ValidModes.Contains(mode))
        {
            throw new ArgumentException(
                $"Position mode must be one of: {Auto}, {Manual}, {Pinned}. Got '{mode}'.",
                nameof(mode));
        }
    }

    private static WorkspaceDocument Copy(WorkspaceDocument doc)
    {
        return new WorkspaceDocument
        {
            WorkspaceId = doc.WorkspaceId,
            DocumentId = doc.DocumentId,
            CanvasX = doc.CanvasX,
            CanvasY = doc.CanvasY,
            PositionMode = doc.PositionMode,
            Pinned = doc.Pinned,
            CreatedAt = doc.CreatedAt,
        };
    }
}
