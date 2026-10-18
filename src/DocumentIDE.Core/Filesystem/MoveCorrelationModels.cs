using System;
using System.Collections.Generic;
using DocumentIDE.Core.Domain;

namespace DocumentIDE.Core.Filesystem;

public enum CandidateMatchConfidence
{
    ExactNTFSIdentity = 100,
    ExactJournalMatch = 95,
    HighConfidence = 80,
    ModerateConfidence = 50,
    LowConfidence = 20,
    Ambiguous = 0
}

public record PhysicalMoveIntent(
    string IntentId,
    string DocumentId,
    string SourcePath,
    string DestinationPath,
    DateTimeOffset StartedAt,
    TimeSpan Ttl)
{
    public bool IsExpired(DateTimeOffset now) => now > StartedAt.Add(Ttl);
}

public record MoveCandidateScore(
    Document Document,
    int MatchScore,
    CandidateMatchConfidence Confidence,
    bool IsAmbiguous,
    string Reason,
    bool ExactFingerprintMatch,
    bool SameFilenameMatch,
    string CandidatePath
);

public record RebindCandidateDto(
    string TargetPath,
    string Filename,
    long SizeBytes,
    string ModifiedAt,
    string ContentFingerprint,
    double MatchScore,
    bool ExactFingerprintMatch,
    bool SameFilenameMatch,
    string MatchReason,
    bool IsAmbiguous
);

public record RebindCandidateListDto(
    string DocumentId,
    string LastKnownPath,
    bool IsAmbiguous,
    List<RebindCandidateDto> Candidates
);

public record ConfirmRebindRequest(
    string TargetPath,
    bool ForceRebind = false
);

public record RebindResultDto(
    string DocumentId,
    string PreviousPath,
    string NewPath,
    string AvailabilityStatus,
    bool Success,
    string? ErrorMessage = null
);

public record MoveDocumentRequest(
    string DestinationPath
);

public record MoveDocumentResultDto(
    string DocumentId,
    string SourcePath,
    string DestinationPath,
    bool Success,
    string? ErrorMessage = null
);
