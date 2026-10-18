using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Core.SourceControl;
using DocumentIDE.Infrastructure.SourceControl;
using Xunit;

namespace DocumentIDE.SecurityTests.SourceControl;

/// <summary>
/// SCM-17 — Windows path/Unicode/injection release-gate tests (spec 13
/// §18/§20/§23, spec 11 §28).
///
/// These tests prove the Source Control boundary cannot be turned into a shell:
/// <list type="bullet">
/// <item>no invocation is ever built as a shell command string — every hostile
/// token is one element of a structured argument list,</item>
/// <item>paths with spaces, Unicode, emoji, leading dashes, quotes and shell
/// metacharacters are staged verbatim as data,</item>
/// <item>traversal, absolute/UNC/drive-root, NUL, malformed and option-like
/// paths are rejected typed — and the purely local validators spawn no Git
/// process at all,</item>
/// <item>an option-like leading-dash filename is a path after <c>--</c>, never
/// an option,</item>
/// <item>a commit message is one structured argument (never a command
/// string),</item>
/// <item>a credential embedded in a remote URL is never surfaced in a typed
/// failure.</item>
/// </list>
///
/// All repositories are disposable temp repositories; no user files are
/// touched.
/// </summary>
public sealed class GitProcessSecurityTests
{
    // =====================================================================
    // 1. Structured arguments — hostile filenames are data, never shell text
    // =====================================================================

    [Theory]
    [InlineData("file with spaces.md")]
    [InlineData("unicode-\u00dcn\u00efc\u00f6d\u00e9-\u6f22\u5b57-\u30d5\u30a1\u30a4\u30eb.md")]
    [InlineData("\ud83d\udcc4 emoji report.md")]
    [InlineData("--upload-pack=evil.md")]
    [InlineData("-c")]
    [InlineData("quotes ' single apostrophe.md")]
    [InlineData("amp & semi ; percent %PATH% bang ! dollar $() caret ^.md")]
    [InlineData("brackets [1] and {2}.md")]
    [InlineData("backtick `whoami`.md")]
    [InlineData("hash # and at @.md")]
    [InlineData("sub dir (x)/nested file with space.md")]
    public async Task HostileFileName_IsStagedVerbatim_AsOneStructuredArgument(string hostileName)
    {
        using SecurityGitRepository repo = SecurityGitRepository.Create();
        repo.WriteFile(hostileName, "hostile content");
        repo.WriteFile("sentinel.txt", "must survive");
        var recording = new RecordingGitProcessRunner(new GitProcessRunner());
        var repositories = new GitRepositoryService(recording);
        var stages = new GitStageService(recording, repositories);

        GitStageResult result = await stages.StageAsync(repo.RootPath, hostileName);

        Assert.True(result.Outcome == GitStageOutcome.Succeeded, result.Message ?? result.Failure?.ErrorMessage);

        // The exact structured vector: ["add", "--", <hostileName>]. The name is
        // one argument and is never concatenated with a command or a shell.
        GitProcessRequest add = Assert.Single(
            recording.Requests,
            request => request.Arguments.Count > 0 && request.Arguments[0] == "add");
        Assert.Equal(new[] { "add", "--", hostileName }, add.Arguments);
        Assert.Equal(repo.RootPath, add.WorkingDirectory, StringComparer.OrdinalIgnoreCase);

        // No shell ran: the sentinel survived and no metacharacter was
        // interpreted as a command separator.
        Assert.True(
            File.Exists(Path.Combine(repo.RootPath, "sentinel.txt")),
            "A shell executed metacharacters from a filename: the sentinel file was deleted.");
        RecordedInvocation addInvocation = Assert.Single(
            recording.Invocations,
            invocation => invocation.Request.Arguments.Count > 0
                && invocation.Request.Arguments[0] == "add");
        Assert.DoesNotContain("usage:", addInvocation.Result.StandardOutput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("usage:", addInvocation.Result.StandardError, StringComparison.OrdinalIgnoreCase);

        string staged = SecurityGitRepository.RunGitCaptureUtf8(
            repo.RootPath,
            "diff",
            "--cached",
            "--name-only",
            "-z");
        Assert.Contains(hostileName, staged);

        AssertNoShellTextWasEverConstructed(recording.Requests);
    }

    /// <summary>
    /// Spec 13 §23: a leading-dash filename is only safe because it travels
    /// after <c>--</c>. This test pins that the exact vector is used and that
    /// Git treats the value as a path (no help/option output).
    /// </summary>
    [Fact]
    public async Task LeadingDashFileName_IsAPathAfterSeparator_NotAnOption()
    {
        using SecurityGitRepository repo = SecurityGitRepository.Create();
        repo.WriteFile("--help", "not help text");
        var recording = new RecordingGitProcessRunner(new GitProcessRunner());
        var repositories = new GitRepositoryService(recording);
        var stages = new GitStageService(recording, repositories);

        GitStageResult result = await stages.StageAsync(repo.RootPath, "--help");

        Assert.Equal(GitStageOutcome.Succeeded, result.Outcome);
        GitProcessRequest add = Assert.Single(
            recording.Requests,
            request => request.Arguments.Count > 0 && request.Arguments[0] == "add");
        Assert.Equal(new[] { "add", "--", "--help" }, add.Arguments);
        RecordedInvocation addInvocation = Assert.Single(
            recording.Invocations,
            invocation => invocation.Request.Arguments.Count > 0
                && invocation.Request.Arguments[0] == "add");
        Assert.DoesNotContain("usage:", addInvocation.Result.StandardError, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "--help",
            SecurityGitRepository.RunGitCaptureUtf8(repo.RootPath, "diff", "--cached", "--name-only", "-z"));
    }

    /// <summary>
    /// Spec 13 §18/§23: characters that cannot even exist in a Windows filename
    /// (<c>|</c>, <c>&gt;</c>, <c>"</c>, <c>?</c>) can still arrive from a
    /// client. They travel as one structured pathspec element and Git rejects
    /// them typed; no shell ever sees them, and nothing is staged.
    /// </summary>
    [Theory]
    [InlineData("pipe|name.md")]
    [InlineData("redirect>name.md")]
    [InlineData("double\"quote.md")]
    [InlineData("question?name.md")]
    [InlineData("star*glob.md")]
    public async Task ClientSuppliedShellMetacharacterPath_IsDataNotAShellCommand(string hostilePath)
    {
        using SecurityGitRepository repo = SecurityGitRepository.Create();
        repo.WriteFile("sentinel.txt", "must survive");
        var recording = new RecordingGitProcessRunner(new GitProcessRunner());
        var repositories = new GitRepositoryService(recording);
        var stages = new GitStageService(recording, repositories);

        GitStageResult result = await stages.StageAsync(repo.RootPath, hostilePath);

        Assert.NotEqual(GitStageOutcome.Succeeded, result.Outcome);
        GitProcessRequest add = Assert.Single(
            recording.Requests,
            request => request.Arguments.Count > 0 && request.Arguments[0] == "add");
        Assert.Equal(new[] { "add", "--", hostilePath }, add.Arguments);
        Assert.True(
            File.Exists(Path.Combine(repo.RootPath, "sentinel.txt")),
            "A shell executed metacharacters from a client-supplied path.");
        AssertNoShellTextWasEverConstructed(recording.Requests);
    }

    /// <summary>
    /// Spec 13 §18/§23: control characters (other than NUL) are legal in a .NET
    /// path string but cannot name a Windows file. They still travel as one
    /// structured pathspec element; Git rejects them typed and no shell runs.
    /// </summary>
    [Theory]
    [InlineData("bell\u0007name.md")]
    [InlineData("unit\u001fseparator.md")]
    [InlineData("del\u007fname.md")]
    public async Task ControlCharacterPath_IsDataAndFailsTyped_WithoutAShell(string hostilePath)
    {
        using SecurityGitRepository repo = SecurityGitRepository.Create();
        repo.WriteFile("sentinel.txt", "must survive");
        var recording = new RecordingGitProcessRunner(new GitProcessRunner());
        var repositories = new GitRepositoryService(recording);
        var stages = new GitStageService(recording, repositories);

        GitStageResult result = await stages.StageAsync(repo.RootPath, hostilePath);

        Assert.NotEqual(GitStageOutcome.Succeeded, result.Outcome);
        GitProcessRequest add = Assert.Single(
            recording.Requests,
            request => request.Arguments.Count > 0 && request.Arguments[0] == "add");
        Assert.Equal(new[] { "add", "--", hostilePath }, add.Arguments);
        Assert.True(File.Exists(Path.Combine(repo.RootPath, "sentinel.txt")));
        AssertNoShellTextWasEverConstructed(recording.Requests);
    }

    /// <summary>
    /// Spec 13 §11.1/§18: the commit message is one <c>-m</c> argument. Shell
    /// metacharacters and quotes stay message text and never become commands.
    /// </summary>
    [Fact]
    public async Task CommitMessage_WithShellMetacharacters_IsOneStructuredArgument()
    {
        using SecurityGitRepository repo = SecurityGitRepository.Create();
        const string message =
            "fix: a & b | c ; d > e %PATH% 'single' \"double\" $HOME `whoami`";
        repo.WriteFile("docs/message.md", "changed");
        var recording = new RecordingGitProcessRunner(new GitProcessRunner());
        var repositories = new GitRepositoryService(recording);
        var stages = new GitStageService(recording, repositories);
        var commits = new GitCommitService(recording, repositories, stages);

        GitStageResult staged = await stages.StageAsync(repo.RootPath, "docs/message.md");
        Assert.Equal(GitStageOutcome.Succeeded, staged.Outcome);

        GitCommitResult result = await commits.CommitAsync(repo.RootPath, message);

        Assert.Equal(GitCommitOutcome.Succeeded, result.Outcome);
        GitProcessRequest commit = Assert.Single(
            recording.Requests,
            request => request.Arguments.Count > 0 && request.Arguments[0] == "commit");
        Assert.Equal(new[] { "commit", "-m", message }, commit.Arguments);
        Assert.Equal(
            message,
            SecurityGitRepository.RunGitCaptureUtf8(repo.RootPath, "log", "-1", "--format=%s").Trim());
        AssertNoShellTextWasEverConstructed(recording.Requests);
    }

    // =====================================================================
    // 2. Rejected before Git — purely local validation, zero Git invocations
    // =====================================================================

    [Theory]
    [InlineData("../escape.md")]
    [InlineData("..\\escape.md")]
    [InlineData("docs/../../escape.md")]
    [InlineData("docs/../other/file.md")]
    [InlineData("..")]
    [InlineData("/etc/passwd")]
    [InlineData("C:\\Windows\\win.ini")]
    [InlineData("D:\\")]
    [InlineData("\\\\server\\share\\file.md")]
    [InlineData("docs//double.md")]
    [InlineData("docs/./file.md")]
    [InlineData("a:b.md")]
    [InlineData("nul\0char.md")]
    public async Task DiffService_RejectsUnsafePath_WithoutSpawningGit(string hostilePath)
    {
        using SecurityGitRepository repo = SecurityGitRepository.Create();
        var recording = new RecordingGitProcessRunner(new GitProcessRunner());
        var diffs = new GitDiffService(recording);

        GitDiffResult result = await diffs.GetFileDiffAsync(
            repo.RootPath,
            hostilePath,
            GitDiffTarget.WorkingTree);

        Assert.Equal(GitDiffStatus.PathRejected, result.Status);
        Assert.False(result.IsAvailable);
        Assert.Null(result.Diff);
        Assert.Empty(recording.Requests);
    }

    [Theory]
    [InlineData("../escape.md")]
    [InlineData("..\\escape.md")]
    [InlineData("/etc/passwd")]
    [InlineData("C:\\Windows\\win.ini")]
    [InlineData("D:\\")]
    [InlineData("\\\\server\\share\\file.md")]
    [InlineData("docs//double.md")]
    [InlineData("a:b.md")]
    [InlineData("nul\0char.md")]
    public async Task StageService_RejectsUnsafePath_WithoutAnyIndexMutation(string hostilePath)
    {
        using SecurityGitRepository repo = SecurityGitRepository.Create();
        repo.WriteFile("docs/baseline.md", "changed");
        var recording = new RecordingGitProcessRunner(new GitProcessRunner());
        var repositories = new GitRepositoryService(recording);
        var stages = new GitStageService(recording, repositories);

        GitStageResult result = await stages.StageAsync(repo.RootPath, hostilePath);

        Assert.Equal(GitStageOutcome.RejectedPath, result.Outcome);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorCode));

        // Repository discovery is a read-only `rev-parse`; no index-writing
        // command may ever have run for a rejected path.
        Assert.DoesNotContain(
            recording.Requests,
            request => request.Arguments.Count > 0
                && request.Arguments[0] is "add" or "restore" or "commit" or "reset" or "checkout" or "clean");
        Assert.Empty(SecurityGitRepository.RunGitCaptureUtf8(repo.RootPath, "diff", "--cached", "--name-only"));
    }

    /// <summary>
    /// NUL is rejected by the pure path validator: the diff surface spawns no
    /// Git process at all, and the stage surface runs no index mutation.
    /// </summary>
    [Fact]
    public async Task NulInPath_IsRejectedBeforeAnyIndexMutation()
    {
        using SecurityGitRepository repo = SecurityGitRepository.Create();
        var recording = new RecordingGitProcessRunner(new GitProcessRunner());
        var repositories = new GitRepositoryService(recording);
        var stages = new GitStageService(recording, repositories);
        var diffs = new GitDiffService(recording);

        GitDiffResult diffResult = await diffs.GetFileDiffAsync(
            repo.RootPath,
            "nul\0char.md",
            GitDiffTarget.WorkingTree);
        Assert.Equal(GitDiffStatus.PathRejected, diffResult.Status);
        Assert.Empty(recording.Requests);

        GitStageResult stageResult = await stages.StageAsync(repo.RootPath, "nul\0char.md");
        Assert.Equal(GitStageOutcome.RejectedPath, stageResult.Outcome);
        Assert.DoesNotContain(
            recording.Requests,
            request => request.Arguments.Count > 0
                && request.Arguments[0] is "add" or "restore" or "commit");
    }

    [Theory]
    [InlineData("--force")]
    [InlineData("-b")]
    [InlineData("main~1")]
    [InlineData("main^")]
    [InlineData("main:path")]
    [InlineData("main?")]
    [InlineData("main*")]
    [InlineData("main[")]
    [InlineData("main\\")]
    [InlineData("x@{1}")]
    [InlineData("main..other")]
    [InlineData("main.lock")]
    [InlineData("/main")]
    [InlineData("main/")]
    [InlineData("main//x")]
    [InlineData("main.")]
    [InlineData("has space")]
    public async Task WorktreeCreate_RejectsUnsafeRef_WithoutSpawningGit(string hostileRef)
    {
        using SecurityGitRepository repo = SecurityGitRepository.Create();
        string target = Path.Combine(Path.GetTempPath(), "scm17-security", $"wt-{Guid.NewGuid():N}");
        var recording = new RecordingGitProcessRunner(new GitProcessRunner());
        var worktrees = new GitWorktreeService(recording, new UnusedFolderSourceService());
        var mutations = new GitWorktreeMutationService(recording, worktrees);

        GitWorktreeCreateResult result = await mutations.CreateWorktreeAsync(
            new GitWorktreeCreateRequest(repo.RootPath, target, hostileRef));

        Assert.Equal(GitWorktreeCreateStatus.InvalidRequest, result.Status);
        Assert.Empty(recording.Requests);
        Assert.False(Directory.Exists(target));
    }

    [Theory]
    [InlineData("../wt")]
    [InlineData("..\\wt")]
    [InlineData("wt.")]
    [InlineData("wt ")]
    [InlineData("D:\\")]
    [InlineData("path/with/.git/segment")]
    public async Task WorktreeCreate_RejectsUnsafeTargetPath_WithoutSpawningGit(string hostileTarget)
    {
        using SecurityGitRepository repo = SecurityGitRepository.Create();
        string target = hostileTarget.StartsWith("path/with", StringComparison.Ordinal)
            ? Path.Combine(repo.RootPath, hostileTarget.Replace('/', Path.DirectorySeparatorChar))
            : hostileTarget;
        var recording = new RecordingGitProcessRunner(new GitProcessRunner());
        var worktrees = new GitWorktreeService(recording, new UnusedFolderSourceService());
        var mutations = new GitWorktreeMutationService(recording, worktrees);

        GitWorktreeCreateResult result = await mutations.CreateWorktreeAsync(
            new GitWorktreeCreateRequest(repo.RootPath, target, "main"));

        Assert.Equal(GitWorktreeCreateStatus.InvalidTargetPath, result.Status);
        Assert.Empty(recording.Requests);
    }

    // =====================================================================
    // 3. Repository escape — `.git` and drive-root handling stay contained
    // =====================================================================

    /// <summary>
    /// Spec 13 §23: a <c>.git</c> path can never become a staged change (Git
    /// refuses to add files inside the Git directory, so the real index stays
    /// untouched) and the diff read is read-only.
    ///
    /// Residual risks (recorded in `docs/quality/source-control-release-checklist.md`):
    /// the stage surface reports <c>Succeeded</c> for a silently ignored
    /// <c>.git</c> path (Git exit 0, no index change), and the diff surface
    /// resolves a contained <c>.git/config</c> path and can render its text
    /// because it reads the working-tree side directly. Both stay inside the
    /// authenticated privileged API and the worktree boundary; `.git/config`
    /// may contain remote URLs with embedded credentials, so callers must not
    /// request `.git` paths.
    /// </summary>
    [Fact]
    public async Task DotGitPath_IsNeverStaged_AndTheDiffReadIsReadOnly()
    {
        using SecurityGitRepository repo = SecurityGitRepository.Create();
        var stages = new GitStageService(new GitProcessRunner(), new GitRepositoryService(new GitProcessRunner()));
        var diffs = new GitDiffService(new GitProcessRunner());

        // Git ignores files inside its own directory: whatever the typed
        // outcome, the real index must not gain a `.git` entry.
        _ = await stages.StageAsync(repo.RootPath, ".git/config");
        string staged = SecurityGitRepository.RunGitCaptureUtf8(
            repo.RootPath,
            "diff",
            "--cached",
            "--name-only",
            "-z");
        Assert.DoesNotContain(".git", staged);

        // The path is contained (it resolves inside the worktree), so the read
        // is never an escape; it must leave the repository untouched.
        _ = await diffs.GetFileDiffAsync(repo.RootPath, ".git/config", GitDiffTarget.WorkingTree);

        Assert.Equal(0, SecurityGitRepository.RunGit(repo.RootPath, "fsck", "--no-progress", "--no-dangling"));
    }

    /// <summary>
    /// R-3 follow-up: a contained <c>.git</c> path is still not a document path.
    /// <c>.git/config</c> may carry a remote URL with embedded credentials, so
    /// the diff surface must reject it rather than render it. This is a document
    /// surface rule, not a containment rule - the path resolves inside the
    /// worktree, which is exactly why containment alone is not enough.
    /// </summary>
    [Fact]
    public async Task DotGitPath_IsRejectedByTheDiffSurface()
    {
        using SecurityGitRepository repo = SecurityGitRepository.Create();
        var diffs = new GitDiffService(new GitProcessRunner());

        GitDiffResult result = await diffs.GetFileDiffAsync(
            repo.RootPath,
            ".git/config",
            GitDiffTarget.WorkingTree);

        Assert.Equal(GitDiffStatus.PathRejected, result.Status);
        Assert.Null(result.Diff);
    }

    // =====================================================================
    // 4. Windows path and Unicode repository locations
    // =====================================================================

    [Fact]
    public async Task RepositoryPathWithSpacesUnicodeAndEmoji_StagesNormally()
    {
        string directoryName = $"repo \u00fc \u6f22\u5b57 \ud83d\udcc4 {Guid.NewGuid():N}";
        using SecurityGitRepository repo = SecurityGitRepository.Create(directoryName);
        repo.WriteFile("docs/\u00e9l\u00e9ment with space.md", "content");
        var stages = new GitStageService(new GitProcessRunner(), new GitRepositoryService(new GitProcessRunner()));

        GitStageResult result = await stages.StageAsync(
            repo.RootPath,
            "docs/\u00e9l\u00e9ment with space.md");

        Assert.Equal(GitStageOutcome.Succeeded, result.Outcome);
        Assert.Contains(
            "docs/\u00e9l\u00e9ment with space.md",
            SecurityGitRepository.RunGitCaptureUtf8(repo.RootPath, "diff", "--cached", "--name-only", "-z"));
    }

    // =====================================================================
    // 5. Credentials never leak into typed failures
    // =====================================================================

    /// <summary>
    /// Spec 13 §20/§23: a remote URL may embed credentials; a failed remote
    /// operation must never echo them into the typed result or its message.
    /// The remote is an unreachable localhost port, so no network is used.
    /// </summary>
    [Fact]
    public async Task FailedFetch_DoesNotExposeCredentialsFromTheRemoteUrl()
    {
        using SecurityGitRepository repo = SecurityGitRepository.Create();
        const string secret = "scm17-super-secret-token";
        Assert.Equal(
            0,
            SecurityGitRepository.RunGit(
                repo.RootPath,
                "remote",
                "add",
                "origin",
                $"https://user:{secret}@127.0.0.1:9/repo.git"));
        var runner = new GitProcessRunner();
        var repositories = new GitRepositoryService(runner);
        var remotes = new GitRemoteService(runner, repositories);

        GitFetchResult result = await remotes.FetchAsync(repo.RootPath);

        Assert.NotEqual(GitFetchOutcome.Succeeded, result.Outcome);
        Assert.DoesNotContain(secret, result.Message ?? string.Empty);
        string diagnostics = (result.Failure?.StandardOutput ?? string.Empty)
            + (result.Failure?.StandardError ?? string.Empty)
            + (result.Failure?.ErrorMessage ?? string.Empty);
        Assert.DoesNotContain(secret, diagnostics);
    }

    // =====================================================================
    // Helpers
    // =====================================================================

    /// <summary>
    /// Asserts the runner only ever received structured argument lists: no
    /// element is a shell command string that concatenates another element.
    /// </summary>
    private static void AssertNoShellTextWasEverConstructed(IReadOnlyList<GitProcessRequest> requests)
    {
        foreach (GitProcessRequest request in requests)
        {
            Assert.NotEmpty(request.Arguments);
            foreach (string argument in request.Arguments)
            {
                Assert.NotNull(argument);
                Assert.DoesNotContain("cmd.exe", argument, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("powershell", argument, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(" && ", argument, StringComparison.Ordinal);
                Assert.DoesNotContain(" || ", argument, StringComparison.Ordinal);
            }
        }
    }

    /// <summary>Disposable temp repository; setup uses raw structured Git.</summary>
    private sealed class SecurityGitRepository : IDisposable
    {
        private SecurityGitRepository(string rootPath)
        {
            RootPath = rootPath;
        }

        public string RootPath { get; }

        public static SecurityGitRepository Create(string? directoryName = null)
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "scm17-security",
                directoryName ?? $"repo-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            RunOrThrow(root, "init", "-q", ".");
            RunOrThrow(root, "checkout", "-b", "main");
            RunOrThrow(root, "config", "user.email", "scm17-security@example.invalid");
            RunOrThrow(root, "config", "user.name", "SCM-17 Security Tests");
            File.WriteAllText(Path.Combine(root, "baseline.md"), "baseline");
            RunOrThrow(root, "add", "-A");
            RunOrThrow(root, "commit", "-qm", "baseline");
            return new SecurityGitRepository(root);
        }

        public string WriteFile(string relativePath, string content)
        {
            string fullPath = Path.Combine(RootPath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return fullPath;
        }

        public static int RunGit(string workingDirectory, params string[] arguments)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            foreach (string argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using Process process = Process.Start(startInfo)!;
            process.StandardInput.Close();
            process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode;
        }

        public static string RunGitCaptureUtf8(string workingDirectory, params string[] arguments)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            foreach (string argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using Process process = Process.Start(startInfo)!;
            process.StandardInput.Close();
            string stdout = process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            process.WaitForExit();
            return stdout;
        }

        private static void RunOrThrow(string workingDirectory, params string[] arguments)
        {
            int exitCode = RunGit(workingDirectory, arguments);
            if (exitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Fixture `git {string.Join(' ', arguments)}` failed with exit code {exitCode}.");
            }
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(RootPath))
                {
                    Directory.Delete(RootPath, recursive: true);
                }
            }
            catch (Exception)
            {
                // Best effort: a locked .git file must not fail the test run.
            }
        }
    }

    /// <summary>Records every structured request while delegating to real Git.</summary>
    private sealed class RecordingGitProcessRunner : IGitProcessRunner
    {
        private readonly IGitProcessRunner _inner;
        private readonly List<GitProcessRequest> _requests = new();
        private readonly List<RecordedInvocation> _invocations = new();

        public RecordingGitProcessRunner(IGitProcessRunner inner)
        {
            _inner = inner;
        }

        public IReadOnlyList<GitProcessRequest> Requests
        {
            get
            {
                lock (_requests)
                {
                    return _requests.ToArray();
                }
            }
        }

        public IReadOnlyList<RecordedInvocation> Invocations
        {
            get
            {
                lock (_invocations)
                {
                    return _invocations.ToArray();
                }
            }
        }

        public Task<GitCapabilityInfo> GetCapabilityAsync(CancellationToken cancellationToken = default) =>
            _inner.GetCapabilityAsync(cancellationToken);

        public async Task<GitProcessResult> RunAsync(
            GitProcessRequest request,
            CancellationToken cancellationToken = default)
        {
            lock (_requests)
            {
                _requests.Add(request);
            }

            GitProcessResult result = await _inner.RunAsync(request, cancellationToken);
            lock (_invocations)
            {
                _invocations.Add(new RecordedInvocation(request, result));
            }

            return result;
        }
    }

    private sealed record RecordedInvocation(GitProcessRequest Request, GitProcessResult Result);

    private sealed class UnusedFolderSourceService : DocumentIDE.Core.Filesystem.IFolderSourceService
    {
        public Task<DocumentIDE.Core.Filesystem.AttachFolderResult> AttachFolderAsync(
            string workspaceId,
            string rawFolderPath) =>
            throw new NotSupportedException("The security suite never attaches a folder.");

        public Task<IReadOnlyList<DocumentIDE.Core.Domain.FolderSource>> GetFolderSourcesAsync(
            string workspaceId) =>
            Task.FromResult<IReadOnlyList<DocumentIDE.Core.Domain.FolderSource>>(
                Array.Empty<DocumentIDE.Core.Domain.FolderSource>());

        public int CountSupportedFiles(string canonicalPath) => 0;
    }
}
