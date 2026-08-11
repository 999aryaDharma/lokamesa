namespace DocumentIDE.Core.Word;

public enum WordLaunchStatus
{
    Success,
    WordNotInstalled,
    DocumentNotFound,
    InvalidFileType,
    ComError
}

public class WordLaunchResult
{
    public bool Success { get; set; }
    public WordLaunchStatus Status { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public string? DocumentId { get; set; }
    public string? FilePath { get; set; }

    public static WordLaunchResult Ok(string documentId, string filePath) => new()
    {
        Success = true,
        Status = WordLaunchStatus.Success,
        DocumentId = documentId,
        FilePath = filePath
    };

    public static WordLaunchResult WordNotInstalled(string? message = null) => new()
    {
        Success = false,
        Status = WordLaunchStatus.WordNotInstalled,
        ErrorMessage = message ?? "Microsoft Word is required for this operation."
    };

    public static WordLaunchResult NotFound(string documentId, string message) => new()
    {
        Success = false,
        Status = WordLaunchStatus.DocumentNotFound,
        DocumentId = documentId,
        ErrorMessage = message
    };

    public static WordLaunchResult InvalidType(string documentId, string message) => new()
    {
        Success = false,
        Status = WordLaunchStatus.InvalidFileType,
        DocumentId = documentId,
        ErrorMessage = message
    };

    public static WordLaunchResult Failed(WordLaunchStatus status, string message, string? documentId = null, string? filePath = null) => new()
    {
        Success = false,
        Status = status,
        ErrorMessage = message,
        DocumentId = documentId,
        FilePath = filePath
    };
}
