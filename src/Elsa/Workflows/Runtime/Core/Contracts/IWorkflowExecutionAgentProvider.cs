using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Resolves provider-owned execution agents. Providers enforce one active mailbox per workflow execution id.
/// </summary>
public interface IWorkflowExecutionAgentProvider
{
    WorkflowExecutionAgentCapabilities Capabilities { get; }

    ValueTask<IWorkflowExecutionAgent> GetAgentAsync(WorkflowExecutionAgentActivationRequest request, CancellationToken cancellationToken = default);

    ValueTask PassivateAsync(WorkflowExecutionAgentPassivationRequest request, CancellationToken cancellationToken = default);
}
