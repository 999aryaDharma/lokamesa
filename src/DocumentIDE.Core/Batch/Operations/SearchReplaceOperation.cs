using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Core.Docx;

namespace DocumentIDE.Core.Batch.Operations;

/// <summary>
/// BAT-05 — Exact Search &amp; Replace Operation.
/// Query and replacement parameters for deterministic exact (ordinal) matching.
/// No matching-mode knob exists here by design: fuzzy, semantic, and regex
/// matching are never reachable from this operation (spec 07 §6, master §24.3).
/// </summary>
public sealed record SearchReplaceParameters
{
    /// <summary>The exact text to find (ordinal comparison, no case folding).</summary>
    public string Find { get; init; } = string.Empty;

    /// <summary>The literal replacement text. May be empty (deletion).</summary>
    public string Replacement { get; init; } = string.Empty;
}

/// <summary>
/// A reviewed plan file bound to its on-disk path, the preview fingerprint that
/// was reviewed, and the occurrence IDs the user selected for replacement.
/// </summary>
public sealed record SearchReplaceTarget(
    string FilePath,
    string ExpectedFingerprint,
    IReadOnlyList<string> SelectedOccurrenceIds);

/// <summary>
/// Occurrence-precise DOCX mutation seam. Implemented in Infrastructure by
/// <c>StrictTextMutator</c> (DOCX-03) so the operation in Core never performs
/// raw zip/XML string replacement (spec 07 §15, master §24.5).
/// </summary>
public interface IDocxOccurrenceMutator
{
    void MutateOccurrences(string filePath, IReadOnlyList<DocxMatchResult> occurrences, string replacementText);
}

/// <summary>
/// The BAT-05 operation plug-in for the BAT-04 transactional apply engine:
/// builds exact occurrence previews (DOCX logical text across runs, Markdown
/// plain text) and supplies the mutate delegate that applies only reviewed,
/// selected occurrences. Preview staleness, locking, snapshot, and atomic
/// replace remain owned by <see cref="BatchApplyEngine"/>.
/// </summary>
public sealed class SearchReplaceOperation
{
    /// <summary>Occurrence budget, identical to the BAT-03 preview budget.</summary>
    private const int OccurrenceBudget = 10000;

    /// <summary>Maximum query/replacement length, identical to the BAT-03 preview contract.</summary>
    private const int MaxParameterLength = 4096;

    /// <summary>Markdown/text context radius, identical to the BAT-03 preview window.</summary>
    private const int TextContextRadius = 30;

    // internal: shared with the BAT-07 RegexReplaceOperation so preview/apply
    // byte semantics and safety gates stay single-sourced (visibility only).
    internal static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly IDocxOccurrenceMutator _docxMutator;
    private readonly LogicalTextMatcher _matcher = new();

    public SearchReplaceOperation(IDocxOccurrenceMutator docxMutator)
    {
        _docxMutator = docxMutator ?? throw new ArgumentNullException(nameof(docxMutator));
    }

    private static void ValidateParameters(SearchReplaceParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (string.IsNullOrEmpty(parameters.Find) ||
            parameters.Find.Length > MaxParameterLength ||
            parameters.Replacement == null ||
            parameters.Replacement.Length > MaxParameterLength)
        {
            throw new ArgumentException("InvalidParameters", nameof(parameters));
        }
    }

    /// <summary>
    /// Builds the exact occurrence list for a file: DOCX matches across runs with
    /// occurrence ID/origin/preview context from <see cref="LogicalTextMatcher"/>,
    /// or exact ordinal Markdown/text matches.
    /// </summary>
    public async Task<IReadOnlyList<BatchPreviewOccurrence>> BuildOccurrencesAsync(
        string filePath,
        SearchReplaceParameters parameters,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateParameters(parameters);

        if (!IsDocx(filePath))
        {
            // Markdown / plain text: strict UTF-8 decode and ordinal scan,
            // identical semantics to the BAT-03 preview (Body origin, body:{index} ids).
            var textBytes = await ReadBoundedAsync(filePath, cancellationToken);
            var text = StrictUtf8.GetString(textBytes);
            return ScanText(text, parameters.Find, cancellationToken)
                .Select(m => new BatchPreviewOccurrence(m.Id, "Body", TextPreviewContext(text, m.Index, parameters.Find.Length)))
                .ToList();
        }

        var bytes = await ReadBoundedAsync(filePath, cancellationToken);
        using var stream = new MemoryStream(bytes, writable: false);
        var matches = _matcher.Search(stream, parameters.Find, new DocxSearchOptions
        {
            MaxMatches = OccurrenceBudget,
            CancellationToken = cancellationToken
        });
        return matches
            .Select(m => new BatchPreviewOccurrence(m.OccurrenceId, m.Origin.ToString(), m.PreviewContext))
            .ToList();
    }

    /// <summary>
    /// Creates the mutate delegate for <c>BatchApplyEngine.ApplyAsync</c>. The
    /// delegate identifies the reviewed plan file by directory + preview
    /// fingerprint (the engine guarantees the temp copy is byte-identical to the
    /// reviewed source) and replaces only the reviewed occurrence IDs.
    /// </summary>
    public Func<string, CancellationToken, Task> CreateMutate(
        SearchReplaceParameters parameters,
        IReadOnlyList<SearchReplaceTarget> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ValidateParameters(parameters);

        async Task Mutate(string tempPath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = ResolveTarget(tempPath, targets);
            var selected = new HashSet<string>(target.SelectedOccurrenceIds ?? Array.Empty<string>(), StringComparer.Ordinal);
            if (selected.Count == 0)
                return; // nothing was reviewed/selected for this file → no change

            if (IsDocx(target.FilePath))
            {
                var matches = _matcher.Search(tempPath, parameters.Find, new DocxSearchOptions
                {
                    MaxMatches = OccurrenceBudget,
                    CancellationToken = cancellationToken
                });
                var chosen = matches.Where(m => selected.Contains(m.OccurrenceId)).ToList();
                EnsureAllSelectedFound(chosen.Count, selected, matches.Count);
                if (chosen.Count > 0)
                    _docxMutator.MutateOccurrences(tempPath, chosen, parameters.Replacement);
                return;
            }

            // Markdown / plain text: exact ordinal replacement of the selected
            // occurrences only, right-to-left so earlier offsets stay valid.
            var textBytes = await File.ReadAllBytesAsync(tempPath, cancellationToken);
            var text = StrictUtf8.GetString(textBytes);
            var found = ScanText(text, parameters.Find, cancellationToken);
            var chosenText = found.Where(m => selected.Contains(m.Id)).ToList();
            EnsureAllSelectedFound(chosenText.Count, selected, found.Count);

            var builder = new StringBuilder(text);
            foreach (var match in chosenText.OrderByDescending(m => m.Index))
            {
                builder.Remove(match.Index, parameters.Find.Length);
                builder.Insert(match.Index, parameters.Replacement);
            }
            await File.WriteAllBytesAsync(tempPath, StrictUtf8.GetBytes(builder.ToString()), cancellationToken);
        }

        return Mutate;
    }

    /// <summary>
    /// Fails closed: every reviewed occurrence ID must be found in the (fingerprint-
    /// verified) file, otherwise the correct selection cannot be honored and the
    /// mutation must not guess.
    /// </summary>
    internal static void EnsureAllSelectedFound(int chosenCount, HashSet<string> selected, int totalFound)
    {
        if (chosenCount != selected.Count)
            throw new InvalidOperationException(
                $"SelectedOccurrenceNotFound: {selected.Count - chosenCount} of {selected.Count} reviewed occurrence IDs were not found ({totalFound} exact matches present).");
    }

    internal static bool IsDocx(string filePath) =>
        Path.GetExtension(filePath).Equals(".docx", StringComparison.OrdinalIgnoreCase);

    internal static string TextPreviewContext(string text, int index, int findLength)
    {
        var start = Math.Max(0, index - TextContextRadius);
        var end = Math.Min(text.Length, index + findLength + TextContextRadius);
        return text.Substring(start, end - start);
    }

    private readonly record struct TextMatch(string Id, int Index);

    /// <summary>Exact ordinal scan producing BAT-03-compatible occurrence IDs.</summary>
    private static List<TextMatch> ScanText(string text, string find, CancellationToken cancellationToken)
    {
        var matches = new List<TextMatch>();
        for (var start = 0; start < text.Length;)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var index = text.IndexOf(find, start, StringComparison.Ordinal);
            if (index < 0) break;
            if (matches.Count == OccurrenceBudget) throw new InvalidDataException("OccurrenceBudgetExceeded");
            matches.Add(new TextMatch($"body:{index}", index));
            start = index + find.Length;
        }
        return matches;
    }

    internal static SearchReplaceTarget ResolveTarget(string tempPath, IReadOnlyList<SearchReplaceTarget> targets)
    {
        var tempDirectory = Path.GetDirectoryName(Path.GetFullPath(tempPath))
            ?? throw new InvalidOperationException("Cannot determine the directory of the file being mutated.");

        string fingerprint;
        using (var stream = File.OpenRead(tempPath))
            fingerprint = Convert.ToHexString(SHA256.HashData(stream));

        var candidates = new List<SearchReplaceTarget>();
        foreach (var target in targets)
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(target.FilePath));
            if (string.Equals(directory, tempDirectory, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(target.ExpectedFingerprint, fingerprint, StringComparison.Ordinal))
                candidates.Add(target);
        }

        if (candidates.Count == 0)
            throw new InvalidOperationException(
                "ApplyFingerprintMismatch: the file being mutated does not match its reviewed preview fingerprint.");

        // Byte-identical copies of the same content in one directory are only safe
        // to apply when their reviewed occurrence selections agree; otherwise the
        // correct selection is unknowable here, so mutation fails closed.
        var selections = candidates
            .Select(c => SelectionKey(c.SelectedOccurrenceIds))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (selections.Count > 1)
            throw new InvalidOperationException(
                "AmbiguousApplyTarget: multiple reviewed files match this file with different occurrence selections.");

        return candidates[0];

        static string SelectionKey(IReadOnlyList<string>? ids) => ids == null
            ? string.Empty
            : string.Join('\u001f', ids.Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal));
    }

    // Mirrors BatchPreviewService.ReadBoundedAsync / BatchApplyEngine.ReadBoundedAsync
    // so preview-time fingerprints and bounded reads stay byte-identical.
    // internal: shared with the BAT-07 RegexReplaceOperation.
    internal static async Task<byte[]> ReadBoundedAsync(string path, CancellationToken cancellationToken)
    {
        for (string? component = path; component != null; component = Path.GetDirectoryName(component))
            if ((File.GetAttributes(component) & FileAttributes.ReparsePoint) != 0) throw new IOException("ReparsePoint");
        var limit = Path.GetExtension(path).Equals(".docx", StringComparison.OrdinalIgnoreCase) ? 100 * 1024 * 1024 : 10 * 1024 * 1024;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        if (stream.Length > limit) throw new InvalidDataException("FileTooLarge");
        var bytes = new byte[checked((int)stream.Length)];
        await stream.ReadExactlyAsync(bytes, cancellationToken);
        if (stream.ReadByte() != -1) throw new IOException("SourceChanged");
        return bytes;
    }
}
