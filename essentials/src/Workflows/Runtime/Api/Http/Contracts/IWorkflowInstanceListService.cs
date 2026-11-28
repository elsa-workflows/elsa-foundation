using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;

namespace Elsa.Workflows.Runtime.Api.Handlers;

/// <summary>
/// The workflow-instance list operation the runtime endpoints dispatch to. The request's paging contract selects
/// the historical array route's bounds or the additive page route's bounded defaults.
/// </summary>
public interface IWorkflowInstanceListService
{
    Task<WorkflowInstanceListView> ListAsync(ListWorkflowInstances request, CancellationToken cancellationToken);
}
