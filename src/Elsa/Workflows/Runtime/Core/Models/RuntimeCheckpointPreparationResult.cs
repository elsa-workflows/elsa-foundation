namespace Elsa.Workflows.Runtime.Core.Models;

public enum RuntimeCheckpointPreparationStatus
{
    Prepared,
    Replay,
    Conflict,
    OwnershipLost
}

public sealed record RuntimeCheckpointPreparationResult(
    RuntimeCheckpointPreparationStatus Status,
    RuntimeCheckpointPreparationToken? Token,
    RuntimeCheckpointCommitStoreResult? Receipt = null)
{
    public static RuntimeCheckpointPreparationResult Prepared(RuntimeCheckpointPreparationToken token) => new(RuntimeCheckpointPreparationStatus.Prepared, token);
    public static RuntimeCheckpointPreparationResult Replay(RuntimeCheckpointPreparationToken token, RuntimeCheckpointCommitStoreResult? receipt) => new(RuntimeCheckpointPreparationStatus.Replay, token, receipt);
    public static RuntimeCheckpointPreparationResult Conflict() => new(RuntimeCheckpointPreparationStatus.Conflict, null);
    public static RuntimeCheckpointPreparationResult OwnershipLost() => new(RuntimeCheckpointPreparationStatus.OwnershipLost, null);
}

public enum RuntimeCheckpointCommitStoreStatus
{
    Committed,
    Skipped,
    Failed,
    Replay,
    Conflict,
    OwnershipLost
}
