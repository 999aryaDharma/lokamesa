using System.Collections.Generic;

namespace DocumentIDE.Core.Search;

public record FtsIndexEntry(
    string DocumentId,
    string? Filename = null,
    string? Body = null,
    string? Headings = null,
    string? Tables = null,
    string? Headers = null,
    string? Footers = null,
    string? Frontmatter = null
);

public record FtsSearchResult(
    string DocumentId,
    string? Filename,
    double Score,
    string? Snippet,
    IReadOnlyList<string> MatchSources
);

public record FtsIndexDiagnostics(
    int TotalIndexedDocuments,
    bool IsHealthy,
    string IndexVersion,
    string? ErrorMessage = null
);
