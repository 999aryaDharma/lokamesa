using System;

namespace DocumentIDE.Core.Indexing;

public class IndexStateChangedEventArgs : EventArgs
{
    public string DocumentId { get; }
    public string State { get; }
    public string? ErrorMessage { get; }
    public DateTime Timestamp { get; }

    public IndexStateChangedEventArgs(string documentId, string state, string? errorMessage = null)
    {
        DocumentId = documentId;
        State = state;
        ErrorMessage = errorMessage;
        Timestamp = DateTime.UtcNow;
    }
}

public class CacheInvalidationEventArgs : EventArgs
{
    public string DocumentId { get; }
    public string Path { get; }
    public DateTime Timestamp { get; }

    public CacheInvalidationEventArgs(string documentId, string path)
    {
        DocumentId = documentId;
        Path = path;
        Timestamp = DateTime.UtcNow;
    }
}
