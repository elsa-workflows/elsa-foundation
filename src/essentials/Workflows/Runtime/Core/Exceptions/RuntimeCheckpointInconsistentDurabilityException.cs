namespace Elsa.Workflows.Runtime.Core.Exceptions;

public sealed class RuntimeCheckpointInconsistentDurabilityException : Exception
{
    public RuntimeCheckpointInconsistentDurabilityException(string commitId, Exception innerException)
        : base($"Runtime checkpoint commit '{commitId}' may have persisted checkpoint state without recording all pending post-commit work.", innerException)
    {
        CommitId = commitId;
    }

    public string CommitId { get; }
}
