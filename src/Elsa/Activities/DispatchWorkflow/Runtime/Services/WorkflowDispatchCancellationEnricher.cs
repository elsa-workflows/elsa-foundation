using System.Text.Json;
using System.Runtime.CompilerServices;
using Elsa.Activities.DispatchWorkflow.Runtime.Constants;
using Elsa.Activities.DispatchWorkflow.Runtime.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.DispatchWorkflow.Runtime.Services;

/// <summary>Records provider-atomic child cancellation responsibility in a parent Cancel checkpoint.</summary>
public sealed class WorkflowDispatchCancellationEnricher : IRuntimeCheckpointCommitEnricher
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly IWorkflowDispatchStore _store;
    private readonly IPostCommitOutboxLookupStore? _outboxLookupStore;

    public WorkflowDispatchCancellationEnricher(
        IWorkflowDispatchStore store,
        IPostCommitOutboxLookupStore? outboxLookupStore = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
        _outboxLookupStore = outboxLookupStore;
    }

    public int Order => 50;

    public async ValueTask<RuntimeCheckpointCommit> EnrichAsync(
        RuntimeCheckpointCommit commit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commit);
        cancellationToken.ThrowIfCancellationRequested();

        if (commit.StateChanges.WorkflowExecution?.State is not { Status: WorkflowExecutionStatus.Cancelled } parent)
            return commit;

        var requests = commit.StateChanges.WorkflowDispatchCancellations.ToList();
        var intents = commit.PostCommitIntents.ToList();
        await foreach (var record in ListForParentAsync(parent.WorkflowExecutionId, cancellationToken))
        {
            if (record.Mode != WorkflowDispatchMode.WaitForCompletion ||
                !WorkflowDispatchLifecycle.IsCancellationPropagationEnabled(record))
            {
                continue;
            }

            var identity = new WorkflowDispatchIdentity(
                record.ParentWorkflowExecutionId,
                record.ParentActivityExecutionId);
            var isActiveOrMarked = record.Status is WorkflowDispatchStatus.Pending or WorkflowDispatchStatus.Started ||
                                   WorkflowDispatchLifecycle.WasCancelledBeforeAdmission(record) ||
                                   WorkflowDispatchLifecycle.IsCancellationRequested(record);
            RuntimePostCommitOutboxItem? committed = null;
            if (!isActiveOrMarked && _outboxLookupStore is not null)
            {
                committed = await _outboxLookupStore.FindAsync(
                    identity.ChildCancelOutboxItemId(commit.CommitId),
                    cancellationToken);
            }
            if (!isActiveOrMarked && committed is null)
                continue;

            var request = new WorkflowDispatchCancellationRequest(
                record.DispatchId,
                record.ParentWorkflowExecutionId,
                record.ParentActivityExecutionId,
                record.ChildWorkflowExecutionId,
                commit.Checkpoint.OccurredAt);
            var existingRequest = requests.SingleOrDefault(item =>
                StringComparer.Ordinal.Equals(item.DispatchId, request.DispatchId));
            if (existingRequest is not null && !WorkflowDispatchCancellationRequest.Equivalent(existingRequest, request))
                throw new InvalidOperationException($"Workflow dispatch cancellation request '{request.DispatchId}' conflicts with the parent checkpoint.");
            if (existingRequest is null)
                requests.Add(request);

            var intent = WorkflowDispatchCancelIntentFactory.Create(record, commit.Checkpoint.OccurredAt);
            if (committed is not null && !Equivalent(committed.Intent, intent))
            {
                throw new InvalidOperationException(
                    $"Committed child-cancel intent '{intent.IntentId}' conflicts with dispatch '{record.DispatchId}'.");
            }
            var existingIntent = intents.SingleOrDefault(item =>
                StringComparer.Ordinal.Equals(item.IntentId, intent.IntentId));
            if (existingIntent is not null && !Equivalent(existingIntent, intent))
                throw new InvalidOperationException($"Child-cancel intent '{intent.IntentId}' conflicts with dispatch '{record.DispatchId}'.");
            if (existingIntent is null)
                intents.Add(intent);
        }

        return commit with
        {
            StateChanges = commit.StateChanges.WithWorkflowDispatchCancellations(requests),
            PostCommitIntents = intents
        };
    }

    private async IAsyncEnumerable<WorkflowDispatchRecord> ListForParentAsync(
        string parentWorkflowExecutionId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (_store is not IWorkflowDispatchQueryStore queryStore)
        {
            foreach (var record in (await _store.ListAsync(parentWorkflowExecutionId, cancellationToken))
                         .OrderBy(record => record.CreatedAt)
                         .ThenBy(record => record.DispatchId, StringComparer.Ordinal))
            {
                yield return record;
            }

            yield break;
        }

        DateTimeOffset? afterCreatedAt = null;
        string? afterDispatchId = null;
        while (true)
        {
            var page = (await queryStore.QueryAsync(
                    new WorkflowDispatchQuery(
                        parentWorkflowExecutionId: parentWorkflowExecutionId,
                        take: WorkflowDispatchQuery.MaximumTake,
                        afterCreatedAt: afterCreatedAt,
                        afterDispatchId: afterDispatchId),
                    cancellationToken))
                .OrderBy(record => record.CreatedAt)
                .ThenBy(record => record.DispatchId, StringComparer.Ordinal)
                .ToArray();
            foreach (var record in page)
                yield return record;
            if (page.Length < WorkflowDispatchQuery.MaximumTake)
                break;

            var last = page[^1];
            if (afterCreatedAt is { } previousCreatedAt &&
                (last.CreatedAt < previousCreatedAt ||
                 last.CreatedAt == previousCreatedAt &&
                 StringComparer.Ordinal.Compare(last.DispatchId, afterDispatchId) <= 0))
            {
                throw new InvalidOperationException(
                    $"Workflow dispatch query for parent '{parentWorkflowExecutionId}' did not advance its continuation cursor.");
            }

            afterCreatedAt = last.CreatedAt;
            afterDispatchId = last.DispatchId;
        }
    }

    private static bool Equivalent(RuntimePostCommitIntent left, RuntimePostCommitIntent right) =>
        StringComparer.Ordinal.Equals(left.IntentId, right.IntentId) &&
        StringComparer.Ordinal.Equals(left.WorkflowExecutionId, right.WorkflowExecutionId) &&
        StringComparer.Ordinal.Equals(left.Kind, right.Kind) &&
        left.RecordedAt == right.RecordedAt &&
        StringComparer.Ordinal.Equals(left.ActivityExecutionId, right.ActivityExecutionId) &&
        StringComparer.Ordinal.Equals(left.IdempotencyKey, right.IdempotencyKey) &&
        StringComparer.Ordinal.Equals(left.DependsOnWaitRegistrationId, right.DependsOnWaitRegistrationId) &&
        left.WaitFailurePolicy == right.WaitFailurePolicy &&
        left.Payload is { } leftPayload &&
        right.Payload is { } rightPayload &&
        JsonElement.DeepEquals(leftPayload, rightPayload) &&
        left.Metadata.Count == right.Metadata.Count &&
        left.Metadata.All(item => right.Metadata.TryGetValue(item.Key, out var value) &&
                                  StringComparer.Ordinal.Equals(item.Value, value));
}
