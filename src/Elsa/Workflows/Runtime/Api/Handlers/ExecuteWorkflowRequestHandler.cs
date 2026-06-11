using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Api.Handlers;

public sealed class ExecuteWorkflowRequestHandler(
    IWorkflowExecutionStartDispatcher startDispatcher)
    : IRequestHandler<ExecuteWorkflow, WorkflowExecutionStartDispatchView>
{
    private const string RequestedBy = "runtime-api";

    public async Task<WorkflowExecutionStartDispatchView> Handle(ExecuteWorkflow request, CancellationToken cancellationToken)
    {
        var result = await startDispatcher.DispatchAsync(new WorkflowExecutionStartDispatchRequest(request.ArtifactId, RequestedBy), cancellationToken);
        return WorkflowExecutionStartDispatchView.From(result);
    }
}
