using System.Text.Json;
using Elsa.Activities.DispatchWorkflow.Runtime.Constants;
using Elsa.Activities.DispatchWorkflow.Runtime.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Activities.DispatchWorkflow.Tests;

public sealed class ChildStartExecutorTests
{
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

    private static RuntimePostCommitOutboxItem NewOutboxItem()
    {
        var identity = NewIdentity();
        var source = WorkflowExecutableSourceProvenance.From(DispatchWorkflowRuntimeTestFixture.ChildSourceReference());
        var payload = new WorkflowDispatchStartPayload(
            identity.DispatchId,
            "parent-handler",
            "activity-handler",
            identity.ChildWorkflowExecutionId,
            DispatchWorkflowRuntimeTestFixture.ChildIdentity,
            source,
            new Dictionary<string, JsonElement> { ["message"] = JsonSerializer.SerializeToElement("hello") },
            "correlation-handler",
            "tenant-42",
            new WorkflowExecutionPartition("partition-eu"),
            WorkflowRunKind.PublishedRun,
            new WorkflowExecutionAuthoritySnapshot("parent-handler", "root-initiator"));
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
            var reason = status is WorkflowExecutionCommandDispatchStatus.Rejected or WorkflowExecutionCommandDispatchStatus.Deferred
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

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
