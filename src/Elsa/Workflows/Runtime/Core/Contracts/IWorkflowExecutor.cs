using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

public interface IWorkflowExecutor
{
    ValueTask<WorkflowExecutionResult> ExecuteAsync(WorkflowExecutable executable, CancellationToken cancellationToken = default);
}
