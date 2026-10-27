using System.Diagnostics;
using System.Text.Json;
using Elsa.Activities.Bpmn.Contracts;
using Elsa.Activities.Bpmn.Tests;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using F = Elsa.Activities.Bpmn.Tests.BpmnRuntimeFixture;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class CombinedChildOutcomeEfTests
{
    [Fact]
    public async Task Bpmn_combined_outcome_survives_failed_enqueue_parent_commit_before_ack_and_competing_fresh_providers()
    {
        RuntimePostCommitOutboxItem failedSource;
        RuntimeSchedulerWorkItem work;
        string incidentCore;
        string reporterToken;
        string originalFault;
        await using (var first = await StartGenerationAsync(
            [CombinedChildOutcomeEfFixture.ParentActivityExecutionId, CombinedChildOutcomeEfFixture.CompositeActivityExecutionId],
            failFirstCombinedOutcome: true, bpmn: true))
        {
            var run = await first.RunAsync(await BpmnCombinedExecutableAsync(), allowPendingWorkOnTerminalCompletion: false, allowAcceptedButFaulted: true);
            Assert.Equal(WorkflowExecutionStatus.Running, run.WorkflowState!.Status);
            Assert.Empty(_observation.Outcomes);
            Assert.Equal(ActivityExecutionStatus.Faulted, run.State(CombinedChildOutcomeEfFixture.CompositeNodeId).Status);
            Assert.Equal("test.bpmn.durable-original", run.State(CombinedChildOutcomeEfFixture.CompositeNodeId).Fault!.Code);
            Assert.Empty(await BookmarksAsync(first));
            var reporter = run.State(CombinedChildOutcomeEfFixture.CompositeNodeId);
            reporterToken = reporter.Provenance.ExecutionPathId!;
            Assert.False(string.IsNullOrWhiteSpace(reporterToken));
            Assert.Single(run.State(F.ProcessNodeId).PrivateState!.Value.InlineValue!.Value.GetProperty("activeWork").EnumerateArray(),
                active => active.GetProperty("nodeId").GetString() == reporter.Execution.ExecutableNodeId && active.GetProperty("tokenId").GetString() == reporterToken);
            originalFault = JsonSerializer.Serialize(reporter.Fault);
            failedSource = await FindFailedParentOutcomeAsync(first);
            Assert.Equal(RuntimePostCommitOutboxStatus.FailedRetryable, failedSource.Status);
            Assert.Equal(1, failedSource.DeliveryAttemptCount);
            work = ReadParentOutcomeWorkItem(failedSource);
            var payload = ReadCompletionPayload(work);
            Assert.Equal(F.ProcessNodeId, payload.ExecutableNodeId);
            Assert.Equal(CombinedChildOutcomeEfFixture.ParentActivityExecutionId, payload.ActivityExecutionId);
            Assert.Equal(CombinedChildOutcomeEfFixture.CompositeActivityExecutionId, payload.CompletedChildActivityExecutionId);
            var outcome = Assert.IsType<RuntimeParentOutcome>(payload.ParentOutcome);
            Assert.Equal("test.bpmn.durable-original", outcome.Fault.Code);
            Assert.Equal(["bpmn.escalation", "bpmn.escalation"], outcome.Notifications.Select(notification => notification.Code));
            Assert.Equal([1, 2], outcome.Notifications.Select(notification => notification.Payload!.Value.GetProperty("ordinal").GetInt32()));
            var incident = Assert.Single(await ListIncidentsAsync(first));
            Assert.Equal(outcome.IncidentId, incident.IncidentId);
            Assert.True(incident.IsBlocking);
            Assert.Null(incident.ResolutionOutcome);
            incidentCore = BpmnIncidentCore(incident);
        }

        _clock.Advance(RuntimeSchedulerPostCommitIntentDispatcher.RetryPolicy.Delay!.Value);
        var cut = new BpmnCombinedOutcomeAckCut(work.WorkItemId);
        BpmnCombinedSnapshot committed;
        await using (var second = await StartGenerationAsync([CombinedChildOutcomeEfFixture.AfterActivityExecutionId], bpmn: true, ackCut: cut))
        {
            await using var deliveryScope = second.Services.CreateAsyncScope();
            var delivery = deliveryScope.ServiceProvider.GetRequiredService<IRuntimePostCommitOutboxProcessor>()
                .ProcessAsync(new RuntimePostCommitOutboxProcessRequest(100, CombinedChildOutcomeEfFixture.WorkflowExecutionId,
                    RuntimePostCommitIntentKinds.EnqueueSchedulerWork)).AsTask();
            Task? recovery = null;
            try
            {
                var actualQueued = await cut.Enqueued.Task.WaitAsync(TimeSpan.FromSeconds(30));
                Assert.True(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(work), JsonSerializer.SerializeToElement(actualQueued)));
                var inflight = Assert.IsType<RuntimePostCommitOutboxItem>(await FindOutboxItemAsync(second, failedSource.OutboxItemId));
                Assert.Equal(RuntimePostCommitOutboxStatus.Delivering, inflight.Status);
                Assert.True(inflight.DeliveryFencingToken > 0);
                Assert.Equal(1, inflight.DeliveryAttemptCount); // Attempts count recorded results, not the in-flight claim.
                await using (var queueScope = second.Services.CreateAsyncScope())
                    Assert.Contains(await queueScope.ServiceProvider.GetRequiredService<IWorkflowSchedulerWorkQueue>()
                        .ListAllAsync(new RuntimeSchedulerWorkQuery(CombinedChildOutcomeEfFixture.WorkflowExecutionId)), item => item.WorkItemId == work.WorkItemId);
                recovery = second.SweepAsync();
                committed = await AwaitBpmnParentCommitAsync(second, work.WorkItemId, recovery);
                var stillUnacknowledged = Assert.IsType<RuntimePostCommitOutboxItem>(await FindOutboxItemAsync(second, failedSource.OutboxItemId));
                Assert.Equal(RuntimePostCommitOutboxStatus.Delivering, stillUnacknowledged.Status);
                Assert.Equal(inflight.DeliveryFencingToken, stillUnacknowledged.DeliveryFencingToken);
                Assert.False(delivery.IsCompleted);
                Assert.False(cut.Release.Task.IsCompleted);
                Assert.Single(_observation.Outcomes);
                var observed = Assert.Single(_observation.Outcomes);
                Assert.Equal(work.WorkItemId, committed.Reporter.Metadata.GetValueOrDefault(RuntimeMetadataKeys.ParentCompletionProcessedWorkItemId));
                Assert.Equal(CombinedChildOutcomeEfFixture.CompositeActivityExecutionId, observed.ChildActivityExecutionId);
                Assert.Null(observed.ChildIterationId);
                Assert.Equal([1, 2], observed.Notifications.Select(notification => JsonSerializer.Deserialize<JsonElement>(notification.Payload!).GetProperty("ordinal").GetInt32()));
                Assert.Equal(incidentCore, BpmnIncidentCore(Assert.Single(committed.Incidents)));
                var absorbed = Assert.Single(committed.Incidents);
                Assert.False(absorbed.IsBlocking);
                Assert.Equal(IncidentResolutionActionKinds.AbsorbFault, absorbed.ResolutionOutcome!.ActionKind);
                Assert.Equal(IncidentResolutionSystemSources.StructuralFaultAbsorption, absorbed.ResolutionOutcome.SystemSource);
                Assert.Equal(CombinedChildOutcomeEfFixture.ParentActivityExecutionId, absorbed.Metadata.GetValueOrDefault(RuntimeMetadataKeys.FaultAbsorbedBy));
                Assert.Equal("bpmn.work.late-fault-absorbed", absorbed.Metadata.GetValueOrDefault(RuntimeMetadataKeys.FaultAbsorptionReason));
                Assert.Equal(ActivityExecutionStatus.Running, committed.Parent.Status);
                Assert.Equal(WorkflowExecutionStatus.Running, await WorkflowStatusAsync(second));
                Assert.Equal(originalFault, JsonSerializer.Serialize(committed.Reporter.Fault));
                var afterWait = Assert.Single(committed.Bookmarks);
                Assert.Equal(CombinedChildOutcomeEfFixture.AfterActivityExecutionId, afterWait.State.ActivityExecutionId);
                Assert.Equal(CombinedChildOutcomeEfFixture.AfterNodeId, afterWait.State.ExecutableNodeId);
                Assert.DoesNotContain(committed.Parent.PrivateState!.Value.InlineValue!.Value.GetProperty("tokens").EnumerateArray(),
                    token => token.GetProperty("tokenId").GetString() == reporterToken);
                Assert.Equal(2, committed.Parent.PrivateState!.StateVersion);
                Assert.DoesNotContain(committed.Parent.PrivateState.Value.InlineValue!.Value.GetProperty("activeWork").EnumerateArray(),
                    active => active.GetProperty("nodeId").GetString() == CombinedChildOutcomeEfFixture.CompositeNodeId);
            }
            finally
            {
                cut.Release.TrySetResult();
                if (recovery is not null)
                    await Task.WhenAll(delivery, recovery).WaitAsync(TimeSpan.FromSeconds(30));
                else
                    await delivery.WaitAsync(TimeSpan.FromSeconds(30));
            }
            Assert.Equal(1, (await delivery).FailedCount);
            var afterLostAck = Assert.IsType<RuntimePostCommitOutboxItem>(await FindOutboxItemAsync(second, failedSource.OutboxItemId));
            Assert.Equal(RuntimePostCommitOutboxStatus.FailedRetryable, afterLostAck.Status);
            Assert.Equal(2, afterLostAck.DeliveryAttemptCount);
            Assert.Equal(work.WorkItemId, ReadParentOutcomeWorkItem(afterLostAck).WorkItemId);
            AssertBpmnSnapshotEqual(committed, await ReadBpmnSnapshotAsync(second));
        }

        _clock.Advance(RuntimeSchedulerPostCommitIntentDispatcher.RetryPolicy.Delay!.Value);
        var barrier = new CombinedOutcomeRetryClaimBarrier();
        await using (var third = await StartGenerationAsync([], claimBarrier: barrier, bpmn: true))
        await using (var peer = await StartGenerationAsync([], claimBarrier: barrier, bpmn: true))
        {
            Assert.NotSame(third.Services, peer.Services);
            var results = await Task.WhenAll(third.SweepAsync(), peer.SweepAsync());
            Assert.Equal(2, barrier.Arrivals);
            Assert.Equal(1, results.Count(result => result.OutboxAttemptedCount > 0));
            Assert.Equal(1, results.Sum(result => result.OutboxDeliveredCount));
            Assert.Equal(0, results.Sum(result => result.OutboxFailedCount));
            Assert.Single(_observation.Outcomes);
            AssertBpmnSnapshotEqual(committed, await ReadBpmnSnapshotAsync(third));
            Assert.Equal(WorkflowExecutionStatus.Running, await WorkflowStatusAsync(third));
            var delivered = Assert.IsType<RuntimePostCommitOutboxItem>(await FindOutboxItemAsync(third, failedSource.OutboxItemId));
            Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, delivered.Status);
            Assert.Equal(3, delivered.DeliveryAttemptCount);
            Assert.True(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(work), JsonSerializer.SerializeToElement(ReadParentOutcomeWorkItem(delivered))));
            var attempts = _outboxProbe.Attempts.Where(attempt => attempt.CompletionKind == SchedulerCompletionKind.ParentOutcomeEvaluation).ToArray();
            Assert.Equal(3, attempts.Length);
            Assert.All(attempts, attempt => Assert.Equal(work.WorkItemId, attempt.WorkItemId));
            Assert.DoesNotContain(_outboxProbe.Attempts, attempt => attempt.CommandKind == WorkflowExecutionCommandKind.NotifyParentActivity);
            await using (var scope = third.Services.CreateAsyncScope())
            {
                Assert.Empty(await scope.ServiceProvider.GetRequiredService<IWorkflowSchedulerWorkQueue>()
                    .ListAllAsync(new RuntimeSchedulerWorkQuery(CombinedChildOutcomeEfFixture.WorkflowExecutionId)));
                Assert.Empty(await scope.ServiceProvider.GetRequiredService<IWorkflowSchedulerPoisonStore>()
                    .ListAsync(CombinedChildOutcomeEfFixture.WorkflowExecutionId));
            }
            var wait = Assert.Single(await BookmarksAsync(third));
            var completed = await third.ResumeAsync(WorkflowExecutionHarness.Identity, wait.BookmarkId, wait.ActivityExecutionId,
                wait.ExecutableNodeId, wait.ResumeTargetId, wait.StimulusType, wait.StimulusHash, JsonSerializer.SerializeToElement(new { eventName = "after" }));
            Assert.Equal(WorkflowExecutionStatus.Completed, completed.WorkflowState!.Status);
            Assert.Single(_observation.Outcomes);
            Assert.Empty(await BookmarksAsync(third));
        }
    }

    private static async Task<WorkflowExecutable> BpmnCombinedExecutableAsync()
    {
        // The existing fixture constructs the complete real BPMN contract; it is not a second evaluator.
        // Its temporary provider is needed only by the builder API and never executes this graph.
        await using var builder = await F.CreateAsync([]);
        {
            var reporter = new ExecutableNode(CombinedChildOutcomeEfFixture.CompositeNodeId, "authored-reporter",
                typeof(BpmnDurableSignallingFaultActivity).FullName!, "1.0.0", "test/clr", JsonSerializer.SerializeToElement(new { type = "clr" }),
                new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>());
            return builder.NewExecutable([reporter, F.EventWaitNode(CombinedChildOutcomeEfFixture.AfterNodeId, "after")],
                [F.StartEvent(), F.SubProcess("host", reporter.ExecutableNodeId), F.EscalationBoundary("boundary", "host", "notice", true),
                    F.Task("after", CombinedChildOutcomeEfFixture.AfterNodeId), F.EndEvent("host-end"), F.EndEvent("after-end")],
                [F.Flow("start-host", "start", "host"), F.Flow("host-end", "host", "host-end"),
                    F.Flow("boundary-after", "boundary", "after"), F.Flow("after-end", "after", "after-end")],
                [F.NodeResumeTarget(CombinedChildOutcomeEfFixture.AfterNodeId, Event.ResumeTargetId)]);
        }
    }

    private static async Task<BpmnCombinedSnapshot> AwaitBpmnParentCommitAsync(WorkflowExecutionHarness harness, string workId, Task recovery)
    {
        var timer = Stopwatch.StartNew();
        BpmnCombinedSnapshot? last = null;
        bool HasRequiredCut(BpmnCombinedSnapshot snapshot) =>
            snapshot.Reporter.Metadata.GetValueOrDefault(RuntimeMetadataKeys.ParentCompletionProcessedWorkItemId) == workId &&
            snapshot.Activities.Any(state => state.Execution.ExecutableNodeId == CombinedChildOutcomeEfFixture.AfterNodeId && state.Status == ActivityExecutionStatus.Suspended) &&
            snapshot.Bookmarks.Count == 1 && snapshot.Bookmarks.Single().State.ActivityExecutionId == CombinedChildOutcomeEfFixture.AfterActivityExecutionId;
        while (timer.Elapsed < TimeSpan.FromSeconds(30))
        {
            last = await ReadBpmnSnapshotAsync(harness);
            if (HasRequiredCut(last))
                return last;
            if (recovery.IsCompleted)
            {
                await recovery;
                // Recovery may finish between the preceding physical read and IsCompleted.
                last = await ReadBpmnSnapshotAsync(harness);
                Assert.True(HasRequiredCut(last), "Recovery ended before the real BPMN parent committed its outcome and trailing wait.");
                return last;
            }
            await Task.Delay(10);
        }
        Assert.Fail("No committed BPMN outcome observed before acknowledgement release: " + JsonSerializer.Serialize(last?.Activities));
        throw new InvalidOperationException();
    }

    private static async Task<BpmnCombinedSnapshot> ReadBpmnSnapshotAsync(WorkflowExecutionHarness harness)
    {
        await using var scope = harness.Services.CreateAsyncScope();
        var states = await scope.ServiceProvider.GetRequiredService<EfActivityExecutionStateStore>().ListAllAsync(CombinedChildOutcomeEfFixture.WorkflowExecutionId);
        var bookmarks = scope.ServiceProvider.GetRequiredService<EfBookmarkStateStore>();
        var observations = new List<BookmarkStateObservation>();
        foreach (var wait in (await bookmarks.ListAllBookmarkStatesAsync(CombinedChildOutcomeEfFixture.WorkflowExecutionId)).OrderBy(wait => wait.BookmarkId, StringComparer.Ordinal))
            observations.Add(Assert.IsType<BookmarkStateObservation>(await bookmarks.ObserveAsync(CombinedChildOutcomeEfFixture.WorkflowExecutionId, wait.BookmarkId)));
        var incidents = await scope.ServiceProvider.GetRequiredService<EfIncidentStateStore>().ListAsync(CombinedChildOutcomeEfFixture.WorkflowExecutionId);
        return new(states.OrderBy(state => state.InvocationId, StringComparer.Ordinal).ToArray(), observations,
            incidents.OrderBy(incident => incident.IncidentId, StringComparer.Ordinal).ToArray());
    }

    private static void AssertBpmnSnapshotEqual(BpmnCombinedSnapshot expected, BpmnCombinedSnapshot actual)
    {
        Assert.True(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(expected), JsonSerializer.SerializeToElement(actual)),
            "Combined-outcome retry changed durable activity, exact physical wait or original incident facts.");
    }

    private static string BpmnIncidentCore(IncidentState incident) => JsonSerializer.Serialize(new
    {
        incident.IncidentId, incident.WorkflowExecutionId, incident.ActivityExecutionId, incident.ExecutableNodeId,
        incident.Severity, incident.FailureType, incident.Message, incident.CreatedAt
    });

    private sealed record BpmnCombinedSnapshot(IReadOnlyCollection<ActivityExecutionState> Activities,
        IReadOnlyCollection<BookmarkStateObservation> Bookmarks, IReadOnlyCollection<IncidentState> Incidents)
    {
        public ActivityExecutionState Parent => Assert.Single(Activities, state => state.Execution.ExecutableNodeId == F.ProcessNodeId);
        public ActivityExecutionState Reporter => Assert.Single(Activities, state => state.Execution.ExecutableNodeId == CombinedChildOutcomeEfFixture.CompositeNodeId);
    }
}

public sealed class BpmnDurableSignallingFaultActivity : StructuralActivity, IRuntimeStructuralActivity
{
    public ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext context)
    {
        for (var ordinal = 1; ordinal <= 2; ordinal++)
            context.RequestParentNotification("bpmn.escalation", JsonSerializer.SerializeToElement(new { code = "notice", ordinal }));
        return ValueTask.FromResult(RuntimeStructuralContinuation.Faulted(new ActivityFault("test.bpmn.durable-original", "original durable child fault")));
    }
}

internal sealed class ObservedBpmnCombinedOutcomeRuntime(IBpmnProcessRuntime inner, CombinedOutcomeObservation observation) : IBpmnProcessRuntime
{
    public ValueTask<RuntimeStructuralContinuation> StartAsync(IRuntimeActivityExecutionContext context) => inner.StartAsync(context);
    public ValueTask<RuntimeStructuralContinuation> CompleteAsync(IRuntimeActivityExecutionContext context, ActivityChildCompletedContext completion) => inner.CompleteAsync(context, completion);
    public ValueTask<RuntimeStructuralContinuation> FaultAsync(IRuntimeActivityExecutionContext context, ActivityChildFaultedContext fault) => inner.FaultAsync(context, fault);
    public ValueTask<RuntimeStructuralContinuation> SignalAsync(IRuntimeActivityExecutionContext context, ActivityChildNotifiedContext signal) => inner.SignalAsync(context, signal);
    public ValueTask<RuntimeStructuralContinuation> ControlAsync(IRuntimeActivityExecutionContext context, RuntimeChildControlContext control) => inner.ControlAsync(context, control);
    public ValueTask<RuntimeStructuralContinuation> OutcomeAsync(IRuntimeActivityExecutionContext context, ActivityChildOutcomeContext outcome)
    { observation.Record(outcome); return inner.OutcomeAsync(context, outcome); }
}

internal sealed class BpmnCombinedOutcomeAckCut(string workId)
{
    public TaskCompletionSource<RuntimeSchedulerWorkItem> Enqueued { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public string WorkId => workId;
}

internal sealed class BpmnCombinedOutcomeAckCutDispatcher(IRuntimePostCommitIntentDispatcher inner, BpmnCombinedOutcomeAckCut cut) : IRuntimePostCommitIntentDispatcher
{
    public async ValueTask DispatchAsync(RuntimePostCommitIntent intent, CancellationToken cancellationToken = default)
    {
        await inner.DispatchAsync(intent, cancellationToken);
        if (intent.Kind == RuntimePostCommitIntentKinds.EnqueueSchedulerWork && intent.Payload is { } payload &&
            payload.Deserialize<RuntimeSchedulerWorkItem>() is { } work && work.WorkItemId == cut.WorkId)
        {
            cut.Enqueued.TrySetResult(work);
            await cut.Release.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            throw new TimeoutException("The actual combined callback was queued and its parent committed, but delivery acknowledgement was lost.");
        }
    }
}
