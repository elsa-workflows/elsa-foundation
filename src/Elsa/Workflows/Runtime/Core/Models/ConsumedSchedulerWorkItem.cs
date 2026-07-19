namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// A claimed scheduler work item folded into a checkpoint commit so its fence-checked deletion happens inside the same
/// durable unit-of-work as the checkpoint (WU-1 / spec 105). The fence is the claim <see cref="ClaimOwnerId"/> and
/// <see cref="FencingToken"/> — both renewal-stable — not the provider revision: a claim renewal advances the revision
/// but keeps owner and token, so fencing on owner+token lets a renewed claim consume its item while a successor reclaim
/// (which advances the token) fails claim-lost.
/// </summary>
public sealed record ConsumedSchedulerWorkItem(
    string WorkflowExecutionId,
    string WorkItemId,
    string ClaimOwnerId,
    long FencingToken)
{
    /// <summary>Projects the consume record from a live scheduler-work claim.</summary>
    public static ConsumedSchedulerWorkItem FromClaim(RuntimeSchedulerWorkClaim claim)
    {
        ArgumentNullException.ThrowIfNull(claim);
        return new ConsumedSchedulerWorkItem(
            claim.Item.WorkflowExecutionId,
            claim.Item.WorkItemId,
            claim.OwnerId,
            claim.FencingToken);
    }
}
