using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts.Alterations;

/// <summary>
/// Activity-owned runtime rule for an already-compiled operator-schedulable child relation. Runtime passes only
/// immutable compiled evidence and observed state; callers never choose the successor identity, scope, provenance,
/// or materialized inputs.
/// </summary>
public interface IOperatorActivitySchedulingPolicy
{
    string PolicyKey { get; }
    int SchemaVersion { get; }

    OperatorActivitySchedulingPolicyDecision Evaluate(OperatorActivitySchedulingPolicyContext context);
}

public interface IOperatorActivitySchedulingPolicyRegistry
{
    IOperatorActivitySchedulingPolicy? Find(string policyKey, int schemaVersion);
}

public sealed record OperatorActivitySchedulingPolicyContext(
    WorkflowExecutionState Workflow,
    ActivityExecutionState Parent,
    ExecutableNode Child,
    OperatorActivitySchedulingCapability Capability,
    IReadOnlyList<ActivityExecutionState> ActivityExecutions);

public sealed record OperatorActivitySchedulingPolicyDecision(
    bool IsAccepted,
    string? Code,
    string? Message,
    ActivitySchedulingProvenance? Provenance)
{
    public static OperatorActivitySchedulingPolicyDecision Accepted(ActivitySchedulingProvenance provenance) =>
        new(true, null, null, provenance ?? throw new ArgumentNullException(nameof(provenance)));

    public static OperatorActivitySchedulingPolicyDecision Rejected(string code, string message) =>
        new(false, code, message, null);
}
