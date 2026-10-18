using System;
using System.IO;
using DocumentIDE.Core.Domain;

namespace DocumentIDE.Core.Filesystem;

public static class MoveCorrelationScorer
{
    public static MoveCandidateScore ScoreCandidate(
        Document document,
        string candidateFilename,
        long candidateSizeBytes,
        string candidateFingerprint,
        DateTime candidateModifiedAt,
        string candidatePath = "")
    {
        int score = 0;
        bool exactFingerprint = false;
        bool sameFilename = false;

        // 1. Content Fingerprint (+50 pts)
        if (!string.IsNullOrEmpty(document.ContentFingerprint) &&
            !string.IsNullOrEmpty(candidateFingerprint) &&
            string.Equals(document.ContentFingerprint, candidateFingerprint, StringComparison.OrdinalIgnoreCase))
        {
            score += 50;
            exactFingerprint = true;
        }

        // 2. Filename (+25 pts exact, +10 pts stem match)
        if (!string.IsNullOrEmpty(document.Filename) && !string.IsNullOrEmpty(candidateFilename))
        {
            if (string.Equals(document.Filename, candidateFilename, StringComparison.OrdinalIgnoreCase))
            {
                score += 25;
                sameFilename = true;
            }
            else
            {
                string docStem = Path.GetFileNameWithoutExtension(document.Filename);
                string candStem = Path.GetFileNameWithoutExtension(candidateFilename);
                if (string.Equals(docStem, candStem, StringComparison.OrdinalIgnoreCase))
                {
                    score += 10;
                }
            }
        }

        // 3. SizeBytes (+15 pts exact, +5 pts delta <= 64 bytes)
        long sizeDelta = Math.Abs(document.SizeBytes - candidateSizeBytes);
        if (sizeDelta == 0)
        {
            score += 15;
        }
        else if (sizeDelta <= 64)
        {
            score += 5;
        }

        // 4. ModifiedAt (+10 pts <= 2s, +5 pts <= 60s)
        if (DateTime.TryParse(document.ModifiedAt, out var docModifiedAt))
        {
            double secondsDelta = Math.Abs((docModifiedAt.ToUniversalTime() - candidateModifiedAt.ToUniversalTime()).TotalSeconds);
            if (secondsDelta <= 2.0)
            {
                score += 10;
            }
            else if (secondsDelta <= 60.0)
            {
                score += 5;
            }
        }

        CandidateMatchConfidence confidence = score switch
        {
            >= 80 => CandidateMatchConfidence.HighConfidence,
            >= 50 => CandidateMatchConfidence.ModerateConfidence,
            >= 20 => CandidateMatchConfidence.LowConfidence,
            _ => CandidateMatchConfidence.Ambiguous
        };

        string reason = (exactFingerprint, sameFilename) switch
        {
            (true, true) => "Exact content fingerprint and filename match",
            (true, false) => "Content fingerprint match with different filename",
            (false, true) => "Same filename match but content fingerprint differs",
            _ => "Partial attribute match"
        };

        return new MoveCandidateScore(
            Document: document,
            MatchScore: score,
            Confidence: confidence,
            IsAmbiguous: score < 75,
            Reason: reason,
            ExactFingerprintMatch: exactFingerprint,
            SameFilenameMatch: sameFilename,
            CandidatePath: candidatePath
        );
    }
}
