using System;
using System.Collections.Generic;
using System.IO;

namespace DocumentIDE.Infrastructure.Word;

/// <summary>Confidence that a document is being edited in Word.</summary>
public enum WordEditingConfidence
{
    None,
    Low,
    High
}

/// <summary>One named heuristic contributing to the editing verdict.</summary>
public sealed record WordEditingSignal(string Name, bool Positive, string Detail);

/// <summary>
/// Combined editing verdict. No single heuristic is infallible:
/// <see cref="Signals"/> always carries the per-signal breakdown and
/// <see cref="Confidence"/> stays <see cref="WordEditingConfidence.Low"/>
/// when only one heuristic is positive.
/// </summary>
public sealed record WordEditingState(
    string CanonicalPath,
    bool IsEditing,
    WordEditingConfidence Confidence,
    IReadOnlyList<WordEditingSignal> Signals,
    string Summary);

public enum WordPreflightDecision
{
    Allowed,
    BlockedEditingInWord,
    FileMissing
}

/// <summary>
/// Mutation-preflight outcome for batch/strict DOCX operations.
/// A blocked document must be skipped (or the user sent to Word) and
/// retried after save — never silently overwritten.
/// </summary>
public sealed record WordMutationPreflightResult(
    bool Allowed,
    WordPreflightDecision Decision,
    string Reason,
    WordEditingState? EditingState);

public sealed class WordEditingStateChangedEventArgs(string canonicalPath, WordEditingState state) : EventArgs
{
    public string CanonicalPath { get; } = canonicalPath;
    public WordEditingState State { get; } = state;
}

/// <summary>
/// WORD-02 — Word Editing/Lock Detection.
///
/// Combines three independent signals, mirroring spec 04 §19:
/// Word owner-lock/temp file presence, exclusive-access probe failure,
/// and the Word automation document-list probe. Exposes
/// "Editing in Word" state and a mutation-preflight verdict.
/// Stateless evaluation via <see cref="Check"/>; <see cref="Refresh"/>
/// re-evaluates (call on filesystem/process changes, e.g. FS-03 watcher
/// events), updates the cache, and raises <see cref="StateChanged"/>
/// when the editing verdict flips.
/// </summary>
public sealed class WordEditingStateDetector
{
    public const string OwnerLockFileSignal = "owner-lock-file";
    public const string ExclusiveAccessSignal = "exclusive-access";
    public const string AutomationOpenSignal = "automation-open";

    private readonly Func<string, bool> _automationOpenProbe;
    private readonly Dictionary<string, bool> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _cacheLock = new();

    /// <param name="automationOpenProbe">
    /// Word automation document-list probe (canonical path → open).
    /// Defaults to "unknown/closed" when Word automation is unavailable.
    /// </param>
    public WordEditingStateDetector(Func<string, bool>? automationOpenProbe = null)
    {
        _automationOpenProbe = automationOpenProbe ?? (_ => false);
    }

    public event EventHandler<WordEditingStateChangedEventArgs>? StateChanged;

    /// <summary>Pure evaluation of the current editing state. No caching.</summary>
    public WordEditingState Check(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Document path is required.", nameof(path));
        }

        string canonical;
        try
        {
            canonical = Path.GetFullPath(path);
        }
        catch (Exception ex)
        {
            return new WordEditingState(path, false, WordEditingConfidence.None,
                Array.Empty<WordEditingSignal>(), $"Path is not usable: {ex.Message}");
        }

        var signals = new List<WordEditingSignal>(3)
        {
            CheckOwnerLockFile(canonical),
            CheckExclusiveAccess(canonical),
            CheckAutomationOpen(canonical)
        };

        int positives = 0;
        foreach (var signal in signals)
        {
            if (signal.Positive) positives++;
        }

        bool isEditing = positives > 0;
        var confidence = positives switch
        {
            0 => WordEditingConfidence.None,
            1 => WordEditingConfidence.Low,
            _ => WordEditingConfidence.High
        };

        string summary = isEditing
            ? $"Likely editing in Word ({positives} of {signals.Count} signals positive)."
            : "No Word editing signals detected.";

        return new WordEditingState(canonical, isEditing, confidence, signals, summary);
    }

    /// <summary>
    /// Re-evaluates and refreshes the cached verdict. Call this when the
    /// filesystem watcher (FS-03) or a process change suggests the state
    /// may have moved; subscribers of <see cref="StateChanged"/> refresh UI.
    /// </summary>
    public WordEditingState Refresh(string path)
    {
        WordEditingState state = Check(path);

        bool previous;
        lock (_cacheLock)
        {
            previous = _cache.TryGetValue(state.CanonicalPath, out bool cached) && cached;
            _cache[state.CanonicalPath] = state.IsEditing;
        }

        if (previous != state.IsEditing)
        {
            StateChanged?.Invoke(this, new WordEditingStateChangedEventArgs(state.CanonicalPath, state));
        }

        return state;
    }

    /// <summary>
    /// Mutation preflight for batch/strict DOCX operations. An open document
    /// is blocked (skip / open Word / retry after save); a missing file is
    /// reported as missing rather than mislabeled.
    /// </summary>
    public WordMutationPreflightResult CheckMutationPreflight(string path)
    {
        WordEditingState state;
        try
        {
            state = Check(path);
        }
        catch (ArgumentException ex)
        {
            return new WordMutationPreflightResult(false, WordPreflightDecision.FileMissing, ex.Message, null);
        }

        if (!File.Exists(state.CanonicalPath))
        {
            return new WordMutationPreflightResult(
                false,
                WordPreflightDecision.FileMissing,
                $"Document file at '{state.CanonicalPath}' does not exist on disk.",
                state);
        }

        if (state.IsEditing)
        {
            return new WordMutationPreflightResult(
                false,
                WordPreflightDecision.BlockedEditingInWord,
                $"Document is likely being edited in Word ({state.Summary}) Skip, open Word, or retry after save.",
                state);
        }

        return new WordMutationPreflightResult(true, WordPreflightDecision.Allowed, "No Word editing detected.", state);
    }

    private static WordEditingSignal CheckOwnerLockFile(string canonicalPath)
    {
        try
        {
            string? directory = Path.GetDirectoryName(canonicalPath);
            string fileName = Path.GetFileName(canonicalPath);
            if (directory == null || fileName.Length == 0)
            {
                return new WordEditingSignal(OwnerLockFileSignal, false, "Path has no file name.");
            }

            // Word owner-lock convention keeps "~$" plus the tail of the
            // file name in the same directory; both the full-name and the
            // first-character-replaced variants are recognized because the
            // exact truncation differs across Word versions.
            string[] candidates =
            {
                Path.Combine(directory, "~$" + fileName),
                fileName.Length > 1 ? Path.Combine(directory, "~$" + fileName.Substring(1)) : string.Empty
            };

            foreach (string candidate in candidates)
            {
                if (candidate.Length > 0 && File.Exists(candidate))
                {
                    return new WordEditingSignal(OwnerLockFileSignal, true,
                        $"Owner lock file present: '{Path.GetFileName(candidate)}'.");
                }
            }

            return new WordEditingSignal(OwnerLockFileSignal, false, "No owner lock file found.");
        }
        catch (Exception ex)
        {
            return new WordEditingSignal(OwnerLockFileSignal, false, $"Owner-lock probe failed: {ex.Message}");
        }
    }

    private static WordEditingSignal CheckExclusiveAccess(string canonicalPath)
    {
        if (!File.Exists(canonicalPath))
        {
            return new WordEditingSignal(ExclusiveAccessSignal, false, "File does not exist.");
        }

        try
        {
            using var stream = new FileStream(canonicalPath, FileMode.Open, FileAccess.Read, FileShare.None);
            return new WordEditingSignal(ExclusiveAccessSignal, false, "Exclusive access probe succeeded.");
        }
        catch (IOException ex)
        {
            return new WordEditingSignal(ExclusiveAccessSignal, true,
                $"Exclusive access blocked (likely open elsewhere): {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return new WordEditingSignal(ExclusiveAccessSignal, true, $"Access denied: {ex.Message}");
        }
        catch (Exception ex)
        {
            return new WordEditingSignal(ExclusiveAccessSignal, false, $"Exclusive-access probe failed: {ex.Message}");
        }
    }

    private WordEditingSignal CheckAutomationOpen(string canonicalPath)
    {
        try
        {
            bool open = _automationOpenProbe(canonicalPath);
            return new WordEditingSignal(AutomationOpenSignal, open,
                open ? "Word automation reports the document as open." : "Word automation does not report the document as open.");
        }
        catch (Exception ex)
        {
            // Word unavailable/busy/modal or COM failure: typed signal, not a silent fallback.
            return new WordEditingSignal(AutomationOpenSignal, false, $"Automation probe failed: {ex.Message}");
        }
    }
}
