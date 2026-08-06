namespace Elsa.Workflows.Runtime.Core.Models;

public enum RuntimeCheckpointPreparedAdoptionStatus
{
    Adopted,
    Replay,
    Conflict,
    OwnershipLost
}
