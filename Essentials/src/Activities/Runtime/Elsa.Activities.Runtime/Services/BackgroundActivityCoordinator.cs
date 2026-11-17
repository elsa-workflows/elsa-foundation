using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Values;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Elsa.Activities.Runtime.Services;

/// <summary>Admission and receipt validation run only inside the normal serialized native invocation handler.</summary>
internal sealed class BackgroundActivityCoordinator(
    BackgroundActivityCompletionCodec codec,
    RuntimeCheckpointCommitter committer,
    IWorkflowExecutionStateStore workflowStateStore,
    IActivityExecutionStateStore activityStateStore,
    TimeProvider timeProvider,
    IOptions<BackgroundActivityWorkerOptions> options,
    IBackgroundActivityWorkStore? store = null)
{
    public async ValueTask<BackgroundActivityWorkItem?> GetResultOrAdmitAsync(
        RuntimeSchedulerWorkItem workItem,
        RuntimeInvokeActivityCommandPayload payload,
        WorkflowExecutable executable,
        ExecutableNode node,
        ActivityExecutionState state,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
            throw new BackgroundActivityValidationException("BG-ACT-001: Enable BackgroundActivities on this coordinator; remote worker deployments can set RunWorkers to false.");
        if (store is null)
            throw new BackgroundActivityValidationException("BG-ACT-001: Background activities require a checkpoint-integrated work store.");
        BackgroundActivityEligibility.Validate(node);
        codec.ResolveResultType(node.ActivityContract!);
        if (state.Metadata.TryGetValue(BackgroundActivityEligibility.WorkIdMetadataKey, out var workId))
        {
            var item = await store.FindAsync(workItem.WorkflowExecutionId, workId, cancellationToken)
                ?? throw new BackgroundActivityValidationException("BG-ACT-003: The admitted background work record is missing.");
            ValidateReceiptTarget(item.Request, payload, state);
            if (item.Status != BackgroundActivityWorkStatus.ResultReady ||
                item.Request.CoordinatorWorkItemId != workItem.WorkItemId)
                return null;
            if (item.Result is null || item.ReceiptDigest != BackgroundActivityWorkReceipt.ComputeDigest(item.Request, item.Result))
                throw new BackgroundActivityValidationException("BG-ACT-003: The background result receipt does not match its pinned work.");
            return item;
        }
        if (payload.Reason == BackgroundActivityEligibility.ResultReason)
            throw new BackgroundActivityValidationException("BG-ACT-003: A background result cannot create a new invocation.");
        if (state.InputSnapshot is null)
            throw new BackgroundActivityValidationException("BG-ACT-003: Background admission requires committed immutable inputs.");
        state = await RefreshInputsAsync(executable, node, state, serviceProvider, cancellationToken);
        var prepared = ActivityAttemptActivationClaimer.PrepareBackgroundInvokeClaim(timeProvider, workItem, payload, state);
        var occurredAt = prepared.Commit.Checkpoint.OccurredAt;
        // A refreshed evaluation is its own piece of work: its id, and the keys derived from it, name the evaluation.
        var evaluation = state.InputSnapshot!.EvaluationNumber;
        workId = RuntimeChainId.Derive(
            $"background:{workItem.WorkflowExecutionId}",
            evaluation == 1 ? prepared.Claim.Attempt.AttemptId : $"{prepared.Claim.Attempt.AttemptId}:evaluation:{evaluation}");
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
            state.InputSnapshot, resultWorkItem)
        {
            OperationKey = ActivityOperationKeyResolver.ResolveOperationKey(state),
            TargetOperationKey = ActivityOperationKeyResolver.ResolveTargetOperationKey(state)
        };
        var metadata = new Dictionary<string, string>(prepared.Claim.State.Metadata, StringComparer.Ordinal)
        {
            [BackgroundActivityEligibility.WorkIdMetadataKey] = workId
        };
        var admitted = ActivityAttemptActivationClaimer.MarkInitialActivationCompleted(
            prepared.Claim.State with { Metadata = RuntimeModelMetadata.Snapshot(metadata), SubStatus = "BackgroundPending" }, workItem.WorkItemId);
        // The marker is monotonic and committed with admission. Terminal checkpoints can then cancel all
        // outstanding branches without querying the work table for workflows that have never offloaded a body.
        var workflow = await workflowStateStore.FindAsync(workItem.WorkflowExecutionId, cancellationToken)
            ?? throw new BackgroundActivityValidationException("BG-ACT-003: Background admission requires a live workflow execution.");
        RuntimeStateChange<WorkflowExecutionState>? workflowChange = null;
        if (!workflow.SystemMetadata.ContainsKey(RuntimeMetadataKeys.WorkflowHasBackgroundActivityWork))
        {
            var workflowMetadata = new Dictionary<string, string>(workflow.SystemMetadata, StringComparer.Ordinal)
            {
                [RuntimeMetadataKeys.WorkflowHasBackgroundActivityWork] = "true"
            };
            workflowChange = new RuntimeStateChange<WorkflowExecutionState>(
                workflow.WorkflowExecutionId, RuntimeStateChangeOperation.Upsert,
                workflow with { SystemMetadata = RuntimeModelMetadata.Snapshot(workflowMetadata) }, prepared.Commit.Metadata);
        }
        var changes = new RuntimeCheckpointStateChangeSet(
            workflowExecution: workflowChange, scheduler: null,
            activityExecutions: [new RuntimeStateChange<ActivityExecutionState>(payload.ActivityExecutionId, RuntimeStateChangeOperation.Upsert, admitted, prepared.Commit.Metadata)],
            bookmarks: [], durableValues: [], incidents: [], operational: [])
            .WithBackgroundActivityWork(new BackgroundActivityWorkCheckpointChanges(admissions: [request]));
        await committer.CommitAsync(prepared.Commit with { StateChanges = changes }, cancellationToken);
        return null;
    }

    /// <summary>
    /// Spec 1221 FR-018 to FR-020: the admission refresh. A background step's inputs were read at Start; if a variable
    /// they read has a newer version now, its inputs are read again as a new evaluation of the same invocation, so the
    /// worker sees the current value instead of meeting a stale base at commit. The superseded snapshot stays in the
    /// history with the variables that changed, and the snapshot pinned here is immutable from then on. A node that
    /// turned the refresh off keeps the inputs Start read; its stale base meets the commit rule. Only the Start-to-
    /// admission gap is covered: the worker's own run is not refreshed.
    /// </summary>
    private async ValueTask<ActivityExecutionState> RefreshInputsAsync(
        WorkflowExecutable executable,
        ExecutableNode node,
        ActivityExecutionState state,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        var snapshot = state.InputSnapshot!;
        // A retry keeps its committed inputs (FR-014, ADR 0092 decision 8): a successor in a retry lineage carries the
        // source's snapshot on purpose and is not refreshed. A re-execution read its inputs at its own Start.
        if (state.Attempt is not null)
            return state;
        if (snapshot.InputReadSet is not { Entries.Count: > 0 } readSet ||
            (node.Metadata.TryGetValue(RuntimeMetadataKeys.BackgroundInputRefresh, out var refresh) &&
             StringComparer.Ordinal.Equals(refresh, RuntimeMetadataKeys.BackgroundInputRefreshOff)))
            return state;

        // Reading the scopes and the inputs fails as Start's materialization does: a provider failure goes to the
        // scheduler's retry policy, and an input expression failure keeps its typed evidence.
        var scopes = new RuntimeContainerScopeService(activityStateStore, workflowStateStore);
        var visible = await scopes.BuildVisibleFramesAsync(state.Execution.WorkflowExecutionId, state, cancellationToken: cancellationToken);
        // Inside a closed scope (a graph body) the variables are rows, carried by the durable values: those are loaded
        // only then, so a read of a graph-body variable is checked too.
        if (visible.ClosedScopeBoundary is not null)
        {
            var durableValues = await serviceProvider.GetRequiredService<IDurableValueStateStore>()
                .ListAllDurableValueStatesAsync(state.Execution.WorkflowExecutionId, cancellationToken);
            visible = await scopes.BuildVisibleFramesAsync(state.Execution.WorkflowExecutionId, state, cancellationToken: cancellationToken, durableValues: durableValues);
        }

        var changes = new List<VariableInputChange>();
        foreach (var entry in readSet.Entries)
        {
            var frame = visible.Frames.LastOrDefault(item => StringComparer.Ordinal.Equals(item.ScopeId, entry.ScopeId));
            if (frame is null || !frame.Values.ContainsKey(entry.VariableKey))
                continue;
            var current = frame.VersionOf(entry.VariableKey).Version;
            if (current != entry.Version)
                changes.Add(new VariableInputChange(entry.ScopeId, entry.VariableKey, entry.Version, current));
        }

        if (changes.Count == 0)
            return state;

        var refreshedAt = timeProvider.GetUtcNow();
        // Resolved only when a refresh is due, from the invocation's own services, as the Start handler materializes.
        var fresh = await serviceProvider.GetRequiredService<RuntimeActivityInputSnapshotMaterializer>().MaterializeAsync(executable, node, state, serviceProvider, refreshedAt, cancellationToken)
            with { Evaluation = snapshot.EvaluationNumber + 1 };
        return state with
        {
            InputSnapshot = fresh,
            InputRefreshes = [.. state.InputRefreshes ?? [], new ActivityInputRefresh(snapshot, fresh.EvaluationNumber, changes, refreshedAt)]
        };
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
            throw new BackgroundActivityValidationException("BG-ACT-003: The receipt does not belong to the current pinned native invocation.");
    }
}
