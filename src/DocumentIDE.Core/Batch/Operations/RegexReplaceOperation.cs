using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Core.Docx;

namespace DocumentIDE.Core.Batch.Operations;

/// <summary>
/// BAT-07 — Regex Advanced Replace parameters.
/// <para>
/// The Advanced/Regex toggle (<see cref="RegexEnabled"/>) is OFF by default
/// (spec 07 §6, master §24.3): with default parameters matching stays
/// exact/ordinal and the regex engine is never reached — regex requires the
/// same explicit opt-in the UI's Advanced mode performs.
/// </para>
/// </summary>
public sealed record RegexReplaceParameters
{
    /// <summary>Exact text (default) or regular-expression pattern (advanced mode).</summary>
    public string Find { get; init; } = string.Empty;

    /// <summary>
    /// The replacement. Exact mode: literal text. Advanced/regex mode: a .NET
    /// substitution template using the documented standard ($1, ${name},
    /// $$, $&amp;, …). May be empty (deletion).
    /// </summary>
    public string Replacement { get; init; } = string.Empty;

    /// <summary>Explicit Advanced/Regex toggle. OFF (exact) by default.</summary>
    public bool RegexEnabled { get; init; }

    /// <summary>
    /// Opt-in non-backtracking evaluation (<c>RegexOptions.NonBacktracking</c>,
    /// supported by this runtime) — linear-time matching that cannot
    /// catastrophically backtrack. Patterns incompatible with the engine
    /// (lookaround/backreferences) are rejected with a typed error.
    /// </summary>
    public bool NonBacktracking { get; init; }

    /// <summary>
    /// Per-match evaluation budget passed to the compiled <see cref="Regex"/>.
    /// Always finite — never <see cref="Regex.InfiniteMatchTimeout"/>.
    /// </summary>
    public TimeSpan MatchTimeout { get; init; } = DefaultMatchTimeout;

    /// <summary>Default match budget (2s per match call).</summary>
    public static readonly TimeSpan DefaultMatchTimeout = TimeSpan.FromSeconds(2);
}

/// <summary>
/// A previewed regex (or exact-default) occurrence: stable ID, origin, preview
/// context, and the exact replacement output that apply will produce for this
/// occurrence (capture groups already expanded) — the reviewed promise.
/// </summary>
public sealed record RegexPreviewOccurrence(
    string Id,
    string Origin,
    string Context,
    string ReplacementOutput,
    bool Selected = true);

/// <summary>
/// BAT-07 — Regex Advanced Replace Operation. Opt-in regex search &amp;
/// replace alongside the BAT-05 exact default: the explicit
/// <see cref="RegexReplaceParameters.RegexEnabled"/> toggle gates every regex
/// path, matching runs with a finite <c>RegexMatchTimeout</c>, and apply only
/// ever happens through <see cref="CreateMutate"/> inside the BAT-04
/// <c>BatchApplyEngine</c> (reviewed fingerprint + selected occurrence IDs +
/// snapshot + atomic replace — no bypass).
/// </summary>
public sealed class RegexReplaceOperation
{
    /// <summary>Occurrence budget, identical to the BAT-03 preview budget.</summary>
    private const int OccurrenceBudget = 10000;

    /// <summary>Maximum query/replacement length, identical to the BAT-03 preview contract.</summary>
    private const int MaxParameterLength = 4096;

    private readonly IDocxOccurrenceMutator _docxMutator;

    /// <summary>Exact-mode delegate: the toggle-OFF default reuses the BAT-05 operation verbatim.</summary>
    private readonly SearchReplaceOperation _exactOperation;

    private readonly LogicalTextMatcher _matcher = new();

    public RegexReplaceOperation(IDocxOccurrenceMutator docxMutator)
    {
        _docxMutator = docxMutator ?? throw new ArgumentNullException(nameof(docxMutator));
        _exactOperation = new SearchReplaceOperation(docxMutator);
    }

    private static void ValidateParameters(RegexReplaceParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (string.IsNullOrEmpty(parameters.Find) ||
            parameters.Find.Length > MaxParameterLength ||
            parameters.Replacement == null ||
            parameters.Replacement.Length > MaxParameterLength)
        {
            throw new ArgumentException("InvalidParameters", nameof(parameters));
        }

        if (parameters.MatchTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentException("InvalidParameters", nameof(parameters));
        }
    }

    private static SearchReplaceParameters ToExact(RegexReplaceParameters parameters) =>
        new() { Find = parameters.Find, Replacement = parameters.Replacement };

    /// <summary>
    /// Compiles the pattern with the explicit match timeout (and the opt-in
    /// non-backtracking engine) so pathological input can never run
    /// unbounded. Invalid or engine-incompatible patterns are rejected with a
    /// typed error before any file access or matching.
    /// </summary>
    private static Regex Compile(RegexReplaceParameters parameters)
    {
        var options = parameters.NonBacktracking ? RegexOptions.NonBacktracking : RegexOptions.None;
        try
        {
            return new Regex(parameters.Find, options, parameters.MatchTimeout);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            // Invalid syntax, or incompatible with the requested engine
            // (e.g. lookaround/backreference under NonBacktracking).
            throw new ArgumentException($"InvalidRegexPattern: {ex.Message}", nameof(parameters));
        }
    }

    /// <summary>
    /// Re-derives a reviewed occurrence's capture expansion from its full
    /// paragraph text so preview output and apply output are computed by the
    /// identical expansion rule. Fails closed if the match cannot be re-derived.
    /// </summary>
    private static string ExpandDocxReplacement(Regex regex, DocxMatchResult occurrence, string replacement)
    {
        var match = regex.Match(occurrence.FullParagraphText, occurrence.LogicalStartIndex);
        if (!match.Success || match.Index != occurrence.LogicalStartIndex)
            throw new InvalidOperationException(
                $"RegexPreviewMismatch: capture expansion for '{occurrence.OccurrenceId}' could not be re-derived deterministically.");
        return match.Result(replacement);
    }

    /// <summary>
    /// Builds the occurrence preview for a file. Toggle OFF: exact/ordinal
    /// matching delegated to the BAT-05 operation. Toggle ON: compiled regex
    /// with a finite match timeout; preview shows the expanded replacement
    /// output (capture groups resolved) that apply will later produce.
    /// </summary>
    public async Task<IReadOnlyList<RegexPreviewOccurrence>> BuildOccurrencesAsync(
        string filePath,
        RegexReplaceParameters parameters,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateParameters(parameters);

        if (!parameters.RegexEnabled)
        {
            var exact = await _exactOperation.BuildOccurrencesAsync(filePath, ToExact(parameters), cancellationToken);
            return exact
                .Select(m => new RegexPreviewOccurrence(m.Id, m.Origin, m.Context, parameters.Replacement))
                .ToList();
        }

        // Compile (and thereby reject invalid patterns) BEFORE any file access.
        var regex = Compile(parameters);

        if (!SearchReplaceOperation.IsDocx(filePath))
        {
            // Markdown / plain text: strict UTF-8 decode and bounded regex scan.
            var textBytes = await SearchReplaceOperation.ReadBoundedAsync(filePath, cancellationToken);
            var text = SearchReplaceOperation.StrictUtf8.GetString(textBytes);
            var occurrences = new List<RegexPreviewOccurrence>();
            foreach (Match match in regex.Matches(text))
            {
                cancellationToken.ThrowIfCancellationRequested();
                // A zero-length match has no reviewable span and cannot be
                // mutated reliably — reject typed instead of silently skipping.
                if (match.Length == 0) throw new InvalidDataException("ZeroLengthMatch");
                if (occurrences.Count == OccurrenceBudget) throw new InvalidDataException("OccurrenceBudgetExceeded");
                occurrences.Add(new RegexPreviewOccurrence(
                    $"body:{match.Index}",
                    "Body",
                    SearchReplaceOperation.TextPreviewContext(text, match.Index, match.Length),
                    match.Result(parameters.Replacement)));
            }
            return occurrences;
        }

        // DOCX: regex over each paragraph's logical text across runs — the
        // compiled instance's match timeout bounds this path too.
        var bytes = await SearchReplaceOperation.ReadBoundedAsync(filePath, cancellationToken);
        using var stream = new MemoryStream(bytes, writable: false);
        var matches = _matcher.Search(stream, string.Empty, new DocxSearchOptions
        {
            MaxMatches = OccurrenceBudget,
            CancellationToken = cancellationToken,
            Pattern = regex
        });

        var docxOccurrences = new List<RegexPreviewOccurrence>(matches.Count);
        foreach (var match in matches)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (match.LogicalLength == 0) throw new InvalidDataException("ZeroLengthMatch");
            docxOccurrences.Add(new RegexPreviewOccurrence(
                match.OccurrenceId,
                match.Origin.ToString(),
                match.PreviewContext,
                ExpandDocxReplacement(regex, match, parameters.Replacement)));
        }
        return docxOccurrences;
    }

    /// <summary>
    /// Creates the mutate delegate for <c>BatchApplyEngine.ApplyAsync</c>.
    /// Reviewed targets are resolved by directory + preview fingerprint and
    /// only the reviewed occurrence IDs are replaced (BAT-04 engine owns
    /// staleness, snapshot, and atomic replace).
    /// </summary>
    public Func<string, CancellationToken, Task> CreateMutate(
        RegexReplaceParameters parameters,
        IReadOnlyList<SearchReplaceTarget> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ValidateParameters(parameters);

        if (!parameters.RegexEnabled)
            return _exactOperation.CreateMutate(ToExact(parameters), targets);

        // Eager typed rejection: no mutate delegate exists for a bad pattern.
        var regex = Compile(parameters);

        async Task Mutate(string tempPath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = SearchReplaceOperation.ResolveTarget(tempPath, targets);
            var selected = new HashSet<string>(target.SelectedOccurrenceIds ?? Array.Empty<string>(), StringComparer.Ordinal);
            if (selected.Count == 0)
                return; // nothing was reviewed/selected for this file → no change

            if (SearchReplaceOperation.IsDocx(target.FilePath))
            {
                var matches = _matcher.Search(tempPath, string.Empty, new DocxSearchOptions
                {
                    MaxMatches = OccurrenceBudget,
                    CancellationToken = cancellationToken,
                    Pattern = regex
                });
                var chosen = matches.Where(m => selected.Contains(m.OccurrenceId)).ToList();
                SearchReplaceOperation.EnsureAllSelectedFound(chosen.Count, selected, matches.Count);
                if (chosen.Count == 0)
                    return;

                // Expand each reviewed occurrence's capture replacement, then
                // mutate strictly right-to-left (part → paragraph → index) in
                // consecutive equal-expansion batches so earlier logical
                // offsets stay valid and preview==apply expansion holds.
                var ordered = chosen
                    .OrderByDescending(m => m.PartName, StringComparer.Ordinal)
                    .ThenByDescending(m => m.ParagraphIndex)
                    .ThenByDescending(m => m.LogicalStartIndex)
                    .ToList();

                var batch = new List<DocxMatchResult>();
                string? currentExpansion = null;
                foreach (var match in ordered)
                {
                    if (match.LogicalLength == 0) throw new InvalidDataException("ZeroLengthMatch");
                    var expansion = ExpandDocxReplacement(regex, match, parameters.Replacement);
                    if (currentExpansion != null && !string.Equals(expansion, currentExpansion, StringComparison.Ordinal))
                    {
                        _docxMutator.MutateOccurrences(tempPath, batch, currentExpansion);
                        batch = new List<DocxMatchResult>();
                    }
                    currentExpansion = expansion;
                    batch.Add(match);
                }
                if (batch.Count > 0)
                    _docxMutator.MutateOccurrences(tempPath, batch, currentExpansion!);
                return;
            }

            // Markdown / plain text: bounded regex scan on the fingerprint-
            // verified temp copy, expansion per match, right-to-left replace.
            var textBytes = await File.ReadAllBytesAsync(tempPath, cancellationToken);
            var text = SearchReplaceOperation.StrictUtf8.GetString(textBytes);
            var found = new List<TextMatch>();
            foreach (Match match in regex.Matches(text))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (match.Length == 0) throw new InvalidDataException("ZeroLengthMatch");
                if (found.Count == OccurrenceBudget) throw new InvalidDataException("OccurrenceBudgetExceeded");
                found.Add(new TextMatch(match.Index, match.Length, match.Result(parameters.Replacement)));
            }
            var chosenText = found.Where(m => selected.Contains($"body:{m.Index}")).ToList();
            SearchReplaceOperation.EnsureAllSelectedFound(chosenText.Count, selected, found.Count);
            if (chosenText.Count == 0)
                return;

            var builder = new StringBuilder(text);
            foreach (var match in chosenText.OrderByDescending(m => m.Index))
            {
                builder.Remove(match.Index, match.Length);
                builder.Insert(match.Index, match.Expansion);
            }
            await File.WriteAllBytesAsync(tempPath, SearchReplaceOperation.StrictUtf8.GetBytes(builder.ToString()), cancellationToken);
        }

        return Mutate;
    }

    private readonly record struct TextMatch(int Index, int Length, string Expansion);
}
