using System;
using System.Collections.Generic;
using System.Linq;

namespace DocumentIDE.Core.Search;

public record FuzzyMatchResult(
    bool IsMatch,
    int EditDistance,
    double SimilarityScore,
    string? MatchedWord = null,
    string? MatchField = null
);

public record FuzzySearchCandidate(
    string DocumentId,
    string Filename,
    string? Body = null,
    string? Headings = null,
    string? CurrentPath = null
);

public record FuzzySearchOptions(
    int MaxDistance = 2,
    int MaxCandidates = 1000,
    double MinSimilarity = 0.5,
    bool Enabled = true
);

public interface IFuzzySearchService
{
    FuzzyMatchResult CalculateDistance(string queryTerm, string targetTerm, int maxDistance = 2);
    
    IReadOnlyList<SearchResult> PerformFuzzySearch(
        SearchQuery query,
        IEnumerable<FuzzySearchCandidate> candidates,
        FuzzySearchOptions? options = null);
}

public class FuzzySearchService : IFuzzySearchService
{
    public FuzzyMatchResult CalculateDistance(string queryTerm, string targetTerm, int maxDistance = 2)
    {
        if (string.IsNullOrWhiteSpace(queryTerm) || string.IsNullOrWhiteSpace(targetTerm))
        {
            return new FuzzyMatchResult(
                IsMatch: false,
                EditDistance: Math.Max(queryTerm?.Length ?? 0, targetTerm?.Length ?? 0),
                SimilarityScore: 0.0,
                MatchedWord: targetTerm
            );
        }

        string q = queryTerm.Trim().ToLowerInvariant();
        string t = targetTerm.Trim().ToLowerInvariant();

        if (string.Equals(q, t, StringComparison.Ordinal))
        {
            return new FuzzyMatchResult(
                IsMatch: true,
                EditDistance: 0,
                SimilarityScore: 1.0,
                MatchedWord: targetTerm
            );
        }

        int maxAllowedDistance = GetMaxAllowedDistance(q.Length, maxDistance);
        int editDistance = ComputeLevenshteinDistance(q, t);
        int maxLen = Math.Max(q.Length, t.Length);
        double similarity = maxLen == 0 ? 1.0 : 1.0 - ((double)editDistance / maxLen);
        bool isMatch = editDistance <= maxAllowedDistance && similarity >= 0.4;

        return new FuzzyMatchResult(
            IsMatch: isMatch,
            EditDistance: editDistance,
            SimilarityScore: similarity,
            MatchedWord: targetTerm
        );
    }

    public IReadOnlyList<SearchResult> PerformFuzzySearch(
        SearchQuery query,
        IEnumerable<FuzzySearchCandidate> candidates,
        FuzzySearchOptions? options = null)
    {
        if (query == null) throw new ArgumentNullException(nameof(query));
        if (candidates == null) return Array.Empty<SearchResult>();

        options ??= new FuzzySearchOptions();
        if (!options.Enabled) return Array.Empty<SearchResult>();

        var queryTerms = query.Terms.Concat(query.ExactPhrases)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim().ToLowerInvariant())
            .Distinct()
            .ToList();

        if (queryTerms.Count == 0) return Array.Empty<SearchResult>();

        // Step 4: Cap candidate set before expensive distance scoring
        var boundedCandidates = candidates.Take(options.MaxCandidates).ToList();
        var results = new List<SearchResult>();

        foreach (var candidate in boundedCandidates)
        {
            double maxScore = 0.0;
            string? bestMatchedWord = null;
            string? bestField = null;

            // Evaluate fields: Filename stem, Headings, Body
            var filenameTokens = Tokenize(candidate.Filename);
            var headingTokens = Tokenize(candidate.Headings);
            var bodyTokens = Tokenize(candidate.Body);

            foreach (var qTerm in queryTerms)
            {
                // Check Filename tokens
                foreach (var token in filenameTokens)
                {
                    var distRes = CalculateDistance(qTerm, token, options.MaxDistance);
                    if (distRes.IsMatch && distRes.SimilarityScore >= options.MinSimilarity)
                    {
                        double score = CalculateFuzzyScore(distRes.SimilarityScore);
                        if (score > maxScore)
                        {
                            maxScore = score;
                            bestMatchedWord = token;
                            bestField = "filename";
                        }
                    }
                }

                // Check Heading tokens
                foreach (var token in headingTokens)
                {
                    var distRes = CalculateDistance(qTerm, token, options.MaxDistance);
                    if (distRes.IsMatch && distRes.SimilarityScore >= options.MinSimilarity)
                    {
                        double score = CalculateFuzzyScore(distRes.SimilarityScore);
                        if (score > maxScore)
                        {
                            maxScore = score;
                            bestMatchedWord = token;
                            bestField = "heading";
                        }
                    }
                }

                // Check Body tokens
                foreach (var token in bodyTokens)
                {
                    var distRes = CalculateDistance(qTerm, token, options.MaxDistance);
                    if (distRes.IsMatch && distRes.SimilarityScore >= options.MinSimilarity)
                    {
                        double score = CalculateFuzzyScore(distRes.SimilarityScore);
                        if (score > maxScore)
                        {
                            maxScore = score;
                            bestMatchedWord = token;
                            bestField = "body";
                        }
                    }
                }
            }

            if (maxScore > 0.0)
            {
                // Step 3: Label fuzzy match source
                var matchSources = new List<string> { "fuzzy" };
                if (!string.IsNullOrEmpty(bestField))
                {
                    matchSources.Add(bestField);
                }

                string snippet = ExtractSnippet(candidate, bestMatchedWord) ?? $"Fuzzy match: '{bestMatchedWord}'";

                results.Add(new SearchResult(
                    DocumentId: candidate.DocumentId,
                    Filename: candidate.Filename,
                    Score: maxScore,
                    MatchSources: matchSources,
                    Snippets: new[] { snippet },
                    MetadataSummary: null,
                    Path: candidate.CurrentPath
                ));
            }
        }

        return results
            .OrderByDescending(r => r.Score)
            .ThenBy(r => r.Filename, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.DocumentId)
            .ToList();
    }

    private static int GetMaxAllowedDistance(int termLength, int defaultMaxDistance)
    {
        if (termLength <= 3) return 0;
        if (termLength <= 7) return Math.Min(1, defaultMaxDistance);
        return Math.Min(2, defaultMaxDistance);
    }

    // Step 2: Blend fuzzy score below exact lexical score (max 50.0)
    private static double CalculateFuzzyScore(double similarity)
    {
        return 50.0 * similarity;
    }

    private static int ComputeLevenshteinDistance(string s, string t)
    {
        int sLen = s.Length;
        int tLen = t.Length;

        var distance = new int[sLen + 1, tLen + 1];

        for (int i = 0; i <= sLen; i++) distance[i, 0] = i;
        for (int j = 0; j <= tLen; j++) distance[0, j] = j;

        for (int i = 1; i <= sLen; i++)
        {
            for (int j = 1; j <= tLen; j++)
            {
                int cost = (s[i - 1] == t[j - 1]) ? 0 : 1;
                distance[i, j] = Math.Min(
                    Math.Min(distance[i - 1, j] + 1, distance[i, j - 1] + 1),
                    distance[i - 1, j - 1] + cost);
            }
        }

        return distance[sLen, tLen];
    }

    private static List<string> Tokenize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new List<string>();

        var charArray = text.Select(ch => char.IsLetterOrDigit(ch) ? ch : ' ').ToArray();
        var cleanText = new string(charArray);

        return cleanText.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length >= 2)
            .Select(t => t.ToLowerInvariant())
            .Distinct()
            .ToList();
    }

    private static string? ExtractSnippet(FuzzySearchCandidate candidate, string? matchedWord)
    {
        if (string.IsNullOrEmpty(matchedWord)) return null;

        string sourceText = candidate.Body ?? candidate.Headings ?? candidate.Filename;
        if (string.IsNullOrWhiteSpace(sourceText)) return null;

        int index = sourceText.IndexOf(matchedWord, StringComparison.OrdinalIgnoreCase);
        if (index < 0) return $"Fuzzy match on '{matchedWord}'";

        int start = Math.Max(0, index - 20);
        int length = Math.Min(sourceText.Length - start, matchedWord.Length + 40);

        string snippet = sourceText.Substring(start, length);
        if (start > 0) snippet = "..." + snippet;
        if (start + length < sourceText.Length) snippet += "...";

        return snippet;
    }
}
