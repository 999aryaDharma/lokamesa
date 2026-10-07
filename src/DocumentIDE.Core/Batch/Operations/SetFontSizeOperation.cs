using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DocumentIDE.Core.Batch.Operations;

/// <summary>
/// BAT-08 — Set Font Size parameters (spec 07 §18). Only the size changes; the
/// OOXML unit is half-points (12 pt = 24) for exact, rounding-free determinism.
/// </summary>
public sealed record SetFontSizeParameters
{
    /// <summary>The new font size in half-points (spec 06 §20: w:sz unit).</summary>
    public int SizeHalfPoints { get; init; }
}

/// <summary>
/// A previewed run-level font-size change: stable occurrence ID, origin,
/// visible-text context, and the current→new half-point size for the run.
/// </summary>
public sealed record FontSizeChangePreviewOccurrence(
    string Id,
    string Origin,
    string Context,
    int CurrentSizeHalfPoints,
    int NewSizeHalfPoints,
    bool Selected = true);

/// <summary>
/// BAT-08 — Strict Set Font Size operation. Separate from
/// <see cref="SetFontOperation"/> so a size request can never carry a family
/// change (spec 07 §18).
/// <para>
/// Explicit V1 target semantics: a target is a text-bearing run in
/// word/document.xml, word/header*.xml, or word/footer*.xml that carries a
/// <b>direct</b> w:sz size. Runs whose size resolves from styles or document
/// defaults are deliberately out of scope (no flattening, spec 06 §21). Only
/// reviewed, selected runs are mutated, and only w:sz/@w:val changes — never
/// family, style, spacing, or section margins.
/// </para>
/// </summary>
public sealed class SetFontSizeOperation
{
    private readonly IDocxFontMutator _docxFontMutator;

    public SetFontSizeOperation(IDocxFontMutator docxFontMutator)
    {
        _docxFontMutator = docxFontMutator ?? throw new ArgumentNullException(nameof(docxFontMutator));
    }

    /// <summary>
    /// Typed validation before any file access: the new size must be inside the
    /// WordprocessingML w:sz range (spec 06 §20).
    /// </summary>
    private static void ValidateParameters(SetFontSizeParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (parameters.SizeHalfPoints < DocxFontScan.MinHalfPoints ||
            parameters.SizeHalfPoints > DocxFontScan.MaxHalfPoints)
        {
            throw new ArgumentException("InvalidParameters", nameof(parameters));
        }
    }

    /// <summary>
    /// Builds the reviewed run list for a file: every text run that carries a
    /// direct font size, with the current→new half-point size and the run's
    /// visible-text context. The source file is never modified.
    /// </summary>
    public async Task<IReadOnlyList<FontSizeChangePreviewOccurrence>> BuildOccurrencesAsync(
        string filePath,
        SetFontSizeParameters parameters,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateParameters(parameters);

        if (!SearchReplaceOperation.IsDocx(filePath))
            return Array.Empty<FontSizeChangePreviewOccurrence>();

        var bytes = await SearchReplaceOperation.ReadBoundedAsync(filePath, cancellationToken);
        using var stream = new MemoryStream(bytes, writable: false);
        var runs = DocxFontScan.ScanStream(stream, cancellationToken);

        var occurrences = new List<FontSizeChangePreviewOccurrence>();
        foreach (var run in runs)
        {
            if (run.DirectSizeHalfPoints == null) continue;
            // A run already using the requested size is not affected.
            if (run.DirectSizeHalfPoints == parameters.SizeHalfPoints) continue;
            if (occurrences.Count == DocxFontScan.OccurrenceBudget)
                throw new InvalidDataException("OccurrenceBudgetExceeded");

            occurrences.Add(new FontSizeChangePreviewOccurrence(
                SizeOccurrenceId(run),
                run.Origin.ToString(),
                SearchReplaceOperation.TextPreviewContext(run.ParagraphText, run.LogicalStartIndex, run.VisibleText.Length),
                run.DirectSizeHalfPoints.Value,
                parameters.SizeHalfPoints));
        }

        return occurrences;
    }

    /// <summary>
    /// Creates the mutate delegate for <c>BatchApplyEngine.ApplyAsync</c>. The
    /// delegate identifies the reviewed plan file by directory + preview
    /// fingerprint (the engine guarantees the temp copy is byte-identical to the
    /// reviewed source) and mutates only the reviewed, selected runs.
    /// </summary>
    public Func<string, CancellationToken, Task> CreateMutate(
        SetFontSizeParameters parameters,
        IReadOnlyList<FontChangeTarget> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ValidateParameters(parameters);

        async Task Mutate(string tempPath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = SetFontOperation.ResolveTarget(tempPath, targets);
            var selected = new HashSet<string>(target.SelectedOccurrenceIds ?? Array.Empty<string>(), StringComparer.Ordinal);
            if (selected.Count == 0)
                return; // nothing was reviewed/selected for this file → no change

            var bytes = await SearchReplaceOperation.ReadBoundedAsync(tempPath, cancellationToken);
            using var stream = new MemoryStream(bytes, writable: false);
            var runs = DocxFontScan.ScanStream(stream, cancellationToken);

            var chosen = new List<DocxFontRunTarget>();
            int candidateCount = 0;
            foreach (var run in runs)
            {
                if (run.DirectSizeHalfPoints == null) continue;
                candidateCount++;
                if (!selected.Contains(SizeOccurrenceId(run))) continue;
                chosen.Add(new DocxFontRunTarget(
                    run.PartName, run.ParagraphIndex, run.RunIndex, run.DirectFontFamily, run.DirectSizeHalfPoints));
            }

            SearchReplaceOperation.EnsureAllSelectedFound(chosen.Count, selected, candidateCount);
            if (chosen.Count == 0)
                return;

            _docxFontMutator.SetFontSize(tempPath, chosen, parameters.SizeHalfPoints);
        }

        return Mutate;
    }

    internal static string SizeOccurrenceId(DocxFontRun run) =>
        $"size_{run.PartName}_{run.Origin}_p{run.ParagraphIndex}_r{run.RunIndex}";
}
