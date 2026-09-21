using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Stable provider-neutral detached-dispatch durability assessment.</summary>
public interface IWorkflowDispatchReadinessAssessor
{
    ValueTask<WorkflowDispatchReadinessReport> AssessAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Contributed evidence for one required dispatch durability component. Not reporting-only since #1320:
/// <c>WorkflowSchedulerCommandRouter</c> parks a shed command's work item only when some contribution
/// names <c>WorkflowDispatchDurabilityComponents.Resumption</c>, so a leaf supplying its own re-driver without
/// contributing that component turns parking off for every gated kind on that host.
/// </summary>
public interface IWorkflowDispatchDurabilityEvidence
{
    string Component { get; }
    WorkflowDispatchDurabilityLevel Level { get; }
}
