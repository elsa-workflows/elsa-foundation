namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Mutable provider authority for an otherwise immutable checkpoint preparation identity.</summary>
public sealed record RuntimeCheckpointAuthorityBinding(RuntimeExecutionFence? CurrentAuthorityFence, long AuthorityRevision)
{
    public RuntimeCheckpointAuthorityBinding Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(AuthorityRevision, 1);
        return this;
    }
}
