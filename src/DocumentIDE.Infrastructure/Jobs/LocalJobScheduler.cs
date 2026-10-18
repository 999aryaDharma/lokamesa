using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using DocumentIDE.Core.Jobs;

namespace DocumentIDE.Infrastructure.Jobs;

public class LocalJobScheduler : IJobScheduler, IDisposable
{
    private readonly Channel<JobWorkItem> _queue;
    private readonly ConcurrentDictionary<string, JobWorkItem> _jobsById = new();
    private readonly ConcurrentDictionary<(string JobType, string DocumentId, string Fingerprint), string> _dedupMap = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _documentLocks = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _ctsMap = new();
    private readonly List<Task> _workers = new();
    private readonly CancellationTokenSource _shutdownCts = new();

    public event EventHandler<JobInfo>? OnJobProgressChanged;

    private class JobWorkItem
    {
        public JobInfo Info { get; }
        public IJob Job { get; }

        public JobWorkItem(JobInfo info, IJob job)
        {
            Info = info;
            Job = job;
        }
    }

    public LocalJobScheduler(int maxDegreeOfParallelism = 4, int queueCapacity = 1000)
    {
        var options = new BoundedChannelOptions(queueCapacity)
        {
            SingleReader = false,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        };
        _queue = Channel.CreateBounded<JobWorkItem>(options);

        for (int i = 0; i < maxDegreeOfParallelism; i++)
        {
            _workers.Add(Task.Run(() => WorkerLoopAsync(_shutdownCts.Token)));
        }
    }

    public async Task<JobInfo> EnqueueJobAsync(
        string jobType,
        string documentId,
        string? fingerprint,
        IJob job,
        bool isMutation = false)
    {
        if (job == null) throw new ArgumentNullException(nameof(job));

        var fpKey = fingerprint ?? string.Empty;
        var dedupKey = (jobType, documentId, fpKey);

        if (_dedupMap.TryGetValue(dedupKey, out var existingJobId))
        {
            if (_jobsById.TryGetValue(existingJobId, out var existingItem))
            {
                if (existingItem.Info.State == JobState.Pending || existingItem.Info.State == JobState.Running)
                {
                    return existingItem.Info;
                }
            }
        }

        var jobId = Guid.NewGuid().ToString();
        var info = new JobInfo
        {
            JobId = jobId,
            JobType = jobType,
            DocumentId = documentId,
            Fingerprint = fingerprint,
            State = JobState.Pending,
            IsMutationJob = isMutation,
            IsIdempotent = !isMutation
        };

        var workItem = new JobWorkItem(info, job);
        _jobsById[jobId] = workItem;
        _ctsMap[jobId] = new CancellationTokenSource();
        _dedupMap[dedupKey] = jobId;

        await _queue.Writer.WriteAsync(workItem);
        OnJobProgressChanged?.Invoke(this, info);

        return info;
    }

    public bool CancelJob(string jobId)
    {
        if (_jobsById.TryGetValue(jobId, out var item))
        {
            if (item.Info.State == JobState.Pending || item.Info.State == JobState.Running)
            {
                item.Info.State = JobState.Cancelled;
                item.Info.CompletedAt = DateTime.UtcNow;
                if (_ctsMap.TryGetValue(jobId, out var cts))
                {
                    try { cts.Cancel(); } catch { }
                }
                OnJobProgressChanged?.Invoke(this, item.Info);
                return true;
            }
        }
        return false;
    }

    public JobInfo? GetJob(string jobId)
    {
        if (_jobsById.TryGetValue(jobId, out var item))
        {
            return item.Info;
        }
        return null;
    }

    public IReadOnlyCollection<JobInfo> GetActiveJobs()
    {
        return _jobsById.Values
            .Select(v => v.Info)
            .Where(i => i.State == JobState.Pending || i.State == JobState.Running)
            .ToList();
    }

    private async Task WorkerLoopAsync(CancellationToken shutdownToken)
    {
        while (!shutdownToken.IsCancellationRequested)
        {
            JobWorkItem workItem;
            try
            {
                workItem = await _queue.Reader.ReadAsync(shutdownToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            var info = workItem.Info;

            if (info.State == JobState.Cancelled)
            {
                continue;
            }

            if (!_ctsMap.TryGetValue(info.JobId, out var cts))
            {
                cts = new CancellationTokenSource();
            }

            if (cts.IsCancellationRequested)
            {
                info.State = JobState.Cancelled;
                info.CompletedAt = DateTime.UtcNow;
                OnJobProgressChanged?.Invoke(this, info);
                continue;
            }

            info.State = JobState.Running;
            OnJobProgressChanged?.Invoke(this, info);

            SemaphoreSlim? docLock = null;
            if (info.IsMutationJob)
            {
                docLock = _documentLocks.GetOrAdd(info.DocumentId, _ => new SemaphoreSlim(1, 1));
                try
                {
                    await docLock.WaitAsync(cts.Token);
                }
                catch (OperationCanceledException)
                {
                    info.State = JobState.Cancelled;
                    info.CompletedAt = DateTime.UtcNow;
                    OnJobProgressChanged?.Invoke(this, info);
                    continue;
                }
            }

            try
            {
                var context = new JobContext(
                    info.JobId,
                    info.JobType,
                    info.DocumentId,
                    info.Fingerprint,
                    cts.Token,
                    (progress, msg) =>
                    {
                        info.ProgressPercentage = progress;
                        if (msg != null) info.StatusMessage = msg;
                        OnJobProgressChanged?.Invoke(this, info);
                    });

                await workItem.Job.ExecuteAsync(context);

                if (info.State == JobState.Running)
                {
                    info.State = JobState.Completed;
                    info.ProgressPercentage = 100.0;
                    info.CompletedAt = DateTime.UtcNow;
                }
            }
            catch (OperationCanceledException)
            {
                info.State = JobState.Cancelled;
                info.CompletedAt = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                info.State = JobState.Failed;
                info.ErrorDetails = ex.ToString();
                info.CompletedAt = DateTime.UtcNow;
            }
            finally
            {
                if (docLock != null)
                {
                    docLock.Release();
                }
                OnJobProgressChanged?.Invoke(this, info);
            }
        }
    }

    public void Dispose()
    {
        _shutdownCts.Cancel();
        _queue.Writer.TryComplete();
        foreach (var cts in _ctsMap.Values)
        {
            try { cts.Cancel(); cts.Dispose(); } catch { }
        }
        _shutdownCts.Dispose();
    }
}
