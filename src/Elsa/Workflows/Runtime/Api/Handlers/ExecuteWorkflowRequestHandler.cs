using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Workflows.Runtime.Api.Handlers;

public sealed class ExecuteWorkflowRequestHandler(
    IWorkflowExecutableStore executableStore,
    IWorkflowExecutor executor)
    : IRequestHandler<ExecuteWorkflow, WorkflowExecutionView>
{
    public async Task<WorkflowExecutionView> Handle(ExecuteWorkflow request, CancellationToken cancellationToken)
    {
        var executable = await executableStore.FindAsync(request.ArtifactId, cancellationToken)
            ?? throw new ArgumentException($"Workflow executable artifact '{request.ArtifactId}' does not exist.");

        var result = await executor.ExecuteAsync(executable, cancellationToken);
        return WorkflowExecutionView.From(result);
    }
}
