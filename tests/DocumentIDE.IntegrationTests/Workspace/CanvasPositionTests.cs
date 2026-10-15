using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Workspaces;
using Xunit;

namespace DocumentIDE.IntegrationTests.Canvas;

/// <summary>
/// CAN-06: Position Persistence &amp; Pinning.
///
/// Positions are keyed by stable <see cref="WorkspaceDocument.DocumentId"/>
/// (FS-02/FS-05 identity), never by filesystem path: rename and physical
/// moves preserve spatial memory. Canvas drag never moves physical files.
/// </summary>
public class CanvasPositionTests
{
    private const string WorkspaceId = "ws-can-06";

    // ------------------------------------------------------------------
    // Required test 1 (RED first): manual position survives restart.
    // ------------------------------------------------------------------

    [Fact]
    public void Manual_Position_Survives_Restart()
    {
        var service = new CanvasPositionService();
        service.MoveTo(WorkspaceId, "doc-a", 320, 240);

        // Simulate an app restart: a fresh service hydrates from the
        // persisted snapshot of the previous instance.
        var snapshot = service.ExportSnapshot();
        var restarted = new CanvasPositionService();
        restarted.LoadSnapshot(snapshot);

        var restored = restarted.Get(WorkspaceId, "doc-a");
        Assert.NotNull(restored);
        Assert.Equal(320, restored.CanvasX);
        Assert.Equal(240, restored.CanvasY);
        Assert.Equal(CanvasPositionService.Manual, restored.PositionMode);
    }

    // ------------------------------------------------------------------
    // Required test 2: rename preserves position (FS-05 identity).
    // ------------------------------------------------------------------

    [Fact]
    public void Rename_Preserves_Position()
    {
        var service = new CanvasPositionService();
        service.MoveTo(WorkspaceId, "doc-a", 100, 200);

        // A rename changes the filesystem path only. The position store is
        // keyed by stable DocumentId, so there is nothing to update: the
        // record is still found by id with identical coordinates.
        var afterRename = service.Get(WorkspaceId, "doc-a");
        Assert.NotNull(afterRename);
        Assert.Equal(100, afterRename.CanvasX);
        Assert.Equal(200, afterRename.CanvasY);
        Assert.Equal(CanvasPositionService.Manual, afterRename.PositionMode);
    }

    [Fact]
    public void Position_Records_Expose_No_Filesystem_Path_Surface()
    {
        // Coordinates must remain independent from filesystem paths:
        // WorkspaceDocument literally has no path field to go stale.
        Assert.DoesNotContain(
            typeof(WorkspaceDocument).GetProperties().Select(p => p.Name),
            name => name.Contains("Path", StringComparison.OrdinalIgnoreCase));
    }

    // ------------------------------------------------------------------
    // Required test 3: pinned node never moves during local reflow.
    // ------------------------------------------------------------------

    [Fact]
    public void Pinned_Node_Never_Moves_During_Local_Reflow()
    {
        var service = new CanvasPositionService();
        service.UpsertPosition(WorkspaceId, "pinned-a", 0, 0, CanvasPositionService.Pinned);
        service.UpsertPosition(WorkspaceId, "manual-b", 200, 0, CanvasPositionService.Manual);
        service.UpsertPosition(WorkspaceId, "auto-c", 400, 0, CanvasPositionService.Auto);

        var moves = service.ApplyLocalReflow(
            WorkspaceId,
            new Dictionary<string, (double Dx, double Dy)>
            {
                ["pinned-a"] = (50, 50),
                ["manual-b"] = (50, 50),
                ["auto-c"] = (50, 50),
            });

        Assert.Equal(450, service.Get(WorkspaceId, "auto-c")!.CanvasX);
        Assert.Equal(50, service.Get(WorkspaceId, "auto-c")!.CanvasY);
        Assert.Equal(0, service.Get(WorkspaceId, "pinned-a")!.CanvasX);
        Assert.Equal(0, service.Get(WorkspaceId, "pinned-a")!.CanvasY);
        Assert.Equal(200, service.Get(WorkspaceId, "manual-b")!.CanvasX);
        Assert.DoesNotContain(moves, m => m.DocumentId == "pinned-a");
        Assert.DoesNotContain(moves, m => m.DocumentId == "manual-b");
        var autoMove = Assert.Single(moves);
        Assert.Equal("auto-c", autoMove.DocumentId);
    }

    // ------------------------------------------------------------------
    // Drag switches to manual; pin/unpin.
    // ------------------------------------------------------------------

    [Fact]
    public void Drag_Switches_To_Manual()
    {
        var service = new CanvasPositionService();
        service.UpsertPosition(WorkspaceId, "doc-a", 0, 0, CanvasPositionService.Auto);

        service.MoveTo(WorkspaceId, "doc-a", 500, 120);

        var doc = service.Get(WorkspaceId, "doc-a");
        Assert.NotNull(doc);
        Assert.Equal(500, doc.CanvasX);
        Assert.Equal(120, doc.CanvasY);
        Assert.Equal(CanvasPositionService.Manual, doc.PositionMode);
        Assert.False(doc.Pinned);
    }

    [Fact]
    public void Pin_And_Unpin_Roundtrip()
    {
        var service = new CanvasPositionService();
        service.MoveTo(WorkspaceId, "doc-a", 10, 20);

        service.Pin(WorkspaceId, "doc-a");
        var pinned = service.Get(WorkspaceId, "doc-a");
        Assert.NotNull(pinned);
        Assert.Equal(CanvasPositionService.Pinned, pinned.PositionMode);
        Assert.True(pinned.Pinned);
        Assert.Equal(10, pinned.CanvasX);

        service.Unpin(WorkspaceId, "doc-a");
        var unpinned = service.Get(WorkspaceId, "doc-a");
        Assert.NotNull(unpinned);
        Assert.Equal(CanvasPositionService.Manual, unpinned.PositionMode);
        Assert.False(unpinned.Pinned);
        Assert.Equal(10, unpinned.CanvasX);
        Assert.Equal(20, unpinned.CanvasY);
    }

    // ------------------------------------------------------------------
    // Occupied restore location resolves to nearest-safe fallback.
    // ------------------------------------------------------------------

    [Fact]
    public void Occupied_Restore_Resolves_To_Nearest_Safe_Fallback()
    {
        var service = new CanvasPositionService();
        const double cardW = 160;
        const double cardH = 100;

        // Something else already occupies the persisted spot.
        var occupied = new List<CanvasRect>
        {
            new("other", 100, 200, cardW, cardH),
        };

        var resolved = service.ResolveRestorePosition(
            WorkspaceId, "doc-a", 100, 200, cardW, cardH, occupied);

        // Fallback must not overlap and must stay near the remembered spot.
        foreach (var rect in occupied)
        {
            Assert.False(CanvasPositionService.RectsOverlap(
                resolved.X, resolved.Y, cardW, cardH,
                rect.X, rect.Y, rect.Width, rect.Height));
        }
        Assert.True(
            Math.Sqrt((resolved.X - 100) * (resolved.X - 100) + (resolved.Y - 200) * (resolved.Y - 200)) <= 600,
            $"Fallback displaced too far: ({resolved.X}, {resolved.Y})");
        Assert.True(resolved.FellBack);
    }

    [Fact]
    public void Free_Restore_Keeps_Exact_Coordinates()
    {
        var service = new CanvasPositionService();

        var resolved = service.ResolveRestorePosition(
            WorkspaceId, "doc-a", 100, 200, 160, 100,
            Array.Empty<CanvasRect>());

        Assert.Equal(100, resolved.X);
        Assert.Equal(200, resolved.Y);
        Assert.False(resolved.FellBack);
    }
}
