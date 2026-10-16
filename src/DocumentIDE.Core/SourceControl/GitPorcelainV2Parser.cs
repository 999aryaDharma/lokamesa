using System;
using System.Collections.Generic;
using System.Globalization;

namespace DocumentIDE.Core.SourceControl;

/// <summary>
/// SCM-02 — deterministic parser for
/// <c>git status --porcelain=v2 -z --branch</c> (spec 13 §6).
///
/// The porcelain v2 <c>-z</c> wire format is NUL-terminated records:
/// <list type="bullet">
/// <item><c># branch.*</c> headers (NUL-terminated like every record),</item>
/// <item><c>1 &lt;XY&gt; &lt;sub&gt; &lt;mH&gt; &lt;mI&gt; &lt;mW&gt; &lt;hH&gt; &lt;hI&gt; &lt;path&gt;</c>,</item>
/// <item><c>2 ... &lt;X&gt;&lt;score&gt; &lt;path&gt;</c> followed by a separate NUL-terminated original path,</item>
/// <item><c>u &lt;XY&gt; &lt;sub&gt; &lt;m1&gt; &lt;m2&gt; &lt;m3&gt; &lt;mW&gt; &lt;h1&gt; &lt;h2&gt; &lt;h3&gt; &lt;path&gt;</c>,</item>
/// <item><c>? &lt;path&gt;</c> and <c>! &lt;path&gt;</c>.</item>
/// </list>
///
/// Paths are never quoted in <c>-z</c> mode, spaces and Unicode survive
/// verbatim, and the path is the last field of each record. Structurally
/// malformed input throws <see cref="FormatException"/>; callers convert that
/// into a typed failure (no silent fallback).
/// </summary>
public static class GitPorcelainV2Parser
{
    /// <summary>
    /// Parses one complete <c>git status --porcelain=v2 -z --branch</c> output
    /// into a typed <see cref="GitStatusSnapshot"/>.
    /// </summary>
    /// <param name="worktreeRoot">
    /// Worktree root the status was produced for; stored verbatim on the snapshot.
    /// </param>
    /// <param name="porcelainOutput">Raw stdout of the status invocation.</param>
    public static GitStatusSnapshot Parse(string worktreeRoot, string porcelainOutput)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(worktreeRoot);
        ArgumentNullException.ThrowIfNull(porcelainOutput);

        string? branchName = null;
        bool isDetached = false;
        string? headCommit = null;
        string? upstreamReference = null;
        int? ahead = null;
        int? behind = null;
        var entries = new List<GitStatusEntry>();

        string[] records = porcelainOutput.Split('\0');
        for (int i = 0; i < records.Length; i++)
        {
            string record = records[i];
            if (record.Length == 0)
            {
                continue;
            }

            switch (record[0])
            {
                case '#':
                    ParseHeader(record, ref branchName, ref isDetached, ref headCommit, ref upstreamReference, ref ahead, ref behind);
                    break;
                case '1':
                    entries.Add(ParseOrdinary(record));
                    break;
                case '2':
                    if (i + 1 >= records.Length)
                    {
                        throw new FormatException(
                            $"Porcelain v2 rename/copy record is missing its original path: '{record}'.");
                    }

                    entries.Add(ParseRenamedOrCopied(record, records[++i]));
                    break;
                case 'u':
                    entries.Add(ParseUnmerged(record));
                    break;
                case '?':
                    entries.Add(ParsePathOnly(record, GitStatusEntryType.Untracked));
                    break;
                case '!':
                    entries.Add(ParsePathOnly(record, GitStatusEntryType.Ignored));
                    break;
                default:
                    throw new FormatException($"Unrecognized porcelain v2 record type '{record[0]}': '{record}'.");
            }
        }

        return new GitStatusSnapshot(
            worktreeRoot,
            branchName,
            isDetached,
            headCommit,
            upstreamReference,
            ahead,
            behind,
            entries);
    }

    private static void ParseHeader(
        string record,
        ref string? branchName,
        ref bool isDetached,
        ref string? headCommit,
        ref string? upstreamReference,
        ref int? ahead,
        ref int? behind)
    {
        string body = record.Length > 2 ? record[2..] : string.Empty;
        int separator = body.IndexOf(' ');
        string key = separator < 0 ? body : body[..separator];
        string value = separator < 0 ? string.Empty : body[(separator + 1)..];

        switch (key)
        {
            case "branch.oid":
                if (!string.IsNullOrEmpty(value) && !string.Equals(value, "(initial)", StringComparison.Ordinal))
                {
                    headCommit = value;
                }

                break;

            case "branch.head":
                if (string.Equals(value, "(detached)", StringComparison.Ordinal))
                {
                    isDetached = true;
                    branchName = null;
                }
                else if (!string.IsNullOrEmpty(value))
                {
                    isDetached = false;
                    branchName = value;
                }

                break;

            case "branch.upstream":
                upstreamReference = string.IsNullOrEmpty(value) ? null : value;
                break;

            case "branch.ab":
                ParseAheadBehind(value, ref ahead, ref behind);
                break;

            default:
                // Forward-compatible: unknown `#` metadata headers (e.g.
                // `# stash N`) are skipped; they are not status entries.
                break;
        }
    }

    private static void ParseAheadBehind(string value, ref int? ahead, ref int? behind)
    {
        foreach (string token in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.Length < 2
                || !int.TryParse(token.AsSpan(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int count))
            {
                continue;
            }

            if (token[0] == '+')
            {
                ahead = count;
            }
            else if (token[0] == '-')
            {
                behind = count;
            }
        }
    }

    private static GitStatusEntry ParseOrdinary(string record)
    {
        string[] fields = record.Split(' ', 9);
        if (fields.Length != 9)
        {
            throw Malformed(record);
        }

        (GitChangeKind index, GitChangeKind workingTree) = ParseChangeKinds(fields[1], record);
        return new GitStatusEntry
        {
            Path = fields[8],
            EntryType = GitStatusEntryType.Ordinary,
            IndexChange = index,
            WorkingTreeChange = workingTree,
            StatusCharacters = fields[1],
        };
    }

    private static GitStatusEntry ParseRenamedOrCopied(string record, string originalPath)
    {
        string[] fields = record.Split(' ', 10);
        if (fields.Length != 10)
        {
            throw Malformed(record);
        }

        (GitChangeKind index, GitChangeKind workingTree) = ParseChangeKinds(fields[1], record);
        return new GitStatusEntry
        {
            Path = fields[9],
            OriginalPath = originalPath,
            EntryType = GitStatusEntryType.RenamedOrCopied,
            IndexChange = index,
            WorkingTreeChange = workingTree,
            StatusCharacters = fields[1],
        };
    }

    private static GitStatusEntry ParseUnmerged(string record)
    {
        string[] fields = record.Split(' ', 11);
        if (fields.Length != 11)
        {
            throw Malformed(record);
        }

        string statusCharacters = fields[1];
        if (statusCharacters.Length != 2)
        {
            throw Malformed(record);
        }

        return new GitStatusEntry
        {
            Path = fields[10],
            EntryType = GitStatusEntryType.Unmerged,
            IndexChange = GitChangeKind.Unmerged,
            WorkingTreeChange = GitChangeKind.Unmerged,
            ConflictKind = MapConflictKind(statusCharacters),
            StatusCharacters = statusCharacters,
        };
    }

    private static GitStatusEntry ParsePathOnly(string record, GitStatusEntryType entryType)
    {
        if (record.Length < 3 || record[1] != ' ')
        {
            throw Malformed(record);
        }

        return new GitStatusEntry
        {
            Path = record[2..],
            EntryType = entryType,
            // Convention mirroring `git status --short` for inspectability: the
            // porcelain v2 `?`/`!` records carry no XY pair themselves.
            StatusCharacters = entryType == GitStatusEntryType.Ignored ? "!!" : "??",
        };
    }

    private static (GitChangeKind Index, GitChangeKind WorkingTree) ParseChangeKinds(string statusCharacters, string record)
    {
        if (statusCharacters.Length != 2)
        {
            throw Malformed(record);
        }

        return (MapChangeKind(statusCharacters[0], record), MapChangeKind(statusCharacters[1], record));
    }

    private static GitChangeKind MapChangeKind(char value, string record) => value switch
    {
        '.' => GitChangeKind.Unmodified,
        'M' => GitChangeKind.Modified,
        'A' => GitChangeKind.Added,
        'D' => GitChangeKind.Deleted,
        'R' => GitChangeKind.Renamed,
        'C' => GitChangeKind.Copied,
        'T' => GitChangeKind.TypeChanged,
        'U' => GitChangeKind.Unmerged,
        _ => throw Malformed(record),
    };

    /// <summary>
    /// Maps the seven porcelain v2 unmerged combinations (spec 13 §21).
    /// An unknown combination stays conflicted with
    /// <see cref="GitConflictKind.None"/>: the conflict is never silently
    /// dropped, only its specific class stays unclassified.
    /// </summary>
    private static GitConflictKind MapConflictKind(string statusCharacters) => statusCharacters switch
    {
        "DD" => GitConflictKind.BothDeleted,
        "AU" => GitConflictKind.AddedByUs,
        "UD" => GitConflictKind.DeletedByThem,
        "UA" => GitConflictKind.AddedByThem,
        "DU" => GitConflictKind.DeletedByUs,
        "AA" => GitConflictKind.BothAdded,
        "UU" => GitConflictKind.BothModified,
        _ => GitConflictKind.None,
    };

    private static FormatException Malformed(string record) =>
        new($"Unrecognized porcelain v2 record: '{record}'.");
}
