using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Activities.DispatchWorkflow.Tests;

public sealed class DispatchWorkflowHistoryExecutionTests
{
    public static IEnumerable<object[]> Cases() => from mode in Enum.GetValues<PersistenceMode>()
        from wait in new[] { false, true } select new object[] { mode, wait };

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Actual_child_start_and_terminal_resume_keep_their_distinct_native_origins(PersistenceMode mode, bool wait)
    {
        await using var fixture = await DispatchWorkflowRuntimeTestFixture.CreateAsync(
            persistenceModes: options => { options.Mode = mode; options.RecordCompletion = true; },
            configureServices: services =>
            {
                services.AddWorkflowRuntimeHistory(); services.AddWorkflowStimulusRouter();
                services.Replace(ServiceDescriptor.Singleton<IPersistenceAccessContextAccessor>(new Access()));
            });
        var run = await fixture.StartParentAsync("history-child-" + mode + wait, "history-parent-" + mode + wait, "history-correlation",
            waitForCompletion: wait, parentPartition: new("tenant-42"));
        await using var scope = fixture.Services.CreateAsyncScope();
        var history = scope.ServiceProvider.GetRequiredService<IRuntimeHistoryStore>();
        var parentBefore = await EventsAsync(history, run.Start.WorkflowExecutionId);
        var dispatch = Assert.Single(parentBefore, e => e.Name == "ChildWorkflowDispatchRequested");
        Assert.Equal(run.Identity.ChildWorkflowExecutionId, dispatch.Metadata["history.relatedWorkflowExecutionId"]);
        Assert.Equal(run.Activity.InvocationId, dispatch.ActivityExecutionId);
        Assert.True(dispatch.Metadata.ContainsKey("history.sourceDeliveryId"));
        var policy = (await history.FindHeadAsync(run.Start.WorkflowExecutionId))!.Policy;
        Assert.Equal("runtime-history-v5", policy.PolicyId);
        foreach (var kind in new[] { "ChildWorkflowDispatchRequested", "ChildWorkflowSettled", "ChildStartAttemptObserved", "ChildStartDispositionObserved" })
            Assert.Contains(kind, policy.SupportedKinds);
        Assert.DoesNotContain("TimerCancellationRequested", policy.SupportedKinds);
        await fixture.SweepAsync();
        await fixture.SweepAsync();
        var childEvents = await EventsAsync(history, run.Identity.ChildWorkflowExecutionId);
        var started = Assert.Single(childEvents, e => e.Name == "WorkflowStarted");
        Assert.Equal(dispatch.HistoryEventId, started.Causation!.HistoryEventId);
        Assert.Equal(run.Start.WorkflowExecutionId, started.Causation.WorkflowExecutionId);
        var settled = Assert.Single(childEvents, e => e.Name == "ChildWorkflowSettled");
        Assert.Equal(run.Start.WorkflowExecutionId, settled.Metadata["history.relatedWorkflowExecutionId"]);
        var parentAfter = await EventsAsync(history, run.Start.WorkflowExecutionId);
        Assert.Contains(parentAfter, e => e.Name == "ChildStartAttemptObserved" && e.Causation?.HistoryEventId == dispatch.HistoryEventId);
        Assert.Contains(parentAfter, e => e.Name == "ChildStartDispositionObserved");
        if (wait)
        {
            Assert.True(settled.Metadata.ContainsKey("history.sourceDeliveryId"));
            var receipt = Assert.Single(parentAfter, e => e.Name == "DeliveryReceived");
            Assert.Equal(settled.HistoryEventId, receipt.Causation!.HistoryEventId);
            Assert.Equal(run.Identity.ChildWorkflowExecutionId, receipt.Causation.WorkflowExecutionId);
            Assert.Contains(parentAfter, e => e.Name == "BookmarkConsumed" && e.BookmarkId == run.Identity.WaitBookmarkId);
        }
        else Assert.False(settled.Metadata.ContainsKey("history.sourceDeliveryId"));
        Assert.Equal(WorkflowExecutionStatus.Completed, (await fixture.FindWorkflowAsync(run.Start.WorkflowExecutionId))!.Status);
        Assert.Equal(WorkflowExecutionStatus.Completed, (await fixture.FindWorkflowAsync(run.Identity.ChildWorkflowExecutionId))!.Status);
        await fixture.SweepAsync();
        Assert.Equal(parentAfter.Select(e => e.HistoryEventId), (await EventsAsync(history, run.Start.WorkflowExecutionId)).Select(e => e.HistoryEventId));
        Assert.Equal(childEvents.Select(e => e.HistoryEventId), (await EventsAsync(history, run.Identity.ChildWorkflowExecutionId)).Select(e => e.HistoryEventId));
    }

    private static async Task<IReadOnlyList<RuntimeHistoryEvent>> EventsAsync(IRuntimeHistoryStore history, string run)
    {
        var events = new List<RuntimeHistoryEvent>();
        string? cursor = null;
        do
        {
            var page = await history.ReadPageAsync(new(run, 100, cursor));
            Assert.NotNull(page.Head); Assert.True(page.Head.CapturedFromStart); Assert.False(page.Head.HasGaps);
            events.AddRange(page.Items); cursor = page.NextContinuationToken;
        } while (cursor is not null);
        return events;
    }

    private sealed class Access : IPersistenceAccessContextAccessor
    { public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new("tenant-42")); }
}
