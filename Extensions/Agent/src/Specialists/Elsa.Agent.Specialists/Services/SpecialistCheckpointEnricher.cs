using Elsa.Agent.Specialists.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Agent.Specialists.Services;

// Records responsibility in the native checkpoint. No specialist-store writes occur here.
internal sealed class SpecialistCheckpointEnricher(SpecialistSettlementBoundary boundary) : IRuntimeCheckpointCommitEnricher
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
        var actual = commit.PostCommitIntents.Where(item => item.Kind == SpecialistSettlementBoundary.AppliedKind).ToArray();
        if (actual.Length != expected.Count || expected.Any(intent => actual.Count(item => item.IsEquivalentTo(intent)) != 1))
            throw SpecialistSettlementBoundary.Invalid();
    }

    private async ValueTask<IReadOnlyList<RuntimePostCommitIntent>> ExpectedAsync(RuntimeCheckpointCommit commit, CancellationToken cancellationToken)
    {
        if (commit.Checkpoint.Name != RuntimeCheckpointNames.ActivityCompleted) return [];
        var candidates = commit.StateChanges.ActivityExecutions.Where(change => change.Operation == RuntimeStateChangeOperation.Upsert &&
            change.State.Status == ActivityExecutionStatus.Completed && change.State.ContractIdentity is not null).ToArray();
        if (candidates.Length == 0) return [];
        var (workflow, executable) = await boundary.WorkflowAsync(commit.WorkflowExecutionId, cancellationToken);
        var intents = new List<RuntimePostCommitIntent>();
        foreach (var change in candidates)
        {
            if (!executable.NodesById.TryGetValue(change.State.Execution.ExecutableNodeId, out var node)) throw SpecialistSettlementBoundary.Invalid();
            if (node.ActivityContract?.Activation.Capability != SpecialistRuntimeIds.FacadeCapability) continue;
            intents.Add(SpecialistSettlementBoundary.Intent(await boundary.AppliedAsync(workflow, change.State, node, cancellationToken)));
        }
        return intents;
    }
}
