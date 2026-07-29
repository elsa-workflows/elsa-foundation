using System.Text.Json;
using Elsa.Activities.DispatchWorkflow.Runtime.Constants;
using Elsa.Activities.DispatchWorkflow.Runtime.Models;
using Elsa.Activities.DispatchWorkflow.Runtime.Services;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Elsa.Activities.DispatchWorkflow.Tests;

public sealed class WorkflowDispatchDeliveryFailureProjectorTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(WorkflowDispatchMode.WaitForCompletion, true)]
    [InlineData(WorkflowDispatchMode.FireAndForget, false)]
    public async Task ExhaustionProjectsOnlySafeDeadLetterEvidenceAndWaitOnlyFollowUp(
        WorkflowDispatchMode mode,
        bool expectsFollowUp)
    {
        var dispatchStore = new InMemoryWorkflowDispatchStore();
        var dispatch = NewDispatch(mode);
        await dispatchStore.SaveAsync(dispatch);
        var pending = StartItem(dispatch, "secret provider payload");
        var claim = RuntimePostCommitOutboxClaimTransitions.Claim(
            pending,
            new RuntimePostCommitOutboxClaimRequest("owner-1", Now, TimeSpan.FromMinutes(1), 1));
        var failure = new RuntimePostCommitOutboxDeliveryResult(
            pending.OutboxItemId,
            RuntimePostCommitOutboxStatus.FailedRetryable,
            Now.AddSeconds(1),
            "secret exception and provider reason");

        var projection = Assert.IsType<PostCommitFailureProjection>(
            await new WorkflowDispatchDeliveryFailureProjector(dispatchStore)
                .ProjectAsync(claim.Item, failure));

        var projected = projection.WorkflowDispatch;
        var identity = Identity(dispatch);
        Assert.Equal(WorkflowDispatchStatus.DispatchFailed, projected.Status);
        Assert.Equal(dispatch.DispatchNestingDepth, projected.DispatchNestingDepth);
        Assert.Equal(pending.OutboxItemId, WorkflowDispatchLifecycle.ReadDeliveryDeadLetterId(projected));
        Assert.Equal(identity.DeliveryIncidentId(0), WorkflowDispatchLifecycle.ReadDeliveryIncidentId(projected));
        Assert.Equal(0, WorkflowDispatchLifecycle.ReadDeliveryGeneration(projected));
        Assert.Equal(1, WorkflowDispatchLifecycle.ReadDeliveryAttemptCount(projected));
        Assert.Equal(Now.AddSeconds(1), WorkflowDispatchLifecycle.ReadDeliveryFailedAt(projected));
        Assert.DoesNotContain(projected.Metadata.Values, value => value.Contains("secret", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(mode == WorkflowDispatchMode.FireAndForget, WorkflowDispatchLifecycle.IsRedriveEligible(projected));

        if (!expectsFollowUp)
        {
            Assert.Null(projection.FollowUpOutboxItem);
            return;
        }

        var followUp = Assert.IsType<RuntimePostCommitOutboxItem>(projection.FollowUpOutboxItem);
        Assert.Equal(identity.WaitFailureResumeOutboxItemId(0), followUp.OutboxItemId);
        Assert.True(followUp.RetryPolicy.RetryUntilAcknowledged);
        var payload = followUp.Intent.Payload!.Value.Deserialize<WorkflowDispatchParentResumePayload>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(payload);
        Assert.Equal(WorkflowDispatchStatus.DispatchFailed, payload.Result.Status);
        Assert.Empty(payload.Result.Outputs);
        Assert.Equal(identity.DeliveryIncidentId(0), payload.Result.DiagnosticMetadata[DispatchWorkflowDiagnostics.DeliveryIncidentIdKey]);
        Assert.DoesNotContain(followUp.Intent.Payload.Value.GetRawText(), "secret", StringComparison.OrdinalIgnoreCase);
        _ = new RuntimePostCommitOutboxClaimCompletion(claim, failure, projected, followUp);

        var wrongStartItem = CopyStartItem(claim.Item, kind: "Unsupported.Kind");
        var wrongStartClaim = new RuntimePostCommitOutboxClaim(
            wrongStartItem,
            claim.OwnerId,
            claim.FencingToken,
            claim.ClaimedAt,
            claim.VisibleAfter);
        Assert.Throws<ArgumentException>(() =>
            new RuntimePostCommitOutboxClaimCompletion(wrongStartClaim, failure, projected, followUp));

        var wrongFollowUp = CopyStartItem(followUp, kind: "Unsupported.Kind");
        Assert.Throws<ArgumentException>(() =>
            new RuntimePostCommitOutboxClaimCompletion(claim, failure, projected, wrongFollowUp));
    }

    [Fact]
    public async Task InMemoryCompletionAtomicallyPersistsDeadLetterDispatchAndWaitFollowUp()
    {
        var state = new InMemoryRuntimeCheckpointStoreState();
        var dispatchStore = new InMemoryWorkflowDispatchStore(state);
        var outbox = new InMemoryRuntimeCheckpointCommitStore(state: state, workflowDispatchStore: dispatchStore);
        var dispatch = NewDispatch(WorkflowDispatchMode.WaitForCompletion);
        var pending = StartItem(dispatch, "secret");
        await dispatchStore.SaveAsync(dispatch);
        await outbox.AddPendingForTestingAsync(pending);
        var claim = Assert.Single(await outbox.ClaimAsync(
            new RuntimePostCommitOutboxClaimRequest("owner-1", Now, TimeSpan.FromMinutes(1), 1)));
        var failure = new RuntimePostCommitOutboxDeliveryResult(
            pending.OutboxItemId,
            RuntimePostCommitOutboxStatus.FailedRetryable,
            Now.AddSeconds(1),
            "secret provider reason");
        var projection = Assert.IsType<PostCommitFailureProjection>(
            await new WorkflowDispatchDeliveryFailureProjector(dispatchStore).ProjectAsync(claim.Item, failure));

        await outbox.CompleteClaimAsync(new RuntimePostCommitOutboxClaimCompletion(
            claim,
            failure,
            projection.WorkflowDispatch,
            projection.FollowUpOutboxItem));

        Assert.Equal(RuntimePostCommitOutboxStatus.FailedFinal, (await outbox.FindAsync(pending.OutboxItemId))!.Status);
        Assert.Equal(WorkflowDispatchStatus.DispatchFailed, (await dispatchStore.FindAsync(dispatch.DispatchId))!.Status);
        var followUp = Assert.IsType<RuntimePostCommitOutboxItem>(
            await outbox.FindAsync(Identity(dispatch).WaitFailureResumeOutboxItemId(0)));
        Assert.Equal(RuntimePostCommitOutboxStatus.Pending, followUp.Status);
        Assert.Single(await outbox.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(Now.AddSeconds(2), 10)));

        await Assert.ThrowsAsync<RuntimePostCommitOutboxStaleClaimException>(() =>
            outbox.CompleteClaimAsync(new RuntimePostCommitOutboxClaimCompletion(
                claim,
                failure,
                projection.WorkflowDispatch,
                projection.FollowUpOutboxItem)).AsTask());
        Assert.Equal(RuntimePostCommitOutboxStatus.FailedFinal, (await outbox.FindAsync(pending.OutboxItemId))!.Status);
        Assert.NotNull(await outbox.FindAsync(Identity(dispatch).WaitFailureResumeOutboxItemId(0)));
    }

    [Fact]
    public async Task InMemoryClaimedFailure_AcknowledgesVisibleChildInsteadOfProjectingDispatchFailed()
    {
        var state = new InMemoryRuntimeCheckpointStoreState();
        var workflowExecutions = new InMemoryWorkflowExecutionStateStore();
        var dispatchStore = new InMemoryWorkflowDispatchStore(state);
        var outbox = new InMemoryRuntimeCheckpointCommitStore(
            workflowExecutionStateStore: workflowExecutions,
            state: state,
            workflowDispatchStore: dispatchStore);
        var dispatch = NewDispatch(WorkflowDispatchMode.WaitForCompletion);
        var pending = StartItem(dispatch, "secret");
        await dispatchStore.SaveAsync(dispatch);
        await outbox.AddPendingForTestingAsync(pending);
        var claim = Assert.Single(await outbox.ClaimAsync(
            new RuntimePostCommitOutboxClaimRequest("owner-1", Now, TimeSpan.FromMinutes(1), 1)));
        var failure = new RuntimePostCommitOutboxDeliveryResult(
            pending.OutboxItemId,
            RuntimePostCommitOutboxStatus.FailedRetryable,
            Now.AddSeconds(1),
            "secret provider reason");
        var projection = Assert.IsType<PostCommitFailureProjection>(
            await new WorkflowDispatchDeliveryFailureProjector(dispatchStore).ProjectAsync(claim.Item, failure));
        await workflowExecutions.SaveAsync(ChildState(dispatch));

        await outbox.CompleteClaimAsync(new RuntimePostCommitOutboxClaimCompletion(
            claim,
            failure,
            projection.WorkflowDispatch,
            projection.FollowUpOutboxItem));

        var completed = Assert.IsType<RuntimePostCommitOutboxItem>(await outbox.FindAsync(pending.OutboxItemId));
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, completed.Status);
        Assert.Null(completed.LastFailureMessage);
        Assert.Equal(WorkflowDispatchStatus.Started, (await dispatchStore.FindAsync(dispatch.DispatchId))!.Status);
        Assert.Null(await outbox.FindAsync(Identity(dispatch).WaitFailureResumeOutboxItemId(0)));
    }

    [Fact]
    public async Task Processor_emits_safe_attempt_final_incident_resume_queued_and_consumed_events()
    {
        var state = new InMemoryRuntimeCheckpointStoreState();
        var dispatchStore = new InMemoryWorkflowDispatchStore(state);
        var outbox = new InMemoryRuntimeCheckpointCommitStore(state: state, workflowDispatchStore: dispatchStore);
        var dispatch = NewDispatch(WorkflowDispatchMode.WaitForCompletion);
        await dispatchStore.SaveAsync(dispatch);
        await outbox.AddPendingForTestingAsync(StartItem(dispatch, "payload-secret"));
        var logger = new RecordingLogger<RuntimePostCommitOutboxProcessor>();
        var failure = new RuntimePostCommitDeliveryException(
            PostCommitFailureKind.Transient,
            "child-start-delivery-failed",
            "The child workflow could not be started.",
            new InvalidOperationException("provider-secret"));
        var failing = new RuntimePostCommitOutboxProcessor(
            outbox,
            new StubDispatcher(failure),
            new FixedTimeProvider(Now.AddSeconds(1)),
            DefaultRuntimeFaultCapturePolicy.CreateDefault(),
            dispatchStore,
            [new WorkflowDispatchDeliveryFailureProjector(dispatchStore)],
            logger);

        await failing.ProcessAsync(new RuntimePostCommitOutboxProcessRequest(10));
        var consuming = new RuntimePostCommitOutboxProcessor(
            outbox,
            new StubDispatcher(),
            new FixedTimeProvider(Now.AddSeconds(2)),
            DefaultRuntimeFaultCapturePolicy.CreateDefault(),
            dispatchStore,
            logger);
        await consuming.ProcessAsync(new RuntimePostCommitOutboxProcessRequest(
            10,
            intentKind: DispatchWorkflowConstants.ResumeParentIntentKind));

        Assert.Contains(logger.Entries, entry => entry.EventId.Name == "RuntimePostCommitDeliveryAttemptFailed");
        Assert.Contains(logger.Entries, entry => entry.EventId.Name == "RuntimePostCommitDeliveryFailedFinal");
        Assert.Contains(logger.Entries, entry => entry.EventId.Name == "WorkflowDispatchDeliveryIncidentRecorded");
        Assert.Contains(logger.Entries, entry => entry.EventId.Name == "WorkflowDispatchFailureResumeQueued");
        Assert.Contains(logger.Entries, entry =>
            entry.EventId.Name == "RuntimePostCommitDeliverySucceeded" &&
            entry.Fields.Values.Any(value => StringComparer.Ordinal.Equals(value?.ToString(), DispatchWorkflowConstants.ResumeParentIntentKind)));
        var serialized = string.Join(" ", logger.Entries.SelectMany(entry => entry.Fields).Select(pair => $"{pair.Key}={pair.Value}"));
        Assert.DoesNotContain("provider-secret", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("payload-secret", serialized, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(WorkflowDispatchMode.WaitForCompletion)]
    [InlineData(WorkflowDispatchMode.FireAndForget)]
    public async Task Three_exhaustion_replays_per_mode_preserve_one_stable_projection_and_incident(
        WorkflowDispatchMode mode)
    {
        var state = new InMemoryRuntimeCheckpointStoreState();
        var dispatchStore = new InMemoryWorkflowDispatchStore(state);
        var outbox = new InMemoryRuntimeCheckpointCommitStore(state: state, workflowDispatchStore: dispatchStore);
        var dispatch = NewDispatch(mode);
        var pending = StartItem(dispatch, "safe");
        await dispatchStore.SaveAsync(dispatch);
        await outbox.AddPendingForTestingAsync(pending);
        var claim = Assert.Single(await outbox.ClaimAsync(
            new RuntimePostCommitOutboxClaimRequest("owner-1", Now, TimeSpan.FromMinutes(1), 1)));
        var failure = new RuntimePostCommitOutboxDeliveryResult(
            pending.OutboxItemId,
            RuntimePostCommitOutboxStatus.FailedFinal,
            Now.AddSeconds(1),
            "fixed-safe-summary");
        var projector = new WorkflowDispatchDeliveryFailureProjector(dispatchStore);
        var initial = Assert.IsType<PostCommitFailureProjection>(
            await projector.ProjectAsync(claim.Item, failure));
        await outbox.CompleteClaimAsync(new RuntimePostCommitOutboxClaimCompletion(
            claim,
            failure,
            initial.WorkflowDispatch,
            initial.FollowUpOutboxItem));

        for (var replay = 0; replay < 3; replay++)
        {
            var projection = Assert.IsType<PostCommitFailureProjection>(
                await projector.ProjectAsync(claim.Item, failure));
            Assert.Equal(
                WorkflowDispatchLifecycle.ReadDeliveryIncidentId(initial.WorkflowDispatch),
                WorkflowDispatchLifecycle.ReadDeliveryIncidentId(projection.WorkflowDispatch));
            await Assert.ThrowsAsync<RuntimePostCommitOutboxStaleClaimException>(() => outbox.CompleteClaimAsync(
                new RuntimePostCommitOutboxClaimCompletion(
                    claim,
                    failure,
                    projection.WorkflowDispatch,
                    projection.FollowUpOutboxItem)).AsTask());
        }

        var persisted = Assert.IsType<WorkflowDispatchRecord>(await dispatchStore.FindAsync(dispatch.DispatchId));
        Assert.Equal(WorkflowDispatchStatus.DispatchFailed, persisted.Status);
        Assert.Equal(Identity(dispatch).DeliveryIncidentId(0), WorkflowDispatchLifecycle.ReadDeliveryIncidentId(persisted));
        var followUps = await outbox.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(
            Now.AddSeconds(2),
            10,
            intentKind: DispatchWorkflowConstants.ResumeParentIntentKind));
        Assert.Equal(mode == WorkflowDispatchMode.WaitForCompletion ? 1 : 0, followUps.Count);
    }

    [Fact]
    public async Task Projector_ignores_nonfinal_or_wrong_kind_and_rejects_missing_or_conflicting_identity()
    {
        var store = new InMemoryWorkflowDispatchStore();
        var dispatch = NewDispatch(WorkflowDispatchMode.FireAndForget);
        await store.SaveAsync(dispatch);
        var item = StartItem(dispatch, "safe");
        var projector = new WorkflowDispatchDeliveryFailureProjector(store);
        var nonfinal = new RuntimePostCommitOutboxDeliveryResult(
            item.OutboxItemId,
            RuntimePostCommitOutboxStatus.Delivered,
            Now,
            failureMessage: null);
        Assert.Null(await projector.ProjectAsync(item, nonfinal));

        var wrongKind = CopyStartItem(item, kind: "Unsupported.Kind");
        var final = new RuntimePostCommitOutboxDeliveryResult(
            item.OutboxItemId,
            RuntimePostCommitOutboxStatus.FailedFinal,
            Now,
            "fixed-safe-summary");
        Assert.Null(await projector.ProjectAsync(wrongKind, final));

        var missingMetadata = CopyStartItem(item, metadata: new Dictionary<string, string>());
        await Assert.ThrowsAsync<InvalidOperationException>(() => projector.ProjectAsync(missingMetadata, final).AsTask());

        var conflictingIntent = CopyStartItem(item, intentId: "intent:conflicting");
        await Assert.ThrowsAsync<InvalidOperationException>(() => projector.ProjectAsync(conflictingIntent, final).AsTask());

        var missingStore = new InMemoryWorkflowDispatchStore();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new WorkflowDispatchDeliveryFailureProjector(missingStore).ProjectAsync(item, final).AsTask());
    }

    [Fact]
    public async Task Projector_validates_guards_and_rejects_a_conflicting_existing_failure()
    {
        var store = new InMemoryWorkflowDispatchStore();
        var dispatch = NewDispatch(WorkflowDispatchMode.FireAndForget);
        var item = StartItem(dispatch, "safe");
        var final = new RuntimePostCommitOutboxDeliveryResult(
            item.OutboxItemId,
            RuntimePostCommitOutboxStatus.FailedFinal,
            Now,
            "fixed-safe-summary");
        var projector = new WorkflowDispatchDeliveryFailureProjector(store);

        await Assert.ThrowsAsync<ArgumentNullException>(() => projector.ProjectAsync(null!, final).AsTask());
        await Assert.ThrowsAsync<ArgumentNullException>(() => projector.ProjectAsync(item, null!).AsTask());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            projector.ProjectAsync(item, final, cancellation.Token).AsTask());

        var conflicting = WorkflowDispatchLifecycle.TransitionToDispatchFailed(
            dispatch,
            "outbox:conflicting",
            generation: 0,
            attemptCount: 1,
            firstAttemptAt: Now.AddMinutes(-1),
            failedAt: Now);
        await store.SaveAsync(dispatch);
        await store.SaveAsync(conflicting);

        await Assert.ThrowsAsync<InvalidOperationException>(() => projector.ProjectAsync(item, final).AsTask());
        Assert.True(WorkflowDispatchLifecycle.RecordsEqual(
            conflicting,
            (await store.FindAsync(dispatch.DispatchId))!));
    }

    private static RuntimePostCommitOutboxItem CopyStartItem(
        RuntimePostCommitOutboxItem item,
        string? kind = null,
        string? intentId = null,
        IReadOnlyDictionary<string, string>? metadata = null) =>
        new(
            item.OutboxItemId,
            new RuntimePostCommitIntent(
                intentId ?? item.Intent.IntentId,
                item.Intent.WorkflowExecutionId,
                kind ?? item.Intent.Kind,
                item.Intent.RecordedAt,
                item.Intent.ActivityExecutionId,
                item.Intent.IdempotencyKey,
                item.Intent.Payload,
                metadata ?? item.Intent.Metadata),
            item.Status,
            item.RecordedAt,
            item.AvailableAt,
            item.RetryPolicy,
            item.DeliveryAttemptCount,
            item.DeliveringOwnerId,
            item.DeliveryStartedAt,
            item.DeliveredAt,
            item.LastFailureMessage,
            item.Metadata,
            item.DeliveryFencingToken,
            item.DeliveryVisibleAfter);

    private static RuntimePostCommitOutboxItem StartItem(WorkflowDispatchRecord dispatch, string payload) =>
        new(
            "outbox-start-1",
            new RuntimePostCommitIntent(
                Identity(dispatch).StartIntentId,
                dispatch.ParentWorkflowExecutionId,
                DispatchWorkflowConstants.StartChildIntentKind,
                Now.AddMinutes(-1),
                dispatch.ParentActivityExecutionId,
                Identity(dispatch).StartIdempotencyKey,
                JsonSerializer.SerializeToElement(payload),
                new Dictionary<string, string>
                {
                    [RuntimeMetadataKeys.DispatchId] = dispatch.DispatchId,
                    [RuntimeMetadataKeys.ChildWorkflowExecutionId] = dispatch.ChildWorkflowExecutionId
                }),
            RuntimePostCommitOutboxStatus.Pending,
            Now.AddMinutes(-1),
            Now,
            RuntimePostCommitRetryPolicy.None);

    private static WorkflowDispatchRecord NewDispatch(WorkflowDispatchMode mode)
    {
        var identity = new WorkflowDispatchIdentity("parent-1", "activity-1");
        return new WorkflowDispatchRecord(
            identity.DispatchId,
            "parent-1",
            "activity-1",
            identity.ChildWorkflowExecutionId,
            new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1", "hash-1"),
            new WorkflowExecutableSourceProvenance("source-1", "WorkflowDefinitionVersion", "version-1", "1", "definition-1", "version-1", "1", "publication-1", "slot-1"),
            mode,
            WorkflowDispatchStatus.Pending,
            null,
            null,
            new WorkflowExecutionPartition(WorkflowExecutionPartition.DefaultValue),
            WorkflowRunKind.PublishedRun,
            new WorkflowExecutionAuthoritySnapshot("parent-1", "initiator-1"),
            [],
            Now.AddMinutes(-2),
            Now.AddMinutes(-2),
            metadata: null,
            dispatchNestingDepth: 2);
    }

    private static WorkflowDispatchIdentity Identity(WorkflowDispatchRecord dispatch) =>
        new(dispatch.ParentWorkflowExecutionId, dispatch.ParentActivityExecutionId);

    private static WorkflowExecutionState ChildState(WorkflowDispatchRecord dispatch) => new(
        dispatch.ChildWorkflowExecutionId,
        dispatch.ChildExecutable,
        WorkflowExecutionStatus.Running,
        null,
        Now,
        Now,
        Now,
        null,
        dispatch.CorrelationId,
        dispatch.ParentWorkflowExecutionId,
        dispatch.TenantId,
        new Dictionary<string, string>())
    {
        RunKind = dispatch.RunKind,
        PinnedSource = dispatch.ChildSource,
        Partition = dispatch.Partition,
        Authority = dispatch.Authority,
        DispatchNestingDepth = dispatch.DispatchNestingDepth
    };

    private sealed class StubDispatcher(Exception? exception = null) : IRuntimePostCommitIntentDispatcher
    {
        public ValueTask DispatchAsync(RuntimePostCommitIntent intent, CancellationToken cancellationToken = default) =>
            exception is null ? ValueTask.CompletedTask : ValueTask.FromException(exception);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var fields = state is IEnumerable<KeyValuePair<string, object?>> structured
                ? structured.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
                : new Dictionary<string, object?> { ["Message"] = formatter(state, exception) };
            Entries.Add(new LogEntry(eventId, fields, exception));
        }
    }

    private sealed record LogEntry(
        EventId EventId,
        IReadOnlyDictionary<string, object?> Fields,
        Exception? Exception);
}
