using System;

namespace DocumentIDE.Core.Domain;

public class Workspace
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string ActiveView { get; set; } = "canvas";
    public string? CanvasViewportJson { get; set; }
    public string? ListStateJson { get; set; }
    public string? GraphViewportJson { get; set; }
    public string CreatedAt { get; set; } = DateTime.UtcNow.ToString("O");
    public string UpdatedAt { get; set; } = DateTime.UtcNow.ToString("O");
}
