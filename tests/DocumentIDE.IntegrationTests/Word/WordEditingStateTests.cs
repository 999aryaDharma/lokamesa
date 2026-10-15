using System;
using System.IO;
using System.Linq;
using DocumentIDE.Infrastructure.Word;
using Xunit;

namespace DocumentIDE.IntegrationTests.Word;

/// <summary>
/// WORD-02 — Word Editing/Lock Detection.
/// Red stage: this test must FAIL before WordEditingStateDetector exists.
/// Acceptance: no single heuristic is treated as infallible (per-signal
/// breakdown + confidence); open docs expose "Editing in Word" state and
/// batch preflight can block/skip them.
/// </summary>
public sealed class WordEditingStateTests : IDisposable
{
    private readonly string _tempDir;

    public WordEditingStateTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "word-editing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // Best effort temp cleanup.
        }
    }

    private string CreateDocx(string name = "Report.docx")
    {
        string path = Path.Combine(_tempDir, name);
        File.WriteAllBytes(path, new byte[] { 0x50, 0x4B, 0x03, 0x04 });
        return path;
    }

    [Fact]
    public void OpenWordDoc_ShowsEditingState()
    {
        string path = CreateDocx();
        // Word owner-lock convention: "~$" + remainder of the file name.
        File.WriteAllText(Path.Combine(_tempDir, "~$eport.docx"), "owner");

        var detector = new WordEditingStateDetector();
        WordEditingState state = detector.Check(path);

        Assert.True(state.IsEditing);
        Assert.Contains(state.Signals, s => s.Name == WordEditingStateDetector.OwnerLockFileSignal && s.Positive);
    }

    [Fact]
    public void ClosedWordDoc_ClearsState()
    {
        string path = CreateDocx();
        string ownerFile = Path.Combine(_tempDir, "~$eport.docx");
        File.WriteAllText(ownerFile, "owner");

        var detector = new WordEditingStateDetector();
        Assert.True(detector.Refresh(path).IsEditing);

        File.Delete(ownerFile);
        bool changed = false;
        detector.StateChanged += (_, _) => changed = true;

        WordEditingState cleared = detector.Refresh(path);

        Assert.False(cleared.IsEditing);
        Assert.True(changed);
    }

    [Fact]
    public void BatchPreflight_CanBlockOpenDoc()
    {
        string openPath = CreateDocx("Open.docx");
        File.WriteAllText(Path.Combine(_tempDir, "~$pen.docx"), "owner");
        string closedPath = CreateDocx("Closed.docx");

        var detector = new WordEditingStateDetector();

        WordMutationPreflightResult blocked = detector.CheckMutationPreflight(openPath);
        Assert.False(blocked.Allowed);
        Assert.Equal(WordPreflightDecision.BlockedEditingInWord, blocked.Decision);

        WordMutationPreflightResult allowed = detector.CheckMutationPreflight(closedPath);
        Assert.True(allowed.Allowed);
        Assert.Equal(WordPreflightDecision.Allowed, allowed.Decision);
    }

    [Fact]
    public void ExclusiveAccessFailure_IsIndependentSignal()
    {
        string path = CreateDocx();
        using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var detector = new WordEditingStateDetector();
        WordEditingState state = detector.Check(path);

        Assert.True(state.IsEditing);
        Assert.Contains(state.Signals, s => s.Name == WordEditingStateDetector.ExclusiveAccessSignal && s.Positive);
        // One positive heuristic alone yields low confidence — never certainty.
        Assert.Equal(WordEditingConfidence.Low, state.Confidence);
    }

    [Fact]
    public void AutomationProbe_ContributesSignal()
    {
        string path = CreateDocx();

        var detector = new WordEditingStateDetector(automationOpenProbe: _ => true);
        WordEditingState state = detector.Check(path);

        Assert.True(state.IsEditing);
        Assert.Contains(state.Signals, s => s.Name == WordEditingStateDetector.AutomationOpenSignal && s.Positive);
    }

    [Fact]
    public void MultipleSignals_RaiseConfidence()
    {
        string path = CreateDocx();
        File.WriteAllText(Path.Combine(_tempDir, "~$eport.docx"), "owner");

        var detector = new WordEditingStateDetector(automationOpenProbe: _ => true);
        WordEditingState state = detector.Check(path);

        Assert.True(state.IsEditing);
        Assert.True(state.Signals.Count(s => s.Positive) >= 2);
        Assert.Equal(WordEditingConfidence.High, state.Confidence);
    }

    [Fact]
    public void MissingFile_PreflightReportsMissing()
    {
        var detector = new WordEditingStateDetector();
        WordMutationPreflightResult result = detector.CheckMutationPreflight(
            Path.Combine(_tempDir, "Gone.docx"));

        Assert.False(result.Allowed);
        Assert.Equal(WordPreflightDecision.FileMissing, result.Decision);
    }
}
