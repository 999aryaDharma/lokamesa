using System;
using System.IO;
using System.Threading.Tasks;
using DocumentIDE.Core.Domain;
using DocumentIDE.Core.Filesystem;
using Xunit;

namespace DocumentIDE.Core.Tests.Filesystem;

public class MoveCorrelationTests
{
    [Fact]
    public void PhysicalMoveIntent_IsExpired_ReturnsTrueWhenPastTtl()
    {
        var started = DateTimeOffset.UtcNow.AddSeconds(-35);
        var intent = new PhysicalMoveIntent(
            IntentId: "intent-123",
            DocumentId: "doc-123",
            SourcePath: @"C:\source\file.md",
            DestinationPath: @"C:\dest\file.md",
            StartedAt: started,
            Ttl: TimeSpan.FromSeconds(30));

        Assert.True(intent.IsExpired(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void PhysicalMoveIntent_IsExpired_ReturnsFalseWithinTtl()
    {
        var started = DateTimeOffset.UtcNow;
        var intent = new PhysicalMoveIntent(
            IntentId: "intent-123",
            DocumentId: "doc-123",
            SourcePath: @"C:\source\file.md",
            DestinationPath: @"C:\dest\file.md",
            StartedAt: started,
            Ttl: TimeSpan.FromSeconds(30));

        Assert.False(intent.IsExpired(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void CandidateScoring_ExactFingerprintAndFilename_ScoresHigh()
    {
        var doc = new Document
        {
            Id = "doc-1",
            Filename = "test.md",
            SizeBytes = 100,
            ContentFingerprint = "abcd1234efgh5678",
            ModifiedAt = DateTime.UtcNow.ToString("O")
        };

        var score = MoveCorrelationScorer.ScoreCandidate(
            doc,
            candidateFilename: "test.md",
            candidateSizeBytes: 100,
            candidateFingerprint: "abcd1234efgh5678",
            candidateModifiedAt: DateTime.UtcNow);

        Assert.True(score.MatchScore >= 80);
        Assert.True(score.ExactFingerprintMatch);
        Assert.True(score.SameFilenameMatch);
    }

    [Fact]
    public void CandidateScoring_DifferentFingerprint_ReturnsZeroContentScore()
    {
        var doc = new Document
        {
            Id = "doc-1",
            Filename = "test.md",
            SizeBytes = 100,
            ContentFingerprint = "abcd1234efgh5678",
            ModifiedAt = DateTime.UtcNow.ToString("O")
        };

        var score = MoveCorrelationScorer.ScoreCandidate(
            doc,
            candidateFilename: "different_name.md",
            candidateSizeBytes: 100,
            candidateFingerprint: "different_fingerprint_hash",
            candidateModifiedAt: DateTime.UtcNow);

        Assert.False(score.ExactFingerprintMatch);
        Assert.True(score.MatchScore < 50);
    }

    [Fact]
    public void CandidateScoring_StemMatchAndSizeDelta_ScoresModerate()
    {
        var doc = new Document
        {
            Id = "doc-1",
            Filename = "report_v1.md",
            SizeBytes = 1000,
            ContentFingerprint = "hash1",
            ModifiedAt = DateTime.UtcNow.ToString("O")
        };

        var score = MoveCorrelationScorer.ScoreCandidate(
            doc,
            candidateFilename: "report_v1.docx",
            candidateSizeBytes: 1020, // size delta 20 <= 64
            candidateFingerprint: "hash2", // different fingerprint
            candidateModifiedAt: DateTime.UtcNow.AddSeconds(-30)); // mtime delta <= 60s

        Assert.False(score.ExactFingerprintMatch);
        Assert.False(score.SameFilenameMatch);
        Assert.True(score.MatchScore > 0);
    }

    [Fact]
    public void CandidateScoring_ExactFingerprintDifferentName_ScoresModerateOrHigh()
    {
        var doc = new Document
        {
            Id = "doc-1",
            Filename = "original_name.md",
            SizeBytes = 500,
            ContentFingerprint = "exact_content_hash_12345",
            ModifiedAt = DateTime.UtcNow.ToString("O")
        };

        var score = MoveCorrelationScorer.ScoreCandidate(
            doc,
            candidateFilename: "completely_new_name.md",
            candidateSizeBytes: 500,
            candidateFingerprint: "exact_content_hash_12345",
            candidateModifiedAt: DateTime.UtcNow);

        Assert.True(score.ExactFingerprintMatch);
        Assert.False(score.SameFilenameMatch);
        Assert.True(score.MatchScore >= 50);
        Assert.Equal("Content fingerprint match with different filename", score.Reason);
    }
}

