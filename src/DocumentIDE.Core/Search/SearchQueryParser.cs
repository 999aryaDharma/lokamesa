using System;
using System.Collections.Generic;
using System.Text;

namespace DocumentIDE.Core.Search;

public static class SearchQueryParser
{
    private static readonly HashSet<string> KnownFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "folder", "type", "font", "paper", "margin", "modified", "name", "heading", "font-size", "mode", "scope"
    };

    private static readonly Dictionary<string, string> FieldAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        { "path", "folder" },
        { "dir", "folder" },
        { "ext", "type" },
        { "extension", "type" },
        { "filetype", "type" },
        { "date", "modified" },
        { "updated", "modified" },
        { "filename", "name" },
        { "title", "name" },
        { "header", "heading" },
        { "h1", "heading" },
        { "h2", "heading" },
        { "h3", "heading" },
        { "fontsize", "font-size" },
        { "size", "font-size" }
    };

    public static SearchQuery Parse(string? rawQuery)
    {
        if (string.IsNullOrWhiteSpace(rawQuery))
        {
            return new SearchQuery(
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<QueryFilter>(),
                SearchMode.Exact,
                SearchScope.Workspace,
                Array.Empty<QueryDiagnostic>()
            );
        }

        var terms = new List<string>();
        var exactPhrases = new List<string>();
        var excludedTerms = new List<string>();
        var filters = new List<QueryFilter>();
        var diagnostics = new List<QueryDiagnostic>();

        var mode = SearchMode.Exact;
        var scope = SearchScope.Workspace;

        int i = 0;
        int len = rawQuery.Length;

        while (i < len)
        {
            // Skip whitespace
            while (i < len && char.IsWhiteSpace(rawQuery[i]))
            {
                i++;
            }

            if (i >= len) break;

            int tokenStart = i;

            // Check for negative prefix '-'
            bool isNegative = false;
            if (rawQuery[i] == '-' && i + 1 < len && !char.IsWhiteSpace(rawQuery[i + 1]))
            {
                isNegative = true;
                i++; // consume '-'
            }

            // Check if quoted phrase
            if (i < len && rawQuery[i] == '"')
            {
                i++; // consume open quote
                var sb = new StringBuilder();
                bool closed = false;

                while (i < len)
                {
                    if (rawQuery[i] == '"')
                    {
                        closed = true;
                        i++; // consume close quote
                        break;
                    }
                    if (rawQuery[i] == '\\' && i + 1 < len && rawQuery[i + 1] == '"')
                    {
                        sb.Append('"');
                        i += 2;
                    }
                    else
                    {
                        sb.Append(rawQuery[i]);
                        i++;
                    }
                }

                string phraseContent = sb.ToString();
                if (!closed)
                {
                    diagnostics.Add(new QueryDiagnostic(
                        DiagnosticSeverity.Warning,
                        "Unclosed quoted phrase in query.",
                        rawQuery.Substring(tokenStart)
                    ));
                }

                if (isNegative)
                {
                    excludedTerms.Add(phraseContent);
                }
                else
                {
                    exactPhrases.Add(phraseContent);
                }

                continue;
            }

            // Read word or filter key:value
            int wordStart = i;
            while (i < len && !char.IsWhiteSpace(rawQuery[i]))
            {
                i++;
            }

            string tokenWord = rawQuery.Substring(wordStart, i - wordStart);
            string fullToken = rawQuery.Substring(tokenStart, i - tokenStart);

            // Check if filter format `key:value`
            int colonIndex = tokenWord.IndexOf(':');
            if (colonIndex > 0 && !isNegative && IsValidFilterKey(tokenWord.Substring(0, colonIndex)))
            {
                string rawKey = tokenWord.Substring(0, colonIndex);
                string rawVal = tokenWord.Substring(colonIndex + 1);

                // Handle if val starts with quote key:"val with spaces"
                if (rawVal.StartsWith("\"") && !rawVal.EndsWith("\""))
                {
                    var valSb = new StringBuilder(rawVal.Substring(1));
                    bool valClosed = false;
                    while (i < len)
                    {
                        if (rawQuery[i] == '"')
                        {
                            valClosed = true;
                            i++;
                            break;
                        }
                        valSb.Append(rawQuery[i]);
                        i++;
                    }
                    rawVal = valSb.ToString();
                    if (!valClosed)
                    {
                        diagnostics.Add(new QueryDiagnostic(
                            DiagnosticSeverity.Warning,
                            $"Unclosed quote in filter '{rawKey}'.",
                            rawQuery.Substring(tokenStart, Math.Min(i, len) - tokenStart)
                        ));
                    }
                }
                else if (rawVal.StartsWith("\"") && rawVal.EndsWith("\"") && rawVal.Length >= 2)
                {
                    rawVal = rawVal.Substring(1, rawVal.Length - 2);
                }

                if (string.IsNullOrEmpty(rawVal))
                {
                    diagnostics.Add(new QueryDiagnostic(
                        DiagnosticSeverity.Error,
                        $"Filter '{rawKey}' has empty value.",
                        fullToken
                    ));
                }
                else
                {
                    ProcessFilter(rawKey, rawVal, filters, diagnostics, ref mode, ref scope, fullToken);
                }

                continue;
            }

            if (isNegative)
            {
                excludedTerms.Add(tokenWord);
            }
            else
            {
                terms.Add(tokenWord);
            }
        }

        return new SearchQuery(
            terms,
            exactPhrases,
            excludedTerms,
            filters,
            mode,
            scope,
            diagnostics
        );
    }

    private static bool IsValidFilterKey(string key)
    {
        if (string.IsNullOrEmpty(key)) return false;
        foreach (char c in key)
        {
            if (!char.IsLetterOrDigit(c) && c != '_' && c != '-') return false;
        }
        return true;
    }

    private static void ProcessFilter(
        string key,
        string value,
        List<QueryFilter> filters,
        List<QueryDiagnostic> diagnostics,
        ref SearchMode mode,
        ref SearchScope scope,
        string fullToken)
    {
        string normKey = key.ToLowerInvariant();
        if (FieldAliases.TryGetValue(normKey, out var canonicalKey))
        {
            normKey = canonicalKey;
        }

        if (!KnownFields.Contains(normKey))
        {
            diagnostics.Add(new QueryDiagnostic(
                DiagnosticSeverity.Warning,
                $"Unknown filter field '{key}'.",
                fullToken
            ));
            filters.Add(new QueryFilter(key, value));
            return;
        }

        string normValue = value;
        if (normKey == "type")
        {
            normValue = normValue.ToLowerInvariant();
            if (normValue == "markdown") normValue = "md";
            else if (normValue == "doc") normValue = "docx";
        }
        else if (normKey == "margin" || normKey == "paper" || normKey == "modified")
        {
            normValue = normValue.ToLowerInvariant();
        }
        else if (normKey == "mode")
        {
            string modeVal = value.ToLowerInvariant();
            if (modeVal == "exact") mode = SearchMode.Exact;
            else if (modeVal == "fuzzy" || modeVal == "exploratory") mode = SearchMode.Fuzzy;
            else if (modeVal == "metadata") mode = SearchMode.Metadata;
            else if (modeVal == "smart" || modeVal == "semantic") mode = SearchMode.Smart;
            else
            {
                diagnostics.Add(new QueryDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"Unknown search mode '{value}'.",
                    fullToken
                ));
            }
        }
        else if (normKey == "scope")
        {
            string scopeVal = value.ToLowerInvariant();
            if (scopeVal == "workspace" || scopeVal == "global") scope = SearchScope.Workspace;
            else if (scopeVal == "folder" || scopeVal == "dir") scope = SearchScope.Folder;
            else if (scopeVal == "selection") scope = SearchScope.Selection;
            else if (scopeVal == "collection") scope = SearchScope.Collection;
            else
            {
                diagnostics.Add(new QueryDiagnostic(
                    DiagnosticSeverity.Warning,
                    $"Unknown search scope '{value}'.",
                    fullToken
                ));
            }
        }

        filters.Add(new QueryFilter(normKey, normValue));
    }

    public static string ToFts5Query(SearchQuery query)
    {
        if (query == null) return string.Empty;

        var parts = new List<string>();

        foreach (var phrase in query.ExactPhrases)
        {
            if (!string.IsNullOrWhiteSpace(phrase))
            {
                parts.Add($"\"{EscapeFts5Token(phrase)}\"");
            }
        }

        foreach (var term in query.Terms)
        {
            if (!string.IsNullOrWhiteSpace(term))
            {
                parts.Add($"\"{EscapeFts5Token(term)}\"");
            }
        }

        foreach (var excluded in query.ExcludedTerms)
        {
            if (!string.IsNullOrWhiteSpace(excluded))
            {
                parts.Add($"NOT \"{EscapeFts5Token(excluded)}\"");
            }
        }

        return string.Join(" ", parts);
    }

    public static string EscapeFts5Token(string token)
    {
        if (string.IsNullOrEmpty(token)) return string.Empty;
        return token.Replace("\"", "\"\"");
    }
}
