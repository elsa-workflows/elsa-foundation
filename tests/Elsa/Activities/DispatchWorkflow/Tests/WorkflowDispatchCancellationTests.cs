using System.Text.Json;
using Elsa.Activities.DispatchWorkflow.Runtime.Constants;
using Elsa.Activities.DispatchWorkflow.Runtime.Models;
using Elsa.Activities.DispatchWorkflow.Runtime.Services;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Activities.DispatchWorkflow.Tests;

public sealed class WorkflowDispatchCancellationTests
{
    private static readonly DateTimeOffset Now = DispatchWorkflowRuntimeTestFixture.Now;

    [Theory]
    [InlineData(WorkflowDispatchStatus.Pending)]
    [InlineData(WorkflowDispatchStatus.Started)]
    public async Task CancellationCheckpoint_StagesDeterministicWork(WorkflowDispatchStatus status)
    {
        var store = new InMemoryWorkflowDispatchStore();
        var dispatch = await SaveDispatchAsync(store, status);
        var enricher = new WorkflowDispatchCancellationEnricher(store);
        var commit = NewParentCancelCommit();

        var first = await enricher.EnrichAsync(commit);
        var replay = await enricher.EnrichAsync(commit);

        AssertDeterministicCancellationWork(first, replay, dispatch);
    }

    [Theory]
    [InlineData(WorkflowDispatchStatus.Pending, false)]
    [InlineData(WorkflowDispatchStatus.Pending, true)]
    [InlineData(WorkflowDispatchStatus.Started, false)]
    [InlineData(WorkflowDispatchStatus.Started, true)]
    public async Task ActivityCancellationCheckpoint_StagesDeterministicWork(
        WorkflowDispatchStatus status,
        bool cancelWholeParent)
    {
        var store = new InMemoryWorkflowDispatchStore();
        var dispatch = await SaveDispatchAsync(store, status);
        var enricher = new WorkflowDispatchCancellationEnricher(store);
        var commit = NewCancellationCommit(cancelWholeParent, dispatch.ParentActivityExecutionId);

        var first = await enricher.EnrichAsync(commit);
        var replay = await enricher.EnrichAsync(commit);

        AssertDeterministicCancellationWork(first, replay, dispatch);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task CancellationCheckpoint_ReplaysAfterProviderResolutionAndTerminalProjection(
        bool admitBeforeCancellation,
        bool cancelWholeParent)
    {
        var store = new InMemoryWorkflowDispatchStore();
        var dispatch = await SaveDispatchAsync(store, WorkflowDispatchStatus.Pending);
        if (admitBeforeCancellation)
            await store.TryAdmitAsync(dispatch.DispatchId, Now.AddSeconds(1));
        var enricher = new WorkflowDispatchCancellationEnricher(store);
        var commit = NewCancellationCommit(cancelWholeParent, "activity-cancel");
        var first = await enricher.EnrichAsync(commit);
        await store.ApplyCancellationAsync(Assert.Single(first.StateChanges.WorkflowDispatchCancellations));
        if (admitBeforeCancellation)
        {
            var marked = Assert.IsType<WorkflowDispatchRecord>(await store.FindAsync(dispatch.DispatchId));
            await store.SaveAsync(marked.TransitionTo(WorkflowDispatchStatus.Faulted, Now.AddSeconds(2)));
        }

        var replay = await enricher.EnrichAsync(commit);

        Assert.True(WorkflowDispatchCancellationRequest.Equivalent(
            Assert.Single(first.StateChanges.WorkflowDispatchCancellations),
            Assert.Single(replay.StateChanges.WorkflowDispatchCancellations)));
        var firstIntent = Assert.Single(first.PostCommitIntents);
        var replayIntent = Assert.Single(replay.PostCommitIntents);
        Assert.Equal(firstIntent.IntentId, replayIntent.IntentId);
        Assert.Equal(firstIntent.RecordedAt, replayIntent.RecordedAt);
        Assert.True(JsonElement.DeepEquals(firstIntent.Payload!.Value, replayIntent.Payload!.Value));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationCheckpoint_ReplaysCommittedOutboxWhenTerminalWinsAfterEnrichment(
        bool cancelWholeParent)
    {
        var store = new InMemoryWorkflowDispatchStore();
        var dispatch = await SaveDispatchAsync(store, WorkflowDispatchStatus.Started);
        var commit = NewCancellationCommit(cancelWholeParent, "activity-cancel");
        var first = await new WorkflowDispatchCancellationEnricher(store).EnrichAsync(commit);
        var firstIntent = Assert.Single(first.PostCommitIntents);
        await store.SaveAsync(dispatch.TransitionTo(WorkflowDispatchStatus.Completed, Now.AddSeconds(1)));
        var committed = new RuntimePostCommitOutboxItem(
            Identity().ChildCancelOutboxItemId(commit.CommitId),
            firstIntent,
            RuntimePostCommitOutboxStatus.Pending,
            Now,
            Now,
            RuntimePostCommitRetryPolicy.UntilAcknowledged(TimeSpan.FromSeconds(1)));

        var replay = await new WorkflowDispatchCancellationEnricher(
                store,
                new StubOutboxLookupStore(committed))
            .EnrichAsync(commit);

        Assert.True(WorkflowDispatchCancellationRequest.Equivalent(
            Assert.Single(first.StateChanges.WorkflowDispatchCancellations),
            Assert.Single(replay.StateChanges.WorkflowDispatchCancellations)));
        var replayIntent = Assert.Single(replay.PostCommitIntents);
        Assert.Equal(firstIntent.IntentId, replayIntent.IntentId);
        Assert.Equal(firstIntent.RecordedAt, replayIntent.RecordedAt);
        Assert.True(JsonElement.DeepEquals(firstIntent.Payload!.Value, replayIntent.Payload!.Value));
    }

    [Theory]
    [InlineData(WorkflowDispatchStatus.Completed, false)]
    [InlineData(WorkflowDispatchStatus.Completed, true)]
    [InlineData(WorkflowDispatchStatus.Faulted, false)]
    [InlineData(WorkflowDispatchStatus.Faulted, true)]
    [InlineData(WorkflowDispatchStatus.Cancelled, false)]
    [InlineData(WorkflowDispatchStatus.Cancelled, true)]
    [InlineData(WorkflowDispatchStatus.DispatchFailed, false)]
    [InlineData(WorkflowDispatchStatus.DispatchFailed, true)]
    public async Task TerminalDispatch_DoesNotCreateCancelWork(
        WorkflowDispatchStatus status,
        bool cancelWholeParent)
    {
        var store = new InMemoryWorkflowDispatchStore();
        await SaveDispatchAsync(store, status);

        var enriched = await new WorkflowDispatchCancellationEnricher(store)
            .EnrichAsync(NewCancellationCommit(cancelWholeParent, "activity-cancel"));

        Assert.Empty(enriched.StateChanges.WorkflowDispatchCancellations);
        Assert.Empty(enriched.PostCommitIntents);
    }

    [Fact]
    public async Task ActivityCancellationCheckpoint_DoesNotCancelSiblingDispatch()
    {
        var store = new InMemoryWorkflowDispatchStore();
        await SaveDispatchAsync(store, WorkflowDispatchStatus.Started);

        var enriched = await new WorkflowDispatchCancellationEnricher(store)
            .EnrichAsync(NewCancellationCommit(cancelWholeParent: false, "activity-sibling"));

        Assert.Empty(enriched.StateChanges.WorkflowDispatchCancellations);
        Assert.Empty(enriched.PostCommitIntents);
    }

    [Theory]
    [InlineData(ActivityExecutionStatus.Running, RuntimeStateChangeOperation.Upsert)]
    [InlineData(ActivityExecutionStatus.Cancelled, RuntimeStateChangeOperation.Delete)]
    public async Task NonCancellationActivityChange_DoesNotCreateCancelWork(
        ActivityExecutionStatus status,
        RuntimeStateChangeOperation operation)
    {
        var store = new InMemoryWorkflowDispatchStore();
        await SaveDispatchAsync(store, WorkflowDispatchStatus.Started);

        var enriched = await new WorkflowDispatchCancellationEnricher(store)
            .EnrichAsync(NewActivityChangeCommit("activity-cancel", status, operation));

        Assert.Empty(enriched.StateChanges.WorkflowDispatchCancellations);
        Assert.Empty(enriched.PostCommitIntents);
    }

    [Fact]
    public async Task ActivityCancellationCheckpoint_FindsMatchingDispatchOnLaterPage()
    {
        var store = new InMemoryWorkflowDispatchStore();
        for (var index = 0; index < WorkflowDispatchQuery.MaximumTake; index++)
        {
            await store.SaveAsync(NewDispatch(
                $"activity-page-{index:D3}",
                WorkflowDispatchStatus.Pending,
                createdAt: Now.AddMinutes(-3)));
        }
        var dispatch = NewDispatch(
            "activity-cancel",
            WorkflowDispatchStatus.Pending,
            createdAt: Now.AddMinutes(-2));
        await store.SaveAsync(dispatch);

        var enriched = await new WorkflowDispatchCancellationEnricher(store)
            .EnrichAsync(NewCancellationCommit(cancelWholeParent: false, "activity-cancel"));

        Assert.Equal(dispatch.DispatchId, Assert.Single(enriched.StateChanges.WorkflowDispatchCancellations).DispatchId);
        Assert.Equal(
            new WorkflowDispatchIdentity("parent-cancel", "activity-cancel").ChildCancelIntentId,
            Assert.Single(enriched.PostCommitIntents).IntentId);
    }

    [Fact]
    public async Task CancellationCheckpoint_RejectsANonAdvancingProviderCursor()
    {
        var store = new NonAdvancingQueryDispatchStore();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new WorkflowDispatchCancellationEnricher(store)
                .EnrichAsync(NewParentCancelCommit())
                .AsTask());

        Assert.Contains("did not advance its continuation cursor", exception.Message, StringComparison.Ordinal);
        Assert.Equal(2, store.QueryCount);
    }

    [Theory]
    [InlineData(WorkflowDispatchMode.WaitForCompletion, false, false)]
    [InlineData(WorkflowDispatchMode.WaitForCompletion, false, true)]
    [InlineData(WorkflowDispatchMode.FireAndForget, false, false)]
    [InlineData(WorkflowDispatchMode.FireAndForget, false, true)]
    [InlineData(WorkflowDispatchMode.FireAndForget, true, false)]
    [InlineData(WorkflowDispatchMode.FireAndForget, true, true)]
    public async Task IndependentDispatch_DoesNotCreateCancelWork(
        WorkflowDispatchMode mode,
        bool authoredPolicy,
        bool cancelWholeParent)
    {
        var store = new InMemoryWorkflowDispatchStore();
        await SaveDispatchAsync(store, WorkflowDispatchStatus.Started, mode, authoredPolicy);

        var enriched = await new WorkflowDispatchCancellationEnricher(store)
            .EnrichAsync(NewCancellationCommit(cancelWholeParent, "activity-cancel"));

        Assert.Empty(enriched.StateChanges.WorkflowDispatchCancellations);
        Assert.Empty(enriched.PostCommitIntents);
    }

    [Fact]
    public async Task CancelledBeforeAdmission_SuppressesActorDelivery()
    {
        var dispatchStore = new InMemoryWorkflowDispatchStore();
        var dispatch = NewDispatch(WorkflowDispatchStatus.Pending);
        await dispatchStore.SaveAsync(dispatch);
        await dispatchStore.ApplyCancellationAsync(NewCancelRequest());
        var actorProvider = new RecordingActorProvider();
        var executor = new ChildCancelExecutor(
            actorProvider,
            new InMemoryWorkflowExecutionStateStore(),
            dispatchStore);

        await executor.HandleAsync(NewCancelIntent());

        Assert.Empty(actorProvider.Activations);
        Assert.Empty(actorProvider.Actor.Envelopes);
    }

    [Theory]
    [InlineData(WorkflowExecutionStatus.Completed)]
    [InlineData(WorkflowExecutionStatus.Faulted)]
    [InlineData(WorkflowExecutionStatus.Cancelled)]
    public async Task TerminalChild_SuppressesActorDelivery(WorkflowExecutionStatus status)
    {
        var dispatchStore = new InMemoryWorkflowDispatchStore();
        await SaveDispatchAsync(dispatchStore, WorkflowDispatchStatus.Started);
        var stateStore = new InMemoryWorkflowExecutionStateStore();
        await stateStore.SaveAsync(NewChildState(status));
        var actorProvider = new RecordingActorProvider();
        var executor = new ChildCancelExecutor(actorProvider, stateStore, dispatchStore);

        await executor.HandleAsync(NewCancelIntent());

        Assert.Empty(actorProvider.Activations);
        Assert.Empty(actorProvider.Actor.Envelopes);
        Assert.Equal(status, (await stateStore.FindAsync(Identity().ChildWorkflowExecutionId))!.Status);
    }

    [Fact]
    public async Task ActiveChild_ReceivesDeterministicCancelOnce()
    {
        var dispatchStore = new InMemoryWorkflowDispatchStore();
        var dispatch = await SaveDispatchAsync(dispatchStore, WorkflowDispatchStatus.Started);
        await dispatchStore.ApplyCancellationAsync(NewCancelRequest());
        var stateStore = new InMemoryWorkflowExecutionStateStore();
        await stateStore.SaveAsync(NewChildState(WorkflowExecutionStatus.Running));
        var actorProvider = new RecordingActorProvider(async envelope =>
        {
            await stateStore.SaveAsync(NewChildState(WorkflowExecutionStatus.Cancelled));
            return NewDispatchResult(envelope, WorkflowExecutionCommandDispatchStatus.Accepted);
        });
        var executor = new ChildCancelExecutor(actorProvider, stateStore, dispatchStore);
        var intent = NewCancelIntent();

        await executor.HandleAsync(intent);
        await executor.HandleAsync(intent);
        await executor.HandleAsync(intent);

        var activation = Assert.Single(actorProvider.Activations);
        Assert.Equal(dispatch.ChildWorkflowExecutionId, activation.WorkflowExecutionId);
        Assert.Equal(WorkflowExecutionActorActivationReason.ControlPlaneCommand, activation.Reason);
        Assert.Equal(intent.RecordedAt, activation.RequestedAt);
        Assert.Equal(dispatch.Authority.SystemIdentity, activation.RequestedBy);
        Assert.Equal(WorkflowExecutionActorCapabilities.None, activation.RequiredCapabilities);
        Assert.Equal(dispatch.Partition, activation.Partition);
        Assert.Equal(dispatch.DispatchId, activation.Metadata[RuntimeMetadataKeys.DispatchId]);
        Assert.Equal(dispatch.ChildWorkflowExecutionId, activation.Metadata[RuntimeMetadataKeys.ChildWorkflowExecutionId]);
        Assert.Equal(2, activation.Metadata.Count);

        var identity = Identity();
        var envelope = Assert.Single(actorProvider.Actor.Envelopes);
        Assert.Equal(identity.ChildCancelEnvelopeId, envelope.EnvelopeId);
        Assert.Equal(dispatch.ChildWorkflowExecutionId, envelope.WorkflowExecutionId);
        Assert.Equal(identity.ChildCancelIdempotencyKey, envelope.IdempotencyKey);
        Assert.Equal(WorkflowExecutionCommandDeliveryMode.AtLeastOnce, envelope.DeliveryMode);
        Assert.Equal(intent.RecordedAt, envelope.EnqueuedAt);
        Assert.Null(envelope.Sequence);
        Assert.Equal(dispatch.Partition, envelope.Partition);
        Assert.Equal(activation.Metadata, envelope.Metadata);
        Assert.Equal(identity.ChildCancelCommandId, envelope.Command.CommandId);
        Assert.Equal(dispatch.ChildWorkflowExecutionId, envelope.Command.WorkflowExecutionId);
        Assert.Equal(WorkflowExecutionCommandKind.Cancel, envelope.Command.Kind);
        Assert.Equal(intent.RecordedAt, envelope.Command.EnqueuedAt);
        Assert.Null(envelope.Command.Payload);
        Assert.Equal(envelope.Metadata, envelope.Command.Metadata);
        Assert.Equal(WorkflowExecutionStatus.Cancelled, (await stateStore.FindAsync(dispatch.ChildWorkflowExecutionId))!.Status);
    }

    [Theory]
    [InlineData(WorkflowExecutionCommandDispatchStatus.Accepted)]
    [InlineData(WorkflowExecutionCommandDispatchStatus.AcceptedButFaulted)]
    [InlineData(WorkflowExecutionCommandDispatchStatus.Duplicate)]
    [InlineData(WorkflowExecutionCommandDispatchStatus.Rejected)]
    [InlineData(WorkflowExecutionCommandDispatchStatus.Deferred)]
    public async Task NonterminalAfterDelivery_RemainsRetryable(
        WorkflowExecutionCommandDispatchStatus status)
    {
        var dispatchStore = new InMemoryWorkflowDispatchStore();
        await SaveDispatchAsync(dispatchStore, WorkflowDispatchStatus.Started);
        var stateStore = new InMemoryWorkflowExecutionStateStore();
        await stateStore.SaveAsync(NewChildState(WorkflowExecutionStatus.Running));
        var actorProvider = new RecordingActorProvider(envelope =>
            ValueTask.FromResult(NewDispatchResult(envelope, status)));
        var executor = new ChildCancelExecutor(actorProvider, stateStore, dispatchStore);

        await Assert.ThrowsAsync<ChildCancelDeferredException>(
            () => executor.HandleAsync(NewCancelIntent()).AsTask());

        Assert.Single(actorProvider.Activations);
        Assert.Single(actorProvider.Actor.Envelopes);
        Assert.Equal(WorkflowExecutionStatus.Running, (await stateStore.FindAsync(Identity().ChildWorkflowExecutionId))!.Status);
    }

    [Fact]
    public async Task MissingAdmittedChild_RemainsRetryableWithoutActivation()
    {
        var dispatchStore = new InMemoryWorkflowDispatchStore();
        await SaveDispatchAsync(dispatchStore, WorkflowDispatchStatus.Started);
        var actorProvider = new RecordingActorProvider();
        var executor = new ChildCancelExecutor(
            actorProvider,
            new InMemoryWorkflowExecutionStateStore(),
            dispatchStore);

        await Assert.ThrowsAsync<ChildCancelDeferredException>(
            () => executor.HandleAsync(NewCancelIntent()).AsTask());

        Assert.Empty(actorProvider.Activations);
        Assert.Empty(actorProvider.Actor.Envelopes);
    }

    [Fact]
    public async Task VisibleChildWithoutDurableAdmission_RemainsRetryableWithoutActivation()
    {
        var dispatchStore = new InMemoryWorkflowDispatchStore();
        await SaveDispatchAsync(dispatchStore, WorkflowDispatchStatus.Pending);
        var stateStore = new InMemoryWorkflowExecutionStateStore();
        await stateStore.SaveAsync(NewChildState(WorkflowExecutionStatus.Running));
        var actorProvider = new RecordingActorProvider();
        var executor = new ChildCancelExecutor(actorProvider, stateStore, dispatchStore);

        await Assert.ThrowsAsync<ChildCancelDeferredException>(
            () => executor.HandleAsync(NewCancelIntent()).AsTask());

        Assert.Empty(actorProvider.Activations);
        Assert.Empty(actorProvider.Actor.Envelopes);
    }

    [Fact]
    public async Task CancelledAdmission_SuppressesChildStart()
    {
        var dispatchStore = new InMemoryWorkflowDispatchStore();
        await dispatchStore.SaveAsync(NewDispatch(WorkflowDispatchStatus.Pending));
        await dispatchStore.ApplyCancellationAsync(NewCancelRequest());
        var startDispatcher = new RecordingStartDispatcher();
        var executor = new ChildStartExecutor(startDispatcher, dispatchStore, new FixedTimeProvider(Now.AddMinutes(1)));

        await executor.HandleAsync(NewStartIntent());
        await executor.HandleAsync(NewStartIntent());

        Assert.Empty(startDispatcher.Requests);
        var dispatch = await dispatchStore.FindAsync(Identity().DispatchId);
        Assert.Equal(WorkflowDispatchStatus.Cancelled, dispatch!.Status);
        Assert.True(WorkflowDispatchLifecycle.WasCancelledBeforeAdmission(dispatch));
    }

    [Fact]
    public async Task AdmittedStart_ReplaysThroughStableIdentity()
    {
        var dispatchStore = new InMemoryWorkflowDispatchStore();
        await dispatchStore.SaveAsync(NewDispatch(WorkflowDispatchStatus.Pending));
        var startDispatcher = new RecordingStartDispatcher();
        var executor = new ChildStartExecutor(startDispatcher, dispatchStore, new FixedTimeProvider(Now.AddMinutes(1)));
        var intent = NewStartIntent();

        await executor.HandleAsync(intent);
        await executor.HandleAsync(intent);

        Assert.Equal(2, startDispatcher.Requests.Count);
        Assert.All(startDispatcher.Requests, request =>
        {
            Assert.Equal(Identity().ChildWorkflowExecutionId, request.WorkflowExecutionId);
            Assert.Equal(Identity().StartIdempotencyKey, request.IdempotencyKey);
        });
        Assert.Equal(WorkflowDispatchStatus.Started, (await dispatchStore.FindAsync(Identity().DispatchId))!.Status);
    }

    private static RuntimeCheckpointCommit NewParentCancelCommit()
        => NewCancellationCommit(cancelWholeParent: true);

    private static RuntimeCheckpointCommit NewCancellationCommit(
        bool cancelWholeParent,
        params string[] cancelledActivityExecutionIds)
    {
        var activityChanges = cancelledActivityExecutionIds
            .Select(activityExecutionId =>
            {
                var state = NewActivityState(activityExecutionId, ActivityExecutionStatus.Cancelled);
                return new RuntimeStateChange<ActivityExecutionState>(
                    activityExecutionId,
                    RuntimeStateChangeOperation.Upsert,
                    state,
                    new Dictionary<string, string>());
            })
            .ToArray();
        return NewCancellationCommit(cancelWholeParent, activityChanges);
    }

    private static RuntimeCheckpointCommit NewActivityChangeCommit(
        string activityExecutionId,
        ActivityExecutionStatus status,
        RuntimeStateChangeOperation operation)
    {
        var state = NewActivityState(activityExecutionId, status);
        return NewCancellationCommit(
            cancelWholeParent: false,
            [
                new RuntimeStateChange<ActivityExecutionState>(
                    activityExecutionId,
                    operation,
                    state,
                    new Dictionary<string, string>())
            ]);
    }

    private static RuntimeCheckpointCommit NewCancellationCommit(
        bool cancelWholeParent,
        IReadOnlyCollection<RuntimeStateChange<ActivityExecutionState>> activityChanges)
    {
        var execution = NewParentState(
            cancelWholeParent ? WorkflowExecutionStatus.Cancelled : WorkflowExecutionStatus.Running);
        var workflowChange = cancelWholeParent
            ? new RuntimeStateChange<WorkflowExecutionState>(
                execution.WorkflowExecutionId,
                RuntimeStateChangeOperation.Upsert,
                execution,
                new Dictionary<string, string>())
            : null;
        return new RuntimeCheckpointCommit(
            "commit-parent-cancelled",
            new RuntimeCheckpoint(
                "checkpoint-parent-cancelled",
                cancelWholeParent ? RuntimeCheckpointNames.WorkflowCancelled : "ActivitySubtreeCancelled",
                execution.WorkflowExecutionId,
                Now,
                [],
                new Dictionary<string, string>()),
            new RuntimeCheckpointStateChangeSet(
                workflowChange,
                null,
                activityChanges, [], [], [], []),
            [],
            new Dictionary<string, string>());
    }

    private static ActivityExecutionState NewActivityState(
        string activityExecutionId,
        ActivityExecutionStatus status) => new(
        new ActivityExecution(
            activityExecutionId,
            "parent-cancel",
            $"node-{activityExecutionId}",
            activityExecutionId,
            DispatchWorkflowConstants.ActivityType,
            "1.0.0"),
        status,
        null,
        Now.AddMinutes(-2),
        Now.AddMinutes(-2),
        status == ActivityExecutionStatus.Cancelled ? Now : null,
        null,
        null,
        null,
        null,
        0,
        [],
        [],
        0,
        0,
        new Dictionary<string, string>());

    private static void AssertDeterministicCancellationWork(
        RuntimeCheckpointCommit first,
        RuntimeCheckpointCommit replay,
        WorkflowDispatchRecord dispatch)
    {
        var request = Assert.Single(first.StateChanges.WorkflowDispatchCancellations);
        Assert.Equal(dispatch.DispatchId, request.DispatchId);
        Assert.Equal(dispatch.ParentWorkflowExecutionId, request.ParentWorkflowExecutionId);
        Assert.Equal(dispatch.ParentActivityExecutionId, request.ParentActivityExecutionId);
        Assert.Equal(dispatch.ChildWorkflowExecutionId, request.ChildWorkflowExecutionId);
        Assert.Equal(Now, request.RequestedAt);
        Assert.True(WorkflowDispatchCancellationRequest.Equivalent(
            request,
            Assert.Single(replay.StateChanges.WorkflowDispatchCancellations)));

        var identity = new WorkflowDispatchIdentity(
            dispatch.ParentWorkflowExecutionId,
            dispatch.ParentActivityExecutionId);
        var intent = Assert.Single(first.PostCommitIntents);
        Assert.Equal(identity.ChildCancelIntentId, intent.IntentId);
        Assert.Equal(dispatch.ParentWorkflowExecutionId, intent.WorkflowExecutionId);
        Assert.Equal(dispatch.ParentActivityExecutionId, intent.ActivityExecutionId);
        Assert.Equal(DispatchWorkflowConstants.CancelChildIntentKind, intent.Kind);
        Assert.Equal(identity.ChildCancelIdempotencyKey, intent.IdempotencyKey);
        Assert.Equal(Now, intent.RecordedAt);
        Assert.Equal(dispatch.DispatchId, intent.Metadata[RuntimeMetadataKeys.DispatchId]);
        Assert.Equal(dispatch.ChildWorkflowExecutionId, intent.Metadata[RuntimeMetadataKeys.ChildWorkflowExecutionId]);
        Assert.Equal(2, intent.Metadata.Count);
        var payload = intent.Payload!.Value.Deserialize<WorkflowDispatchChildCancelPayload>(JsonOptions())!;
        Assert.Equal(dispatch.DispatchId, payload.DispatchId);
        Assert.Equal(dispatch.ParentWorkflowExecutionId, payload.ParentWorkflowExecutionId);
        Assert.Equal(dispatch.ParentActivityExecutionId, payload.ParentActivityExecutionId);
        Assert.Equal(dispatch.ChildWorkflowExecutionId, payload.ChildWorkflowExecutionId);
        Assert.True(JsonElement.DeepEquals(
            intent.Payload.Value,
            Assert.Single(replay.PostCommitIntents).Payload!.Value));
    }

    private static WorkflowDispatchRecord NewDispatch(
        WorkflowDispatchStatus status,
        WorkflowDispatchMode mode = WorkflowDispatchMode.WaitForCompletion,
        bool authoredPolicy = true) =>
        NewDispatch("activity-cancel", status, mode, authoredPolicy);

    private static WorkflowDispatchRecord NewDispatch(
        string activityExecutionId,
        WorkflowDispatchStatus status,
        WorkflowDispatchMode mode = WorkflowDispatchMode.WaitForCompletion,
        bool authoredPolicy = true,
        DateTimeOffset? createdAt = null)
    {
        var identity = new WorkflowDispatchIdentity("parent-cancel", activityExecutionId);
        var metadata = new Dictionary<string, string>();
        WorkflowDispatchLifecycle.SetEffectiveCancellationPolicy(metadata, mode, authoredPolicy);
        return new WorkflowDispatchRecord(
            identity.DispatchId,
            "parent-cancel",
            activityExecutionId,
            identity.ChildWorkflowExecutionId,
            ChildIdentity(),
            ChildSource(),
            mode,
            status,
            "correlation-cancel",
            "tenant-42",
            new WorkflowExecutionPartition("partition-eu"),
            WorkflowRunKind.PublishedRun,
            new WorkflowExecutionAuthoritySnapshot("parent-cancel", "root-initiator"),
            [],
            createdAt ?? Now.AddMinutes(-2),
            Now.AddMinutes(-1),
            metadata);
    }

    private static async ValueTask<WorkflowDispatchRecord> SaveDispatchAsync(
        InMemoryWorkflowDispatchStore store,
        WorkflowDispatchStatus status,
        WorkflowDispatchMode mode = WorkflowDispatchMode.WaitForCompletion,
        bool authoredPolicy = true)
    {
        var pending = NewDispatch(WorkflowDispatchStatus.Pending, mode, authoredPolicy);
        await store.SaveAsync(pending);
        if (status == WorkflowDispatchStatus.Pending)
            return pending;

        var successor = status == WorkflowDispatchStatus.DispatchFailed
            ? pending.TransitionToDispatchFailed(Now.AddMinutes(-1))
            : pending.TransitionTo(status, Now.AddMinutes(-1));
        await store.SaveAsync(successor);
        return successor;
    }

    private static WorkflowDispatchCancellationRequest NewCancelRequest() => new(
        Identity().DispatchId,
        "parent-cancel",
        "activity-cancel",
        Identity().ChildWorkflowExecutionId,
        Now);

    private static RuntimePostCommitIntent NewCancelIntent()
    {
        var identity = Identity();
        var payload = new WorkflowDispatchChildCancelPayload(
            identity.DispatchId,
            "parent-cancel",
            "activity-cancel",
            identity.ChildWorkflowExecutionId);
        return new RuntimePostCommitIntent(
            identity.ChildCancelIntentId,
            "parent-cancel",
            DispatchWorkflowConstants.CancelChildIntentKind,
            Now,
            "activity-cancel",
            identity.ChildCancelIdempotencyKey,
            JsonSerializer.SerializeToElement(payload, JsonOptions()),
            new Dictionary<string, string>
            {
                [RuntimeMetadataKeys.DispatchId] = identity.DispatchId,
                [RuntimeMetadataKeys.ChildWorkflowExecutionId] = identity.ChildWorkflowExecutionId
            });
    }

    private static RuntimePostCommitIntent NewStartIntent()
    {
        var identity = Identity();
        var dispatch = NewDispatch(WorkflowDispatchStatus.Pending);
        var payload = new WorkflowDispatchStartPayload(
            identity.DispatchId,
            dispatch.ParentWorkflowExecutionId,
            dispatch.ParentActivityExecutionId,
            identity.ChildWorkflowExecutionId,
            dispatch.ChildExecutable,
            dispatch.ChildSource,
            new Dictionary<string, JsonElement>(),
            dispatch.CorrelationId,
            dispatch.TenantId,
            dispatch.Partition,
            dispatch.RunKind,
            dispatch.Authority,
            parentExecutable: null,
            dispatchNodeId: null,
            dispatchNestingDepth: dispatch.DispatchNestingDepth);
        return new RuntimePostCommitIntent(
            identity.StartIntentId,
            dispatch.ParentWorkflowExecutionId,
            DispatchWorkflowConstants.StartChildIntentKind,
            Now,
            dispatch.ParentActivityExecutionId,
            identity.StartIdempotencyKey,
            JsonSerializer.SerializeToElement(payload, JsonOptions()));
    }

    private static WorkflowExecutionState NewParentState(WorkflowExecutionStatus status) => new(
        "parent-cancel",
        new WorkflowExecutableIdentity("artifact-parent", "definition-parent", "version-parent", "1.0.0", "sha256:parent"),
        status,
        null,
        Now.AddMinutes(-3),
        Now.AddMinutes(-3),
        Now,
        Now,
        "correlation-parent",
        null,
        "tenant-42",
        new Dictionary<string, string>());

    private static WorkflowExecutionState NewChildState(WorkflowExecutionStatus status) => new(
        Identity().ChildWorkflowExecutionId,
        ChildIdentity(),
        status,
        null,
        Now.AddMinutes(-1),
        Now.AddMinutes(-1),
        Now,
        status.IsTerminal() ? Now : null,
        "correlation-cancel",
        "parent-cancel",
        "tenant-42",
        new Dictionary<string, string>())
    {
        RunKind = WorkflowRunKind.PublishedRun,
        PinnedSource = ChildSource(),
        Partition = new WorkflowExecutionPartition("partition-eu"),
        Authority = new WorkflowExecutionAuthoritySnapshot("parent-cancel", "root-initiator"),
        DispatchNestingDepth = 1
    };

    private static WorkflowExecutionCommandDispatchResult NewDispatchResult(
        WorkflowExecutionCommandEnvelope envelope,
        WorkflowExecutionCommandDispatchStatus status) => new(
        envelope.EnvelopeId,
        envelope.WorkflowExecutionId,
        status,
        Now,
        status is WorkflowExecutionCommandDispatchStatus.Rejected or
            WorkflowExecutionCommandDispatchStatus.Deferred or
            WorkflowExecutionCommandDispatchStatus.AcceptedButFaulted
            ? "set by test"
            : null);

    private static WorkflowDispatchIdentity Identity() => new("parent-cancel", "activity-cancel");

    private static WorkflowExecutableIdentity ChildIdentity() =>
        new("artifact-child", "definition-child", "version-child", "1.0.0", "sha256:child");

    private static WorkflowExecutableSourceProvenance ChildSource() =>
        new("source-child", "WorkflowDefinitionVersion", "version-child", "1.0.0", "definition-child", "version-child", "1.0.0", "publication-child", "slot-child");

    private static JsonSerializerOptions JsonOptions() => new(JsonSerializerDefaults.Web);

    private sealed class StubOutboxLookupStore(RuntimePostCommitOutboxItem item) : IPostCommitOutboxLookupStore
    {
        public ValueTask<RuntimePostCommitOutboxItem?> FindAsync(
            string outboxItemId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<RuntimePostCommitOutboxItem?>(
                StringComparer.Ordinal.Equals(outboxItemId, item.OutboxItemId) ? item : null);
    }

    private sealed class NonAdvancingQueryDispatchStore : IWorkflowDispatchStore, IWorkflowDispatchQueryStore
    {
        private readonly IReadOnlyCollection<WorkflowDispatchRecord> _page = Enumerable.Range(0, WorkflowDispatchQuery.MaximumTake)
            .Select(index =>
            {
                var activityExecutionId = $"activity-cursor-{index:D3}";
                var identity = new WorkflowDispatchIdentity("parent-cancel", activityExecutionId);
                return new WorkflowDispatchRecord(
                    identity.DispatchId,
                    "parent-cancel",
                    activityExecutionId,
                    identity.ChildWorkflowExecutionId,
                    ChildIdentity(),
                    ChildSource(),
                    WorkflowDispatchMode.FireAndForget,
                    WorkflowDispatchStatus.Completed,
                    null,
                    "tenant-42",
                    new WorkflowExecutionPartition("partition-eu"),
                    WorkflowRunKind.PublishedRun,
                    new WorkflowExecutionAuthoritySnapshot("parent-cancel", "root-initiator"),
                    [],
                    Now.AddMinutes(-2),
                    Now.AddMinutes(-1));
            })
            .ToArray();

        public int QueryCount { get; private set; }

        public ValueTask<IReadOnlyCollection<WorkflowDispatchRecord>> QueryAsync(
            WorkflowDispatchQuery query,
            CancellationToken cancellationToken = default)
        {
            QueryCount++;
            return ValueTask.FromResult(_page);
        }

        public ValueTask<WorkflowDispatchRecord> SaveAsync(
            WorkflowDispatchRecord record,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<WorkflowDispatchRecord?> FindAsync(
            string dispatchId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<IReadOnlyCollection<WorkflowDispatchRecord>> ListAsync(
            string parentWorkflowExecutionId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingActorProvider(
        Func<WorkflowExecutionCommandEnvelope, ValueTask<WorkflowExecutionCommandDispatchResult>>? dispatch = null)
        : IWorkflowExecutionActorProvider
    {
        public List<WorkflowExecutionActorActivationRequest> Activations { get; } = [];
        public RecordingActor Actor { get; } = new(dispatch);
        public WorkflowExecutionActorCapabilities Capabilities => WorkflowExecutionActorCapabilities.None;

        public ValueTask<IWorkflowExecutionActor> GetAgentAsync(
            WorkflowExecutionActorActivationRequest request,
            CancellationToken cancellationToken = default)
        {
            Activations.Add(request);
            return ValueTask.FromResult<IWorkflowExecutionActor>(Actor);
        }

        public ValueTask PassivateAsync(
            WorkflowExecutionActorPassivationRequest request,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class RecordingActor(
        Func<WorkflowExecutionCommandEnvelope, ValueTask<WorkflowExecutionCommandDispatchResult>>? dispatch)
        : IWorkflowExecutionActor
    {
        public List<WorkflowExecutionCommandEnvelope> Envelopes { get; } = [];
        public WorkflowExecutionActorDescriptor Descriptor { get; } = new(
            Identity().ChildWorkflowExecutionId,
            "actor-child",
            "test",
            WorkflowExecutionActorStatus.Active,
            WorkflowExecutionActorCapabilities.None,
            Now);

        public ValueTask<WorkflowExecutionCommandDispatchResult> EnqueueAsync(
            WorkflowExecutionCommandEnvelope envelope,
            CancellationToken cancellationToken = default)
        {
            Envelopes.Add(envelope);
            return dispatch is null
                ? ValueTask.FromResult(NewDispatchResult(envelope, WorkflowExecutionCommandDispatchStatus.Accepted))
                : dispatch(envelope);
        }
    }

    private sealed class RecordingStartDispatcher : IWorkflowStartDispatcher
    {
        public List<WorkflowExecutionStartDispatchRequest> Requests { get; } = [];

        public ValueTask<WorkflowExecutionStartDispatchResult> DispatchAsync(
            WorkflowExecutionStartDispatchRequest request,
            WorkflowExecutableReferenceScope requiredScope = WorkflowExecutableReferenceScope.Published,
            WorkflowExecutionCommandDispatchOptions? dispatchOptions = null,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return ValueTask.FromResult(new WorkflowExecutionStartDispatchResult(
                request.WorkflowExecutionId!,
                ChildIdentity(),
                new WorkflowExecutionCommandDispatchResult(
                    "envelope-start",
                    request.WorkflowExecutionId!,
                    WorkflowExecutionCommandDispatchStatus.Accepted,
                    Now),
                new WorkflowExecutionActorDescriptor(
                    request.WorkflowExecutionId!,
                    "actor-start",
                    "test",
                    WorkflowExecutionActorStatus.Active,
                    WorkflowExecutionActorCapabilities.None,
                    Now),
                ChildSource()));
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
