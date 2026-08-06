namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>An ordered provider-atomic fold over durable prepared logical checkpoints.</summary>
public sealed record RuntimeCheckpointPreparedFoldRequest(
    string WorkflowExecutionId,
    IReadOnlyList<RuntimeCheckpointPreparedFoldMember> Members,
    long MaxWorkflowCheckpointOrder,
    RuntimeCheckpointStateChangeSet FoldedStateChanges,
    RuntimeExecutionFence? TargetAuthorityFence,
    RuntimeCheckpointRecoveryRoute? RecoveryRoute = null,
    IReadOnlyList<string>? DeliveredPostCommitOutboxItemIds = null)
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
        if (RecoveryRoute is not null and not RuntimeCheckpointRecoveryRoute.SourceFree)
            throw new ArgumentException("Only source-free recovery may combine authority adoption with a terminal fold.", nameof(RecoveryRoute));

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

        var expectedFence = Members[0].ExpectedCurrentAuthorityFence;
        var expectedRevision = Members[0].ExpectedAuthorityRevision;
        if (Members.Any(member =>
                !Equals(member.ExpectedCurrentAuthorityFence, expectedFence) ||
                member.ExpectedAuthorityRevision != expectedRevision))
            throw new ArgumentException("Prepared-checkpoint fold members must share one exact current authority binding.", nameof(Members));

        if (RecoveryRoute is null)
        {
            if (!Equals(TargetAuthorityFence, expectedFence))
                throw new ArgumentException("A live fold cannot change the current authority binding.", nameof(TargetAuthorityFence));
        }
        else
        {
            ArgumentNullException.ThrowIfNull(TargetAuthorityFence);
            if (Members.Any(member => member.Token.RecoveryAuthority is not null))
                throw new ArgumentException("Only originally source-free members may use atomic recovery adoption and fold.", nameof(Members));
            if (!new RuntimeCheckpointAuthorityBinding(expectedFence, expectedRevision).CanAdvanceTo(TargetAuthorityFence))
                throw new ArgumentException("A source-free recovery fold requires a strictly newer target authority.", nameof(TargetAuthorityFence));
        }
        if (!RuntimeCheckpointPreparedFoldBuilder.HasValidDeliveredOutboxExclusions(this))
            throw new ArgumentException("Delivered outbox exclusions must be unique effects contributed by committed fold members.", nameof(DeliveredPostCommitOutboxItemIds));
        return this;
    }
}
