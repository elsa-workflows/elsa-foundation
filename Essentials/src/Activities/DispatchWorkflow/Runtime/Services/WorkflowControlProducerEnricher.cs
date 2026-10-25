using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.DispatchWorkflow.Runtime.Services;

/// <summary>Stages one root's control stream and mandatory terminal barrier through the existing checkpoint authority.</summary>
internal sealed class WorkflowControlProducerEnricher(
    IActivityExecutionStateStore activities,
    IRuntimeWorkflowControlBoundary boundary) : IRuntimeCheckpointCommitEnricher
{
    // Lifecycle projection is phase zero; cancellation is50; the single terminal resume is100.
    public int Order => 75;

    public async ValueTask<RuntimeCheckpointCommit> EnrichAsync(RuntimeCheckpointCommit commit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commit);
        string? rootId = null;
        try
        {
            // A root's own terminal checkpoint holds its complete batch; it never emits an independent
            // nonterminal notification. T042 transports nonterminal emissions and the single terminal result.
            foreach (var change in commit.StateChanges.ActivityExecutions.Where(change =>
                         change.Operation != RuntimeStateChangeOperation.Delete && change.State.ProducedControl is not null))
            {
                var root = change.State;
                rootId = root.InvocationId;
                var target = await boundary.ResolveAsync(commit.WorkflowExecutionId, root.InvocationId, cancellationToken)
                    ?? throw WorkflowDispatchControlPolicy.Failure("control producer has no admitted boundary");
                ValidateOwned(root.ProducedControl!, target);
                if (root.ParentActivityExecutionId is not null)
                    throw WorkflowDispatchControlPolicy.Failure("control producer is not a root");
            }

            if (commit.StateChanges.WorkflowExecution?.State is not { } workflow || !workflow.Status.IsTerminal())
                return commit;
            var dispatches = commit.StateChanges.WorkflowDispatches.Where(change => change.Operation != RuntimeStateChangeOperation.Delete)
                .Select(change => change.State).Where(record => record.ChildWorkflowExecutionId == workflow.WorkflowExecutionId &&
                    record.Mode == WorkflowDispatchMode.WaitForCompletion && WorkflowDispatchControlPolicy.ReadCapability(record.Metadata) is not null)
                .ToArray();
            if (dispatches.Length == 0)
                return commit;
            if (dispatches.Length != 1)
                throw WorkflowDispatchControlPolicy.Failure("terminal child has ambiguous dispatch authority");

            var rootState = await ReadRootAsync(commit, cancellationToken);
            rootId = rootState.InvocationId;
            var address = await boundary.ResolveAsync(workflow.WorkflowExecutionId, rootState.InvocationId, cancellationToken)
                ?? throw WorkflowDispatchControlPolicy.Failure("terminal root has no admitted boundary");
            if (address.DispatchId != dispatches[0].DispatchId)
                throw WorkflowDispatchControlPolicy.Failure("terminal dispatch differs from the root boundary");
            var produced = rootState.ProducedControl;
            if (produced is not null)
                ValidateOwned(produced, address);
            if (produced?.IsTerminal != true)
            {
                if (produced?.Ordinal == long.MaxValue)
                    throw WorkflowDispatchControlPolicy.Failure("root control ordinal is exhausted");
                produced = new RuntimeChildControlBatch(1, address.ConsumerKey, address.ConsumerVersion,
                    address.ChildWorkflowExecutionId, rootState.InvocationId, "terminal-commit:" + commit.CommitId,
                    produced is null ? 0 : produced.Ordinal + 1, isTerminal: true);
            }

            // Always carry the same terminal root projection, even on replay of an originally root-less
            // workflow terminal checkpoint. Otherwise the second enrichment would change its fingerprint.
            var rootChange = new RuntimeStateChange<ActivityExecutionState>(rootState.InvocationId,
                RuntimeStateChangeOperation.Upsert, rootState with { ProducedControl = produced }, commit.Metadata);
            var changes = commit.StateChanges.ActivityExecutions.Where(change => change.StateId != rootState.InvocationId)
                .Append(rootChange).ToArray();
            return commit with
            {
                StateChanges = commit.StateChanges.WithActivityExecutions(changes),
                Checkpoint = commit.Checkpoint with
                {
                    ActivityExecutionIds = commit.Checkpoint.ActivityExecutionIds.Append(rootState.InvocationId).Distinct(StringComparer.Ordinal).ToArray()
                }
            };
        }
        catch (OperationCanceledException) { throw; }
        catch (WorkflowControlBoundaryException) { throw; }
        catch (Exception exception) { throw new WorkflowControlBoundaryException(commit.WorkflowExecutionId, rootId, exception); }
    }

    private async ValueTask<ActivityExecutionState> ReadRootAsync(RuntimeCheckpointCommit commit, CancellationToken cancellationToken)
    {
        var roots = new Dictionary<string, ActivityExecutionState>(StringComparer.Ordinal);
        string? cursor = null;
        do
        {
            var page = await activities.ListPageAsync(new ActivityExecutionStatePageQuery(commit.WorkflowExecutionId, continuationToken: cursor), cancellationToken);
            foreach (var state in page.Items.Where(state => state.ParentActivityExecutionId is null))
                roots.Add(state.InvocationId, state);
            cursor = page.NextContinuationToken;
        } while (cursor is not null);
        foreach (var change in commit.StateChanges.ActivityExecutions)
        {
            if (change.Operation == RuntimeStateChangeOperation.Delete || change.State.ParentActivityExecutionId is not null)
                roots.Remove(change.StateId);
            else
                roots[change.StateId] = change.State;
        }
        return roots.Count == 1 ? roots.Values.Single() : throw WorkflowDispatchControlPolicy.Failure("terminal root history is missing or ambiguous");
    }

    internal static void ValidateOwned(RuntimeChildControlBatch batch, RuntimeChildControlTarget target)
    {
        if (batch.ConsumerKey != target.ConsumerKey || batch.ConsumerVersion != target.ConsumerVersion ||
            batch.ChildWorkflowExecutionId != target.ChildWorkflowExecutionId || batch.RootActivityExecutionId != target.RootActivityExecutionId)
            throw WorkflowDispatchControlPolicy.Failure("produced control identity differs from admitted root");
    }
}
