using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Coalescing;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.Incidents;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Elsa.Workflows.Runtime.Services.Values;
using Elsa.Workflows.Runtime.Services.WorkHandlers;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// Guardrails for RT-1 gap b / RT-12: a scheduler work handler that throws must not have its work item silently
/// dropped. The drainer records a poison record honoring <see cref="IRuntimeDomainRetryPolicy"/>, and only re-enqueues
/// immediately for <see cref="RuntimeDomainRetryMode.RetryNow"/>.
///
/// #412 item 3 (redrive-safe drain): dispatch no longer destructively dequeues up front. On a *handler fault* the
/// drainer ack-deletes the source item BEFORE recording poison / honoring a RetryNow re-enqueue, so a
/// deterministically-poisoning handler cannot hot-loop: the item is either parked (no re-enqueue) or re-enqueued
/// exactly once by RetryNow, never both. <see cref="DrainAsync_WithDefaultNoopRetryPolicy_RecordsPoisonWithoutRetryOrReEnqueue"/>
/// (empty queue after a fault) and <see cref="DrainAsync_RetryNowPoisonedHandler_DeliversBoundedTimes_DoesNotHotLoop"/>
/// are the bites: without the ack-on-fault, the still-queued source item would be re-driven by the sweep AND requeued
/// by RetryNow, and repeated drains would redeliver forever.
/// </summary>
public sealed class WorkflowSchedulerPoisonDrainTests
{
    private readonly DateTimeOffset _now = new(2026, 7, 2, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task DrainAsync_WithDefaultNoopRetryPolicy_RecordsPoisonWithoutRetryOrReEnqueue()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var poisonStore = new InMemoryWorkflowSchedulerPoisonStore();
        var drainer = NewDrainer(queue, poisonStore, new NoopRuntimeDomainRetryPolicy());
        await queue.EnqueueAsync(NewWorkItem(1));

        var result = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1"));
        var remaining = await queue.ListAllAsync(new RuntimeSchedulerWorkQuery("wfexec-1"));

        Assert.True(result.StoppedOnFault);
        var record = Assert.Single(await poisonStore.ListAsync("wfexec-1"));
        Assert.Equal(RuntimeSchedulerPoisonDisposition.Poisoned, record.Disposition);
        Assert.Null(record.NextRetryAt);
        Assert.Equal(1, record.FailureCount);
        Assert.Equal(nameof(InvalidOperationException), record.Fault.ExceptionType.Split('.')[^1]);
        Assert.Null(record.InnerFault); // The handler exception had no inner exception.
        Assert.Empty(remaining); // Not re-enqueued: the default policy is a safe no-loop park.
    }

    [Fact]
    public async Task DrainAsync_HandlerFaultWithNoRetryPolicyAtAll_StillLeavesADurableRecord()
    {
        // #1271: the weakest configuration the drainer can now be built in — no retry policy, no fault capture policy,
        // nothing but the required collaborators. The item is ack-deleted either way, so the invariant this pins is
        // that the record survives the ack: an empty queue must always be paired with a poison record, never with
        // nothing. Before the poison store became required, this same configuration dropped the item without a trace.
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var poisonStore = new InMemoryWorkflowSchedulerPoisonStore();
        var drainer = new WorkflowSchedulerDrainer(
            queue,
            [new AlwaysFaultingSchedulerWorkHandler(), new NoopWorkflowSchedulerWorkHandler()],
            new InMemoryWorkflowExecutionStateStore(),
            poisonStore,
            TestSchedulerDrainer.NewInertPauseGate(),
            new FakeTimeProvider(_now));
        await queue.EnqueueAsync(NewWorkItem(1));

        var result = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1"));

        Assert.True(result.StoppedOnFault);
        Assert.Empty(await queue.ListAllAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
        var record = Assert.Single(await poisonStore.ListAsync("wfexec-1"));
        Assert.Equal(RuntimeSchedulerPoisonDisposition.Poisoned, record.Disposition);
        Assert.Equal("work-1", record.WorkItemId);
        Assert.Empty(record.Metadata); // No policy was consulted, so no retry-mode metadata is stamped.
    }

    [Fact]
    public async Task DrainAsync_HandlerCrashWithWrappedException_RecordsInnerFaultOnPoisonRecord()
    {
        // #1031: handler crashes are routinely wrapped (e.g. a checkpoint-writer exception around the
        // physical storage fault). The poison record must carry the first inner fault, captured under the same
        // policy as the outer one, or the root cause is undiagnosable without temporary logging.
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var poisonStore = new InMemoryWorkflowSchedulerPoisonStore();
        var handler = new AlwaysFaultingSchedulerWorkHandler(workItem => new InvalidOperationException(
            $"Checkpoint write failed for {workItem.WorkItemId}.",
            new ArgumentException("GW-PHYSICAL-037: projected column overflow.")));
        var drainer = TestSchedulerDrainer.Create(
            queue,
            [handler, new NoopWorkflowSchedulerWorkHandler()],
            new FakeTimeProvider(_now),
            poisonStore: poisonStore,
            retryPolicy: new NoopRuntimeDomainRetryPolicy());
        await queue.EnqueueAsync(NewWorkItem(1));

        var result = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1"));

        Assert.True(result.StoppedOnFault);
        var record = Assert.Single(await poisonStore.ListAsync("wfexec-1"));
        Assert.Equal(typeof(InvalidOperationException).FullName, record.Fault.ExceptionType);
        Assert.NotNull(record.InnerFault);
        Assert.Equal(typeof(ArgumentException).FullName, record.InnerFault!.ExceptionType);
        Assert.Equal("GW-PHYSICAL-037: projected column overflow.", record.InnerFault.Message);
    }

    [Fact]
    public async Task DrainAsync_PortableExpressionFailure_PersistsActivityAddressAndTypedFailureMetadata()
    {
        var fixture = NewAssociatedWorkDrain(
            new AlwaysFaultingSchedulerWorkHandler(_ => NewExpressionFailure("qaMissingVariable is not defined.")),
            new NoopRuntimeDomainRetryPolicy());
        await fixture.Queue.EnqueueAsync(NewStartActivityWorkItem(1));

        var result = await fixture.Drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1"));

        Assert.True(result.StoppedOnFault);
        var record = Assert.Single(await fixture.PoisonStore.ListAsync("wfexec-1"));
        Assert.Equal("activity-1", record.Metadata[RuntimeMetadataKeys.ActivityExecutionId]);
        Assert.Equal("node-1", record.Metadata[RuntimeMetadataKeys.ExecutableNodeId]);
        Assert.Equal("text", record.Metadata[RuntimeMetadataKeys.InputKey]);
        Assert.Equal("ExpressionEvaluationFailed", record.Metadata[RuntimeMetadataKeys.InputFailureCode]);
        Assert.Equal("JavaScript", record.Metadata[RuntimeMetadataKeys.ExpressionLanguage]);
        Assert.Equal("Evaluation", record.Metadata[RuntimeMetadataKeys.InputEvaluationPhase]);
        Assert.Equal("qaMissingVariable is not defined.", record.InnerFault!.Message);
    }

    [Fact]
    public async Task DrainAsync_LaterGenericFailure_KeepsPayloadAddressAndDropsStaleInputFailureMetadata()
    {
        var failureCount = 0;
        var handler = new AlwaysFaultingSchedulerWorkHandler(_ => ++failureCount == 1
            ? NewExpressionFailure("qaMissingVariable is not defined.")
            : new InvalidOperationException("storage unavailable"));
        var fixture = NewAssociatedWorkDrain(
            handler,
            new StubRetryPolicy(new RuntimeDomainRetryDecision(RuntimeDomainRetryMode.RetryNow, null, "retry")));

        await fixture.Queue.EnqueueAsync(NewStartActivityWorkItem(1));
        await fixture.Drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1", maxWorkItems: 1));
        await fixture.Drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1", maxWorkItems: 1));

        var record = Assert.Single(await fixture.PoisonStore.ListAsync("wfexec-1"));
        Assert.Equal(RuntimeSchedulerPoisonDisposition.RetryScheduled, record.Disposition);
        Assert.Equal("activity-1", record.Metadata[RuntimeMetadataKeys.ActivityExecutionId]);
        Assert.Equal("node-1", record.Metadata[RuntimeMetadataKeys.ExecutableNodeId]);
        Assert.False(record.Metadata.ContainsKey(RuntimeMetadataKeys.InputFailureCode));
        Assert.False(record.Metadata.ContainsKey(RuntimeMetadataKeys.InputKey));
        Assert.False(record.Metadata.ContainsKey(RuntimeMetadataKeys.ExpressionLanguage));
        Assert.False(record.Metadata.ContainsKey(RuntimeMetadataKeys.InputEvaluationPhase));
        Assert.Equal("RetryNow", record.Metadata[RuntimeMetadataKeys.SchedulerPoisonRetryMode]);
        Assert.Equal("retry", record.Metadata[RuntimeMetadataKeys.SchedulerPoisonRetryReason]);
    }

    [Theory]
    [InlineData(WorkflowExecutionCommandKind.ScheduleActivity)]
    [InlineData(WorkflowExecutionCommandKind.CompleteActivity)]
    [InlineData(WorkflowExecutionCommandKind.ResumeBookmark)]
    [InlineData(WorkflowExecutionCommandKind.CreateBookmark)]
    [InlineData(WorkflowExecutionCommandKind.StartActivity)]
    [InlineData(WorkflowExecutionCommandKind.InvokeActivity)]
    [InlineData(WorkflowExecutionCommandKind.NotifyParentActivity)]
    public async Task DrainAsync_ActivityCommandPoison_PreservesPayloadAddress(WorkflowExecutionCommandKind commandKind)
    {
        var fixture = NewAssociatedWorkDrain(
            new AlwaysFaultingSchedulerWorkHandler(),
            new NoopRuntimeDomainRetryPolicy());
        await fixture.Queue.EnqueueAsync(NewWorkItem(1, commandKind, NewActivityAddressPayload()));

        await fixture.Drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1"));

        var record = Assert.Single(await fixture.PoisonStore.ListAsync("wfexec-1"));
        Assert.Equal("activity-1", record.Metadata[RuntimeMetadataKeys.ActivityExecutionId]);
        Assert.Equal("node-1", record.Metadata[RuntimeMetadataKeys.ExecutableNodeId]);
    }

    [Theory]
    [InlineData(WorkflowExecutionCommandKind.RetryActivityBoundary, false)]
    [InlineData(WorkflowExecutionCommandKind.RetryActivityBoundary, true)]
    [InlineData(WorkflowExecutionCommandKind.CancelActivityScope, false)]
    [InlineData(WorkflowExecutionCommandKind.CancelActivityScope, true)]
    public async Task DrainAsync_BoundaryCommandPoison_ResolvesNodeFromCanonicalActivityState(
        WorkflowExecutionCommandKind commandKind,
        bool failFirstActivityRead)
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var poisonStore = new InMemoryWorkflowSchedulerPoisonStore();
        var activityStore = new InMemoryActivityExecutionStateStore();
        await activityStore.SaveAsync(NewActivityState());
        var observedActivityStore = new FailOnceActivityExecutionStateStore(activityStore, failFirstActivityRead);

        var payload = commandKind switch
        {
            WorkflowExecutionCommandKind.RetryActivityBoundary => JsonSerializer.SerializeToElement(
                new RetryActivityBoundaryCommand(
                    "activity-1",
                    "activity-1-retry",
                    new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test"),
                    "retry after policy")),
            WorkflowExecutionCommandKind.CancelActivityScope => JsonSerializer.SerializeToElement(
                new CancelActivityScopeCommand("activity-1", "activity-1", "operator request")),
            _ => throw new ArgumentOutOfRangeException(nameof(commandKind), commandKind, null)
        };
        await queue.EnqueueAsync(NewWorkItem(1, commandKind, payload));
        var drainer = TestSchedulerDrainer.Create(
            queue,
            [new AlwaysFaultingSchedulerWorkHandler(), new NoopWorkflowSchedulerWorkHandler()],
            new FakeTimeProvider(_now),
            poisonStore: poisonStore,
            retryPolicy: new NoopRuntimeDomainRetryPolicy());

        var drainResult = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1"));
        Assert.True(drainResult.StoppedOnFault);
        var poison = Assert.Single(await poisonStore.ListAsync("wfexec-1"));
        Assert.Equal("activity-1", poison.Metadata[RuntimeMetadataKeys.ActivityExecutionId]);
        // These real command contracts intentionally carry the execution ID but no node ID.
        Assert.False(poison.Metadata.ContainsKey(RuntimeMetadataKeys.ExecutableNodeId));

        var workflowStore = new InMemoryWorkflowExecutionStateStore();
        var incidentStore = new InMemoryIncidentStateStore();
        var commitStore = new InMemoryRuntimeCheckpointCommitStore(
            workflowStore,
            activityExecutionStateStore: activityStore,
            incidentStateStore: incidentStore,
            rootWriteLeaseManager: PassThroughWorkflowExecutableRootWriteLeaseManager.Instance);
        var checkpointCommitter = new RuntimeCheckpointCommitter(
            new ImmediateRuntimeCheckpointPersistencePolicy(),
            commitStore,
            new AsyncLocalRuntimeExecutionOwnershipContextAccessor(),
            [],
            []);
        var observer = new PoisonedSchedulerWorkIncidentObserver(
            poisonStore,
            incidentStore,
            checkpointCommitter,
            new FakeTimeProvider(_now),
            activityExecutionStateStore: observedActivityStore,
            inspectionAccumulator: null);

        await observer.OnDrainedAsync(NewEnvelope(), drainResult);

        var originalIncident = Assert.Single(await incidentStore.ListAsync("wfexec-1"));
        Assert.Equal("activity-1", originalIncident.ActivityExecutionId);
        if (failFirstActivityRead)
        {
            Assert.Null(originalIncident.ExecutableNodeId);
            Assert.False(originalIncident.Metadata.ContainsKey(RuntimeMetadataKeys.ExecutableNodeId));
            Assert.Empty((await activityStore.FindAsync("wfexec-1", "activity-1"))!.IncidentIds);
            Assert.Empty(commitStore.ListCommits().Single().Commit.StateChanges.ActivityExecutions);
        }
        else
        {
            Assert.Equal("node-1", originalIncident.ExecutableNodeId);
            Assert.Equal("node-1", originalIncident.Metadata[RuntimeMetadataKeys.ExecutableNodeId]);
            Assert.Contains(originalIncident.IncidentId, (await activityStore.FindAsync("wfexec-1", "activity-1"))!.IncidentIds);
            Assert.Single(commitStore.ListCommits().Single().Commit.StateChanges.ActivityExecutions);
        }

        await observer.OnDrainedAsync(NewEnvelope(), drainResult);
        await observer.OnDrainedAsync(NewEnvelope(), drainResult);

        var incident = Assert.Single(await incidentStore.ListAsync("wfexec-1"));
        Assert.Equal(originalIncident.IncidentId, incident.IncidentId);
        Assert.Equal("node-1", incident.ExecutableNodeId);
        Assert.Equal("node-1", incident.Metadata[RuntimeMetadataKeys.ExecutableNodeId]);
        Assert.Equal(originalIncident.Severity, incident.Severity);
        Assert.Equal(originalIncident.Status, incident.Status);
        Assert.Equal(originalIncident.ResolutionOutcome, incident.ResolutionOutcome);
        Assert.Equal(originalIncident.CreatedAt, incident.CreatedAt);
        Assert.Equal(originalIncident.ResolvedAt, incident.ResolvedAt);
        Assert.Equal(originalIncident.Message, incident.Message);
        Assert.Contains(incident.IncidentId, (await activityStore.FindAsync("wfexec-1", "activity-1"))!.IncidentIds);
        Assert.Equal(3, observedActivityStore.FindAttempts);
        Assert.Equal(failFirstActivityRead ? 2 : 1, commitStore.ListCommits().Count);
        if (failFirstActivityRead)
        {
            var repairCommit = commitStore.ListCommits()
                .Select(item => item.Commit)
                .Single(commit => commit.CommitId.Contains("scheduler-poison-repair", StringComparison.Ordinal));
            Assert.Equal("activity-1", Assert.Single(repairCommit.Checkpoint.ActivityExecutionIds));
            Assert.Equal(incident.IncidentId, Assert.Single(repairCommit.StateChanges.Incidents).StateId);
            Assert.NotEqual(commitStore.ListCommits().Select(item => item.Commit.Checkpoint.CheckpointId).First(), repairCommit.Checkpoint.CheckpointId);
        }
    }

    [Fact]
    public async Task DrainAsync_WorkflowCommandPoison_DoesNotInferActivityFromArbitraryPayloadFields()
    {
        var fixture = NewAssociatedWorkDrain(
            new AlwaysFaultingSchedulerWorkHandler(),
            new NoopRuntimeDomainRetryPolicy());
        await fixture.Queue.EnqueueAsync(NewWorkItem(
            1,
            WorkflowExecutionCommandKind.RunSchedulerWork,
            NewActivityAddressPayload()));

        await fixture.Drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1"));

        var record = Assert.Single(await fixture.PoisonStore.ListAsync("wfexec-1"));
        Assert.False(record.Metadata.ContainsKey(RuntimeMetadataKeys.ActivityExecutionId));
        Assert.False(record.Metadata.ContainsKey(RuntimeMetadataKeys.ExecutableNodeId));
    }

    [Fact]
    public async Task DrainAsync_WithRetryNowPolicy_ReEnqueuesThroughQueueContractAndRecordsRetryScheduled()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var poisonStore = new InMemoryWorkflowSchedulerPoisonStore();
        var drainer = NewDrainer(queue, poisonStore, new StubRetryPolicy(new RuntimeDomainRetryDecision(RuntimeDomainRetryMode.RetryNow, null, "retry-now")));
        await queue.EnqueueAsync(NewWorkItem(1));

        var result = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1", maxWorkItems: 1));
        var remaining = await queue.ListAllAsync(new RuntimeSchedulerWorkQuery("wfexec-1"));

        Assert.True(result.StoppedOnFault);
        var record = Assert.Single(await poisonStore.ListAsync("wfexec-1"));
        Assert.Equal(RuntimeSchedulerPoisonDisposition.RetryScheduled, record.Disposition);
        Assert.Equal(_now, record.NextRetryAt);
        Assert.Collection(remaining, item => Assert.Equal("work-1", item.WorkItemId)); // Re-enqueued via the public queue contract.
    }

    [Fact]
    public async Task DrainAsync_WithRetryAfterPolicy_ReleasesTheItemUntilItIsDueThenRunsItAgain()
    {
        // #3275: RetryAfter used to ack-delete the item and record a NextRetryAt that nothing read, so the step stalled
        // silently. The claimed item is now released with the policy's delay: it stays queued as the only copy, is not
        // claimable before the delay, and runs again once due (the resumption sweep re-drives claimable work).
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var poisonStore = new InMemoryWorkflowSchedulerPoisonStore();
        var time = new FakeTimeProvider(_now);
        var handler = new AlwaysFaultingSchedulerWorkHandler();
        var delay = TimeSpan.FromMinutes(5);
        var drainer = NewDrainer(queue, poisonStore, new StubRetryPolicy(new RuntimeDomainRetryDecision(RuntimeDomainRetryMode.RetryAfter, delay, "retry-after")), time, handler,
            hasResumptionRedriver: true);
        await queue.EnqueueAsync(NewWorkItem(1, WorkflowExecutionCommandKind.InvokeActivity));

        var result = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1"));

        Assert.True(result.StoppedOnFault);
        var record = Assert.Single(await poisonStore.ListAsync("wfexec-1"));
        Assert.Equal(RuntimeSchedulerPoisonDisposition.RetryScheduled, record.Disposition);
        Assert.Equal(_now + delay, record.NextRetryAt);
        Assert.Collection(await queue.ListAllAsync(new RuntimeSchedulerWorkQuery("wfexec-1")), item => Assert.Equal("work-1", item.WorkItemId));

        // The sweep's backlog discovery sees the execution exactly when the retry is due.
        time.Advance(delay - TimeSpan.FromSeconds(1));
        Assert.Empty(await queue.ListClaimableWorkflowExecutionIdsAsync(new RuntimeSchedulerClaimableBacklogQuery(time.GetUtcNow())));
        var early = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1"));
        Assert.Empty(early.Items);
        Assert.Equal(1, handler.InvocationCount);

        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(["wfexec-1"], await queue.ListClaimableWorkflowExecutionIdsAsync(new RuntimeSchedulerClaimableBacklogQuery(time.GetUtcNow())));
        var due = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1"));
        Assert.Equal("work-1", Assert.Single(due.Items).WorkItemId);
        Assert.Equal(2, handler.InvocationCount);
        Assert.Equal(2, Assert.Single(await poisonStore.ListAsync("wfexec-1")).FailureCount);
    }

    [Theory]
    [InlineData("no-redriver")]
    [InlineData("policy-throws")]
    [InlineData("checkpoint-rule")]
    [InlineData("release-refused")]
    [InlineData("delay-unrepresentable")]
    public async Task DrainAsync_WithRetryAfterPolicy_WhenTheRetryCannotBeHeld_PoisonsTheDelayableItem(string reason)
    {
        // Each case would otherwise leave a released item nothing re-drives, an item claimed to loop on every lapse, a
        // retry of a run the faulter terminates, or a claim-lost throw with no record. A delay that ends past the last
        // representable time used to throw before the ack, so the item stayed claimed to fault again on every lapse.
        // All are poisoned and acked.
        var inner = new InMemoryWorkflowSchedulerWorkQueue();
        IWorkflowSchedulerWorkQueue queue = reason == "release-refused" ? new RefusingReleaseWorkQueue(inner) : inner;
        var poisonStore = new InMemoryWorkflowSchedulerPoisonStore();
        var delay = reason == "delay-unrepresentable" ? TimeSpan.MaxValue : TimeSpan.FromMinutes(5);
        IRuntimeDomainRetryPolicy policy = reason == "policy-throws"
            ? new ThrowingRetryPolicy()
            : new StubRetryPolicy(new RuntimeDomainRetryDecision(RuntimeDomainRetryMode.RetryAfter, delay, "retry-after"));
        Func<RuntimeSchedulerWorkItem, Exception>? checkpointRuleFault = reason == "checkpoint-rule"
            ? item => new RuntimeCheckpointCommitValidationException($"A checkpoint rule refused {item.WorkItemId}.")
            : null;
        var handler = new AlwaysFaultingSchedulerWorkHandler(checkpointRuleFault);
        var drainer = TestSchedulerDrainer.Create(
            queue,
            [handler, new NoopWorkflowSchedulerWorkHandler()],
            new FakeTimeProvider(_now),
            faultCapturePolicy: new DefaultRuntimeFaultCapturePolicy(Options.Create(new RuntimeFaultCaptureOptions())),
            poisonStore: poisonStore,
            retryPolicy: policy,
            hasResumptionRedriver: reason != "no-redriver");
        await inner.EnqueueAsync(NewWorkItem(1, WorkflowExecutionCommandKind.InvokeActivity));

        var result = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1"));

        Assert.True(result.StoppedOnFault);
        var record = Assert.Single(await poisonStore.ListAsync("wfexec-1"));
        Assert.Equal(RuntimeSchedulerPoisonDisposition.Poisoned, record.Disposition);
        Assert.Null(record.NextRetryAt);
        Assert.Empty(await inner.ListAllAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
        if (reason == "policy-throws")
        {
            Assert.Equal("PolicyFailed", record.Metadata[RuntimeMetadataKeys.SchedulerPoisonRetryMode]);
            Assert.Equal(typeof(InvalidOperationException).FullName, record.Metadata[RuntimeMetadataKeys.SchedulerPoisonRetryReason]);
        }
    }

    [Fact]
    public async Task DrainAsync_WhenThePoisonStoreCannotBeRead_KeepsTheFaultedItemClaimedForReDrive()
    {
        // #3275 moved the poison-store read (the prior failure count the retry policy is given) before the ack, because
        // a RetryAfter is released instead of acknowledged. A read that throws now leaves the item claimed and un-acked:
        // the exception surfaces, and the item is claimable again once its claim lapses. Before, the ack ran first and
        // the failed read dropped the item with no record.
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var time = new FakeTimeProvider(_now);
        var handler = new AlwaysFaultingSchedulerWorkHandler();
        var drainer = NewDrainer(queue, new UnreadablePoisonStore(), new NoopRuntimeDomainRetryPolicy(), time, handler);
        await queue.EnqueueAsync(NewWorkItem(1, WorkflowExecutionCommandKind.InvokeActivity));

        await Assert.ThrowsAsync<IOException>(() => drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1")).AsTask());

        Assert.Equal(1, handler.InvocationCount);
        Assert.Equal("work-1", Assert.Single(await queue.ListAllAsync(new RuntimeSchedulerWorkQuery("wfexec-1"))).WorkItemId);
        Assert.Empty(await queue.ListClaimableWorkflowExecutionIdsAsync(new RuntimeSchedulerClaimableBacklogQuery(time.GetUtcNow())));
        time.Advance(new RuntimeSchedulerWorkClaimOptions().VisibilityTimeout);
        Assert.Equal(["wfexec-1"], await queue.ListClaimableWorkflowExecutionIdsAsync(new RuntimeSchedulerClaimableBacklogQuery(time.GetUtcNow())));
    }

    [Fact]
    public async Task DrainAsync_WithRetryAfterPolicy_ForWorkThatCannotBeDelayed_PoisonsInsteadOfParkingSilently()
    {
        // RunSchedulerWork has no delayed release, so the queue cannot hold the retry even where a resumption sweep is
        // composed. #3275: it is poisoned (projected as a blocking incident) rather than recorded as RetryScheduled with
        // a NextRetryAt that nothing would act on.
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var poisonStore = new InMemoryWorkflowSchedulerPoisonStore();
        var delay = TimeSpan.FromMinutes(5);
        var drainer = NewDrainer(queue, poisonStore, new StubRetryPolicy(new RuntimeDomainRetryDecision(RuntimeDomainRetryMode.RetryAfter, delay, "retry-after")),
            hasResumptionRedriver: true);
        await queue.EnqueueAsync(NewWorkItem(1));

        var result = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1"));

        Assert.True(result.StoppedOnFault);
        var record = Assert.Single(await poisonStore.ListAsync("wfexec-1"));
        Assert.Equal(RuntimeSchedulerPoisonDisposition.Poisoned, record.Disposition);
        Assert.Null(record.NextRetryAt);
        Assert.Equal(nameof(RuntimeDomainRetryMode.RetryAfter), record.Metadata[RuntimeMetadataKeys.SchedulerPoisonRetryMode]);
        Assert.Empty(await queue.ListAllAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
    }

    [Fact]
    public async Task DrainAsync_WithRetryAfterPolicy_WhenTheAcknowledgementAfterASuccessfulDispatchFails_PoisonsWithoutRunningTheStepAgain()
    {
        // #3275: the handler returned, so its effect is durable, and only the acknowledgement after it failed. That
        // failure lands in the same fault arm as a handler fault, and a RetryAfter used to release the completed step to
        // run again. Only a fault the handler itself threw is released. This one takes the path it always took: the
        // fault arm acknowledges again and records it, and with the retry not held the record is Poisoned.
        var inner = new InMemoryWorkflowSchedulerWorkQueue();
        var queue = new FailFirstCompleteWorkQueue(inner);
        var poisonStore = new InMemoryWorkflowSchedulerPoisonStore();
        var time = new FakeTimeProvider(_now);
        var handler = new SucceedingSchedulerWorkHandler();
        var delay = TimeSpan.FromMinutes(5);
        var drainer = TestSchedulerDrainer.Create(
            queue,
            [handler],
            time,
            faultCapturePolicy: new DefaultRuntimeFaultCapturePolicy(Options.Create(new RuntimeFaultCaptureOptions())),
            poisonStore: poisonStore,
            retryPolicy: new StubRetryPolicy(new RuntimeDomainRetryDecision(RuntimeDomainRetryMode.RetryAfter, delay, "retry-after")),
            consumedWorkClaimAccessor: new RuntimeConsumedSchedulerWorkClaimAccessor(),
            hasResumptionRedriver: true);
        await inner.EnqueueAsync(NewWorkItem(1, WorkflowExecutionCommandKind.ResumeBookmark));

        var result = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1"));

        Assert.True(queue.CompleteFailed);
        Assert.True(result.StoppedOnFault);
        var record = Assert.Single(await poisonStore.ListAsync("wfexec-1"));
        Assert.Equal(RuntimeSchedulerPoisonDisposition.Poisoned, record.Disposition);
        Assert.Equal(typeof(TimeoutException).FullName, record.Fault.ExceptionType);
        Assert.Null(record.NextRetryAt);
        Assert.Empty(await inner.ListAllAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));

        time.Advance(delay);
        Assert.Empty((await drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1"))).Items);
        Assert.Equal(1, handler.InvocationCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DrainAsync_WithRetryAfterPolicy_WhenAnInactiveCoalescingSegmentRefusesTheRelease_ThrowsClaimLostWithoutAckOrRecord(
        bool otherOwnerClaimed)
    {
        // A handler crosses a durable boundary that deactivates the coalescing overlay, and the durable copy is then
        // claimed by another owner or removed. The release must persist at once, and cannot: this drain lost the item.
        // The fallback acknowledgement used to succeed through the overlay and delete the durable copy by identity, out
        // from under the other owner's live claim, and then record the step Poisoned. The refusal now fences the overlay
        // claim, so the acknowledgement is refused too: the drainer throws claim-lost and records nothing, and whatever
        // holds the durable copy keeps it.
        var inner = new InMemoryWorkflowSchedulerWorkQueue();
        var accessor = new AsyncLocalRuntimeCoalescingSessionAccessor();
        var queue = new CoalescingWorkflowSchedulerWorkQueue(new CoalescingInner<IWorkflowSchedulerWorkQueue>(inner), accessor);
        var session = new RuntimeCoalescingSession("wfexec-1", inner, new PersistenceModeOptions());
        var poisonStore = new InMemoryWorkflowSchedulerPoisonStore();
        var workItem = NewWorkItem(1, WorkflowExecutionCommandKind.InvokeActivity);
        var handler = new AlwaysFaultingSchedulerWorkHandler(beforeFault: item => LoseDurableCopyAfterABoundaryAsync(session, inner, item, otherOwnerClaimed));
        var drainer = TestSchedulerDrainer.Create(
            queue,
            [handler, new NoopWorkflowSchedulerWorkHandler()],
            new FakeTimeProvider(_now),
            poisonStore: poisonStore,
            retryPolicy: new StubRetryPolicy(new RuntimeDomainRetryDecision(RuntimeDomainRetryMode.RetryAfter, TimeSpan.FromMinutes(5), "retry-after")),
            hasResumptionRedriver: true);
        await inner.EnqueueAsync(workItem);

        InvalidOperationException exception;
        using (accessor.Push(session))
            exception = await Assert.ThrowsAnyAsync<InvalidOperationException>(
                () => drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1", maxWorkItems: 1)).AsTask());

        Assert.Contains("was lost during 'complete' with status 'Stale'", exception.Message);
        Assert.Equal(1, handler.InvocationCount);
        Assert.Empty(await poisonStore.ListAsync("wfexec-1"));
        await AssertDurableCopyLeftToItsHolderAsync(inner, otherOwnerClaimed);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DrainAsync_WhenAnInactiveCoalescingSegmentRefusesAnUnavailableRelease_ThrowsClaimLostAndKeepsTheItemQueued(
        bool otherOwnerClaimed)
    {
        // The same refusal on the temporarily-unavailable arm: the drainer throws claim-lost for the refused release.
        // Nothing is acknowledged or poisoned, and whatever holds the durable copy keeps it.
        var inner = new InMemoryWorkflowSchedulerWorkQueue();
        var accessor = new AsyncLocalRuntimeCoalescingSessionAccessor();
        var queue = new CoalescingWorkflowSchedulerWorkQueue(new CoalescingInner<IWorkflowSchedulerWorkQueue>(inner), accessor);
        var session = new RuntimeCoalescingSession("wfexec-1", inner, new PersistenceModeOptions());
        var poisonStore = new InMemoryWorkflowSchedulerPoisonStore();
        var handler = new AlwaysFaultingSchedulerWorkHandler(
            _ => new RuntimeSchedulerWorkUnavailableException("test.unavailable", "The test dependency is unavailable."),
            item => LoseDurableCopyAfterABoundaryAsync(session, inner, item, otherOwnerClaimed));
        var drainer = TestSchedulerDrainer.Create(
            queue,
            [handler, new NoopWorkflowSchedulerWorkHandler()],
            new FakeTimeProvider(_now),
            poisonStore: poisonStore);
        await inner.EnqueueAsync(NewWorkItem(1, WorkflowExecutionCommandKind.InvokeActivity));

        InvalidOperationException exception;
        using (accessor.Push(session))
            exception = await Assert.ThrowsAnyAsync<InvalidOperationException>(
                () => drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1", maxWorkItems: 1)).AsTask());

        Assert.Contains("was lost during 'release after temporary unavailability' with status 'Stale'", exception.Message);
        Assert.Equal(1, handler.InvocationCount);
        Assert.Empty(await poisonStore.ListAsync("wfexec-1"));
        await AssertDurableCopyLeftToItsHolderAsync(inner, otherOwnerClaimed);
    }

    // A retaining boundary deactivates the overlay, and the durable copy then leaves this drain: another owner takes it
    // under a live claim, which no delayed release may override, or it is removed. The session reads the system clock,
    // so the claim is live by that clock. Another writer then queues work-2 behind it.
    private async ValueTask LoseDurableCopyAfterABoundaryAsync(
        RuntimeCoalescingSession session,
        InMemoryWorkflowSchedulerWorkQueue inner,
        RuntimeSchedulerWorkItem item,
        bool otherOwnerClaimed)
    {
        await session.AdvanceInnerQueueAsync(consumeInFlightClaims: false, default);
        session.Deactivate();
        if (otherOwnerClaimed)
        {
            var claim = await inner.ClaimAsync(new RuntimeSchedulerWorkClaimRequest(
                item.WorkflowExecutionId, "other-owner", DateTimeOffset.UtcNow, TimeSpan.FromDays(1)));
            Assert.Equal(item.WorkItemId, claim?.Item.WorkItemId);
        }
        else
        {
            Assert.True(await inner.DeleteAsync(item.WorkflowExecutionId, item.WorkItemId));
        }
        await inner.EnqueueAsync(NewWorkItem(2, WorkflowExecutionCommandKind.InvokeActivity));
    }

    private static async Task AssertDurableCopyLeftToItsHolderAsync(InMemoryWorkflowSchedulerWorkQueue inner, bool otherOwnerClaimed)
    {
        Assert.Equal(otherOwnerClaimed ? ["work-1", "work-2"] : ["work-2"],
            (await inner.ListAllAsync(new RuntimeSchedulerWorkQuery("wfexec-1"))).Select(item => item.WorkItemId).Order(StringComparer.Ordinal));
        if (otherOwnerClaimed)
            Assert.Equal(["work-1"], (await inner.ListActiveClaimsAsync("wfexec-1", DateTimeOffset.UtcNow)).Select(claim => claim.Item.WorkItemId));
    }

    [Fact]
    public async Task DrainAsync_RepeatedHandlerCrash_AccumulatesFailureCount()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var poisonStore = new InMemoryWorkflowSchedulerPoisonStore();
        var drainer = NewDrainer(queue, poisonStore, new NoopRuntimeDomainRetryPolicy());

        await queue.EnqueueAsync(NewWorkItem(1));
        await drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1"));
        await queue.EnqueueAsync(NewWorkItem(1));
        await drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1"));

        var record = Assert.Single(await poisonStore.ListAsync("wfexec-1"));
        Assert.Equal(2, record.FailureCount);
        Assert.Equal(_now, record.FirstFailedAt);
    }

    [Fact]
    public async Task DrainAsync_RetryNowPoisonedHandler_DeliversBoundedTimes_DoesNotHotLoop()
    {
        // #412 item 3 — the load-bearing poison-path edge under the redrive-safe drain. A handler that
        // deterministically faults, paired with a RetryNow policy, must be delivered EXACTLY ONCE per drain and leave
        // EXACTLY ONE copy of the source item queued afterwards. This holds only because the fault branch ack-deletes
        // the source item BEFORE HandleHandlerCrashAsync re-enqueues it via RetryNow: the single drain terminates on
        // fault (one delivery), and the queue converges to a single item (ack removed the original; RetryNow put one
        // back). If the ack-on-fault were removed, the original would survive AND RetryNow would re-enqueue — the item
        // would still be idempotent-by-id single-instance, but the source item would never be consumed, so a
        // resumption re-drive plus RetryNow would keep redelivering it. This test pins the per-drain bound and the
        // steady-state single-instance queue that make poison delivery bounded rather than an infinite hot-loop.
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var poisonStore = new InMemoryWorkflowSchedulerPoisonStore();
        var handler = new AlwaysFaultingSchedulerWorkHandler();
        var drainer = TestSchedulerDrainer.Create(
            queue,
            [handler, new NoopWorkflowSchedulerWorkHandler()],
            new FakeTimeProvider(_now),
            poisonStore: poisonStore,
            retryPolicy: new StubRetryPolicy(new RuntimeDomainRetryDecision(RuntimeDomainRetryMode.RetryNow, null, "retry-now")));

        await queue.EnqueueAsync(NewWorkItem(1));

        // Three successive drains (simulating the pump re-driving the RetryNow-requeued item). Each drain must fault
        // once and stop — never loop within a single drain — and the queue must never accumulate duplicates.
        const int drains = 3;
        for (var i = 0; i < drains; i++)
        {
            var result = await drainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1", maxWorkItems: 1));
            Assert.True(result.StoppedOnFault);
            Assert.Equal(1, result.DrainedCount); // exactly one item processed per drain — no in-drain hot-loop

            var afterDrain = await queue.ListAllAsync(new RuntimeSchedulerWorkQuery("wfexec-1"));
            Assert.Single(afterDrain); // RetryNow re-enqueued exactly one copy; the ack-deleted original did not linger
            Assert.Equal("work-1", afterDrain.Single().WorkItemId);
        }

        // Bounded delivery: exactly one handler invocation per drain, never an unbounded loop.
        Assert.Equal(drains, handler.InvocationCount);

        // The poison record accumulated one failure per drain (single logical item, not duplicated instances).
        var record = Assert.Single(await poisonStore.ListAsync("wfexec-1"));
        Assert.Equal(drains, record.FailureCount);

        // Second leg — the ack-on-fault bite. With a NoopRetry policy (no re-enqueue), a single drain of a faulting
        // handler must leave the queue EMPTY: the ack-delete on fault is the *only* thing that removes the source item.
        // If the ack-on-fault were dropped, the faulted item would linger and the sweep would redeliver it forever.
        var noopQueue = new InMemoryWorkflowSchedulerWorkQueue();
        var noopHandler = new AlwaysFaultingSchedulerWorkHandler();
        var noopDrainer = TestSchedulerDrainer.Create(
            noopQueue,
            [noopHandler, new NoopWorkflowSchedulerWorkHandler()],
            new FakeTimeProvider(_now),
            poisonStore: new InMemoryWorkflowSchedulerPoisonStore(),
            retryPolicy: new NoopRuntimeDomainRetryPolicy());
        await noopQueue.EnqueueAsync(NewWorkItem(2));

        var noopResult = await noopDrainer.DrainAsync(new RuntimeSchedulerDrainRequest("wfexec-1", maxWorkItems: 1));

        Assert.True(noopResult.StoppedOnFault);
        Assert.Equal(1, noopHandler.InvocationCount);
        Assert.Empty(await noopQueue.ListAllAsync(new RuntimeSchedulerWorkQuery("wfexec-1"))); // ack-on-fault removed the item; NoopRetry did not re-enqueue
    }

    private WorkflowSchedulerDrainer NewDrainer(
        InMemoryWorkflowSchedulerWorkQueue queue,
        IWorkflowSchedulerPoisonStore poisonStore,
        IRuntimeDomainRetryPolicy retryPolicy,
        FakeTimeProvider? timeProvider = null,
        IWorkflowSchedulerWorkHandler? handler = null,
        bool hasResumptionRedriver = false) =>
        TestSchedulerDrainer.Create(
            queue,
            [handler ?? new AlwaysFaultingSchedulerWorkHandler(), new NoopWorkflowSchedulerWorkHandler()],
            timeProvider ?? new FakeTimeProvider(_now),
            pauseGate: null,
            workflowExecutionStateStore: null,
            pipelineDispatcher: null,
            faultCapturePolicy: new DefaultRuntimeFaultCapturePolicy(Options.Create(new RuntimeFaultCaptureOptions())),
            poisonStore: poisonStore,
            retryPolicy: retryPolicy,
            hasResumptionRedriver: hasResumptionRedriver);

    private RuntimeSchedulerWorkItem NewWorkItem(
        int index,
        WorkflowExecutionCommandKind commandKind = WorkflowExecutionCommandKind.RunSchedulerWork,
        JsonElement? payload = null)
    {
        using var document = JsonDocument.Parse($$"""{"workItemId":"work-{{index}}"}""");
        return new(
            workItemId: $"work-{index}",
            workflowExecutionId: "wfexec-1",
            commandId: $"command-{index}",
            commandKind: commandKind,
            envelopeId: $"envelope-{index}",
            idempotencyKey: $"wfexec-1:command-{index}:{Guid.NewGuid():N}",
            enqueuedAt: _now,
            recordedAt: _now,
            sequence: index,
            payload: payload ?? document.RootElement.Clone());
    }

    private AssociatedWorkDrainFixture NewAssociatedWorkDrain(
        IWorkflowSchedulerWorkHandler handler,
        IRuntimeDomainRetryPolicy retryPolicy)
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var poisonStore = new InMemoryWorkflowSchedulerPoisonStore();
        var drainer = TestSchedulerDrainer.Create(
            queue,
            [handler, new NoopWorkflowSchedulerWorkHandler()],
            new FakeTimeProvider(_now),
            poisonStore: poisonStore,
            retryPolicy: retryPolicy);
        return new(queue, poisonStore, drainer);
    }

    private RuntimeSchedulerWorkItem NewStartActivityWorkItem(int index)
    {
        var executable = new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test");
        var payload = JsonSerializer.SerializeToElement(new RuntimeStartActivityCommandPayload(
            executable,
            executableNodeId: "node-1",
            activityExecutionId: "activity-1",
            reason: RuntimeStartActivityCommandPayload.ScheduledActivityReason));
        return NewWorkItem(index, WorkflowExecutionCommandKind.StartActivity, payload);
    }

    private ActivityExecutionState NewActivityState() => new(
        Execution: new ActivityExecution("activity-1", "wfexec-1", "node-1", "authored-1", "Elsa.WriteLine", "1.0"),
        Status: ActivityExecutionStatus.Scheduled,
        SubStatus: null,
        ExecutionSequence: 1,
        ScheduledAt: _now,
        StartedAt: null,
        CompletedAt: null,
        SchedulingActivityExecutionId: null,
        ParentActivityExecutionId: null,
        BranchId: null,
        IterationId: null,
        Provenance: ActivitySchedulingProvenance.Empty,
        CallStackDepth: null,
        BookmarkIds: [],
        IncidentIds: [],
        FaultCount: 0,
        AggregateFaultCount: 0,
        Metadata: new Dictionary<string, string>());

    private WorkflowExecutionCommandEnvelope NewEnvelope()
    {
        var command = new WorkflowExecutionCommand(
            CommandId: "command-1",
            WorkflowExecutionId: "wfexec-1",
            Kind: WorkflowExecutionCommandKind.RunSchedulerWork,
            EnqueuedAt: _now,
            Payload: null,
            Metadata: new Dictionary<string, string>());

        return new(
            envelopeId: "envelope-1",
            workflowExecutionId: "wfexec-1",
            command: command,
            idempotencyKey: "wfexec-1:command-1",
            deliveryMode: WorkflowExecutionCommandDeliveryMode.AtLeastOnce,
            enqueuedAt: _now,
            sequence: 1,
            metadata: new Dictionary<string, string>());
    }

    private static JsonElement NewActivityAddressPayload()
    {
        using var document = JsonDocument.Parse("""{"activityExecutionId":"activity-1","executableNodeId":"node-1"}""");
        return document.RootElement.Clone();
    }

    private sealed class FailOnceActivityExecutionStateStore(IActivityExecutionStateStore inner, bool failFirstRead) : IActivityExecutionStateStore
    {
        private bool _hasFailed = !failFirstRead;

        public int FindAttempts { get; private set; }

        public ValueTask<ActivityExecutionState> SaveAsync(ActivityExecutionState state, CancellationToken cancellationToken = default) =>
            inner.SaveAsync(state, cancellationToken);

        public ValueTask<ActivityExecutionState?> FindAsync(
            string workflowExecutionId,
            string activityExecutionId,
            CancellationToken cancellationToken = default)
        {
            FindAttempts++;
            if (!_hasFailed)
            {
                _hasFailed = true;
                return ValueTask.FromException<ActivityExecutionState?>(new InvalidOperationException("transient activity read failure"));
            }

            return inner.FindAsync(workflowExecutionId, activityExecutionId, cancellationToken);
        }

        public ValueTask<RuntimeStorePage<ActivityExecutionState>> ListPageAsync(
            ActivityExecutionStatePageQuery query,
            CancellationToken cancellationToken = default) =>
            inner.ListPageAsync(query, cancellationToken);

        public ValueTask<RuntimeStorePage<ActivityExecutionState>> ListByParentPageAsync(
            ActivityExecutionStateParentPageQuery query,
            CancellationToken cancellationToken = default) =>
            inner.ListByParentPageAsync(query, cancellationToken);
    }

    private static ExpressionInputFailureException NewExpressionFailure(string rootCause) => new(
        ExpressionInputFailureException.ExpressionEvaluationFailed,
        inputKey: "text",
        expressionLanguage: "JavaScript",
        phase: ExpressionInputFailureException.EvaluationPhaseName,
        message: "Input 'text' on executable node 'node-1' failed to evaluate its portable 'JavaScript' expression.",
        innerException: new InvalidOperationException(rootCause));

    private sealed record AssociatedWorkDrainFixture(
        InMemoryWorkflowSchedulerWorkQueue Queue,
        InMemoryWorkflowSchedulerPoisonStore PoisonStore,
        WorkflowSchedulerDrainer Drainer);

    private sealed class AlwaysFaultingSchedulerWorkHandler(
        Func<RuntimeSchedulerWorkItem, Exception>? exceptionFactory = null,
        Func<RuntimeSchedulerWorkItem, ValueTask>? beforeFault = null) : IWorkflowSchedulerWorkHandler
    {
        public string Name => nameof(AlwaysFaultingSchedulerWorkHandler);
        public int InvocationCount { get; private set; }
        public bool CanHandle(RuntimeSchedulerWorkItem workItem) => true;

        public async ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default)
        {
            InvocationCount++;
            if (beforeFault is not null)
                await beforeFault(workItem);
            throw exceptionFactory?.Invoke(workItem) ?? new InvalidOperationException($"Handler crashed for {workItem.WorkItemId}.");
        }
    }

    private sealed class UnreadablePoisonStore : IWorkflowSchedulerPoisonStore
    {
        public ValueTask<RuntimeSchedulerPoisonRecord> RecordAsync(RuntimeSchedulerPoisonRecord record, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("No record is written when the read fails.");
        public ValueTask<RuntimeSchedulerPoisonRecord?> FindAsync(string workflowExecutionId, string workItemId, CancellationToken cancellationToken = default) =>
            throw new IOException("The poison store is unavailable.");
        public ValueTask<IReadOnlyCollection<RuntimeSchedulerPoisonRecord>> ListAsync(string workflowExecutionId, CancellationToken cancellationToken = default) =>
            throw new IOException("The poison store is unavailable.");
    }

    private sealed class ThrowingRetryPolicy : IRuntimeDomainRetryPolicy
    {
        public RuntimeDomainRetryDecision Decide(RuntimeDomainRetryRequest request) =>
            throw new InvalidOperationException("The policy cannot decide.");
    }

    // Refuses every release, as a coalescing segment does for an item a boundary already consumed.
    private sealed class RefusingReleaseWorkQueue(IWorkflowSchedulerWorkQueue inner) : IWorkflowSchedulerWorkQueue
    {
        public bool SupportsClaimTransitions => inner.SupportsClaimTransitions;
        public bool SupportsClaimValidation => inner.SupportsClaimValidation;
        public bool SupportsTargetedDeletion => inner.SupportsTargetedDeletion;
        public ValueTask<RuntimeSchedulerWorkItem> EnqueueAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default) => inner.EnqueueAsync(workItem, cancellationToken);
        public ValueTask<RuntimeStorePage<RuntimeSchedulerWorkItem>> ListAsync(RuntimeSchedulerWorkQuery query, CancellationToken cancellationToken = default) => inner.ListAsync(query, cancellationToken);
        public ValueTask<RuntimeSchedulerWorkItem?> DequeueAsync(string workflowExecutionId, CancellationToken cancellationToken = default) => inner.DequeueAsync(workflowExecutionId, cancellationToken);
        public ValueTask<bool> DeleteAsync(string workflowExecutionId, string workItemId, CancellationToken cancellationToken = default) => inner.DeleteAsync(workflowExecutionId, workItemId, cancellationToken);
        public ValueTask<IReadOnlyCollection<string>> ListPendingWorkflowExecutionIdsAsync(int limit, CancellationToken cancellationToken = default) => inner.ListPendingWorkflowExecutionIdsAsync(limit, cancellationToken);
        public ValueTask<RuntimeSchedulerWorkClaim?> ClaimAsync(RuntimeSchedulerWorkClaimRequest request, CancellationToken cancellationToken = default) => inner.ClaimAsync(request, cancellationToken);
        public ValueTask<RuntimeSchedulerWorkClaimTransitionResult> RenewClaimAsync(RuntimeSchedulerWorkClaim claim, DateTimeOffset now, TimeSpan visibilityTimeout, CancellationToken cancellationToken = default) => inner.RenewClaimAsync(claim, now, visibilityTimeout, cancellationToken);
        public ValueTask<RuntimeSchedulerWorkClaimTransitionResult> CompleteClaimAsync(RuntimeSchedulerWorkClaim claim, CancellationToken cancellationToken = default) => inner.CompleteClaimAsync(claim, cancellationToken);
        public ValueTask<RuntimeSchedulerWorkClaimTransitionResult> ReleaseClaimAsync(RuntimeSchedulerWorkClaim claim, DateTimeOffset visibleAt, CancellationToken cancellationToken = default) =>
            new(RuntimeSchedulerWorkClaimTransitionResult.Stale);
        public ValueTask<RuntimeSchedulerWorkClaimTransitionResult> ConsumeClaimedAsync(ConsumedSchedulerWorkItem consumed, CancellationToken cancellationToken = default) => inner.ConsumeClaimedAsync(consumed, cancellationToken);
    }

    // Times out the first claim completion, as a provider whose acknowledgement times out would.
    private sealed class FailFirstCompleteWorkQueue(IWorkflowSchedulerWorkQueue inner) : IWorkflowSchedulerWorkQueue
    {
        public bool CompleteFailed { get; private set; }
        public bool SupportsClaimTransitions => inner.SupportsClaimTransitions;
        public bool SupportsClaimValidation => inner.SupportsClaimValidation;
        public bool SupportsTargetedDeletion => inner.SupportsTargetedDeletion;
        public ValueTask<RuntimeSchedulerWorkItem> EnqueueAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default) => inner.EnqueueAsync(workItem, cancellationToken);
        public ValueTask<RuntimeStorePage<RuntimeSchedulerWorkItem>> ListAsync(RuntimeSchedulerWorkQuery query, CancellationToken cancellationToken = default) => inner.ListAsync(query, cancellationToken);
        public ValueTask<RuntimeSchedulerWorkItem?> DequeueAsync(string workflowExecutionId, CancellationToken cancellationToken = default) => inner.DequeueAsync(workflowExecutionId, cancellationToken);
        public ValueTask<bool> DeleteAsync(string workflowExecutionId, string workItemId, CancellationToken cancellationToken = default) => inner.DeleteAsync(workflowExecutionId, workItemId, cancellationToken);
        public ValueTask<IReadOnlyCollection<string>> ListPendingWorkflowExecutionIdsAsync(int limit, CancellationToken cancellationToken = default) => inner.ListPendingWorkflowExecutionIdsAsync(limit, cancellationToken);
        public ValueTask<RuntimeSchedulerWorkClaim?> ClaimAsync(RuntimeSchedulerWorkClaimRequest request, CancellationToken cancellationToken = default) => inner.ClaimAsync(request, cancellationToken);
        public ValueTask<RuntimeSchedulerWorkClaimTransitionResult> RenewClaimAsync(RuntimeSchedulerWorkClaim claim, DateTimeOffset now, TimeSpan visibilityTimeout, CancellationToken cancellationToken = default) => inner.RenewClaimAsync(claim, now, visibilityTimeout, cancellationToken);
        public ValueTask<RuntimeSchedulerWorkClaimTransitionResult> ReleaseClaimAsync(RuntimeSchedulerWorkClaim claim, DateTimeOffset visibleAt, CancellationToken cancellationToken = default) => inner.ReleaseClaimAsync(claim, visibleAt, cancellationToken);
        public ValueTask<RuntimeSchedulerWorkClaimTransitionResult> ConsumeClaimedAsync(ConsumedSchedulerWorkItem consumed, CancellationToken cancellationToken = default) => inner.ConsumeClaimedAsync(consumed, cancellationToken);

        public ValueTask<RuntimeSchedulerWorkClaimTransitionResult> CompleteClaimAsync(RuntimeSchedulerWorkClaim claim, CancellationToken cancellationToken = default)
        {
            if (CompleteFailed)
                return inner.CompleteClaimAsync(claim, cancellationToken);
            CompleteFailed = true;
            throw new TimeoutException("The acknowledgement timed out.");
        }
    }

    private sealed class SucceedingSchedulerWorkHandler : IWorkflowSchedulerWorkHandler
    {
        public string Name => nameof(SucceedingSchedulerWorkHandler);
        public int InvocationCount { get; private set; }
        public bool CanHandle(RuntimeSchedulerWorkItem workItem) => true;

        public ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default)
        {
            InvocationCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StubRetryPolicy(RuntimeDomainRetryDecision decision) : IRuntimeDomainRetryPolicy
    {
        public RuntimeDomainRetryDecision Decide(RuntimeDomainRetryRequest request) => decision;
    }
}
