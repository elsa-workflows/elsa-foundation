using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Processes commands after an execution agent accepts them into its single-writer mailbox.
/// </summary>
public interface IWorkflowExecutionCommandProcessor
{
    ValueTask ProcessAsync(WorkflowExecutionCommandEnvelope envelope, CancellationToken cancellationToken = default);
}
