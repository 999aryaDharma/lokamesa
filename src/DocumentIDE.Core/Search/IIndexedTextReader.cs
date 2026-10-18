using System.Threading;
using System.Threading.Tasks;

namespace DocumentIDE.Core.Search;

/// <summary>
/// Read-only access to the text a document parser already indexed.
///
/// UI-05 consumes this so a context pack can include a DOCX's extracted text
/// without re-parsing the package or pretending the index is the document's
/// own bytes. A missing row (or a NULL/empty body) is an honest "not indexed",
/// which callers must surface as a typed unavailable item — never as empty
/// content.
/// </summary>
public interface IIndexedTextReader
{
    /// <summary>
    /// The indexed searchable body for <paramref name="documentId"/>, or
    /// <c>null</c> when the index holds no text for it.
    /// </summary>
    Task<string?> GetIndexedBodyAsync(string documentId, CancellationToken cancellationToken = default);
}
