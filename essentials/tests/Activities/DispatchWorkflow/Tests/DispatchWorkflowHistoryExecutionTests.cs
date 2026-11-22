using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
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

    [Theory]
    [InlineData("defer")] [InlineData("fault")] [InlineData("cancel")]
    public async Task Returned_control_decision_records_acceptance_and_application_with_its_committed_cursor(string response)
    {
        await using var fixture = await DispatchWorkflowRuntimeTestFixture.CreateAsync(configureServices: services =>
        {
            services.AddWorkflowRuntimeHistory(); services.AddWorkflowStimulusRouter();
            services.Replace(ServiceDescriptor.Singleton<IPersistenceAccessContextAccessor>(new Access()));
        });
        var run = await fixture.StartParentAsync("history-control-" + response, "history-control-parent-" + response, "correlation",
            waitForCompletion: true, controlConsumerVersion: 1, controlResponse: response, parentPartition: new("tenant-42"));
        await fixture.SweepAsync(); await fixture.SweepAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var events = await EventsAsync(scope.ServiceProvider.GetRequiredService<IRuntimeHistoryStore>(), run.Start.WorkflowExecutionId);
        var accepted = Assert.Single(events, e => e.Name == "ChildControlAccepted");
        var applied = Assert.Single(events, e => e.Name == "ChildControlApplied");
        Assert.Equal(accepted.CheckpointId, applied.CheckpointId);
        Assert.Equal(accepted.PhysicalCommitId, applied.PhysicalCommitId);
        Assert.Equal(run.Activity.InvocationId, accepted.ActivityExecutionId);
        Assert.Equal(run.Activity.InvocationId, applied.Metadata["history.relatedActivityExecutionId"]);
        var call = (await fixture.ListActivitiesAsync(run.Start.WorkflowExecutionId)).Single(state => state.InvocationId == run.Activity.InvocationId);
        Assert.True(call.ChildControlInbox!.IsTerminalConsumed);
        Assert.Equal(1, call.ChildControlInbox.NextOrdinal);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Required_history_outage_keeps_the_exact_source_owed_without_spending_delivery_budget(bool afterEffect)
    {
        await using var fixture = await DispatchWorkflowRuntimeTestFixture.CreateAsync(configureServices: services =>
        {
            services.AddWorkflowRuntimeHistory(); services.AddWorkflowStimulusRouter();
            services.Replace(ServiceDescriptor.Singleton<IPersistenceAccessContextAccessor>(new Access()));
            var original = services.Single(item => item.ServiceType == typeof(IRuntimeHistoryObservationWriter));
            services.Remove(original);
            services.AddScoped<IRuntimeHistoryObservationWriter>(provider => new UnavailableObservation(
                (IRuntimeHistoryObservationWriter)original.ImplementationFactory!(provider), afterEffect));
        });
        var run = await fixture.StartParentAsync("child-history-outage-" + afterEffect, "parent-history-outage-" + afterEffect, "correlation",
            waitForCompletion: true, parentPartition: new("tenant-42"));
        await using var scope = fixture.Services.CreateAsyncScope();
        var history = scope.ServiceProvider.GetRequiredService<IRuntimeHistoryStore>();
        var origin = Assert.Single(await EventsAsync(history, run.Start.WorkflowExecutionId), e => e.Name == "ChildWorkflowDispatchRequested");
        var id = origin.Metadata["history.sourceDeliveryId"];
        var failure = await Assert.ThrowsAsync<RuntimePostCommitSourceBoundaryException>(async () =>
            await scope.ServiceProvider.GetRequiredService<IRuntimePostCommitOutboxProcessor>().ProcessAsync(
                new RuntimePostCommitOutboxProcessRequest(1, intentKind: WorkflowDispatchLifecycle.StartChildIntentKind)));
        Assert.True(failure.IsUnavailable);
        Assert.Equal(id, failure.SourceOutboxItemId);
        var source = (await scope.ServiceProvider.GetRequiredService<IPostCommitOutboxLookupStore>().FindDurableAsync(id))!;
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivering, source.Status);
        Assert.Equal(0, source.DeliveryAttemptCount);
        Assert.Equal(afterEffect ? 1 : 0, fixture.ChildProbe.Observations.Count);
        Assert.DoesNotContain(await fixture.ListDispatchesAsync(run.Start.WorkflowExecutionId), item => item.Status == WorkflowDispatchStatus.DispatchFailed);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Parent_resume_history_outage_preserves_source_responsibility_before_or_after_consumption(bool afterEffect)
    {
        await using var fixture = await DispatchWorkflowRuntimeTestFixture.CreateAsync(configureServices: services =>
        {
            services.AddWorkflowRuntimeHistory(); services.AddWorkflowStimulusRouter();
            services.Replace(ServiceDescriptor.Singleton<IPersistenceAccessContextAccessor>(new Access()));
            var original = services.Single(item => item.ServiceType == typeof(IRuntimeHistoryObservationWriter));
            services.Remove(original);
            services.AddScoped<IRuntimeHistoryObservationWriter>(provider => new UnavailableObservation(
                (IRuntimeHistoryObservationWriter)original.ImplementationFactory!(provider), afterEffect,
                afterEffect ? "DeliveryDispatched" : "DeliveryReceived"));
        });
        var run = await fixture.StartParentAsync("parent-receipt-outage-" + afterEffect, "parent-receipt-run-" + afterEffect, "correlation",
            waitForCompletion: true, parentPartition: new("tenant-42"));
        await using var scope = fixture.Services.CreateAsyncScope();
        var processor = scope.ServiceProvider.GetRequiredService<IRuntimePostCommitOutboxProcessor>();
        Assert.Equal(1, (await processor.ProcessAsync(new(1, intentKind: WorkflowDispatchLifecycle.StartChildIntentKind))).DeliveredCount);
        var origin = Assert.Single(await EventsAsync(scope.ServiceProvider.GetRequiredService<IRuntimeHistoryStore>(), run.Identity.ChildWorkflowExecutionId),
            e => e.Name == "ChildWorkflowSettled");
        var id = origin.Metadata["history.sourceDeliveryId"];
        var failure = await Assert.ThrowsAsync<RuntimePostCommitSourceBoundaryException>(async () =>
            await processor.ProcessAsync(new(1, intentKind: WorkflowDispatchLifecycle.ResumeParentIntentKind)));
        Assert.True(failure.IsUnavailable); Assert.Equal(id, failure.SourceOutboxItemId);
        var source = (await scope.ServiceProvider.GetRequiredService<IPostCommitOutboxLookupStore>().FindDurableAsync(id))!;
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivering, source.Status); Assert.Equal(0, source.DeliveryAttemptCount);
        var bookmark = await scope.ServiceProvider.GetRequiredService<IBookmarkStateStore>().FindAsync(run.Start.WorkflowExecutionId, run.Identity.WaitBookmarkId);
        if (afterEffect) Assert.Null(bookmark); else Assert.NotNull(bookmark);
    }

    private sealed class UnavailableObservation(IRuntimeHistoryObservationWriter inner, bool afterEffect, string? kind = null) : IRuntimeHistoryObservationWriter
    {
        public bool CanRecordHistory(IRuntimeHistoryStore reader) => inner.CanRecordHistory(reader);
        public ValueTask<RuntimeHistoryObservationResult> RecordAsync(RuntimeHistoryObservation observation, CancellationToken cancellationToken = default) =>
            observation.Event.Name == (kind ?? (afterEffect ? "ChildStartDispositionObserved" : "ChildStartAttemptObserved"))
                ? ValueTask.FromException<RuntimeHistoryObservationResult>(new IOException("owned-child-history-outage"))
                : inner.RecordAsync(observation, cancellationToken);
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
