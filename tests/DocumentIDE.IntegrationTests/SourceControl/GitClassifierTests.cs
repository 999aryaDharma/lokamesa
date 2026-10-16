using DocumentIDE.Core.SourceControl;
using DocumentIDE.Infrastructure.SourceControl;
using Xunit;

namespace DocumentIDE.IntegrationTests.SourceControl;

/// <summary>
/// SCM-01 — deterministic classifier coverage (spec 13 §19/§20).
///
/// <see cref="GitCommandClassifier"/> decides which invocations run through
/// the per-worktree mutation queue; <see cref="GitFailureClassifier"/> maps
/// unambiguous Git exit signatures to typed error kinds. Both must stay
/// conservative: unknown vocabulary is treated as mutation / Unknown rather
/// than guessed as safe.
/// </summary>
public sealed class GitClassifierTests
{
    // ── GitCommandClassifier ────────────────────────────────────────────

    [Theory]
    [InlineData("status")]
    [InlineData("diff")]
    [InlineData("log")]
    [InlineData("show")]
    [InlineData("rev-parse")]
    [InlineData("ls-files")]
    [InlineData("ls-remote")]
    [InlineData("ls-tree")]
    [InlineData("cat-file")]
    [InlineData("for-each-ref")]
    [InlineData("describe")]
    [InlineData("shortlog")]
    [InlineData("blame")]
    [InlineData("grep")]
    [InlineData("count-objects")]
    [InlineData("show-ref")]
    [InlineData("symbolic-ref")]
    [InlineData("merge-base")]
    [InlineData("diff-tree")]
    [InlineData("diff-index")]
    [InlineData("diff-files")]
    [InlineData("check-ignore")]
    [InlineData("check-attr")]
    [InlineData("verify-commit")]
    [InlineData("verify-tag")]
    [InlineData("var")]
    [InlineData("version")]
    public void CommandClassifier_ReadOnlySubcommands_DoNotRequireMutationQueue(string subcommand)
    {
        Assert.False(GitCommandClassifier.MutatesRepositoryState(new[] { subcommand }));
    }

    [Theory]
    [InlineData("add")]
    [InlineData("commit")]
    [InlineData("push")]
    [InlineData("pull")]
    [InlineData("fetch")]
    [InlineData("checkout")]
    [InlineData("switch")]
    [InlineData("restore")]
    [InlineData("reset")]
    [InlineData("merge")]
    [InlineData("rebase")]
    [InlineData("cherry-pick")]
    [InlineData("revert")]
    [InlineData("stash")]
    [InlineData("clean")]
    [InlineData("rm")]
    [InlineData("mv")]
    [InlineData("apply")]
    [InlineData("am")]
    [InlineData("init")]
    [InlineData("clone")]
    [InlineData("branch")]
    [InlineData("tag")]
    [InlineData("remote")]
    [InlineData("config")]
    [InlineData("gc")]
    [InlineData("repack")]
    [InlineData("update-ref")]
    [InlineData("update-index")]
    [InlineData("sparse-checkout")]
    [InlineData("submodule")]
    [InlineData("notes")]
    [InlineData("replace")]
    [InlineData("filter-branch")]
    public void CommandClassifier_MutatingSubcommands_RequireMutationQueue(string subcommand)
    {
        Assert.True(GitCommandClassifier.MutatesRepositoryState(new[] { subcommand }));
    }

    [Fact]
    public void CommandClassifier_UnknownSubcommand_IsConservativeMutation()
    {
        Assert.True(GitCommandClassifier.MutatesRepositoryState(new[] { "frobnicate" }));
        Assert.True(GitCommandClassifier.MutatesRepositoryState(new[] { "scm01-not-a-real-command" }));
    }

    [Fact]
    public void CommandClassifier_BareInvocationAndVersionProbe_DoNotMutate()
    {
        Assert.False(GitCommandClassifier.MutatesRepositoryState(Array.Empty<string>()));
        Assert.False(GitCommandClassifier.MutatesRepositoryState(new[] { "--version" }));
        Assert.False(GitCommandClassifier.MutatesRepositoryState(new[] { "--help" }));
    }

    [Fact]
    public void CommandClassifier_GlobalOptionsBeforeSubcommand_AreSkipped()
    {
        // Inline-value global options.
        Assert.False(GitCommandClassifier.MutatesRepositoryState(
            new[] { "--git-dir=.git", "--work-tree=.", "status" }));

        // Separate-value global options (`-c`, `-C`, `--git-dir`, …).
        Assert.False(GitCommandClassifier.MutatesRepositoryState(new[] { "-c", "user.name=x", "status" }));
        Assert.False(GitCommandClassifier.MutatesRepositoryState(new[] { "-C", "some/path", "rev-parse", "HEAD" }));
        Assert.False(GitCommandClassifier.MutatesRepositoryState(
            new[] { "--git-dir", ".git", "--work-tree", ".", "diff" }));

        // Mutating subcommand behind global options must still be detected.
        Assert.True(GitCommandClassifier.MutatesRepositoryState(
            new[] { "-c", "user.name=x", "commit", "-m", "msg" }));
        Assert.True(GitCommandClassifier.MutatesRepositoryState(
            new[] { "--exec-path", "some/path", "push" }));
    }

    [Fact]
    public void CommandClassifier_WorktreeList_IsReadOnly_AllOtherWorktreeActions_Mutate()
    {
        Assert.False(GitCommandClassifier.MutatesRepositoryState(new[] { "worktree", "list" }));
        Assert.False(GitCommandClassifier.MutatesRepositoryState(new[] { "worktree", "list", "--porcelain" }));

        Assert.True(GitCommandClassifier.MutatesRepositoryState(new[] { "worktree" }));
        Assert.True(GitCommandClassifier.MutatesRepositoryState(new[] { "worktree", "add", "path" }));
        Assert.True(GitCommandClassifier.MutatesRepositoryState(new[] { "worktree", "remove", "path" }));
        Assert.True(GitCommandClassifier.MutatesRepositoryState(new[] { "worktree", "prune" }));
    }

    [Fact]
    public void CommandClassifier_ArgumentsAfterDoubleDash_AreNeverSubcommands()
    {
        // Git itself rejects a top-level `--` before the subcommand
        // (`unknown option: --`), so the classifier must not treat whatever
        // follows as a read-only subcommand either way.
        Assert.False(GitCommandClassifier.MutatesRepositoryState(new[] { "--", "commit" }));
    }

    [Fact]
    public void CommandClassifier_NullArguments_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => GitCommandClassifier.MutatesRepositoryState(null!));
    }

    // ── GitFailureClassifier ────────────────────────────────────────────

    [Theory]
    [InlineData(0, "", "", GitProcessErrorKind.None)]
    [InlineData(0, "output", "some noise", GitProcessErrorKind.None)]
    [InlineData(1, "", "", GitProcessErrorKind.Unknown)]
    [InlineData(128, null, null, GitProcessErrorKind.Unknown)]
    [InlineData(1, "", "fatal: some totally novel failure mode", GitProcessErrorKind.Unknown)]
    public void FailureClassifier_ZeroExitIsNone_UnmappedStaysUnknown(
        int exitCode, string? stdout, string? stderr, GitProcessErrorKind expected)
    {
        Assert.Equal(expected, GitFailureClassifier.Classify(exitCode, stdout, stderr));
    }

    [Theory]
    [InlineData(
        "fatal: not a git repository (or any of the parent directories): .git",
        GitProcessErrorKind.GitRepositoryNotFound)]
    [InlineData(
        "fatal: 'D:/x' is not a working tree",
        GitProcessErrorKind.GitWorktreeInvalid)]
    [InlineData(
        "fatal: Unable to create 'C:/repo/.git/index.lock': File exists.\n\nAnother git process seems to be running in this repository, e.g.\nan editor opened by 'git commit'. Please make sure all processes\nare terminated then try again.",
        GitProcessErrorKind.GitLockContention)]
    [InlineData(
        "fatal: could not lock config file C:/Users/x/.gitconfig: File exists",
        GitProcessErrorKind.GitLockContention)]
    [InlineData(
        "fatal: Authentication failed for 'https://example.com/x.git/'",
        GitProcessErrorKind.GitAuthRequired)]
    [InlineData(
        "fatal: could not read Username for 'https://example.com': terminal prompts disabled",
        GitProcessErrorKind.GitAuthRequired)]
    [InlineData(
        "git@github.com: Permission denied (publickey).\r\nfatal: Could not read from remote repository.",
        GitProcessErrorKind.GitAuthRequired)]
    [InlineData(
        "error: insufficient permission for adding an object to repository database .git/objects\nfatal: failed to write object",
        GitProcessErrorKind.Unknown)]
    [InlineData(
        "fatal: could not create work tree dir 'x': Permission denied",
        GitProcessErrorKind.GitPermissionDenied)]
    [InlineData(
        "fatal: Opening 'file.txt': Access is denied.",
        GitProcessErrorKind.GitPermissionDenied)]
    [InlineData(
        "pre-commit hook exited with code 1 (error)\nhusky - commit-msg hook failed",
        GitProcessErrorKind.GitCommitHookFailed)]
    [InlineData(
        "CONFLICT (content): Merge conflict in file.txt\nAutomatic merge failed; fix conflicts and then commit the result.",
        GitProcessErrorKind.GitConflict)]
    [InlineData(
        "error: The following untracked working tree files would be overwritten by checkout:\n\tfile.txt\nPlease move or remove them before you switch branches.\nAborting",
        GitProcessErrorKind.GitUntrackedConflict)]
    [InlineData(
        "error: Your local changes to the following files would be overwritten by merge:\n\tfile.txt\nPlease commit your changes or stash them before you merge.\nAborting",
        GitProcessErrorKind.GitDirtyWorktree)]
    [InlineData(
        "fatal: 'C:/path/to/worktree' contains modified or untracked files, use --force to delete it",
        GitProcessErrorKind.GitDirtyWorktree)]
    [InlineData(
        "fatal: Not possible to fast-forward, aborting.",
        GitProcessErrorKind.GitPullNotFastForward)]
    [InlineData(
        "hint: You have divergent branches and need to specify how to reconcile them.\nfatal: Need to specify how to reconcile divergent branches.",
        GitProcessErrorKind.GitPullNotFastForward)]
    [InlineData(
        "error: failed to push some refs to 'https://example.com/x.git'",
        GitProcessErrorKind.GitPushRejected)]
    [InlineData(
        "remote: error: branch protection\n ! [remote rejected] main -> main (pre-receive hook declined)",
        GitProcessErrorKind.GitCommitHookFailed)]
    [InlineData(
        " ! [rejected]        main -> main (fetch first)\nerror: failed to push some refs to 'https://example.com/x.git'",
        GitProcessErrorKind.GitPushRejected)]
    public void FailureClassifier_MapsUnambiguousSignatures(string stderr, GitProcessErrorKind expected)
    {
        Assert.Equal(expected, GitFailureClassifier.Classify(1, "", stderr));
    }

    [Fact]
    public void FailureClassifier_RejectedMarkerOnStdout_IsStillPushRejected()
    {
        Assert.Equal(
            GitProcessErrorKind.GitPushRejected,
            GitFailureClassifier.Classify(1, " ! [rejected]        main -> main (fetch first)", ""));
    }

    [Fact]
    public void FailureClassifier_UntrackedConflict_TakesPrecedenceOverDirtyWorktree()
    {
        // Both markers ("untracked working tree files would be overwritten" and
        // "would be overwritten") appear; the more specific one must win.
        const string stderr =
            "error: The following untracked working tree files would be overwritten by checkout:\n\tfile.txt\nAborting";
        Assert.Equal(GitProcessErrorKind.GitUntrackedConflict, GitFailureClassifier.Classify(1, "", stderr));
    }
}
