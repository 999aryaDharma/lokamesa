using System;
using System.Collections.Generic;

namespace DocumentIDE.Infrastructure.SourceControl;

/// <summary>
/// SCM-01 — deterministic classifier for Git subcommands (spec 13 §19).
///
/// Only subcommands known to never mutate local repository state (status,
/// history, inspection) run without the per-worktree mutation queue; anything
/// else — including unknown subcommands — is treated as mutation and
/// serialized. The classification never depends on user-supplied text: option
/// tokens are skipped and only the Git vocabulary decides.
/// </summary>
public static class GitCommandClassifier
{
    /// <summary>Git subcommands that never mutate local repository state.</summary>
    private static readonly HashSet<string> ReadOnlySubcommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "status",
        "diff",
        "log",
        "show",
        "rev-parse",
        "ls-files",
        "ls-remote",
        "ls-tree",
        "cat-file",
        "for-each-ref",
        "describe",
        "shortlog",
        "blame",
        "annotate",
        "grep",
        "whatchanged",
        "count-objects",
        "verify-commit",
        "verify-tag",
        "name-rev",
        "show-ref",
        "symbolic-ref",
        "var",
        "version",
        "help",
        "check-ignore",
        "check-attr",
        "check-ref-format",
        "merge-base",
        "diff-tree",
        "diff-index",
        "diff-files",
    };

    /// <summary>Global options whose value arrives as the next separate token.</summary>
    private static readonly HashSet<string> OptionsTakingSeparateValue = new(StringComparer.OrdinalIgnoreCase)
    {
        "-c",
        "-C",
        "--git-dir",
        "--work-tree",
        "--namespace",
        "--exec-path",
        "--config-env",
        "--super-prefix",
        "--attr-source",
    };

    /// <summary>
    /// True when the invocation may write index/repository/worktree state and
    /// must therefore run through the per-worktree mutation queue.
    /// </summary>
    public static bool MutatesRepositoryState(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        (int index, string? subcommand) = FindSubcommand(arguments, 0);
        if (subcommand is null)
        {
            // No subcommand at all (`git --version`): nothing to mutate.
            return false;
        }

        if (subcommand.Equals("worktree", StringComparison.OrdinalIgnoreCase))
        {
            // `git worktree list` is inspection; every other worktree
            // subcommand mutates registration state (spec 13 §19).
            (_, string? action) = FindSubcommand(arguments, index + 1);
            return action is null || !action.Equals("list", StringComparison.OrdinalIgnoreCase);
        }

        return !ReadOnlySubcommands.Contains(subcommand);
    }

    private static (int Index, string? Token) FindSubcommand(IReadOnlyList<string> arguments, int start)
    {
        for (int i = start; i < arguments.Count; i++)
        {
            string token = arguments[i];
            if (string.IsNullOrEmpty(token))
            {
                continue;
            }

            if (token == "--")
            {
                // Everything after `--` is a path, never a subcommand.
                return (-1, null);
            }

            if (token[0] == '-')
            {
                if (OptionsTakingSeparateValue.Contains(token))
                {
                    i++;
                }

                continue;
            }

            return (i, token);
        }

        return (-1, null);
    }
}
