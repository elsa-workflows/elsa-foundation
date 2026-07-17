namespace Elsa.Workflows.Runtime.Core.Exceptions;

/// <summary>Thrown when an idempotency key is replayed with a different logical checkpoint bundle.</summary>
public sealed class RuntimeCheckpointReplayConflictException(string commitId)
    : Exception($"Runtime checkpoint commit '{commitId}' was replayed with a conflicting payload.")
{
    public string CommitId { get; } = commitId;
}
