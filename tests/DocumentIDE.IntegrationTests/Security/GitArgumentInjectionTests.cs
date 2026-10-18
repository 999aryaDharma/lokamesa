using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DocumentIDE.Core.SourceControl;
using DocumentIDE.Infrastructure.SourceControl;
using DocumentIDE.IntegrationTests.SourceControl;
using Xunit;

namespace DocumentIDE.IntegrationTests.Security;

/// <summary>
/// SCM-01 — security regression tests for Git argument/path injection
/// (spec 13 §18/§23).
///
/// The runner passes a structured argument list, never a shell command string,
/// and never interprets path syntax. These fixtures prove that:
/// filenames with spaces, Unicode, parentheses, semicolons, quotes, percent
/// signs, shell metacharacters, and leading dashes cannot alter Git command
/// semantics, and that path handling cannot escape the worktree.
///
/// The repository used for verification is a throwaway temp repo; no user
/// files are touched.
/// </summary>
public sealed class GitArgumentInjectionTests
{
    [Fact]
    public async Task InjectionFixture_FilenamesWithShellMetacharacters_AreStagedVerbatim_WithoutShellSemantics()
    {
        using var repo = TestGitRepository.Create();
        var runner = new GitProcessRunner();

        string[] injectionNames =
        {
            "file with spaces.md",
            "unicode-\u00dcn\u00efc\u00f6d\u00e9-\u6f22\u5b57.md",
            "semi;colon.md",
            "quotes'apostrophe.md",
            "parens (brackets) {braces}.md",
            "percent%PATH%value.md",
            "sub dir (x)/nested file with space.md",
            "x & del sentinel.txt & rm sentinel.txt & y.md",
            "tick `rm sentinel.txt` tick.md",
        };

        foreach (string name in injectionNames)
        {
            repo.WriteFile(name, "content");
        }

        // Shell-concatenation would run `del`/`rm` against this sentinel.
        repo.WriteFile("sentinel.txt", "must survive");

        string[] arguments = new[] { "add", "--" }.Concat(injectionNames).ToArray();
        GitProcessResult add = await runner.RunAsync(new GitProcessRequest(arguments, repo.RootPath));

        Assert.True(add.IsSuccess, $"`git add` failed: {add.ErrorMessage} / {add.StandardError}");
        Assert.True(
            File.Exists(Path.Combine(repo.RootPath, "sentinel.txt")),
            "A shell executed metacharacters from a filename: the sentinel file was deleted.");

        GitProcessResult staged = await runner.RunAsync(
            new GitProcessRequest(new[] { "diff", "--cached", "--name-only", "-z" }, repo.RootPath));
        Assert.True(staged.IsSuccess, staged.ErrorMessage);

        string[] stagedNames = staged.StandardOutput
            .Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        string[] expectedNames = injectionNames.OrderBy(name => name, StringComparer.Ordinal).ToArray();
        Assert.Equal(expectedNames, stagedNames);
    }

    [Fact]
    public async Task InjectionFixture_LeadingDashFilenames_AreTreatedAsPaths_NotOptions()
    {
        using var repo = TestGitRepository.Create();
        var runner = new GitProcessRunner();
        string[] optionLikeNames = { "--upload-pack=evil.md", "--help", "-c" };

        foreach (string name in optionLikeNames)
        {
            repo.WriteFile(name, "content");
        }

        string[] arguments = new[] { "add", "--" }.Concat(optionLikeNames).ToArray();
        GitProcessResult add = await runner.RunAsync(new GitProcessRequest(arguments, repo.RootPath));

        Assert.True(add.IsSuccess, $"`git add` failed: {add.ErrorMessage} / {add.StandardError}");
        // `--help` must have been a path, not a help request.
        Assert.DoesNotContain("usage:", add.StandardOutput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("usage:", add.StandardError, StringComparison.OrdinalIgnoreCase);

        GitProcessResult staged = await runner.RunAsync(
            new GitProcessRequest(new[] { "diff", "--cached", "--name-only", "-z" }, repo.RootPath));
        foreach (string name in optionLikeNames)
        {
            Assert.Contains(name, staged.StandardOutput);
        }
    }

    [Fact]
    public async Task NoRepositoryEscape_AddOutsideWorktree_FailsTyped_AndStagesNothing()
    {
        using var repo = TestGitRepository.Create();
        string outsideDirectory = Path.Combine(Path.GetTempPath(), "scm01", $"outside-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outsideDirectory);
        string outsideFile = Path.Combine(outsideDirectory, "outside.md");
        File.WriteAllText(outsideFile, "outside the worktree");
        var runner = new GitProcessRunner();

        try
        {
            GitProcessResult result = await runner.RunAsync(
                new GitProcessRequest(new[] { "add", "--", outsideFile }, repo.RootPath));

            Assert.False(result.IsSuccess);
            Assert.Equal(GitProcessStatus.Failed, result.Status);
            Assert.NotNull(result.ExitCode);
            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("outside repository", result.StandardError, StringComparison.OrdinalIgnoreCase);

            GitProcessResult staged = await runner.RunAsync(
                new GitProcessRequest(new[] { "diff", "--cached", "--name-only", "-z" }, repo.RootPath));
            Assert.True(staged.IsSuccess, staged.ErrorMessage);
            Assert.DoesNotContain("outside.md", staged.StandardOutput);
        }
        finally
        {
            Directory.Delete(outsideDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task RunResults_DoNotEchoEnvironmentSecrets()
    {
        using var repo = TestGitRepository.Create();
        var runner = new GitProcessRunner();
        const string secret = "scm01-secret-value-9f2c14ab";

        GitProcessResult result = await runner.RunAsync(new GitProcessRequest(
            new[] { "rev-parse", "--show-toplevel" },
            repo.RootPath,
            EnvironmentOverrides: new Dictionary<string, string>
            {
                ["SCM01_TEST_SECRET"] = secret,
            }));

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.DoesNotContain(secret, result.StandardOutput);
        Assert.DoesNotContain(secret, result.StandardError);
        Assert.DoesNotContain(secret, result.ErrorMessage ?? string.Empty);
    }
}
