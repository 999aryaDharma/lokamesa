using System;
using System.Collections.Generic;

namespace DocumentIDE.Core.Search;

public enum SearchMode
{
    Exact,
    Fuzzy,
    Metadata,
    Smart
}

public enum SearchScope
{
    Workspace,
    Folder,
    Selection,
    Collection
}

public enum DiagnosticSeverity
{
    Info,
    Warning,
    Error
}

public record QueryFilter(
    string Field,
    string Value
);

public record QueryDiagnostic(
    DiagnosticSeverity Severity,
    string Message,
    string? Token = null
);

public record SearchQuery(
    IReadOnlyList<string> Terms,
    IReadOnlyList<string> ExactPhrases,
    IReadOnlyList<string> ExcludedTerms,
    IReadOnlyList<QueryFilter> Filters,
    SearchMode Mode = SearchMode.Exact,
    SearchScope Scope = SearchScope.Workspace,
    IReadOnlyList<QueryDiagnostic>? Diagnostics = null
)
{
    public IReadOnlyList<QueryDiagnostic> Diagnostics { get; init; } = Diagnostics ?? Array.Empty<QueryDiagnostic>();
}
