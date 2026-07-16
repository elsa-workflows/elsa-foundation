using System.Text.Json;
using Elsa.Activities.DispatchWorkflow.Runtime.Constants;
using Elsa.Activities.DispatchWorkflow.Runtime.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.DispatchWorkflow.Runtime.Services;

/// <summary>Records replay-stable parent resume responsibility in a waited child's Completed checkpoint.</summary>
public sealed class WorkflowDispatchCompletionEnricher(
    IWorkflowOutputSource workflowOutputSource,
    IPostCommitOutboxLookupStore outboxLookupStore) : IRuntimeCheckpointCommitEnricher
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    public int Order => 100;

    public async ValueTask<RuntimeCheckpointCommit> EnrichAsync(
        RuntimeCheckpointCommit commit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commit);
        cancellationToken.ThrowIfCancellationRequested();

        if (commit.StateChanges.WorkflowExecution?.State is not { Status: WorkflowExecutionStatus.Completed } child)
            return commit;

        var dispatches = commit.StateChanges.WorkflowDispatches
            .Where(change => change.Operation != RuntimeStateChangeOperation.Delete)
            .Select(change => change.State)
            .Where(record => record.Mode == WorkflowDispatchMode.WaitForCompletion &&
                             record.Status == WorkflowDispatchStatus.Completed &&
                             StringComparer.Ordinal.Equals(record.ChildWorkflowExecutionId, child.WorkflowExecutionId))
            .OrderBy(record => record.DispatchId, StringComparer.Ordinal)
            .ToArray();
        if (dispatches.Length == 0)
            return commit;

        var intents = commit.PostCommitIntents.ToList();
        IReadOnlyCollection<RuntimeWorkflowOutput>? safeOutputs = null;
        foreach (var dispatch in dispatches)
        {
            var identity = new WorkflowDispatchIdentity(
                dispatch.ParentWorkflowExecutionId,
                dispatch.ParentActivityExecutionId);
            var outboxItemId = identity.ParentResumeOutboxItemId(commit.CommitId);
            var committed = await outboxLookupStore.FindAsync(outboxItemId, cancellationToken);
            var existing = intents.SingleOrDefault(intent => StringComparer.Ordinal.Equals(intent.IntentId, identity.ParentResumeIntentId));
            if (committed is not null && existing is not null && !IntentsEquivalent(committed.Intent, existing))
                throw new InvalidOperationException($"Parent-resume intent '{identity.ParentResumeIntentId}' conflicts with its committed outbox item.");

            RuntimePostCommitIntent intent;
            if (committed is not null)
            {
                intent = committed.Intent;
                if (existing is null)
                    intents.Add(intent);
            }
            else
            {
                safeOutputs ??= await workflowOutputSource.ReadAsync(
                    child.WorkflowExecutionId,
                    commit.StateChanges.DurableValues,
                    cancellationToken);
                var canonical = CreateIntent(dispatch, identity, safeOutputs, commit.Checkpoint.OccurredAt);
                if (existing is not null && !IntentsEquivalent(canonical, existing))
                    throw new InvalidOperationException($"Parent-resume intent '{identity.ParentResumeIntentId}' conflicts with the policy-safe completed result.");

                intent = existing ?? canonical;
                if (existing is null)
                    intents.Add(intent);
            }

            ValidateIntent(intent, dispatch, identity, commit.Checkpoint.OccurredAt);
        }

        return commit with { PostCommitIntents = intents };
    }

    private static RuntimePostCommitIntent CreateIntent(
        WorkflowDispatchRecord dispatch,
        WorkflowDispatchIdentity identity,
        IReadOnlyCollection<RuntimeWorkflowOutput> outputs,
        DateTimeOffset recordedAt)
    {
        var resultOutputs = outputs
            .OrderBy(output => output.Name, StringComparer.Ordinal)
            .Select(output => new DispatchWorkflowOutput(
                output.Name,
                output.Value,
                output.Type.Id ?? output.Type.Kind,
                output.IsRedacted))
            .ToArray();
        var result = new DispatchWorkflowResult(
            dispatch.ChildWorkflowExecutionId,
            WorkflowDispatchStatus.Completed,
            resultOutputs);
        var payload = new WorkflowDispatchParentResumePayload(
            dispatch.DispatchId,
            dispatch.ParentWorkflowExecutionId,
            dispatch.ParentActivityExecutionId,
            dispatch.ChildWorkflowExecutionId,
            identity.WaitBookmarkId,
            DispatchWorkflowConstants.WaitStimulusType,
            identity.WaitStimulusHash,
            result);

        return new RuntimePostCommitIntent(
            intentId: identity.ParentResumeIntentId,
            workflowExecutionId: dispatch.ParentWorkflowExecutionId,
            kind: DispatchWorkflowConstants.ResumeParentIntentKind,
            recordedAt: recordedAt,
            activityExecutionId: dispatch.ParentActivityExecutionId,
            idempotencyKey: identity.ParentResumeIdempotencyKey,
            payload: JsonSerializer.SerializeToElement(payload, SerializerOptions),
            metadata: new Dictionary<string, string>
            {
                [RuntimeMetadataKeys.DispatchId] = dispatch.DispatchId,
                [RuntimeMetadataKeys.ChildWorkflowExecutionId] = dispatch.ChildWorkflowExecutionId
            });
    }

    internal static WorkflowDispatchParentResumePayload ValidateIntent(
        RuntimePostCommitIntent intent,
        WorkflowDispatchRecord dispatch,
        WorkflowDispatchIdentity identity,
        DateTimeOffset recordedAt)
    {
        if (!StringComparer.Ordinal.Equals(intent.IntentId, identity.ParentResumeIntentId) ||
            !StringComparer.Ordinal.Equals(intent.WorkflowExecutionId, dispatch.ParentWorkflowExecutionId) ||
            !StringComparer.Ordinal.Equals(intent.ActivityExecutionId, dispatch.ParentActivityExecutionId) ||
            !StringComparer.Ordinal.Equals(intent.Kind, DispatchWorkflowConstants.ResumeParentIntentKind) ||
            !StringComparer.Ordinal.Equals(intent.IdempotencyKey, identity.ParentResumeIdempotencyKey) ||
            intent.RecordedAt != recordedAt ||
            intent.Metadata.Count != 2 ||
            !intent.Metadata.TryGetValue(RuntimeMetadataKeys.DispatchId, out var dispatchId) ||
            !StringComparer.Ordinal.Equals(dispatchId, dispatch.DispatchId) ||
            !intent.Metadata.TryGetValue(RuntimeMetadataKeys.ChildWorkflowExecutionId, out var childWorkflowExecutionId) ||
            !StringComparer.Ordinal.Equals(childWorkflowExecutionId, dispatch.ChildWorkflowExecutionId) ||
            intent.Payload is null)
        {
            throw new InvalidOperationException($"Parent-resume intent '{intent.IntentId}' conflicts with dispatch '{dispatch.DispatchId}'.");
        }

        var payload = intent.Payload.Value.Deserialize<WorkflowDispatchParentResumePayload>(SerializerOptions)
            ?? throw new InvalidOperationException($"Parent-resume intent '{intent.IntentId}' has no valid payload.");
        if (!StringComparer.Ordinal.Equals(payload.DispatchId, dispatch.DispatchId) ||
            !StringComparer.Ordinal.Equals(payload.ChildWorkflowExecutionId, dispatch.ChildWorkflowExecutionId))
        {
            throw new InvalidOperationException($"Parent-resume intent '{intent.IntentId}' payload conflicts with dispatch '{dispatch.DispatchId}'.");
        }

        return payload;
    }

    private static bool IntentsEquivalent(RuntimePostCommitIntent left, RuntimePostCommitIntent right) =>
        StringComparer.Ordinal.Equals(left.IntentId, right.IntentId) &&
        StringComparer.Ordinal.Equals(left.WorkflowExecutionId, right.WorkflowExecutionId) &&
        StringComparer.Ordinal.Equals(left.Kind, right.Kind) &&
        left.RecordedAt == right.RecordedAt &&
        StringComparer.Ordinal.Equals(left.ActivityExecutionId, right.ActivityExecutionId) &&
        StringComparer.Ordinal.Equals(left.IdempotencyKey, right.IdempotencyKey) &&
        StringComparer.Ordinal.Equals(left.DependsOnWaitRegistrationId, right.DependsOnWaitRegistrationId) &&
        left.WaitFailurePolicy == right.WaitFailurePolicy &&
        PayloadsEqual(left.Payload, right.Payload) &&
        DictionariesEqual(left.Metadata, right.Metadata);

    private static bool PayloadsEqual(JsonElement? left, JsonElement? right) =>
        left is null
            ? right is null
            : right is not null && JsonElement.DeepEquals(left.Value, right.Value);

    private static bool DictionariesEqual(
        IReadOnlyDictionary<string, string> left,
        IReadOnlyDictionary<string, string> right) =>
        left.Count == right.Count &&
        left.All(pair => right.TryGetValue(pair.Key, out var value) && StringComparer.Ordinal.Equals(pair.Value, value));
}
