namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>The terminal provider result and exact receipt for every logical checkpoint in an atomic fold.</summary>
public sealed record RuntimeCheckpointPreparedFoldResult(
    RuntimeCheckpointCommitStoreStatus Status,
    IReadOnlyDictionary<string, RuntimeCheckpointCommitStoreResult> Receipts);
