using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.DispatchWorkflow.Runtime.Services;

/// <summary>Stages a nonterminal root emission as one deterministic immutable outbox responsibility.</summary>
internal sealed class WorkflowControlNotificationEnricher(IRuntimeWorkflowControlBoundary boundary,
    IPostCommitOutboxLookupStore outbox, IActivityExecutionStateStore activities) : IRuntimeCheckpointCommitEnricher
{
    public int Order => 90;

    public async ValueTask<RuntimeCheckpointCommit> EnrichAsync(RuntimeCheckpointCommit commit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commit);
        string? rootId = null;
        try
        {
            var intents = commit.PostCommitIntents.ToList();
            var changes = commit.StateChanges.ActivityExecutions.ToDictionary(change => change.StateId, StringComparer.Ordinal);
            foreach (var change in commit.StateChanges.ActivityExecutions.Where(change =>
                         change.Operation != RuntimeStateChangeOperation.Delete && change.State.ProducedControl is { IsTerminal: false }))
            {
                var root = change.State;
                rootId = root.InvocationId;
                var batch = root.ProducedControl!;
                // A same-work inspection/fault checkpoint can carry an unchanged projection too. The
                // source-commit anchor travels atomically with its sole outbox row; source work alone is insufficient.
                var retained = await activities.FindAsync(commit.WorkflowExecutionId, root.InvocationId, cancellationToken);
                var retainedMatches = JsonElement.DeepEquals(JsonSerializer.SerializeToElement(retained?.ProducedControl),
                    JsonSerializer.SerializeToElement(batch));
                var anchor = root.ProducedControlSourceCommitId ?? (retainedMatches ? retained?.ProducedControlSourceCommitId : null);
                if (root.ProducedControlSourceCommitId is { } claimed && retainedMatches &&
                    retained?.ProducedControlSourceCommitId is { } original && claimed != original)
                    throw WorkflowDispatchControlPolicy.Failure("notification source-commit anchor conflicts with retained history");
                if (root.ProducedControlSourceCommitId is { } claimedAnchor && claimedAnchor != commit.CommitId &&
                    (!retainedMatches || retained?.ProducedControlSourceCommitId != claimedAnchor))
                    throw WorkflowDispatchControlPolicy.Failure("notification carries an uncommitted source anchor");
                if (anchor is not null)
                    ArgumentException.ThrowIfNullOrWhiteSpace(anchor);
                if (anchor is not null && anchor != commit.CommitId)
                {
                    changes[root.InvocationId] = change with { State = root with { ProducedControlSourceCommitId = anchor } };
                    continue;
                }
                if (!commit.Metadata.TryGetValue(RuntimeMetadataKeys.SchedulerWorkItemId, out var sourceWork) || sourceWork != batch.SourceWorkItemId)
                    throw WorkflowDispatchControlPolicy.Failure("new notification does not name this checkpoint source work");
                var target = await boundary.ResolveAsync(commit.WorkflowExecutionId, root.InvocationId, cancellationToken)
                    ?? throw WorkflowDispatchControlPolicy.Failure("notification has no admitted boundary");
                WorkflowControlProducerEnricher.ValidateOwned(batch, target);
                var transport = WorkflowControlTransport.Create(target, batch, commit.CommitId,
                    WorkflowControlTransport.NotifyIntentId(target, batch.Ordinal));
                var canonical = WorkflowControlTransport.Notification(transport, commit.Checkpoint.OccurredAt);
                var committed = await outbox.FindAsync(transport.SourceOutboxItemId, cancellationToken);
                var existing = intents.SingleOrDefault(intent => intent.IntentId == canonical.IntentId);
                if (retained?.ProducedControl is { } previous && previous.Ordinal >= batch.Ordinal && committed is null)
                    throw WorkflowDispatchControlPolicy.Failure("anchored notification source outbox item is missing");
                if (committed is not null && (committed.OutboxItemId != transport.SourceOutboxItemId ||
                    !committed.Intent.IsEquivalentTo(canonical)) || existing is not null && !existing.IsEquivalentTo(canonical))
                    throw WorkflowDispatchControlPolicy.Failure("notification conflicts with its immutable committed source");
                if (existing is null)
                    intents.Add(committed?.Intent ?? canonical);
                changes[root.InvocationId] = change with { State = root with { ProducedControlSourceCommitId = commit.CommitId } };
            }
            return commit with { PostCommitIntents = intents,
                StateChanges = commit.StateChanges.WithActivityExecutions(changes.Values.ToArray()) };
        }
        catch (OperationCanceledException) { throw; }
        catch (WorkflowControlBoundaryException) { throw; }
        catch (Exception exception) { throw new WorkflowControlBoundaryException(commit.WorkflowExecutionId, rootId, exception); }
    }
}
