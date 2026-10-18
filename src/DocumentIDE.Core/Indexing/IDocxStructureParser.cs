using System.Collections.Generic;
using DocumentIDE.Core.Domain;

namespace DocumentIDE.Core.Indexing;

public record DocxParseResult(
    DocumentMetadata Metadata,
    string? SearchableBody = null,
    string? HeadingsText = null,
    string? TablesText = null,
    string? HeadersText = null,
    string? FootersText = null
);

public interface IDocxStructureParser
{
    DocxParseResult Parse(string filePath);
}
