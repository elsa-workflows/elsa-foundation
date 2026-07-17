using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Stable provider-neutral detached-dispatch durability assessment.</summary>
public interface IWorkflowDispatchReadinessAssessor
{
    ValueTask<WorkflowDispatchReadinessReport> AssessAsync(CancellationToken cancellationToken = default);
}

/// <summary>Contributed evidence for one required dispatch durability component.</summary>
public interface IWorkflowDispatchDurabilityEvidence
{
    string Component { get; }
    WorkflowDispatchDurabilityLevel Level { get; }
}
