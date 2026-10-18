using System;

namespace DocumentIDE.Core.Semantic;

public class DocumentEmbedding
{
    public string DocumentId { get; set; } = string.Empty;
    public string ProviderId { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public string ContentFingerprint { get; set; } = string.Empty;
    public float[] Vector { get; set; } = Array.Empty<float>();
    public string CreatedAt { get; set; } = DateTime.UtcNow.ToString("O");
}
