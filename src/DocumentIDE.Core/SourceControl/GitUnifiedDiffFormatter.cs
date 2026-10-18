using System;
using System.Collections.Generic;
using System.Text;

namespace DocumentIDE.Core.SourceControl;

/// <summary>
/// SCM-04 — pure, deterministic line-based unified-diff engine for the
/// Markdown Git diff (spec 13 §9.1/§9.3).
///
/// The engine is intentionally hand-rolled (no external diff package):
/// <list type="bullet">
/// <item>lines are compared with a Myers shortest-edit-script search over the
/// region between the common prefix and suffix (typical document edits stay
/// cheap),</item>
/// <item>rendering follows the git/GNU unified-diff conventions: 3 lines of
/// context, hunks merged when separated by at most twice the context, a
/// count of 1 elided in hunk headers, <c>/dev/null</c> for an absent
/// version, and <c>\ No newline at end of file</c> markers,</item>
/// <item>a trace-memory guard degrades pathologically large dissimilar inputs
/// to a correct (if coarse) whole-region change instead of exhausting
/// memory; it never truncates or fabricates content.</item>
/// </list>
///
/// The formatter never touches the filesystem or a Git process: it is a pure
/// function from two line sequences to hunks, which is what makes the diff
/// output deterministic and unit-testable.
/// </summary>
public static class GitUnifiedDiffFormatter
{
    /// <summary>Context lines shown around each change (matches Git's default).</summary>
    public const int DefaultContextLines = 3;

    /// <summary>
    /// Maximum Myers backtracking-memory budget, in array cells
    /// (<c>2·(N+M)+1</c> per edit-distance step). Beyond this the change
    /// region is reported as a single correct coarse replacement.
    /// </summary>
    public const long DefaultMaxTraceCells = 8_000_000L;

    /// <summary>Unified-diff marker for a final line without a trailing newline.</summary>
    public const string NoNewlineMarker = "\\ No newline at end of file";

    // A sentinel appended to the comparison key of a final line that has no
    // trailing newline. Textual sides never contain NUL (the service reports
    // any NUL-containing side as binary before calling the formatter), so the
    // sentinel cannot collide with real content.
    private const char NoNewlineKeySentinel = '\0';

    /// <summary>
    /// Computes the unified-diff hunks between two line sequences.
    /// </summary>
    /// <param name="oldLines">Left/older version lines (no terminators).</param>
    /// <param name="oldEndsWithNewline">True when the older version ends with a line terminator.</param>
    /// <param name="newLines">Right/newer version lines (no terminators).</param>
    /// <param name="newEndsWithNewline">True when the newer version ends with a line terminator.</param>
    /// <param name="contextLines">Context lines around changes (>= 0).</param>
    /// <param name="maxTraceCells">Backtracking-memory guard (see <see cref="DefaultMaxTraceCells"/>).</param>
    /// <returns>Deterministic hunks; empty when both versions are textually equal.</returns>
    public static IReadOnlyList<GitDiffHunk> ComputeHunks(
        IReadOnlyList<string> oldLines,
        bool oldEndsWithNewline,
        IReadOnlyList<string> newLines,
        bool newEndsWithNewline,
        int contextLines = DefaultContextLines,
        long maxTraceCells = DefaultMaxTraceCells)
    {
        ArgumentNullException.ThrowIfNull(oldLines);
        ArgumentNullException.ThrowIfNull(newLines);
        ArgumentOutOfRangeException.ThrowIfNegative(contextLines);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTraceCells);

        string[] oldKeys = BuildComparisonKeys(oldLines, oldEndsWithNewline);
        string[] newKeys = BuildComparisonKeys(newLines, newEndsWithNewline);

        List<EditOp> ops = ComputeEditScript(oldKeys, newKeys, maxTraceCells);
        return BuildHunks(ops, oldLines, oldEndsWithNewline, newLines, newEndsWithNewline, contextLines);
    }

    /// <summary>
    /// Renders the unified-diff text for a set of hunks. An absent version is
    /// rendered as <c>/dev/null</c>, following Git. Returns the empty string
    /// when there are no hunks.
    /// </summary>
    public static string FormatUnifiedText(
        string path,
        string leftLabel,
        string rightLabel,
        bool leftExists,
        bool rightExists,
        IReadOnlyList<GitDiffHunk> hunks)
    {
        ArgumentNullException.ThrowIfNull(hunks);

        if (hunks.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        builder.Append("--- ").Append(leftExists ? $"{leftLabel}:{path}" : "/dev/null").Append('\n');
        builder.Append("+++ ").Append(rightExists ? $"{rightLabel}:{path}" : "/dev/null").Append('\n');

        foreach (GitDiffHunk hunk in hunks)
        {
            builder.Append(hunk.Header).Append('\n');
            foreach (GitDiffLine line in hunk.Lines)
            {
                builder.Append(line.Kind switch
                {
                    GitDiffLineKind.Added => '+',
                    GitDiffLineKind.Removed => '-',
                    _ => ' ',
                });
                builder.Append(line.Content).Append('\n');

                if (line.NoNewlineAtEndOfFile)
                {
                    builder.Append(NoNewlineMarker).Append('\n');
                }
            }
        }

        return builder.ToString();
    }

    private static string[] BuildComparisonKeys(IReadOnlyList<string> lines, bool endsWithNewline)
    {
        var keys = new string[lines.Count];
        for (int i = 0; i < lines.Count; i++)
        {
            bool finalLineWithoutNewline = i == lines.Count - 1 && !endsWithNewline;
            keys[i] = finalLineWithoutNewline
                ? string.Concat(lines[i], NoNewlineKeySentinel)
                : lines[i];
        }

        return keys;
    }

    private static List<EditOp> ComputeEditScript(string[] oldKeys, string[] newKeys, long maxTraceCells)
    {
        int n = oldKeys.Length;
        int m = newKeys.Length;

        int prefix = 0;
        while (prefix < n && prefix < m && string.Equals(oldKeys[prefix], newKeys[prefix], StringComparison.Ordinal))
        {
            prefix++;
        }

        int suffix = 0;
        while (suffix < n - prefix
               && suffix < m - prefix
               && string.Equals(oldKeys[n - 1 - suffix], newKeys[m - 1 - suffix], StringComparison.Ordinal))
        {
            suffix++;
        }

        var ops = new List<EditOp>(Math.Max(n, m));
        for (int i = 0; i < prefix; i++)
        {
            ops.Add(EditOp.Equal(i, i));
        }

        int oldEnd = n - suffix;
        int newEnd = m - suffix;
        if (prefix < oldEnd || prefix < newEnd)
        {
            ops.AddRange(MyersOrCoarse(oldKeys, newKeys, prefix, oldEnd, prefix, newEnd, maxTraceCells));
        }

        for (int i = 0; i < suffix; i++)
        {
            ops.Add(EditOp.Equal(n - suffix + i, m - suffix + i));
        }

        return ops;
    }

    /// <summary>
    /// Myers O(ND) shortest-edit-script search over the middle region, with a
    /// bounded trace. When the trace budget is exceeded the region is emitted
    /// as a coarse removed-then-added replacement, which is still a correct
    /// (never truncated) unified diff.
    /// </summary>
    private static List<EditOp> MyersOrCoarse(
        string[] oldKeys,
        string[] newKeys,
        int oldStart,
        int oldEnd,
        int newStart,
        int newEnd,
        long maxTraceCells)
    {
        int n = oldEnd - oldStart;
        int m = newEnd - newStart;
        int max = n + m;
        int offset = max;
        var v = new int[2 * max + 1];
        var trace = new List<int[]>();
        int foundD = -1;
        bool found = false;

        for (int d = 0; d <= max && !found; d++)
        {
            long cells = ((long)(2 * max + 1)) * (d + 1);
            if (cells > maxTraceCells)
            {
                return Coarse(oldStart, oldEnd, newStart, newEnd);
            }

            trace.Add((int[])v.Clone());

            for (int k = -d; k <= d; k += 2)
            {
                int x;
                if (k == -d || (k != d && v[offset + k - 1] < v[offset + k + 1]))
                {
                    x = v[offset + k + 1];
                }
                else
                {
                    x = v[offset + k - 1] + 1;
                }

                int y = x - k;
                while (x < n && y < m
                       && string.Equals(oldKeys[oldStart + x], newKeys[newStart + y], StringComparison.Ordinal))
                {
                    x++;
                    y++;
                }

                v[offset + k] = x;

                if (x >= n && y >= m)
                {
                    foundD = d;
                    found = true;
                    break;
                }
            }
        }

        if (!found)
        {
            // Unreachable in theory (d = max always reaches the end); kept as a
            // safe, correct outcome instead of throwing from a pure formatter.
            return Coarse(oldStart, oldEnd, newStart, newEnd);
        }

        var result = new List<EditOp>(max);
        int cx = n;
        int cy = m;

        for (int d = foundD; d > 0; d--)
        {
            int[] previous = trace[d];
            int k = cx - cy;
            int prevK = (k == -d || (k != d && previous[offset + k - 1] < previous[offset + k + 1]))
                ? k + 1
                : k - 1;
            int prevX = previous[offset + prevK];
            int prevY = prevX - prevK;

            while (cx > prevX && cy > prevY)
            {
                cx--;
                cy--;
                result.Add(EditOp.Equal(oldStart + cx, newStart + cy));
            }

            if (cx == prevX)
            {
                cy--;
                result.Add(EditOp.Added(newStart + cy));
            }
            else
            {
                cx--;
                result.Add(EditOp.Removed(oldStart + cx));
            }
        }

        while (cx > 0 && cy > 0)
        {
            cx--;
            cy--;
            result.Add(EditOp.Equal(oldStart + cx, newStart + cy));
        }

        while (cx > 0)
        {
            cx--;
            result.Add(EditOp.Removed(oldStart + cx));
        }

        while (cy > 0)
        {
            cy--;
            result.Add(EditOp.Added(newStart + cy));
        }

        result.Reverse();
        return result;
    }

    private static List<EditOp> Coarse(int oldStart, int oldEnd, int newStart, int newEnd)
    {
        var ops = new List<EditOp>(oldEnd - oldStart + newEnd - newStart);
        for (int i = oldStart; i < oldEnd; i++)
        {
            ops.Add(EditOp.Removed(i));
        }

        for (int i = newStart; i < newEnd; i++)
        {
            ops.Add(EditOp.Added(i));
        }

        return ops;
    }

    private static IReadOnlyList<GitDiffHunk> BuildHunks(
        List<EditOp> ops,
        IReadOnlyList<string> oldLines,
        bool oldEndsWithNewline,
        IReadOnlyList<string> newLines,
        bool newEndsWithNewline,
        int contextLines)
    {
        if (ops.Count == 0)
        {
            return Array.Empty<GitDiffHunk>();
        }

        // Change runs: consecutive non-equal ops.
        var runs = new List<(int Start, int End)>();
        for (int i = 0; i < ops.Count; i++)
        {
            if (ops[i].Kind == EditKind.Equal)
            {
                continue;
            }

            int start = i;
            while (i + 1 < ops.Count && ops[i + 1].Kind != EditKind.Equal)
            {
                i++;
            }

            runs.Add((start, i + 1));
        }

        if (runs.Count == 0)
        {
            return Array.Empty<GitDiffHunk>();
        }

        // Expand each run by the context and merge hunks whose contexts touch
        // (gap <= 2·context), exactly like Git/GNU diff.
        var ranges = new List<(int Start, int End)>();
        foreach ((int start, int end) in runs)
        {
            int expandedStart = Math.Max(0, start - contextLines);
            int expandedEnd = Math.Min(ops.Count, end + contextLines);
            if (ranges.Count > 0 && expandedStart <= ranges[^1].End)
            {
                ranges[^1] = (ranges[^1].Start, Math.Max(ranges[^1].End, expandedEnd));
            }
            else
            {
                ranges.Add((expandedStart, expandedEnd));
            }
        }

        // Line numbers each op consumes / would consume next.
        var oldNumbers = new int[ops.Count];
        var newNumbers = new int[ops.Count];
        int oldNumber = 1;
        int newNumber = 1;
        for (int i = 0; i < ops.Count; i++)
        {
            oldNumbers[i] = oldNumber;
            newNumbers[i] = newNumber;
            if (ops[i].Kind != EditKind.Added)
            {
                oldNumber++;
            }

            if (ops[i].Kind != EditKind.Removed)
            {
                newNumber++;
            }
        }

        var hunks = new List<GitDiffHunk>(ranges.Count);
        foreach ((int start, int end) in ranges)
        {
            int oldCount = 0;
            int newCount = 0;
            for (int i = start; i < end; i++)
            {
                if (ops[i].Kind != EditKind.Added)
                {
                    oldCount++;
                }

                if (ops[i].Kind != EditKind.Removed)
                {
                    newCount++;
                }
            }

            int oldStart = oldCount > 0 ? oldNumbers[start] : oldNumbers[start] - 1;
            int newStart = newCount > 0 ? newNumbers[start] : newNumbers[start] - 1;

            var lines = new List<GitDiffLine>(end - start);
            for (int i = start; i < end; i++)
            {
                EditOp op = ops[i];
                switch (op.Kind)
                {
                    case EditKind.Equal:
                        bool contextMissingNewline =
                            IsFinalLineWithoutNewline(oldLines.Count, oldEndsWithNewline, op.OldIndex)
                            && IsFinalLineWithoutNewline(newLines.Count, newEndsWithNewline, op.NewIndex);
                        lines.Add(new GitDiffLine(
                            GitDiffLineKind.Context,
                            oldLines[op.OldIndex],
                            oldNumbers[i],
                            newNumbers[i],
                            contextMissingNewline));
                        break;

                    case EditKind.Removed:
                        lines.Add(new GitDiffLine(
                            GitDiffLineKind.Removed,
                            oldLines[op.OldIndex],
                            oldNumbers[i],
                            null,
                            IsFinalLineWithoutNewline(oldLines.Count, oldEndsWithNewline, op.OldIndex)));
                        break;

                    default:
                        lines.Add(new GitDiffLine(
                            GitDiffLineKind.Added,
                            newLines[op.NewIndex],
                            null,
                            newNumbers[i],
                            IsFinalLineWithoutNewline(newLines.Count, newEndsWithNewline, op.NewIndex)));
                        break;
                }
            }

            hunks.Add(new GitDiffHunk(oldStart, oldCount, newStart, newCount, lines));
        }

        return hunks;
    }

    private static bool IsFinalLineWithoutNewline(int lineCount, bool endsWithNewline, int index) =>
        !endsWithNewline && index == lineCount - 1;

    private enum EditKind
    {
        Equal,
        Removed,
        Added,
    }

    private readonly record struct EditOp(EditKind Kind, int OldIndex, int NewIndex)
    {
        public static EditOp Equal(int oldIndex, int newIndex) => new(EditKind.Equal, oldIndex, newIndex);

        public static EditOp Removed(int oldIndex) => new(EditKind.Removed, oldIndex, -1);

        public static EditOp Added(int newIndex) => new(EditKind.Added, -1, newIndex);
    }
}
