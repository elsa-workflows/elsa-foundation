using System.Text.Json;
using Elsa.Activities.DispatchWorkflow.Runtime.Configuration;
using Elsa.Activities.DispatchWorkflow.Runtime.Constants;
using Elsa.Activities.DispatchWorkflow.Runtime.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Activities.DispatchWorkflow.Tests;

public sealed class ChildStartExecutorTests
{
    [Theory]
    [InlineData(WorkflowExecutionCommandDispatchStatus.Accepted)]
    [InlineData(WorkflowExecutionCommandDispatchStatus.AcceptedButFaulted)]
    [InlineData(WorkflowExecutionCommandDispatchStatus.Duplicate)]
    public async Task MaterializedStart_AdvancesPendingDispatchToStarted(
        WorkflowExecutionCommandDispatchStatus status)
    {
        var startDispatcher = new StubStartDispatcher(status);
        var dispatchStore = new InMemoryWorkflowDispatchStore();
        var pending = NewDispatchRecord();
        await dispatchStore.SaveAsync(pending);
        var executor = new ChildStartExecutor(
            startDispatcher,
            dispatchStore,
            new FixedTimeProvider(DispatchWorkflowRuntimeTestFixture.Now.AddMinutes(1)));

        await executor.HandleAsync(NewOutboxItem().Intent);

        var saved = await dispatchStore.FindAsync(pending.DispatchId);
        Assert.NotNull(saved);
        Assert.Equal(WorkflowDispatchStatus.Started, saved.Status);
        Assert.Equal(DispatchWorkflowRuntimeTestFixture.Now.AddMinutes(1), saved.UpdatedAt);
    }

    [Fact]
    public async Task Test_scoped_start_preserves_scope_and_exact_run_kind()
    {
        var scope = NewTestScope();
        var startDispatcher = new StubStartDispatcher(WorkflowExecutionCommandDispatchStatus.Accepted);
        var state = new InMemoryRuntimeCheckpointStoreState();
        await new InMemoryWorkflowTestScopeStore(state).CreateAsync(
            scope,
            DispatchWorkflowRuntimeTestFixture.Now.AddMinutes(-1));
        var dispatchStore = new InMemoryWorkflowDispatchStore(state);
        await dispatchStore.SaveAsync(NewDispatchRecord(WorkflowRunKind.TestRun, scope));
        var executor = new ChildStartExecutor(
            startDispatcher,
            dispatchStore,
            new FixedTimeProvider(DispatchWorkflowRuntimeTestFixture.Now.AddMinutes(1)));

        await executor.HandleAsync(NewOutboxItem(runKind: WorkflowRunKind.TestRun, testScope: scope).Intent);

        var request = Assert.Single(startDispatcher.Requests);
        Assert.Equal(WorkflowRunKind.TestRun, request.RunKind);
        Assert.NotNull(request.TestScope);
        Assert.Equal(scope.ScopeId, request.TestScope.ScopeId);
        Assert.Equal(scope.ExpiresAt, request.TestScope.ExpiresAt);
        Assert.Equal(scope.TenantId, request.TestScope.TenantId);
        Assert.Equal(scope.Partition.Value, request.TestScope.Partition.Value);
    }

    [Fact]
    public async Task SynchronousTerminalCheckpoint_SupersedesLaterStartedObservation()
    {
        var startDispatcher = new StubStartDispatcher(WorkflowExecutionCommandDispatchStatus.Accepted);
        var dispatchStore = new InMemoryWorkflowDispatchStore();
        var terminal = NewDispatchRecord().TransitionTo(
            WorkflowDispatchStatus.Completed,
            DispatchWorkflowRuntimeTestFixture.Now.AddSeconds(30));
        await dispatchStore.SaveAsync(NewDispatchRecord());
        await dispatchStore.SaveAsync(terminal);
        var executor = new ChildStartExecutor(
            startDispatcher,
            dispatchStore,
            new FixedTimeProvider(DispatchWorkflowRuntimeTestFixture.Now.AddMinutes(1)));

        await executor.HandleAsync(NewOutboxItem().Intent);

        var saved = await dispatchStore.FindAsync(terminal.DispatchId);
        Assert.NotNull(saved);
        Assert.Equal(WorkflowDispatchStatus.Completed, saved.Status);
        Assert.Equal(terminal.UpdatedAt, saved.UpdatedAt);
    }

    [Fact]
    public async Task RejectedStart_RemainsAnOutboxFailure_InsteadOfBeingAcknowledged()
    {
        var startDispatcher = new StubStartDispatcher(WorkflowExecutionCommandDispatchStatus.Rejected);
        var store = new RecordingOutboxStore(NewOutboxItem());
        var processor = new RuntimePostCommitOutboxProcessor(
            store,
            new HandlerIntentDispatcher(new ChildStartExecutor(startDispatcher)),
            new FixedTimeProvider(DispatchWorkflowRuntimeTestFixture.Now));

        var result = await processor.ProcessAsync(new RuntimePostCommitOutboxProcessRequest(limit: 10));

        Assert.Equal(1, result.AttemptedCount);
        Assert.Equal(0, result.DeliveredCount);
        Assert.Equal(1, result.FailedCount);
        Assert.NotEqual(RuntimePostCommitOutboxStatus.Delivered, Assert.Single(store.Results).Status);
        Assert.Single(startDispatcher.Requests);
    }

    [Fact]
    public async Task RejectedStart_IsClassifiedAsSafePermanentDeliveryFailure()
    {
        var executor = new ChildStartExecutor(new StubStartDispatcher(WorkflowExecutionCommandDispatchStatus.Rejected));

        var exception = await Assert.ThrowsAsync<RuntimePostCommitDeliveryException>(
            () => executor.HandleAsync(NewOutboxItem().Intent).AsTask());

        Assert.Equal(PostCommitFailureKind.Permanent, exception.Kind);
        Assert.Equal("child-start-delivery-failed", exception.Code);
        Assert.Equal("The child workflow could not be started.", exception.SafeSummary);
        Assert.DoesNotContain("set by test", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DurablyForwardedDeferredStart_AcknowledgesTheOutboxItem()
    {
        var startDispatcher = new StubStartDispatcher(
            WorkflowExecutionCommandDispatchStatus.Deferred,
            new Dictionary<string, string>
            {
                ["runtime.distributed.owningNode"] = "node-b",
                ["runtime.distributed.transportItemId"] = "transport-child-start"
            });
        var store = new RecordingOutboxStore(NewOutboxItem());
        var processor = new RuntimePostCommitOutboxProcessor(
            store,
            new HandlerIntentDispatcher(new ChildStartExecutor(startDispatcher)),
            new FixedTimeProvider(DispatchWorkflowRuntimeTestFixture.Now));

        var result = await processor.ProcessAsync(new RuntimePostCommitOutboxProcessRequest(limit: 10));

        Assert.Equal(1, result.AttemptedCount);
        Assert.Equal(1, result.DeliveredCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, Assert.Single(store.Results).Status);
        var request = Assert.Single(startDispatcher.Requests);
        Assert.Equal(NewIdentity().ChildWorkflowExecutionId, request.WorkflowExecutionId);
        Assert.Equal(NewIdentity().StartIdempotencyKey, request.IdempotencyKey);
    }

    [Fact]
    public async Task RetainedChildStart_UsesTypedParentEdgeAuthorityWithoutHistoricalChildSource()
    {
        var startDispatcher = new StubStartDispatcher(WorkflowExecutionCommandDispatchStatus.Accepted);
        var parent = new WorkflowExecutableIdentity(
            "parent-artifact",
            "parent-definition",
            "parent-version",
            "1.0.0",
            "sha256:parent");
        var store = new RecordingOutboxStore(NewOutboxItem(
            childSource: null,
            parentExecutable: parent,
            dispatchNodeId: "dispatch-node"));
        var processor = new RuntimePostCommitOutboxProcessor(
            store,
            new HandlerIntentDispatcher(new ChildStartExecutor(startDispatcher)),
            new FixedTimeProvider(DispatchWorkflowRuntimeTestFixture.Now));

        var result = await processor.ProcessAsync(new RuntimePostCommitOutboxProcessRequest(limit: 10));

        Assert.Equal(1, result.DeliveredCount);
        var request = Assert.Single(startDispatcher.Requests);
        Assert.Null(request.SourceSelection);
        Assert.Equal(WorkflowExecutableProvenanceRequirement.AllowReferenceLessLegacy, request.ProvenanceRequirement);
        var retained = Assert.IsType<WorkflowExecutableRetainedDependencyAuthority>(request.StartAuthority!.RetainedDependency);
        Assert.Equal(parent.ArtifactId, retained.ParentArtifactId);
        Assert.Equal(parent.ArtifactHash, retained.ParentArtifactHash);
        Assert.Equal("dispatch-node", retained.DispatchNodeId);
    }

    [Fact]
    public async Task Delivery_reuses_stored_depth_without_incrementing_on_redelivery()
    {
        var startDispatcher = new StubStartDispatcher(WorkflowExecutionCommandDispatchStatus.Accepted);
        var handler = new ChildStartExecutor(
            startDispatcher,
            Options.Create(new DispatchWorkflowOptions { MaxNestingDepth = 12 }));
        var intent = NewOutboxItem(dispatchNestingDepth: 12).Intent;

        await handler.HandleAsync(intent);
        await handler.HandleAsync(intent);

        Assert.Equal(2, startDispatcher.Requests.Count);
        Assert.All(startDispatcher.Requests, request => Assert.Equal(12, request.DispatchNestingDepth));
    }

    [Fact]
    public async Task Over_limit_stored_depth_is_rejected_before_start_dispatch()
    {
        var startDispatcher = new StubStartDispatcher(WorkflowExecutionCommandDispatchStatus.Accepted);
        var handler = new ChildStartExecutor(
            startDispatcher,
            Options.Create(new DispatchWorkflowOptions { MaxNestingDepth = 4 }));
        var intent = NewOutboxItem(
            parentExecutable: new WorkflowExecutableIdentity(
                "parent-artifact",
                "parent-definition",
                "parent-version",
                "1.0.0",
                "sha256:parent"),
            dispatchNodeId: "dispatch-node",
            dispatchNestingDepth: 5).Intent;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () => await handler.HandleAsync(intent));

        Assert.Contains("nesting depth 5", exception.Message, StringComparison.Ordinal);
        Assert.Contains("maximum of 4", exception.Message, StringComparison.Ordinal);
        Assert.Empty(startDispatcher.Requests);
    }

    [Fact]
    public void Executor_rejects_invalid_host_maximum()
    {
        var startDispatcher = new StubStartDispatcher(WorkflowExecutionCommandDispatchStatus.Accepted);

        Assert.Throws<ArgumentOutOfRangeException>(() => new ChildStartExecutor(
            startDispatcher,
            Options.Create(new DispatchWorkflowOptions { MaxNestingDepth = 0 })));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("node-b", null)]
    [InlineData(null, "transport-child-start")]
    [InlineData(" ", "transport-child-start")]
    [InlineData("node-b", " ")]
    public async Task DeferredStartWithoutCompleteDurableEvidence_RemainsAnOutboxFailure(
        string? owningNode,
        string? transportItemId)
    {
        var metadata = new Dictionary<string, string>();
        if (owningNode is not null)
            metadata["runtime.distributed.owningNode"] = owningNode;
        if (transportItemId is not null)
            metadata["runtime.distributed.transportItemId"] = transportItemId;
        var startDispatcher = new StubStartDispatcher(WorkflowExecutionCommandDispatchStatus.Deferred, metadata);
        var store = new RecordingOutboxStore(NewOutboxItem());
        var processor = new RuntimePostCommitOutboxProcessor(
            store,
            new HandlerIntentDispatcher(new ChildStartExecutor(startDispatcher)),
            new FixedTimeProvider(DispatchWorkflowRuntimeTestFixture.Now));

        var result = await processor.ProcessAsync(new RuntimePostCommitOutboxProcessRequest(limit: 10));

        Assert.Equal(1, result.AttemptedCount);
        Assert.Equal(0, result.DeliveredCount);
        Assert.Equal(1, result.FailedCount);
        Assert.NotEqual(RuntimePostCommitOutboxStatus.Delivered, Assert.Single(store.Results).Status);
        Assert.Single(startDispatcher.Requests);
    }

    [Fact]
    public async Task DeferredStartWithoutDurableEvidence_IsClassifiedAsSafeTransientDeliveryFailure()
    {
        var executor = new ChildStartExecutor(new StubStartDispatcher(WorkflowExecutionCommandDispatchStatus.Deferred));

        var exception = await Assert.ThrowsAsync<RuntimePostCommitDeliveryException>(
            () => executor.HandleAsync(NewOutboxItem().Intent).AsTask());

        Assert.Equal(PostCommitFailureKind.Transient, exception.Kind);
        Assert.Equal("child-start-delivery-failed", exception.Code);
        Assert.Equal("The child workflow could not be started.", exception.SafeSummary);
        Assert.DoesNotContain("set by test", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DispatcherInfrastructureException_IsClassifiedWithoutLeakingDetails()
    {
        var executor = new ChildStartExecutor(new ThrowingStartDispatcher(
            new InvalidOperationException("provider-secret stack-secret")));

        var exception = await Assert.ThrowsAsync<RuntimePostCommitDeliveryException>(
            () => executor.HandleAsync(NewOutboxItem().Intent).AsTask());

        Assert.Equal(PostCommitFailureKind.Transient, exception.Kind);
        Assert.Equal("child-start-delivery-failed", exception.Code);
        Assert.DoesNotContain("provider-secret", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stack-secret", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.IsType<InvalidOperationException>(exception.InnerException);
    }

    [Fact]
    public async Task AdmissionStoreException_IsClassifiedWithoutLeakingDetails()
    {
        var executor = new ChildStartExecutor(
            new StubStartDispatcher(WorkflowExecutionCommandDispatchStatus.Accepted),
            new ThrowingAdmissionStore(),
            new FixedTimeProvider(DispatchWorkflowRuntimeTestFixture.Now));

        var exception = await Assert.ThrowsAsync<RuntimePostCommitDeliveryException>(
            () => executor.HandleAsync(NewOutboxItem().Intent).AsTask());

        Assert.Equal(PostCommitFailureKind.Transient, exception.Kind);
        Assert.Equal("child-start-delivery-failed", exception.Code);
        Assert.Equal("The child workflow could not be started.", exception.SafeSummary);
        Assert.DoesNotContain("provider-secret", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.IsType<InvalidOperationException>(exception.InnerException);
    }

    [Fact]
    public async Task DispatcherSuppliedDeliveryClassification_IsWrappedWithFixedChildStartClassification()
    {
        var hostile = new RuntimePostCommitDeliveryException(
            PostCommitFailureKind.Permanent,
            "provider-controlled-code",
            "provider-secret summary-secret");
        var executor = new ChildStartExecutor(new ThrowingStartDispatcher(hostile));

        var exception = await Assert.ThrowsAsync<RuntimePostCommitDeliveryException>(
            () => executor.HandleAsync(NewOutboxItem().Intent).AsTask());

        Assert.NotSame(hostile, exception);
        Assert.Equal(PostCommitFailureKind.Transient, exception.Kind);
        Assert.Equal("child-start-delivery-failed", exception.Code);
        Assert.Equal("The child workflow could not be started.", exception.SafeSummary);
        Assert.DoesNotContain("provider-controlled-code", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("provider-secret", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("summary-secret", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Same(hostile, exception.InnerException);
    }

    [Fact]
    public async Task TamperedStartContext_IsRejectedBeforeAdmissionOrExternalDispatch()
    {
        var startDispatcher = new StubStartDispatcher(WorkflowExecutionCommandDispatchStatus.Accepted);
        var dispatchStore = new InMemoryWorkflowDispatchStore();
        var pending = NewDispatchRecord();
        await dispatchStore.SaveAsync(pending);
        var executor = new ChildStartExecutor(
            startDispatcher,
            dispatchStore,
            new FixedTimeProvider(DispatchWorkflowRuntimeTestFixture.Now.AddMinutes(1)));
        var item = NewOutboxItem(tenantId: "tenant-tampered");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => executor.HandleAsync(item.Intent).AsTask());

        Assert.Contains("does not match the committed dispatch context", exception.Message, StringComparison.Ordinal);
        Assert.Empty(startDispatcher.Requests);
        Assert.Equal(WorkflowDispatchStatus.Pending, (await dispatchStore.FindAsync(pending.DispatchId))?.Status);
    }

    private static RuntimePostCommitOutboxItem NewOutboxItem(
        WorkflowExecutableSourceProvenance? childSource = null,
        WorkflowExecutableIdentity? parentExecutable = null,
        string? dispatchNodeId = null,
        int dispatchNestingDepth = 0,
        string tenantId = "tenant-42",
        WorkflowRunKind runKind = WorkflowRunKind.PublishedRun,
        WorkflowTestScope? testScope = null)
    {
        var identity = NewIdentity();
        var source = childSource ?? (parentExecutable is null
            ? WorkflowExecutableSourceProvenance.From(DispatchWorkflowRuntimeTestFixture.ChildSourceReference())
            : null);
        var payload = new WorkflowDispatchStartPayload(
            identity.DispatchId,
            "parent-handler",
            "activity-handler",
            identity.ChildWorkflowExecutionId,
            DispatchWorkflowRuntimeTestFixture.ChildIdentity,
            source,
            new Dictionary<string, JsonElement> { ["message"] = JsonSerializer.SerializeToElement("hello") },
            "correlation-handler",
            tenantId,
            new WorkflowExecutionPartition("partition-eu"),
            runKind,
            new WorkflowExecutionAuthoritySnapshot("parent-handler", "root-initiator"),
            parentExecutable,
            dispatchNodeId,
            dispatchNestingDepth,
            testScope: testScope);
        var intent = new RuntimePostCommitIntent(
            identity.StartIntentId,
            "parent-handler",
            DispatchWorkflowConstants.StartChildIntentKind,
            DispatchWorkflowRuntimeTestFixture.Now,
            "activity-handler",
            identity.StartIdempotencyKey,
            JsonSerializer.SerializeToElement(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        return new RuntimePostCommitOutboxItem(
            outboxItemId: $"outbox:{intent.IntentId}",
            intent: intent,
            status: RuntimePostCommitOutboxStatus.Pending,
            recordedAt: DispatchWorkflowRuntimeTestFixture.Now,
            availableAt: DispatchWorkflowRuntimeTestFixture.Now);
    }

    private static WorkflowDispatchRecord NewDispatchRecord(
        WorkflowRunKind runKind = WorkflowRunKind.PublishedRun,
        WorkflowTestScope? testScope = null)
    {
        var identity = NewIdentity();
        return new WorkflowDispatchRecord(
            identity.DispatchId,
            "parent-handler",
            "activity-handler",
            identity.ChildWorkflowExecutionId,
            DispatchWorkflowRuntimeTestFixture.ChildIdentity,
            WorkflowExecutableSourceProvenance.From(DispatchWorkflowRuntimeTestFixture.ChildSourceReference()),
            WorkflowDispatchMode.FireAndForget,
            WorkflowDispatchStatus.Pending,
            "correlation-handler",
            "tenant-42",
            new WorkflowExecutionPartition("partition-eu"),
            runKind,
            new WorkflowExecutionAuthoritySnapshot("parent-handler", "root-initiator"),
            [new WorkflowDispatchInputDescriptor("message", "System.String")],
            DispatchWorkflowRuntimeTestFixture.Now,
            DispatchWorkflowRuntimeTestFixture.Now,
            metadata: null,
            testScope: testScope);
    }

    private static WorkflowTestScope NewTestScope() =>
        new(
            "scope-child",
            DispatchWorkflowRuntimeTestFixture.Now.AddHours(1),
            "tenant-42",
            new WorkflowExecutionPartition("partition-eu"));

    private static WorkflowDispatchIdentity NewIdentity() => new("parent-handler", "activity-handler");

    private sealed class HandlerIntentDispatcher(ChildStartExecutor handler) : IRuntimePostCommitIntentDispatcher
    {
        public ValueTask DispatchAsync(RuntimePostCommitIntent intent, CancellationToken cancellationToken = default) =>
            handler.HandleAsync(intent, cancellationToken);
    }

    private sealed class StubStartDispatcher(
        WorkflowExecutionCommandDispatchStatus status,
        IReadOnlyDictionary<string, string>? metadata = null) : IWorkflowStartDispatcher
    {
        internal List<WorkflowExecutionStartDispatchRequest> Requests { get; } = [];

        public ValueTask<WorkflowExecutionStartDispatchResult> DispatchAsync(
            WorkflowExecutionStartDispatchRequest request,
            WorkflowExecutableReferenceScope requiredScope = WorkflowExecutableReferenceScope.Published,
            WorkflowExecutionCommandDispatchOptions? dispatchOptions = null,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            var workflowExecutionId = request.WorkflowExecutionId!;
            var reason = status is WorkflowExecutionCommandDispatchStatus.Rejected or
                WorkflowExecutionCommandDispatchStatus.Deferred or
                WorkflowExecutionCommandDispatchStatus.AcceptedButFaulted
                ? "set by test"
                : null;
            return ValueTask.FromResult(new WorkflowExecutionStartDispatchResult(
                workflowExecutionId,
                DispatchWorkflowRuntimeTestFixture.ChildIdentity,
                new WorkflowExecutionCommandDispatchResult(
                    envelopeId: "envelope-handler",
                    workflowExecutionId: workflowExecutionId,
                    status: status,
                    recordedAt: DispatchWorkflowRuntimeTestFixture.Now,
                    reason: reason,
                    metadata: metadata),
                new WorkflowExecutionActorDescriptor(
                    workflowExecutionId,
                    "actor-handler",
                    "test",
                    WorkflowExecutionActorStatus.Active,
                    WorkflowExecutionActorCapabilities.InProcessMailbox,
                    DispatchWorkflowRuntimeTestFixture.Now),
                WorkflowExecutableSourceProvenance.From(DispatchWorkflowRuntimeTestFixture.ChildSourceReference())));
        }
    }

    private sealed class ThrowingStartDispatcher(Exception exception) : IWorkflowStartDispatcher
    {
        public ValueTask<WorkflowExecutionStartDispatchResult> DispatchAsync(
            WorkflowExecutionStartDispatchRequest request,
            WorkflowExecutableReferenceScope requiredScope = WorkflowExecutableReferenceScope.Published,
            WorkflowExecutionCommandDispatchOptions? dispatchOptions = null,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<WorkflowExecutionStartDispatchResult>(exception);
    }

    private sealed class RecordingOutboxStore(RuntimePostCommitOutboxItem item) : IRuntimePostCommitOutboxStore
    {
        internal List<RuntimePostCommitOutboxDeliveryResult> Results { get; } = [];

        public ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxItem>> GetDeliverableAsync(
            RuntimePostCommitOutboxQuery query,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyCollection<RuntimePostCommitOutboxItem>>([item]);

        public ValueTask RecordDeliveryResultAsync(
            RuntimePostCommitOutboxDeliveryResult result,
            CancellationToken cancellationToken = default)
        {
            Results.Add(result);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingAdmissionStore : IWorkflowDispatchStore, IWorkflowDispatchAdmissionStore
    {
        public ValueTask<WorkflowDispatchRecord> SaveAsync(
            WorkflowDispatchRecord record,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<WorkflowDispatchRecord?> FindAsync(
            string dispatchId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<WorkflowDispatchRecord?>(new InvalidOperationException("provider-secret"));

        public ValueTask<IReadOnlyCollection<WorkflowDispatchRecord>> ListAsync(
            string parentWorkflowExecutionId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<WorkflowDispatchAdmissionResult> TryAdmitAsync(
            string dispatchId,
            DateTimeOffset admittedAt,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
