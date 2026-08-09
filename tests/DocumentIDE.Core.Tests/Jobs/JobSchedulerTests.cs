using System;
using System.Threading;
using System.Threading.Tasks;
using DocumentIDE.Core.Jobs;
using DocumentIDE.Infrastructure.Jobs;
using Xunit;

namespace DocumentIDE.Core.Tests.Jobs;

public class JobSchedulerTests
{
    [Fact]
    public async Task Duplicate_Indexing_Jobs_Collapse()
    {
        using var scheduler = new LocalJobScheduler(maxDegreeOfParallelism: 1);

        var tcs = new TaskCompletionSource<bool>();
        var blockJob = new DelegateJob(async ctx =>
        {
            await tcs.Task;
        });

        var handle1 = await scheduler.EnqueueJobAsync("Indexing", "doc1", "fp123", blockJob);
        var handle2 = await scheduler.EnqueueJobAsync("Indexing", "doc1", "fp123", blockJob);

        Assert.Equal(handle1.JobId, handle2.JobId);

        tcs.SetResult(true);
    }

    [Fact]
    public async Task Cancellation_Stops_Job_At_Safe_Boundary()
    {
        using var scheduler = new LocalJobScheduler(maxDegreeOfParallelism: 2);

        var started = new TaskCompletionSource<bool>();

        var job = new DelegateJob(async ctx =>
        {
            started.SetResult(true);
            for (int i = 0; i < 100; i++)
            {
                ctx.CancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(50, CancellationToken.None);
                ctx.CancellationToken.ThrowIfCancellationRequested();
            }
        });

        var handle = await scheduler.EnqueueJobAsync("Indexing", "doc_cancel", "fp1", job);
        await started.Task;

        var cancelled = scheduler.CancelJob(handle.JobId);
        Assert.True(cancelled);

        for (int i = 0; i < 20; i++)
        {
            var status = scheduler.GetJob(handle.JobId);
            if (status?.State == JobState.Cancelled)
            {
                break;
            }
            await Task.Delay(50);
        }

        var finalStatus = scheduler.GetJob(handle.JobId);
        Assert.Equal(JobState.Cancelled, finalStatus?.State);
    }

    [Fact]
    public async Task Two_Mutation_Jobs_For_Same_Document_Execute_Sequentially()
    {
        using var scheduler = new LocalJobScheduler(maxDegreeOfParallelism: 4);

        int activeMutations = 0;
        int maxSimultaneousMutations = 0;
        var lockObj = new object();

        Func<JobContext, Task> mutationWork = async ctx =>
        {
            lock (lockObj)
            {
                activeMutations++;
                if (activeMutations > maxSimultaneousMutations)
                {
                    maxSimultaneousMutations = activeMutations;
                }
            }

            await Task.Delay(100);

            lock (lockObj)
            {
                activeMutations--;
            }
        };

        var job1 = new DelegateJob(mutationWork);
        var job2 = new DelegateJob(mutationWork);

        var h1 = await scheduler.EnqueueJobAsync("DocumentMutation", "same_doc", "fp_m1", job1, isMutation: true);
        var h2 = await scheduler.EnqueueJobAsync("DocumentMutation", "same_doc", "fp_m2", job2, isMutation: true);

        for (int i = 0; i < 40; i++)
        {
            var s1 = scheduler.GetJob(h1.JobId)?.State;
            var s2 = scheduler.GetJob(h2.JobId)?.State;
            if (s1 == JobState.Completed && s2 == JobState.Completed)
            {
                break;
            }
            await Task.Delay(50);
        }

        Assert.Equal(JobState.Completed, scheduler.GetJob(h1.JobId)?.State);
        Assert.Equal(JobState.Completed, scheduler.GetJob(h2.JobId)?.State);
        Assert.Equal(1, maxSimultaneousMutations);
    }

    [Fact]
    public async Task Job_Progress_And_Status_Exposed()
    {
        using var scheduler = new LocalJobScheduler(maxDegreeOfParallelism: 2);

        double lastProgress = 0;
        string? lastMsg = null;

        scheduler.OnJobProgressChanged += (sender, info) =>
        {
            if (info.DocumentId == "doc_prog")
            {
                lastProgress = info.ProgressPercentage;
                lastMsg = info.StatusMessage;
            }
        };

        var job = new DelegateJob(async ctx =>
        {
            ctx.ReportProgress(50.0, "Halfway done");
            await Task.Delay(50);
            ctx.ReportProgress(100.0, "All done");
        });

        var handle = await scheduler.EnqueueJobAsync("PreviewGeneration", "doc_prog", "fp_p", job);

        for (int i = 0; i < 20; i++)
        {
            var s = scheduler.GetJob(handle.JobId);
            if (s?.State == JobState.Completed)
            {
                break;
            }
            await Task.Delay(50);
        }

        var finalJob = scheduler.GetJob(handle.JobId);
        Assert.Equal(JobState.Completed, finalJob?.State);
        Assert.Equal(100.0, lastProgress);
        Assert.Equal("All done", lastMsg);
    }
}
