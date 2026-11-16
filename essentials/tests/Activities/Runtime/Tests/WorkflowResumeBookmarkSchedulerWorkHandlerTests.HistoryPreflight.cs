using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

public sealed partial class WorkflowResumeBookmarkSchedulerWorkHandlerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Exact_queued_resume_with_required_history_module_removed_defers_before_consuming_the_wait_or_activating_the_callback(bool required)
    {
        var (work, clock) = await PrepareExactResume();
        var workflow = (await _exactWorkflowStates!.FindAsync("wfexec-1"))!;
        await _exactWorkflowStates.SaveAsync(workflow with
        {
            HistoryCapturePolicy = required ? new("runtime-history-v3", 3, true, ["ActivityContinuationStarted"]) : null
        });
        var original = (await _activityStateStore.FindAsync("wfexec-1", "actexec-1"))!;
        var generation = (await _bookmarkStateStore.ObserveAsync("wfexec-1", "bookmark-1"))!.Generation;
        var activator = new StatefulResumeActivator();
        var observer = new RecordingBookmarkObserver();
        var poison = new InMemoryWorkflowSchedulerPoisonStore();
        var readerResolutions = 0;
        await using var provider = NewProvider(activator, observer, services =>
            services.AddSingleton<IRuntimeHistoryStore>(_ => { readerResolutions++; throw new IOException("must not activate query reader"); }));
        await _schedulerWorkQueue.EnqueueAsync(work);
        var result = await ExactDrainer(provider, clock, poison).DrainAsync(new("wfexec-1", maxWorkItems: 1));
        if (required)
        {
            Assert.True(result.StoppedOnDeferral);
            Assert.Equal(0, result.DrainedCount);
            Assert.Equal(work.WorkItemId, Assert.Single(await _schedulerWorkQueue.ListAllAsync("wfexec-1")).WorkItemId);
            Assert.Empty(activator.Requests);
            Assert.Empty(observer.Consumed);
            Assert.Empty(_checkpointWriter.ListCommits());
            Assert.Equal(original, await _activityStateStore.FindAsync("wfexec-1", "actexec-1"));
            Assert.Equal(generation, (await _bookmarkStateStore.ObserveAsync("wfexec-1", "bookmark-1"))!.Generation);
        }
        else
        {
            Assert.Single(activator.Requests);
            Assert.Single(observer.Consumed);
            Assert.Null(await _bookmarkStateStore.FindAsync("wfexec-1", "bookmark-1"));
        }
        Assert.Empty(await poison.ListAsync("wfexec-1"));
        Assert.Equal(0, readerResolutions);
    }
}
