using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace DocumentIDE.IntegrationTests.SourceControl;

/// <summary>
/// SCM-01 test fixture: a disposable Git repository under the temp directory.
/// Repository setup uses raw <see cref="Process"/> invocations (not the code
/// under test) so the fixture stays independent of <c>GitProcessRunner</c>.
/// </summary>
internal sealed class TestGitRepository : IDisposable
{
    private TestGitRepository(string rootPath)
    {
        RootPath = rootPath;
    }

    public string RootPath { get; }

    /// <summary>
    /// Creates an initialized repository. <paramref name="directoryName"/>
    /// allows tests to control the on-disk path (e.g. spaces/Unicode).
    /// </summary>
    public static TestGitRepository Create(string? directoryName = null)
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "scm01",
            directoryName ?? $"repo-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        RunGitOrThrow(root, "init", "-q", ".");
        RunGitOrThrow(root, "config", "user.email", "scm01-tests@example.invalid");
        RunGitOrThrow(root, "config", "user.name", "SCM-01 Tests");

        return new TestGitRepository(root);
    }

    public string WriteFile(string relativePath, string content)
    {
        string fullPath = Path.Combine(RootPath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return fullPath;
    }

    /// <summary>Runs the system Git with structured arguments; returns exit code.</summary>
    public static int RunGit(string workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo)!;
        process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode;
    }

    private static void RunGitOrThrow(string workingDirectory, params string[] arguments)
    {
        int exitCode = RunGit(workingDirectory, arguments);
        if (exitCode != 0)
        {
            throw new InvalidOperationException(
                $"Test fixture `git {string.Join(' ', arguments)}` failed with exit code {exitCode}.");
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
