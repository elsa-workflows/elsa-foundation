using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;

namespace Elsa.Workflows.Runtime.Api.Handlers;

/// <summary>The published-workflow start operation the runtime execute endpoint dispatches to.</summary>
public interface IWorkflowExecutionStartService
{
    Task<WorkflowExecutionStartDispatchView> ExecuteAsync(ExecuteWorkflow request, CancellationToken cancellationToken);
}
