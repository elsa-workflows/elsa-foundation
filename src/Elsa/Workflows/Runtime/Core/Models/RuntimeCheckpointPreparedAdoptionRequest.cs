namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>One workflow-bound, inclusive, exact-set provider CAS for either recovery route.</summary>
public sealed record RuntimeCheckpointPreparedAdoptionRequest(
    string WorkflowExecutionId,
    RuntimeCheckpointRecoveryRoute Route,
    long ThroughWorkflowCheckpointOrder,
    RuntimeExecutionFence TargetAuthorityFence,
    IReadOnlyList<RuntimeCheckpointPreparedAdoptionMember> Members);
