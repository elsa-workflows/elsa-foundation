namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>An ordered provider-atomic fold over durable prepared logical checkpoints.</summary>
public sealed record RuntimeCheckpointPreparedFoldRequest(
    string WorkflowExecutionId,
    IReadOnlyList<RuntimeCheckpointPreparedFoldMember> Members,
    long MaxWorkflowCheckpointOrder,
    RuntimeCheckpointStateChangeSet FoldedStateChanges)
{
    /// <summary>Validates request-local bounds and explicit member ordering; providers still verify durable completeness and authority.</summary>
    /// <exception cref="ArgumentException">The workflow, member set, order, or explicit disposition is invalid.</exception>
    /// <exception cref="ArgumentNullException">A required value is null.</exception>
    public RuntimeCheckpointPreparedFoldRequest Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(WorkflowExecutionId);
        ArgumentNullException.ThrowIfNull(Members);
        ArgumentNullException.ThrowIfNull(FoldedStateChanges);
        if (Members.Count == 0)
            throw new ArgumentException("A prepared-checkpoint fold must contain at least one explicit member.", nameof(Members));
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxWorkflowCheckpointOrder, 1);

        long priorOrder = 0;
        var commitIds = new HashSet<string>(StringComparer.Ordinal);
        var ledgerTokens = new HashSet<string>(StringComparer.Ordinal);
        foreach (var member in Members)
        {
            ArgumentNullException.ThrowIfNull(member);
            member.Validate();
            if (member.WorkflowCheckpointOrder <= priorOrder)
                throw new ArgumentException("Prepared-checkpoint fold members must be in strictly increasing workflow order.", nameof(Members));
            if (!commitIds.Add(member.CommitId) || !ledgerTokens.Add(member.Token.LedgerToken))
                throw new ArgumentException("Prepared-checkpoint fold members cannot duplicate commit or ledger identities.", nameof(Members));
            if (member.PreparedCommit is { } prepared &&
                !StringComparer.Ordinal.Equals(prepared.Commit.WorkflowExecutionId, WorkflowExecutionId))
                throw new ArgumentException("Every rehydrated fold member must belong to the requested workflow execution.", nameof(Members));
            priorOrder = member.WorkflowCheckpointOrder;
        }

        if (priorOrder != MaxWorkflowCheckpointOrder)
            throw new ArgumentException("The fold maximum order must equal the last explicit member order.", nameof(MaxWorkflowCheckpointOrder));
        return this;
    }
}
