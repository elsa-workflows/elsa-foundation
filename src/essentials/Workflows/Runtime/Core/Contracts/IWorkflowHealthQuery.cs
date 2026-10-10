using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Optional provider capability for filtering workflow-execution history by incident health before paging.
/// </summary>
public interface IWorkflowHealthQuery
{
    /// <summary>
    /// Returns whether this provider can filter against the incident rows exposed by the selected incident store.
    /// A provider must not infer compatibility only from the workflow-execution store it implements.
    /// </summary>
    bool SupportsIncidentStore(IIncidentStateStore selectedIncidentStore);

    /// <summary>Queries a bounded history page using the requested current incident-health predicate.</summary>
    ValueTask<WorkflowExecutionStatePage> QueryHealthPageAsync(
        WorkflowExecutionStatePageQuery query,
        IncidentHealth health,
        CancellationToken cancellationToken = default);
}
