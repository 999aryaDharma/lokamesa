using System;
using System.Threading;
using System.Threading.Tasks;

namespace DocumentIDE.Core.Jobs;

public enum JobState
{
    Pending,
    Running,
    Completed,
    Cancelled,
    Failed
}

public static class JobTypes
{
    public const string Indexing = "Indexing";
    public const string PreviewGeneration = "PreviewGeneration";
    public const string PdfExport = "PdfExport";
    public const string Reconciliation = "Reconciliation";
    public const string DocumentMutation = "DocumentMutation";
}

public class JobContext
{
    public string JobId { get; }
    public string JobType { get; }
    public string DocumentId { get; }
    public string? Fingerprint { get; }
    public CancellationToken CancellationToken { get; }
    private readonly Action<double, string?> _reportProgress;

    public JobContext(
        string jobId,
        string jobType,
        string documentId,
        string? fingerprint,
        CancellationToken cancellationToken,
        Action<double, string?> reportProgress)
    {
        JobId = jobId;
        JobType = jobType;
        DocumentId = documentId;
        Fingerprint = fingerprint;
        CancellationToken = cancellationToken;
        _reportProgress = reportProgress;
    }

    public void ReportProgress(double percentage, string? statusMessage = null)
    {
        _reportProgress(percentage, statusMessage);
    }
}

public class JobInfo
{
    public string JobId { get; set; } = string.Empty;
    public string JobType { get; set; } = string.Empty;
    public string DocumentId { get; set; } = string.Empty;
    public string? Fingerprint { get; set; }
    public JobState State { get; set; } = JobState.Pending;
    public double ProgressPercentage { get; set; } = 0.0;
    public string? StatusMessage { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public string? ErrorDetails { get; set; }
    public bool IsMutationJob { get; set; } = false;
    public bool IsIdempotent { get; set; } = true;
}

public interface IJob
{
    Task ExecuteAsync(JobContext context);
}

public class DelegateJob : IJob
{
    private readonly Func<JobContext, Task> _work;

    public DelegateJob(Func<JobContext, Task> work)
    {
        _work = work ?? throw new ArgumentNullException(nameof(work));
    }

    public Task ExecuteAsync(JobContext context)
    {
        return _work(context);
    }
}
