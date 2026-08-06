namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Mutable provider authority for an otherwise immutable checkpoint preparation identity.</summary>
public sealed record RuntimeCheckpointAuthorityBinding(RuntimeExecutionFence? CurrentAuthorityFence, long AuthorityRevision)
{
    public RuntimeCheckpointAuthorityBinding Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(AuthorityRevision, 1);
        return this;
    }

    /// <summary>
    /// Returns whether an active successor lease may take over this binding. Ownership fencing tokens are globally
    /// monotonic for a workflow, so every strictly higher token is a valid candidate regardless of lease identity.
    /// Providers must still prove atomically that the candidate is the durable active ownership lease.
    /// </summary>
    public bool CanAdvanceTo(RuntimeExecutionFence target)
    {
        ArgumentNullException.ThrowIfNull(target);
        Validate();

        if (CurrentAuthorityFence is null)
            return true;
        if (target.FencingToken <= CurrentAuthorityFence.FencingToken)
            return false;
        return true;
    }
}
