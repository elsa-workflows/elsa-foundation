using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;

namespace Elsa.Workflows.Runtime.Api.Handlers;

/// <summary>
/// The workflow-instance detail read operation the runtime endpoints dispatch to. A null view means the workflow
/// execution is missing or the caller may not inspect it.
/// </summary>
public interface IWorkflowInstanceDetailsService
{
    Task<WorkflowInstanceDetailsView?> GetAsync(GetWorkflowInstance request, CancellationToken cancellationToken);
}
