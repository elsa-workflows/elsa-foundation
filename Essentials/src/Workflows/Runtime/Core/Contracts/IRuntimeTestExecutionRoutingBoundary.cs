using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Qualifies the selected actor and physical transport so predecessor consumers cannot receive test work.</summary>
public interface IRuntimeTestExecutionRoutingBoundary
{
    ValueTask<bool> CanRouteTestEntryAsync(WorkflowTestEntry entry, IWorkflowExecutionActorProvider actors,
        CancellationToken cancellationToken = default);
}
