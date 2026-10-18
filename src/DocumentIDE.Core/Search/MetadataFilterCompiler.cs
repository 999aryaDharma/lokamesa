using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DocumentIDE.Core.Domain;

namespace DocumentIDE.Core.Search;

public record FilterChipInfo(
    string Field,
    string Value,
    string QuerySnippet,
    int Count
);

public static class MetadataFilterCompiler
{
    public static Func<Document, DocumentMetadata?, MarkdownDocumentMetadata?, bool> Compile(SearchQuery query)
    {
        if (query == null || query.Filters == null || query.Filters.Count == 0)
        {
            return (doc, docxMeta, mdMeta) => true;
        }

        var predicates = new List<Func<Document, DocumentMetadata?, MarkdownDocumentMetadata?, bool>>();

        foreach (var filter in query.Filters)
        {
            var pred = CompileFilter(filter);
            if (pred != null)
            {
                predicates.Add(pred);
            }
        }

        if (predicates.Count == 0)
        {
            return (doc, docxMeta, mdMeta) => true;
        }

        return (doc, docxMeta, mdMeta) =>
        {
            foreach (var p in predicates)
            {
                if (!p(doc, docxMeta, mdMeta)) return false;
            }
            return true;
        };
    }

    private static Func<Document, DocumentMetadata?, MarkdownDocumentMetadata?, bool>? CompileFilter(QueryFilter filter)
    {
        if (filter == null || string.IsNullOrWhiteSpace(filter.Field)) return null;

        string field = filter.Field.ToLowerInvariant();
        string val = filter.Value;

        return field switch
        {
            "font" => (doc, docxMeta, mdMeta) =>
                docxMeta?.PrimaryFont != null &&
                docxMeta.PrimaryFont.Contains(val, StringComparison.OrdinalIgnoreCase),

            "paper" => (doc, docxMeta, mdMeta) =>
                docxMeta?.PaperSummary != null &&
                docxMeta.PaperSummary.Contains(val, StringComparison.OrdinalIgnoreCase),

            "margin" => (doc, docxMeta, mdMeta) =>
                docxMeta?.MarginSummary != null &&
                docxMeta.MarginSummary.Contains(val, StringComparison.OrdinalIgnoreCase),

            "type" => (doc, docxMeta, mdMeta) =>
                MatchesType(doc, val),

            "folder" => (doc, docxMeta, mdMeta) =>
                MatchesFolder(doc, val),

            "modified" => (doc, docxMeta, mdMeta) =>
                MatchesModifiedDate(doc, val),

            "font-size" or "fontsize" => (doc, docxMeta, mdMeta) =>
                MatchesFontSize(docxMeta, val),

            "name" or "filename" => (doc, docxMeta, mdMeta) =>
                doc != null && doc.Filename != null && doc.Filename.Contains(val, StringComparison.OrdinalIgnoreCase),

            "heading" => (doc, docxMeta, mdMeta) =>
                MatchesHeading(docxMeta, mdMeta, val),

            _ => null
        };
    }

    private static bool MatchesType(Document doc, string targetType)
    {
        if (doc == null) return false;

        string ext = (doc.Extension ?? "").TrimStart('.').ToLowerInvariant();
        string type = (doc.DocumentType ?? "").ToLowerInvariant();
        string normTarget = targetType.TrimStart('.').ToLowerInvariant();

        if (normTarget == "markdown") normTarget = "md";
        if (normTarget == "doc") normTarget = "docx";

        return string.Equals(ext, normTarget, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(type, normTarget, StringComparison.OrdinalIgnoreCase) ||
               (normTarget == "md" && (ext == "md" || type == "markdown")) ||
               (normTarget == "docx" && (ext == "docx" || type == "docx"));
    }

    private static bool MatchesFolder(Document doc, string folderPath)
    {
        if (doc == null || string.IsNullOrWhiteSpace(doc.CurrentPath)) return false;

        string normPath = doc.CurrentPath.Replace('\\', '/').TrimEnd('/');
        string normFolder = folderPath.Replace('\\', '/').TrimEnd('/');

        return normPath.StartsWith(normFolder + "/", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normPath, normFolder, StringComparison.OrdinalIgnoreCase) ||
               normPath.Contains("/" + normFolder + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesModifiedDate(Document doc, string dateQuery)
    {
        if (doc == null) return false;

        string? dateStr = doc.ModifiedAt ?? doc.UpdatedAt;
        if (string.IsNullOrWhiteSpace(dateStr)) return false;

        if (!DateTime.TryParse(dateStr, out DateTime modifiedDate)) return false;

        DateTime now = DateTime.UtcNow;
        string normDate = dateQuery.ToLowerInvariant().Trim();

        return normDate switch
        {
            "today" => modifiedDate.Date == now.Date,
            "yesterday" => modifiedDate.Date == now.Date.AddDays(-1),
            "this-week" => modifiedDate >= now.Date.AddDays(-(int)now.DayOfWeek),
            "this-month" => modifiedDate.Year == now.Year && modifiedDate.Month == now.Month,
            "this-year" => modifiedDate.Year == now.Year,
            _ => ParseAndCompareDate(modifiedDate, dateQuery)
        };
    }

    private static bool ParseAndCompareDate(DateTime modifiedDate, string dateQuery)
    {
        if (dateQuery.StartsWith(">="))
        {
            if (DateTime.TryParse(dateQuery[2..], out DateTime dt)) return modifiedDate >= dt;
        }
        else if (dateQuery.StartsWith(">"))
        {
            if (DateTime.TryParse(dateQuery[1..], out DateTime dt)) return modifiedDate > dt;
        }
        else if (dateQuery.StartsWith("<="))
        {
            if (DateTime.TryParse(dateQuery[2..], out DateTime dt)) return modifiedDate <= dt;
        }
        else if (dateQuery.StartsWith("<"))
        {
            if (DateTime.TryParse(dateQuery[1..], out DateTime dt)) return modifiedDate < dt;
        }
        else if (DateTime.TryParse(dateQuery, out DateTime targetDate))
        {
            return modifiedDate.Date == targetDate.Date;
        }

        return false;
    }

    private static bool MatchesFontSize(DocumentMetadata? docxMeta, string fontSizeQuery)
    {
        if (docxMeta?.PrimaryFontSize == null) return false;
        double size = docxMeta.PrimaryFontSize.Value;

        if (fontSizeQuery.StartsWith(">="))
        {
            if (double.TryParse(fontSizeQuery[2..], out double target)) return size >= target;
        }
        else if (fontSizeQuery.StartsWith(">"))
        {
            if (double.TryParse(fontSizeQuery[1..], out double target)) return size > target;
        }
        else if (fontSizeQuery.StartsWith("<="))
        {
            if (double.TryParse(fontSizeQuery[2..], out double target)) return size <= target;
        }
        else if (fontSizeQuery.StartsWith("<"))
        {
            if (double.TryParse(fontSizeQuery[1..], out double target)) return size < target;
        }
        else if (double.TryParse(fontSizeQuery, out double exact))
        {
            return Math.Abs(size - exact) < 0.01;
        }

        return false;
    }

    private static bool MatchesHeading(DocumentMetadata? docxMeta, MarkdownDocumentMetadata? mdMeta, string headingQuery)
    {
        string? headingsJson = docxMeta?.HeadingsJson ?? mdMeta?.HeadingsJson;
        if (string.IsNullOrWhiteSpace(headingsJson)) return false;
        return headingsJson.Contains(headingQuery, StringComparison.OrdinalIgnoreCase);
    }

    public static string ApplyInspectorFilter(string currentQuery, string field, string value)
    {
        if (string.IsNullOrWhiteSpace(field) || string.IsNullOrWhiteSpace(value))
        {
            return currentQuery ?? "";
        }

        string normField = field.ToLowerInvariant().Trim();
        string normValue = value.Trim();

        string filterToken = normValue.Contains(' ')
            ? $"{normField}:\"{normValue}\""
            : $"{normField}:{normValue}";

        if (string.IsNullOrWhiteSpace(currentQuery))
        {
            return filterToken;
        }

        var parsed = SearchQueryParser.Parse(currentQuery);
        var existing = parsed.Filters.FirstOrDefault(f => string.Equals(f.Field, normField, StringComparison.OrdinalIgnoreCase));

        if (existing != null)
        {
            // If identical value, toggle (remove) it
            if (string.Equals(existing.Value, normValue, StringComparison.OrdinalIgnoreCase))
            {
                return RemoveFilter(currentQuery, normField, normValue);
            }

            // Replace existing filter with new filter token
            string pattern = $@"\b{Regex.Escape(existing.Field)}:(?:""[^""]+""|\S+)";
            string replaced = Regex.Replace(currentQuery, pattern, filterToken, RegexOptions.IgnoreCase);
            return Regex.Replace(replaced, @"\s+", " ").Trim();
        }

        return $"{currentQuery.Trim()} {filterToken}";
    }

    public static string RemoveFilter(string currentQuery, string field, string value)
    {
        if (string.IsNullOrWhiteSpace(currentQuery)) return "";

        string normField = field.ToLowerInvariant().Trim();
        string normValue = value.Trim();

        string pattern = $@"\b{Regex.Escape(normField)}:(?:""{Regex.Escape(normValue)}""|{Regex.Escape(normValue)})\b";

        string result = Regex.Replace(currentQuery, pattern, "", RegexOptions.IgnoreCase);
        return Regex.Replace(result, @"\s+", " ").Trim();
    }

    public static IReadOnlyList<FilterChipInfo> ExtractActiveFilterChips(
        SearchQuery query,
        IEnumerable<(Document Doc, DocumentMetadata? Docx, MarkdownDocumentMetadata? Md)> docs)
    {
        if (query == null || query.Filters == null || query.Filters.Count == 0)
        {
            return Array.Empty<FilterChipInfo>();
        }

        var docList = docs.ToList();
        var chips = new List<FilterChipInfo>();

        foreach (var filter in query.Filters)
        {
            var singleFilterQuery = new SearchQuery(
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                new[] { filter }
            );

            var predicate = Compile(singleFilterQuery);
            int count = docList.Count(d => predicate(d.Doc, d.Docx, d.Md));

            string snippet = filter.Value.Contains(' ')
                ? $"{filter.Field.ToLowerInvariant()}:\"{filter.Value}\""
                : $"{filter.Field.ToLowerInvariant()}:{filter.Value}";

            chips.Add(new FilterChipInfo(
                Field: filter.Field,
                Value: filter.Value,
                QuerySnippet: snippet,
                Count: count
            ));
        }

        return chips;
    }
}
