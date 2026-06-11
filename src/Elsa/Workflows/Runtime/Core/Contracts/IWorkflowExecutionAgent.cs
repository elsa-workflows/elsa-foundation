using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Active execution mailbox for one workflow execution id.
/// </summary>
public interface IWorkflowExecutionAgent
{
    WorkflowExecutionAgentDescriptor Descriptor { get; }

    ValueTask<WorkflowExecutionCommandDispatchResult> EnqueueAsync(WorkflowExecutionCommandEnvelope envelope, CancellationToken cancellationToken = default);
}
