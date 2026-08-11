using System.Collections.Generic;

namespace DocumentIDE.Core.Docx;

public class DocxPreflightOptions
{
    public long MaxCompressedSizeBytes { get; set; } = 100L * 1024 * 1024; // 100 MiB
    public int MaxEntryCount { get; set; } = 10000;
    public long MaxTotalUncompressedSizeBytes { get; set; } = 512L * 1024 * 1024; // 512 MiB
    public long MaxPartUncompressedSizeBytes { get; set; } = 128L * 1024 * 1024; // 128 MiB
}

public enum DocxPreflightStatus
{
    Valid,
    SkippedForSafety,
    Corrupted
}

public enum DocxPreflightErrorCode
{
    None,
    FileNotFound,
    FileTooLarge,
    TooManyEntries,
    TotalUncompressedSizeExceeded,
    PartSizeExceeded,
    InvalidZipArchive,
    ZipSlipAttempt,
    MissingContentTypes,
    MissingRootRels,
    MissingMainDocument,
    DtdProhibited,
    XmlMalformed
}

public record DocxRelationshipInfo(
    string Id,
    string Type,
    string Target,
    string TargetMode = "Internal"
);

public record DocxPackageMetrics(
    long CompressedSizeBytes = 0,
    int EntryCount = 0,
    long TotalUncompressedSizeBytes = 0,
    int PartCount = 0,
    int MediaCount = 0
);

public class DocxPreflightResult
{
    public bool IsValid => Status == DocxPreflightStatus.Valid;
    public DocxPreflightStatus Status { get; init; } = DocxPreflightStatus.Valid;
    public DocxPreflightErrorCode ErrorCode { get; init; } = DocxPreflightErrorCode.None;
    public string? ErrorMessage { get; init; }
    public DocxPackageMetrics Metrics { get; init; } = new();
    public IReadOnlyList<DocxRelationshipInfo> Relationships { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];

    public static DocxPreflightResult Success(
        DocxPackageMetrics metrics,
        IReadOnlyList<DocxRelationshipInfo> relationships,
        IReadOnlyList<string> warnings)
    {
        return new DocxPreflightResult
        {
            Status = DocxPreflightStatus.Valid,
            ErrorCode = DocxPreflightErrorCode.None,
            Metrics = metrics,
            Relationships = relationships,
            Warnings = warnings
        };
    }

    public static DocxPreflightResult Reject(
        DocxPreflightErrorCode errorCode,
        string errorMessage,
        DocxPackageMetrics? metrics = null)
    {
        return new DocxPreflightResult
        {
            Status = DocxPreflightStatus.SkippedForSafety,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            Metrics = metrics ?? new DocxPackageMetrics()
        };
    }

    public static DocxPreflightResult Corrupted(
        DocxPreflightErrorCode errorCode,
        string errorMessage,
        DocxPackageMetrics? metrics = null)
    {
        return new DocxPreflightResult
        {
            Status = DocxPreflightStatus.Corrupted,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            Metrics = metrics ?? new DocxPackageMetrics()
        };
    }
}
