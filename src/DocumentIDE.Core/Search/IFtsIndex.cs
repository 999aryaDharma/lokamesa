using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DocumentIDE.Core.Search;

public interface IFtsIndex
{
    Task IndexDocumentAsync(FtsIndexEntry entry, CancellationToken cancellationToken = default);
    Task IndexBatchAsync(IEnumerable<FtsIndexEntry> entries, CancellationToken cancellationToken = default);
    Task DeleteDocumentAsync(string documentId, CancellationToken cancellationToken = default);
    Task ClearIndexAsync(CancellationToken cancellationToken = default);
    Task RebuildIndexAsync(IEnumerable<FtsIndexEntry> entries, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FtsSearchResult>> SearchAsync(string query, int limit = 50, CancellationToken cancellationToken = default);
    Task<FtsIndexDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default);
}
