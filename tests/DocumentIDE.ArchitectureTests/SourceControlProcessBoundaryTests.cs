using System.Text.RegularExpressions;
using Xunit;

namespace DocumentIDE.ArchitectureTests;

/// <summary>
/// SCM-01 — architecture guards for the controlled Git process boundary
/// (spec 13 §19/§23):
///
/// - the browser frontend has no process-invocation surface at all,
/// - in-process Git spawning exists only under Infrastructure/SourceControl
///   (plus the documented Word desktop shell-execute launcher),
/// - Core carries only contracts,
/// - the API composition root is the only place that wires the runner.
/// </summary>
public sealed class SourceControlProcessBoundaryTests
{
    private static readonly string RepoRoot = FindRepoRoot(AppDomain.CurrentDomain.BaseDirectory);

    private static readonly string[] FrontendTextExtensions =
    {
        ".ts", ".tsx", ".js", ".jsx", ".mjs", ".cjs", ".html", ".json", ".css", ".md",
    };

    private static readonly string[] FrontendSkippedDirectories =
    {
        "node_modules", "dist", "build", ".vite", "bin", "obj",
    };

    /// <summary>Primitives that would let browser code spawn a process.</summary>
    private static readonly Regex[] FrontendProcessMarkers =
    {
        new(@"child_process", RegexOptions.IgnoreCase),
        new(@"execFile\s*\(", RegexOptions.IgnoreCase),
        new(@"execSync\s*\(", RegexOptions.IgnoreCase),
        new(@"spawnSync\s*\(", RegexOptions.IgnoreCase),
        new(@"\bspawn\s*\(", RegexOptions.IgnoreCase),
        new(@"process\.exec\s*\(", RegexOptions.IgnoreCase),
        new(@"git\.exe", RegexOptions.IgnoreCase),
        new(@"shell\s*:\s*true", RegexOptions.IgnoreCase),
    };

    [Fact]
    public void WebFrontend_HasNoProcessInvocationOrGitExecutableSurface()
    {
        string webRoot = Path.Combine(RepoRoot, "src", "DocumentIDE.Web");
        Assert.True(Directory.Exists(webRoot), $"Expected web project at {webRoot}");

        var violations = new List<string>();
        foreach (string file in EnumerateFiles(webRoot, FrontendTextExtensions, FrontendSkippedDirectories))
        {
            string content = File.ReadAllText(file);
            foreach (Regex marker in FrontendProcessMarkers)
            {
                if (marker.IsMatch(content))
                {
                    violations.Add($"{Relative(file)} matched /{marker}/");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "The browser frontend must never gain a direct process/exec surface:" + Environment.NewLine +
            string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void CSharpProcessSpawning_IsConfinedToInfrastructureSourceControl_AndTheDocumentedWordLauncher()
    {
        var allowed = new[]
        {
            "src/DocumentIDE.Infrastructure/SourceControl/",
            "src/DocumentIDE.Infrastructure/Word/WordDesktopLauncher.cs",
            // B5 — the public-share path needs its own documented process
            // boundary: it launches the separate DocumentIDE.ShareHost viewer
            // host and the ngrok tunnel. Neither is Git spawning or a
            // general-purpose exec surface; both are named, single-purpose files.
            "src/DocumentIDE.Infrastructure/Sharing/NgrokProcessRunner.cs",
            "src/DocumentIDE.Infrastructure/Sharing/ShareHostProcess.cs",
        };

        var violations = new List<string>();
        foreach (string file in EnumerateCSharpFiles(Path.Combine(RepoRoot, "src")))
        {
            string content = File.ReadAllText(file);
            if (content.Contains("ProcessStartInfo", StringComparison.Ordinal)
                || content.Contains("Process.Start(", StringComparison.Ordinal))
            {
                string relative = Relative(file);
                if (!allowed.Any(a => relative.StartsWith(a, StringComparison.Ordinal)
                                      || relative.Equals(a, StringComparison.Ordinal)))
                {
                    violations.Add(relative);
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "Process spawning appeared outside the controlled Git runner (and the " +
            "documented Word desktop launcher). Route it through " +
            "IGitProcessRunner in Infrastructure/SourceControl instead:" + Environment.NewLine +
            string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void GitSpawnMarkers_InCSharpSources_LiveOnlyUnderSourceControl()
    {
        var violations = new List<string>();
        foreach (string file in EnumerateCSharpFiles(Path.Combine(RepoRoot, "src")))
        {
            string relative = Relative(file);
            if (relative.StartsWith("src/DocumentIDE.Infrastructure/SourceControl/", StringComparison.Ordinal))
            {
                continue;
            }

            // A literal git executable chosen as a process FileName would be
            // the only way to spawn Git outside the runner; `git.exe` used in
            // prose/comments is tolerated, so match the assignment shape.
            var gitFileNameAssignment = new Regex(
                @"FileName\s*=\s*[^;]*\bgit(\.exe)?\b|Process\.Start\s*\(\s*""git",
                RegexOptions.IgnoreCase);

            if (gitFileNameAssignment.IsMatch(File.ReadAllText(file)))
            {
                violations.Add(relative);
            }
        }

        Assert.True(
            violations.Count == 0,
            "Git must only be spawned by GitProcessRunner under " +
            "Infrastructure/SourceControl:" + Environment.NewLine +
            string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Core_SourceControlContracts_ContainNoProcessSpawning()
    {
        string contractsRoot = Path.Combine(RepoRoot, "src", "DocumentIDE.Core", "SourceControl");
        Assert.True(Directory.Exists(contractsRoot), $"Expected contracts at {contractsRoot}");

        var violations = new List<string>();
        foreach (string file in EnumerateCSharpFiles(contractsRoot))
        {
            string content = File.ReadAllText(file);
            if (content.Contains("ProcessStartInfo", StringComparison.Ordinal)
                || content.Contains("Process.Start(", StringComparison.Ordinal)
                || content.Contains("System.Diagnostics.Process", StringComparison.Ordinal))
            {
                violations.Add(Relative(file));
            }
        }

        Assert.True(
            violations.Count == 0,
            "Core holds Git contracts only; process spawning is an Infrastructure concern:" +
            Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void ApiCompositionRoot_IsTheOnlyCSharpPlaceWiringTheGitRunner()
    {
        string programPath = Path.Combine(RepoRoot, "src", "DocumentIDE.Api", "Program.cs");
        Assert.True(File.Exists(programPath), $"Expected composition root at {programPath}");

        Assert.Contains("IGitProcessRunner", File.ReadAllText(programPath), StringComparison.Ordinal);

        var runnerReference = new Regex(@"\bGitProcessRunner\b");
        var violations = new List<string>();
        foreach (string file in EnumerateCSharpFiles(Path.Combine(RepoRoot, "src")))
        {
            string relative = Relative(file);
            if (relative.StartsWith("src/DocumentIDE.Infrastructure/SourceControl/", StringComparison.Ordinal)
                || relative.Equals("src/DocumentIDE.Api/Program.cs", StringComparison.Ordinal))
            {
                continue;
            }

            if (runnerReference.IsMatch(File.ReadAllText(file)))
            {
                violations.Add(relative);
            }
        }

        Assert.True(
            violations.Count == 0,
            "Only the composition root may name the concrete GitProcessRunner type:" +
            Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    private static IEnumerable<string> EnumerateCSharpFiles(string root) =>
        EnumerateFiles(root, new[] { ".cs" }, new[] { "bin", "obj", "node_modules" });

    private static IEnumerable<string> EnumerateFiles(
        string root, IReadOnlyCollection<string> extensions, IReadOnlyCollection<string> skippedDirectories)
    {
        foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            string[] segments = file.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (segments.Any(s => skippedDirectories.Contains(s, StringComparer.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
            {
                yield return file;
            }
        }
    }

    private static string Relative(string file) =>
        Path.GetRelativePath(RepoRoot, file).Replace(Path.DirectorySeparatorChar, '/');

    private static string FindRepoRoot(string startDir)
    {
        var dir = new DirectoryInfo(startDir);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "DocumentIDE.sln")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not find repository root containing DocumentIDE.sln");
    }
}
