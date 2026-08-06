namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Idempotent provider receipt for one exact-set authority adoption.</summary>
public sealed record RuntimeCheckpointPreparedAdoptionReceipt(
    RuntimeCheckpointPreparedAdoptionStatus Status,
    string WorkflowExecutionId,
    RuntimeCheckpointRecoveryRoute Route,
    long ThroughWorkflowCheckpointOrder,
    RuntimeExecutionFence TargetAuthorityFence,
    IReadOnlyList<string> CommitIds);
