using Elsa.Agent.Specialists.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Agent.Specialists.Services;

// Records responsibility in the native checkpoint. No specialist-store writes occur here.
internal sealed class SpecialistCheckpointEnricher(SpecialistSettlementBoundary boundary, IActivityExecutionStateStore activities) : IRuntimeCheckpointCommitEnricher
{
    /// <exception cref="SpecialistTurnException">A contribution failure retaining its cause and checkpoint/workflow identity.</exception>
    public async ValueTask<RuntimeCheckpointCommit> EnrichAsync(RuntimeCheckpointCommit commit, CancellationToken cancellationToken = default)
    {
        try { return await EnrichCoreAsync(commit, cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) { throw SpecialistSettlementBoundary.Failure(commit.CommitId, commit.WorkflowExecutionId, exception); }
    }

    private async ValueTask<RuntimeCheckpointCommit> EnrichCoreAsync(RuntimeCheckpointCommit commit, CancellationToken cancellationToken)
    {
        var expected = await ExpectedAsync(commit, cancellationToken);
        var intents = commit.PostCommitIntents.ToList();
        foreach (var intent in expected)
        {
            var prior = intents.SingleOrDefault(item => item.IntentId == intent.IntentId);
            if (prior is null) intents.Add(intent);
            else if (!prior.IsEquivalentTo(intent)) throw SpecialistSettlementBoundary.Invalid();
        }
        return commit with { PostCommitIntents = intents };
    }

    /// <exception cref="SpecialistTurnException">A final-envelope failure retaining its cause and checkpoint/workflow identity.</exception>
    public async ValueTask ValidateFinalAsync(RuntimeCheckpointCommit commit, CancellationToken cancellationToken = default)
    {
        try { await ValidateFinalCoreAsync(commit, cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) { throw SpecialistSettlementBoundary.Failure(commit.CommitId, commit.WorkflowExecutionId, exception); }
    }

    private async ValueTask ValidateFinalCoreAsync(RuntimeCheckpointCommit commit, CancellationToken cancellationToken)
    {
        var expected = await ExpectedAsync(commit, cancellationToken);
        var actual = commit.PostCommitIntents.Where(item => item.Kind is SpecialistSettlementBoundary.AppliedKind or SpecialistSettlementBoundary.CancelKind).ToArray();
        if (actual.Length != expected.Count || expected.Any(intent => actual.Count(item => item.IsEquivalentTo(intent)) != 1))
            throw SpecialistSettlementBoundary.Invalid();
    }

    private async ValueTask<IReadOnlyList<RuntimePostCommitIntent>> ExpectedAsync(RuntimeCheckpointCommit commit, CancellationToken cancellationToken)
    {
        var changed = commit.StateChanges.ActivityExecutions.Where(change => change.Operation == RuntimeStateChangeOperation.Upsert)
            .ToDictionary(change => change.StateId, change => change.State, StringComparer.Ordinal);
        var completed = commit.Checkpoint.Name == RuntimeCheckpointNames.ActivityCompleted ?
            changed.Values.Where(state => state.Status == ActivityExecutionStatus.Completed && state.ContractIdentity is not null).ToArray() : [];
        var cancelled = changed.Values.Where(state => state.Status == ActivityExecutionStatus.Cancelled).ToList();
        var wholeCancelled = commit.StateChanges.WorkflowExecution is { Operation: RuntimeStateChangeOperation.Upsert, State.Status: WorkflowExecutionStatus.Cancelled };
        if (wholeCancelled)
        {
            string? continuation = null;
            do
            {
                var page = await activities.ListPageAsync(new(commit.WorkflowExecutionId, continuationToken: continuation), cancellationToken);
                cancelled.AddRange(page.Items.Where(state => !changed.ContainsKey(state.InvocationId) &&
                    state.Status is not (ActivityExecutionStatus.Completed or ActivityExecutionStatus.Faulted or ActivityExecutionStatus.Cancelled or ActivityExecutionStatus.Superseded)));
                if (page.NextContinuationToken is not null && page.NextContinuationToken == continuation) throw SpecialistSettlementBoundary.Invalid();
                continuation = page.NextContinuationToken;
            } while (continuation is not null);
        }
        if (completed.Length == 0 && cancelled.Count == 0) return [];
        var (workflow, executable) = await boundary.WorkflowAsync(commit.WorkflowExecutionId, cancellationToken);
        if (wholeCancelled)
        {
            var terminal = commit.StateChanges.WorkflowExecution!.State;
            if (terminal.PinnedExecutable != workflow.PinnedExecutable || terminal.WorkflowExecutionId != workflow.WorkflowExecutionId || terminal.TenantId != workflow.TenantId || terminal.Partition != workflow.Partition)
                throw SpecialistSettlementBoundary.Invalid();
            workflow = terminal;
        }
        var intents = new List<RuntimePostCommitIntent>();
        foreach (var parent in completed)
        {
            if (!executable.NodesById.TryGetValue(parent.Execution.ExecutableNodeId, out var node)) throw SpecialistSettlementBoundary.Invalid();
            if (node.ActivityContract?.Activation.Capability != SpecialistRuntimeIds.FacadeCapability) continue;
            intents.Add(SpecialistSettlementBoundary.Intent(await boundary.AppliedAsync(workflow, parent, node, cancellationToken)));
        }
        foreach (var parent in cancelled)
        {
            if (!executable.NodesById.TryGetValue(parent.Execution.ExecutableNodeId, out var node)) throw SpecialistSettlementBoundary.Invalid();
            if (node.ActivityContract?.Activation.Capability != SpecialistRuntimeIds.FacadeCapability) continue;
            intents.Add(SpecialistSettlementBoundary.Intent(await boundary.CancelledAsync(workflow, parent, node, cancellationToken)));
        }
        return intents;
    }
}
