using System.Text.Json;
using Elsa.Activities.DispatchWorkflow.Runtime.Constants;
using Elsa.Activities.DispatchWorkflow.Runtime.Models;
using Elsa.Activities.DispatchWorkflow.Runtime.Services;
using Elsa.Persistence.Groundwork.Stores;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Persistence.Groundwork.Tests;

/// <summary>SQLite service-recreation coverage for DispatchWorkflow parent-to-child cancellation.</summary>
public sealed class DispatchWorkflowCancellationCrashTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 16, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Cancellation_before_admission_survives_restart_and_suppresses_three_start_deliveries()
    {
        await WithSqliteAsync(async connectionString =>
        {
            var pending = NewDispatch("parent-cancel-first", "activity-cancel-first");
            await using (var beforeCrash = GroundworkDocumentStoreFixture.CreateSqlite(connectionString))
            {
                var dispatches = Dispatches(beforeCrash);
                await dispatches.SaveAsync(pending);
                var result = await dispatches.ApplyCancellationAsync(CancellationRequest(pending));

                Assert.Equal(WorkflowDispatchCancellationDisposition.AppliedBeforeAdmission, result.Disposition);
                Assert.True(WorkflowDispatchLifecycle.WasCancelledBeforeAdmission(result.Record));
            }

            await using var recovered = GroundworkDocumentStoreFixture.CreateSqlite(connectionString);
            var recoveredDispatches = Dispatches(recovered);
            var startDispatcher = new RecordingStartDispatcher();
            var executor = new ChildStartExecutor(startDispatcher, recoveredDispatches, new FixedTimeProvider(Now));
            var intent = StartIntent(pending);

            for (var delivery = 0; delivery < 3; delivery++)
                await executor.HandleAsync(intent);

            Assert.Empty(startDispatcher.Requests);
            var persisted = Assert.IsType<WorkflowDispatchRecord>(await recoveredDispatches.FindAsync(pending.DispatchId));
            Assert.Equal(WorkflowDispatchStatus.Cancelled, persisted.Status);
            Assert.True(WorkflowDispatchLifecycle.WasCancelledBeforeAdmission(persisted));
        });
    }

    [Fact]
    public async Task Admitted_cancellation_retries_until_child_is_visible_then_duplicate_delivery_is_harmless()
    {
        await WithSqliteAsync(async connectionString =>
        {
            var pending = NewDispatch("parent-admitted", "activity-admitted");
            var cancelIntent = CancelIntent(pending);

            await using (var admitted = GroundworkDocumentStoreFixture.CreateSqlite(connectionString))
            {
                var dispatches = Dispatches(admitted);
                await dispatches.SaveAsync(pending);
                Assert.Equal(
                    WorkflowDispatchAdmissionDisposition.Admitted,
                    (await dispatches.TryAdmitAsync(pending.DispatchId, Now.AddSeconds(1))).Disposition);
                Assert.Equal(
                    WorkflowDispatchCancellationDisposition.CancellationRequestedAfterAdmission,
                    (await dispatches.ApplyCancellationAsync(CancellationRequest(pending))).Disposition);
            }

            await using (var childNotVisible = GroundworkDocumentStoreFixture.CreateSqlite(connectionString))
            {
                var actors = new RecordingActorProvider();
                var executor = new ChildCancelExecutor(
                    actors,
                    Executions(childNotVisible),
                    Dispatches(childNotVisible));

                await Assert.ThrowsAsync<ChildCancelDeferredException>(() => executor.HandleAsync(cancelIntent).AsTask());
                Assert.Empty(actors.Activations);
                Assert.Empty(actors.Actor.Envelopes);
            }

            await using (var childVisible = GroundworkDocumentStoreFixture.CreateSqlite(connectionString))
            {
                var executions = Executions(childVisible);
                var running = ChildState(pending, WorkflowExecutionStatus.Running);
                await executions.SaveAsync(running);
                var actors = new RecordingActorProvider(async _ =>
                {
                    await executions.SaveAsync(running with
                    {
                        Status = WorkflowExecutionStatus.Cancelled,
                        UpdatedAt = Now.AddSeconds(3),
                        CompletedAt = Now.AddSeconds(3)
                    });
                });
                var executor = new ChildCancelExecutor(actors, executions, Dispatches(childVisible));

                for (var delivery = 0; delivery < 3; delivery++)
                    await executor.HandleAsync(cancelIntent);

                Assert.Single(actors.Activations);
                var envelope = Assert.Single(actors.Actor.Envelopes);
                var identity = Identity(pending);
                Assert.Equal(identity.ChildCancelEnvelopeId, envelope.EnvelopeId);
                Assert.Equal(identity.ChildCancelCommandId, envelope.Command.CommandId);
                Assert.Equal(identity.ChildCancelIdempotencyKey, envelope.IdempotencyKey);
                Assert.Equal(WorkflowExecutionCommandKind.Cancel, envelope.Command.Kind);
            }

            await using var recovered = GroundworkDocumentStoreFixture.CreateSqlite(connectionString);
            Assert.Equal(
                WorkflowExecutionStatus.Cancelled,
                (await Executions(recovered).FindAsync(pending.ChildWorkflowExecutionId))!.Status);
            var persisted = Assert.IsType<WorkflowDispatchRecord>(await Dispatches(recovered).FindAsync(pending.DispatchId));
            Assert.Equal(WorkflowDispatchStatus.Started, persisted.Status);
            Assert.True(WorkflowDispatchLifecycle.IsCancellationRequested(persisted));
        });
    }

    [Theory]
    [InlineData(WorkflowDispatchStatus.Completed, WorkflowExecutionStatus.Completed, true)]
    [InlineData(WorkflowDispatchStatus.Faulted, WorkflowExecutionStatus.Faulted, true)]
    [InlineData(WorkflowDispatchStatus.Cancelled, WorkflowExecutionStatus.Cancelled, true)]
    [InlineData(WorkflowDispatchStatus.Completed, WorkflowExecutionStatus.Completed, false)]
    [InlineData(WorkflowDispatchStatus.Faulted, WorkflowExecutionStatus.Faulted, false)]
    [InlineData(WorkflowDispatchStatus.Cancelled, WorkflowExecutionStatus.Cancelled, false)]
    public async Task Terminal_notification_in_either_order_wins_over_three_cancel_deliveries(
        WorkflowDispatchStatus dispatchStatus,
        WorkflowExecutionStatus childStatus,
        bool terminalBeforeParentCancellation)
    {
        await WithSqliteAsync(async connectionString =>
        {
            var pending = NewDispatch($"parent-terminal-{dispatchStatus}", "activity-terminal");
            await using (var terminal = GroundworkDocumentStoreFixture.CreateSqlite(connectionString))
            {
                var dispatches = Dispatches(terminal);
                await dispatches.SaveAsync(pending);
                var started = (await dispatches.TryAdmitAsync(pending.DispatchId, Now.AddSeconds(1))).Record;
                WorkflowDispatchRecord terminalDispatch;
                if (terminalBeforeParentCancellation)
                {
                    terminalDispatch = started.TransitionTo(dispatchStatus, Now.AddSeconds(2));
                    await dispatches.SaveAsync(terminalDispatch);
                    var lateCancellation = await dispatches.ApplyCancellationAsync(CancellationRequest(pending));
                    Assert.Equal(WorkflowDispatchCancellationDisposition.TerminalUnchanged, lateCancellation.Disposition);
                    Assert.True(WorkflowDispatchLifecycle.RecordsEqual(terminalDispatch, lateCancellation.Record));
                    Assert.False(WorkflowDispatchLifecycle.IsCancellationRequested(lateCancellation.Record));
                }
                else
                {
                    var cancellation = await dispatches.ApplyCancellationAsync(CancellationRequest(pending));
                    Assert.Equal(
                        WorkflowDispatchCancellationDisposition.CancellationRequestedAfterAdmission,
                        cancellation.Disposition);
                    terminalDispatch = cancellation.Record.TransitionTo(dispatchStatus, Now.AddSeconds(3));
                    await dispatches.SaveAsync(terminalDispatch);
                }
                await Executions(terminal).SaveAsync(ChildState(pending, childStatus));
            }

            await using var recovered = GroundworkDocumentStoreFixture.CreateSqlite(connectionString);
            var actors = new RecordingActorProvider();
            var executor = new ChildCancelExecutor(actors, Executions(recovered), Dispatches(recovered));
            var intent = CancelIntent(pending);

            for (var delivery = 0; delivery < 3; delivery++)
                await executor.HandleAsync(intent);

            Assert.Empty(actors.Activations);
            Assert.Empty(actors.Actor.Envelopes);
            var persistedDispatch = Assert.IsType<WorkflowDispatchRecord>(
                await Dispatches(recovered).FindAsync(pending.DispatchId));
            Assert.Equal(dispatchStatus, persistedDispatch.Status);
            Assert.Equal(!terminalBeforeParentCancellation, WorkflowDispatchLifecycle.IsCancellationRequested(persistedDispatch));
            Assert.Equal(childStatus, (await Executions(recovered).FindAsync(pending.ChildWorkflowExecutionId))!.Status);
        });
    }

    [Fact]
    public async Task Expired_child_cancel_claim_is_reclaimed_and_uncertain_ack_is_observed_after_restart()
    {
        await WithSqliteAsync(async connectionString =>
        {
            var pending = NewDispatch("parent-claim", "activity-claim");
            var intent = CancelIntent(pending);
            var outboxItem = new RuntimePostCommitOutboxItem(
                "outbox-child-cancel",
                intent,
                RuntimePostCommitOutboxStatus.Pending,
                Now,
                Now,
                RuntimePostCommitRetryPolicy.UntilAcknowledged(TimeSpan.FromSeconds(1)));
            RuntimePostCommitOutboxClaim expiredClaim;

            await using (var beforeCrash = GroundworkDocumentStoreFixture.CreateSqlite(connectionString))
            {
                var dispatches = Dispatches(beforeCrash);
                await dispatches.SaveAsync(pending);
                await dispatches.TryAdmitAsync(pending.DispatchId, Now.AddSeconds(1));
                await dispatches.ApplyCancellationAsync(CancellationRequest(pending));
                await Executions(beforeCrash).SaveAsync(ChildState(pending, WorkflowExecutionStatus.Running));
                var outbox = Outbox(beforeCrash);
                await outbox.SavePendingAsync(outboxItem);
                expiredClaim = Assert.Single(await outbox.ClaimAsync(
                    new RuntimePostCommitOutboxClaimRequest("worker-before-crash", Now, TimeSpan.FromMinutes(1), 1)));
            }

            RuntimePostCommitOutboxClaim recoveredClaim;
            await using (var delivering = GroundworkDocumentStoreFixture.CreateSqlite(connectionString))
            {
                var executions = Executions(delivering);
                var actors = new RecordingActorProvider(async _ =>
                {
                    var child = Assert.IsType<WorkflowExecutionState>(await executions.FindAsync(pending.ChildWorkflowExecutionId));
                    await executions.SaveAsync(child with
                    {
                        Status = WorkflowExecutionStatus.Cancelled,
                        UpdatedAt = Now.AddMinutes(2),
                        CompletedAt = Now.AddMinutes(2)
                    });
                });
                var outbox = Outbox(delivering);
                recoveredClaim = Assert.Single(await outbox.ClaimAsync(
                    new RuntimePostCommitOutboxClaimRequest(
                        "worker-after-crash",
                        Now.AddMinutes(2),
                        TimeSpan.FromMinutes(1),
                        1)));
                Assert.True(recoveredClaim.FencingToken > expiredClaim.FencingToken);

                var executor = new ChildCancelExecutor(actors, executions, Dispatches(delivering));
                for (var delivery = 0; delivery < 3; delivery++)
                    await executor.HandleAsync(recoveredClaim.Item.Intent);
                Assert.Single(actors.Activations);
                Assert.Single(actors.Actor.Envelopes);

                await Assert.ThrowsAsync<RuntimePostCommitOutboxStaleClaimException>(() => outbox.RecordDeliveryResultAsync(
                    expiredClaim,
                    Delivered(expiredClaim, Now.AddMinutes(2))).AsTask());
                await outbox.RecordDeliveryResultAsync(
                    recoveredClaim,
                    Delivered(recoveredClaim, Now.AddMinutes(2)));
                // Simulate process loss after the durable acknowledgement but before the caller observes it.
            }

            await using var recovered = GroundworkDocumentStoreFixture.CreateSqlite(connectionString);
            var persisted = Assert.IsType<RuntimePostCommitOutboxItem>(
                await Outbox(recovered).FindAsync(outboxItem.OutboxItemId));
            Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, persisted.Status);
            Assert.True(persisted.RetryPolicy.RetryUntilAcknowledged);
            Assert.Empty(await Outbox(recovered).ClaimAsync(
                new RuntimePostCommitOutboxClaimRequest("audit", Now.AddYears(1), TimeSpan.FromMinutes(1), 10)));
            Assert.Equal(
                WorkflowExecutionStatus.Cancelled,
                (await Executions(recovered).FindAsync(pending.ChildWorkflowExecutionId))!.Status);
        });
    }

    [Theory]
    [InlineData(WorkflowDispatchMode.WaitForCompletion, false)]
    [InlineData(WorkflowDispatchMode.FireAndForget, true)]
    public async Task Disabled_effective_policy_survives_restart_without_cancellation_responsibility(
        WorkflowDispatchMode mode,
        bool authoredPolicy)
    {
        await WithSqliteAsync(async connectionString =>
        {
            var pending = NewDispatch("parent-independent", $"activity-{mode}", mode, authoredPolicy);
            await using (var beforeCrash = GroundworkDocumentStoreFixture.CreateSqlite(connectionString))
                await Dispatches(beforeCrash).SaveAsync(pending);

            await using var recovered = GroundworkDocumentStoreFixture.CreateSqlite(connectionString);
            var dispatches = Dispatches(recovered);
            var persisted = Assert.IsType<WorkflowDispatchRecord>(await dispatches.FindAsync(pending.DispatchId));
            Assert.False(WorkflowDispatchLifecycle.IsCancellationPropagationEnabled(persisted));

            var enriched = await new WorkflowDispatchCancellationEnricher(dispatches)
                .EnrichAsync(ParentCancelCommit(pending.ParentWorkflowExecutionId));
            Assert.Empty(enriched.StateChanges.WorkflowDispatchCancellations);
            Assert.Empty(enriched.PostCommitIntents);
            Assert.Equal(
                WorkflowDispatchAdmissionDisposition.Admitted,
                (await dispatches.TryAdmitAsync(pending.DispatchId, Now.AddSeconds(1))).Disposition);
        });
    }

    private static async Task WithSqliteAsync(Func<string, Task> test)
    {
        var path = Path.Combine(Path.GetTempPath(), $"elsa-dispatch-cancel-{Guid.NewGuid():N}.db");
        try
        {
            await test($"Data Source={path}");
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static GroundworkWorkflowDispatchStore Dispatches(GroundworkDocumentStoreFixture fixture) => new(
        fixture.DocumentStore,
        GroundworkTestSerialization.Serializer,
        GroundworkTestAccess.DefaultAccessContextAccessor,
        fixture.BoundedDocumentStore);

    private static GroundworkWorkflowExecutionStateStore Executions(GroundworkDocumentStoreFixture fixture) => new(
        fixture.DocumentStore,
        GroundworkTestSerialization.Serializer,
        GroundworkTestAccess.DefaultAccessContextAccessor);

    private static GroundworkRuntimePostCommitOutboxStore Outbox(GroundworkDocumentStoreFixture fixture) => new(
        fixture.DocumentStore,
        GroundworkTestSerialization.Serializer,
        fixture.BoundedDocumentStore,
        GroundworkTestAccess.DefaultAccessContextAccessor);

    private static WorkflowDispatchRecord NewDispatch(
        string parentWorkflowExecutionId,
        string parentActivityExecutionId,
        WorkflowDispatchMode mode = WorkflowDispatchMode.WaitForCompletion,
        bool authoredPolicy = true) =>
        GroundworkWorkflowDispatchStoreTests.Pending(
            parentWorkflowExecutionId,
            parentActivityExecutionId,
            mode: mode,
            cancellationPropagation: authoredPolicy);

    private static WorkflowDispatchIdentity Identity(WorkflowDispatchRecord dispatch) =>
        new(dispatch.ParentWorkflowExecutionId, dispatch.ParentActivityExecutionId);

    private static WorkflowDispatchCancellationRequest CancellationRequest(WorkflowDispatchRecord dispatch) => new(
        dispatch.DispatchId,
        dispatch.ParentWorkflowExecutionId,
        dispatch.ParentActivityExecutionId,
        dispatch.ChildWorkflowExecutionId,
        Now.AddSeconds(2));

    private static RuntimePostCommitIntent CancelIntent(WorkflowDispatchRecord dispatch)
    {
        var identity = Identity(dispatch);
        return new RuntimePostCommitIntent(
            identity.ChildCancelIntentId,
            dispatch.ParentWorkflowExecutionId,
            DispatchWorkflowConstants.CancelChildIntentKind,
            Now.AddSeconds(2),
            dispatch.ParentActivityExecutionId,
            identity.ChildCancelIdempotencyKey,
            JsonSerializer.SerializeToElement(
                new WorkflowDispatchChildCancelPayload(
                    dispatch.DispatchId,
                    dispatch.ParentWorkflowExecutionId,
                    dispatch.ParentActivityExecutionId,
                    dispatch.ChildWorkflowExecutionId),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            new Dictionary<string, string>
            {
                [RuntimeMetadataKeys.DispatchId] = dispatch.DispatchId,
                [RuntimeMetadataKeys.ChildWorkflowExecutionId] = dispatch.ChildWorkflowExecutionId
            });
    }

    private static RuntimePostCommitIntent StartIntent(WorkflowDispatchRecord dispatch)
    {
        var identity = Identity(dispatch);
        var payload = new WorkflowDispatchStartPayload(
            dispatch.DispatchId,
            dispatch.ParentWorkflowExecutionId,
            dispatch.ParentActivityExecutionId,
            dispatch.ChildWorkflowExecutionId,
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
            JsonSerializer.SerializeToElement(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    private static WorkflowExecutionState ChildState(
        WorkflowDispatchRecord dispatch,
        WorkflowExecutionStatus status) => new(
        dispatch.ChildWorkflowExecutionId,
        dispatch.ChildExecutable,
        status,
        null,
        Now,
        Now,
        Now,
        status.IsTerminal() ? Now : null,
        dispatch.CorrelationId,
        dispatch.ParentWorkflowExecutionId,
        dispatch.TenantId,
        new Dictionary<string, string>())
        {
            RunKind = dispatch.RunKind,
            PinnedSource = dispatch.ChildSource,
            Partition = dispatch.Partition,
            Authority = dispatch.Authority,
            DispatchNestingDepth = 1
        };

    private static RuntimeCheckpointCommit ParentCancelCommit(string parentWorkflowExecutionId)
    {
        var parent = new WorkflowExecutionState(
            parentWorkflowExecutionId,
            new WorkflowExecutableIdentity("artifact-parent", "definition-parent", "version-parent", "1", "hash-parent"),
            WorkflowExecutionStatus.Cancelled,
            null,
            Now.AddMinutes(-1),
            Now.AddMinutes(-1),
            Now,
            Now,
            null,
            null,
            null,
            new Dictionary<string, string>());
        return new RuntimeCheckpointCommit(
            "commit-parent-cancel",
            new RuntimeCheckpoint(
                "checkpoint-parent-cancel",
                RuntimeCheckpointNames.WorkflowCancelled,
                parentWorkflowExecutionId,
                Now,
                [],
                new Dictionary<string, string>()),
            new RuntimeCheckpointStateChangeSet(
                new RuntimeStateChange<WorkflowExecutionState>(
                    parentWorkflowExecutionId,
                    RuntimeStateChangeOperation.Upsert,
                    parent,
                    new Dictionary<string, string>()),
                null,
                [], [], [], [], []),
            [],
            new Dictionary<string, string>());
    }

    private static RuntimePostCommitOutboxDeliveryResult Delivered(
        RuntimePostCommitOutboxClaim claim,
        DateTimeOffset recordedAt) =>
        new(claim.OutboxItemId, RuntimePostCommitOutboxStatus.Delivered, recordedAt);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
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
            throw new InvalidOperationException("A cancelled-before-admission dispatch must not reach the start dispatcher.");
        }
    }

    private sealed class RecordingActorProvider(
        Func<WorkflowExecutionCommandEnvelope, ValueTask>? onEnqueue = null) : IWorkflowExecutionActorProvider
    {
        public List<WorkflowExecutionActorActivationRequest> Activations { get; } = [];
        public RecordingActor Actor { get; } = new(onEnqueue);
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
        Func<WorkflowExecutionCommandEnvelope, ValueTask>? onEnqueue) : IWorkflowExecutionActor
    {
        public List<WorkflowExecutionCommandEnvelope> Envelopes { get; } = [];
        public WorkflowExecutionActorDescriptor Descriptor { get; } = new(
            "child",
            "actor-child",
            "test",
            WorkflowExecutionActorStatus.Active,
            WorkflowExecutionActorCapabilities.None,
            Now);

        public async ValueTask<WorkflowExecutionCommandDispatchResult> EnqueueAsync(
            WorkflowExecutionCommandEnvelope envelope,
            CancellationToken cancellationToken = default)
        {
            Envelopes.Add(envelope);
            if (onEnqueue is not null)
                await onEnqueue(envelope);
            return new WorkflowExecutionCommandDispatchResult(
                envelope.EnvelopeId,
                envelope.WorkflowExecutionId,
                WorkflowExecutionCommandDispatchStatus.Accepted,
                Now);
        }
    }
}
