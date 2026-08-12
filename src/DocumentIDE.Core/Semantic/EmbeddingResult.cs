namespace DocumentIDE.Core.Semantic;

public class EmbeddingResult
{
    public bool IsSuccess { get; }
    public DocumentEmbedding? Embedding { get; }
    public string? FailureReason { get; }
    public string? ErrorMessage { get; }

    private EmbeddingResult(bool isSuccess, DocumentEmbedding? embedding, string? failureReason, string? errorMessage)
    {
        IsSuccess = isSuccess;
        Embedding = embedding;
        FailureReason = failureReason;
        ErrorMessage = errorMessage;
    }

    public static EmbeddingResult Success(DocumentEmbedding embedding)
    {
        return new EmbeddingResult(true, embedding, null, null);
    }

    public static EmbeddingResult Failure(string reason, string message)
    {
        return new EmbeddingResult(false, null, reason, message);
    }

    public static EmbeddingResult Disabled()
    {
        return new EmbeddingResult(false, null, "Disabled", "Embedding provider is not enabled in settings.");
    }

    public static EmbeddingResult Unavailable()
    {
        return new EmbeddingResult(false, null, "Unavailable", "Embedding provider is currently unavailable.");
    }
}
