using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Checkpoints;

namespace Elsa.Activities.Runtime.Services;

/// <summary>Admission and receipt validation run only inside the normal serialized native invocation handler.</summary>
internal sealed class BackgroundActivityCoordinator(
    BackgroundActivityCompletionCodec codec,
    RuntimeCheckpointCommitter committer,
    TimeProvider timeProvider,
    IBackgroundActivityWorkStore? store = null)
{
    public async ValueTask<BackgroundActivityWorkItem?> GetResultOrAdmitAsync(
        RuntimeSchedulerWorkItem workItem,
        RuntimeInvokeActivityCommandPayload payload,
        ExecutableNode node,
        ActivityExecutionState state,
        CancellationToken cancellationToken)
    {
        if (store is null)
            throw new InvalidOperationException("BG-ACT-001: Background activities require a checkpoint-integrated durable work store.");
        BackgroundActivityEligibility.Validate(node);
        codec.ResolveResultType(node.ActivityContract!);
        if (state.Metadata.TryGetValue(BackgroundActivityEligibility.WorkIdMetadataKey, out var workId))
        {
            var item = await store.FindAsync(workItem.WorkflowExecutionId, workId, cancellationToken)
                ?? throw new InvalidOperationException("BG-ACT-003: The admitted background work record is missing.");
            ValidateReceiptTarget(item.Request, payload, state);
            if (item.Status != BackgroundActivityWorkStatus.ResultReady ||
                item.Request.CoordinatorWorkItemId != workItem.WorkItemId)
                return null;
            if (item.Result is null || item.ReceiptDigest != BackgroundActivityWorkReceipt.ComputeDigest(item.Request, item.Result))
                throw new InvalidOperationException("BG-ACT-003: The background result receipt does not match its pinned work.");
            return item;
        }
        if (payload.Reason == BackgroundActivityEligibility.ResultReason)
            throw new InvalidOperationException("BG-ACT-003: A background result cannot create a new invocation.");
        if (state.InputSnapshot is null)
            throw new InvalidOperationException("BG-ACT-003: Background admission requires committed immutable inputs.");
        var prepared = ActivityAttemptActivationClaimer.PrepareBackgroundInvokeClaim(timeProvider, workItem, payload, state);
        var occurredAt = prepared.Commit.Checkpoint.OccurredAt;
        workId = RuntimeChainId.Derive($"background:{workItem.WorkflowExecutionId}", prepared.Claim.Attempt.AttemptId);
        var resultWorkItem = new RuntimeSchedulerWorkItem(
            RuntimeChainId.Derive(workItem.WorkItemId, "background-result"),
            workItem.WorkflowExecutionId,
            RuntimeChainId.Derive(workItem.CommandId, "background-result"),
            WorkflowExecutionCommandKind.InvokeActivity,
            workItem.EnvelopeId,
            RuntimeChainId.Derive(workItem.IdempotencyKey, "background-result"),
            occurredAt, occurredAt, workItem.Sequence is { } sequence ? sequence + 1 : null,
            JsonSerializer.SerializeToElement(new RuntimeInvokeActivityCommandPayload(
                payload.PinnedExecutable, payload.ExecutableNodeId, payload.ActivityExecutionId, BackgroundActivityEligibility.ResultReason)),
            workItem.CommandMetadata, workItem.EnvelopeMetadata, workItem.ExecutionScopeId, workItem.Attempt);
        var request = new BackgroundActivityWorkRequest(
            workId, workItem.WorkflowExecutionId, payload.ActivityExecutionId, prepared.Claim.Attempt,
            payload.PinnedExecutable, payload.ExecutableNodeId, node.ActivityContract!.SchemaFingerprint,
            state.InputSnapshot, resultWorkItem);
        var metadata = new Dictionary<string, string>(prepared.Claim.State.Metadata, StringComparer.Ordinal)
        {
            [BackgroundActivityEligibility.WorkIdMetadataKey] = workId
        };
        var admitted = ActivityAttemptActivationClaimer.MarkInitialActivationCompleted(
            prepared.Claim.State with { Metadata = RuntimeModelMetadata.Snapshot(metadata), SubStatus = "BackgroundPending" }, workItem.WorkItemId);
        var changes = new RuntimeCheckpointStateChangeSet(
            workflowExecution: null, scheduler: null,
            activityExecutions: [new RuntimeStateChange<ActivityExecutionState>(payload.ActivityExecutionId, RuntimeStateChangeOperation.Upsert, admitted, prepared.Commit.Metadata)],
            bookmarks: [], durableValues: [], incidents: [], operational: [])
            .WithBackgroundActivityWork(new BackgroundActivityWorkCheckpointChanges(admissions: [request]));
        await committer.CommitAsync(prepared.Commit with { StateChanges = changes }, cancellationToken);
        return null;
    }

    private static void ValidateReceiptTarget(
        BackgroundActivityWorkRequest request,
        RuntimeInvokeActivityCommandPayload payload,
        ActivityExecutionState state)
    {
        if (request.ActivityExecutionId != payload.ActivityExecutionId || request.ExecutableNodeId != payload.ExecutableNodeId ||
            request.Executable != payload.PinnedExecutable || request.InvocationId != state.InvocationId ||
            request.ContractFingerprint != state.InputSnapshot?.ContractFingerprint ||
            state.Attempts?.Any(attempt => attempt.AttemptId == request.AttemptId && attempt.EndedAt is null) != true ||
            !JsonElement.DeepEquals(JsonSerializer.SerializeToElement(request.InputSnapshot), JsonSerializer.SerializeToElement(state.InputSnapshot)))
            throw new InvalidOperationException("BG-ACT-003: The receipt does not belong to the current pinned native invocation.");
    }
}
