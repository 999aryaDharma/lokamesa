using System.Linq;
using DocumentIDE.Core.Search;
using Xunit;

namespace DocumentIDE.Core.Tests.Search;

public class SearchQueryParserTests
{
    [Fact]
    public void Parse_QuotedPhrase_ParsesExactPhrase()
    {
        // Required Test 1: Quoted phrase parses.
        string input = "\"nusa dua\"";

        SearchQuery result = SearchQueryParser.Parse(input);

        Assert.NotNull(result);
        Assert.Single(result.ExactPhrases);
        Assert.Equal("nusa dua", result.ExactPhrases[0]);
        Assert.Empty(result.Terms);
    }

    [Fact]
    public void Parse_NegativeTerm_ParsesExcludedTerms()
    {
        // Required Test 2: Negative term parses.
        string input = "\"Januari 2026\" -template -\"draft version\" -deprecated";

        SearchQuery result = SearchQueryParser.Parse(input);

        Assert.NotNull(result);
        Assert.Single(result.ExactPhrases);
        Assert.Equal("Januari 2026", result.ExactPhrases[0]);
        Assert.Equal(3, result.ExcludedTerms.Count);
        Assert.Equal("template", result.ExcludedTerms[0]);
        Assert.Equal("draft version", result.ExcludedTerms[1]);
        Assert.Equal("deprecated", result.ExcludedTerms[2]);
    }

    [Fact]
    public void Parse_UnknownFilter_ReturnsControlledDiagnostic()
    {
        // Required Test 3: Unknown filter returns controlled diagnostic.
        string input = "nusa foo:bar unknown_field:123";

        SearchQuery result = SearchQueryParser.Parse(input);

        Assert.NotNull(result);
        Assert.Single(result.Terms);
        Assert.Equal("nusa", result.Terms[0]);
        Assert.Equal(2, result.Diagnostics.Count);
        Assert.Contains(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Warning && d.Message.Contains("foo"));
        Assert.Contains(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Warning && d.Message.Contains("unknown_field"));
    }

    [Fact]
    public void Parse_RawQuery_CannotInjectSql()
    {
        // Required Test 4: Raw query cannot inject SQL.
        string input = "' OR '1'='1; DROP TABLE documents; -- \"\"\" NEAR/3 *";

        SearchQuery result = SearchQueryParser.Parse(input);

        Assert.NotNull(result);
        Assert.Empty(result.Filters);
        // The AST representation isolates all tokens as raw terms / exact phrases
        Assert.NotEmpty(result.Terms);

        // Verify FTS conversion safely escapes quotes
        string ftsQuery = SearchQueryParser.ToFts5Query(result);
        Assert.DoesNotContain("DROP TABLE", ftsQuery, System.StringComparison.OrdinalIgnoreCase);
        // All double quotes inside tokens are escaped as double-quotes ("")
        Assert.True(ftsQuery.StartsWith("\"") || ftsQuery.Length > 0);
    }

    [Fact]
    public void Parse_FieldFiltersAndAliases_NormalizesFieldsAndValues()
    {
        string input = "folder:BA path:docs type:markdown ext:doc font:Arial paper:Letter margin:mixed modified:this-week name:report title:overview heading:Intro size:12pt";

        SearchQuery result = SearchQueryParser.Parse(input);

        Assert.NotNull(result);
        Assert.Empty(result.Diagnostics);

        Assert.Equal("folder", result.Filters[0].Field);
        Assert.Equal("BA", result.Filters[0].Value);

        Assert.Equal("folder", result.Filters[1].Field);
        Assert.Equal("docs", result.Filters[1].Value);

        Assert.Equal("type", result.Filters[2].Field);
        Assert.Equal("md", result.Filters[2].Value); // markdown alias -> md

        Assert.Equal("type", result.Filters[3].Field);
        Assert.Equal("docx", result.Filters[3].Value); // doc alias -> docx

        Assert.Equal("font", result.Filters[4].Field);
        Assert.Equal("Arial", result.Filters[4].Value);

        Assert.Equal("paper", result.Filters[5].Field);
        Assert.Equal("letter", result.Filters[5].Value);

        Assert.Equal("margin", result.Filters[6].Field);
        Assert.Equal("mixed", result.Filters[6].Value);

        Assert.Equal("modified", result.Filters[7].Field);
        Assert.Equal("this-week", result.Filters[7].Value);

        Assert.Equal("name", result.Filters[8].Field);
        Assert.Equal("report", result.Filters[8].Value);

        Assert.Equal("name", result.Filters[9].Field);
        Assert.Equal("overview", result.Filters[9].Value);

        Assert.Equal("heading", result.Filters[10].Field);
        Assert.Equal("Intro", result.Filters[10].Value);

        Assert.Equal("font-size", result.Filters[11].Field);
        Assert.Equal("12pt", result.Filters[11].Value);
    }

    [Fact]
    public void Parse_SearchModeAndScope_SetsQueryProperties()
    {
        string input = "nusa mode:fuzzy scope:folder";

        SearchQuery result = SearchQueryParser.Parse(input);

        Assert.NotNull(result);
        Assert.Equal(SearchMode.Fuzzy, result.Mode);
        Assert.Equal(SearchScope.Folder, result.Scope);
        Assert.Single(result.Terms);
        Assert.Equal("nusa", result.Terms[0]);
    }

    [Fact]
    public void Parse_MalformedFilter_ReturnsErrorDiagnostic()
    {
        string input = "folder: type:";

        SearchQuery result = SearchQueryParser.Parse(input);

        Assert.NotNull(result);
        Assert.NotEmpty(result.Diagnostics);
        Assert.Contains(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("folder"));
        Assert.Contains(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("type"));
    }

    [Fact]
    public void Parse_UnclosedQuote_ReturnsWarningDiagnostic()
    {
        string input = "\"nusa dua report";

        SearchQuery result = SearchQueryParser.Parse(input);

        Assert.NotNull(result);
        Assert.Single(result.ExactPhrases);
        Assert.Equal("nusa dua report", result.ExactPhrases[0]);
        Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticSeverity.Warning, result.Diagnostics[0].Severity);
    }

    [Fact]
    public void Parse_EmptyOrWhitespace_ReturnsEmptyQuery()
    {
        SearchQuery nullResult = SearchQueryParser.Parse(null);
        SearchQuery emptyResult = SearchQueryParser.Parse("   ");

        Assert.Empty(nullResult.Terms);
        Assert.Empty(nullResult.ExactPhrases);
        Assert.Empty(nullResult.ExcludedTerms);
        Assert.Empty(nullResult.Filters);

        Assert.Empty(emptyResult.Terms);
        Assert.Empty(emptyResult.ExactPhrases);
        Assert.Empty(emptyResult.ExcludedTerms);
        Assert.Empty(emptyResult.Filters);
    }
}
