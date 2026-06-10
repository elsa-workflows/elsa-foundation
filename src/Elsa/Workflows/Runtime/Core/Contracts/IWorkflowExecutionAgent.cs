using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Active execution mailbox for one workflow execution id.
/// </summary>
public interface IWorkflowExecutionAgent
{
    string WorkflowExecutionId { get; }

    ValueTask EnqueueAsync(WorkflowExecutionCommand command, CancellationToken cancellationToken = default);
}
