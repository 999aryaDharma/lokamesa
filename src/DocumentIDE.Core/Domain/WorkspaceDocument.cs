using System;

namespace DocumentIDE.Core.Domain;

public class WorkspaceDocument
{
    public string WorkspaceId { get; set; } = string.Empty;
    public string DocumentId { get; set; } = string.Empty;
    public double CanvasX { get; set; } = 0;
    public double CanvasY { get; set; } = 0;
    public string PositionMode { get; set; } = "auto";
    public bool Pinned { get; set; } = false;
    public string CreatedAt { get; set; } = DateTime.UtcNow.ToString("O");
}
