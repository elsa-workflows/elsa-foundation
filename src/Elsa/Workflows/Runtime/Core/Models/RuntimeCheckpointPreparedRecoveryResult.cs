namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Controls whether a drain may dispatch after examining the durable Prepared frontier.</summary>
public sealed record RuntimeCheckpointPreparedRecoveryResult(bool CanDispatch)
{
    public static RuntimeCheckpointPreparedRecoveryResult Ready { get; } = new(true);
    public static RuntimeCheckpointPreparedRecoveryResult RetryRequired { get; } = new(false);
}
