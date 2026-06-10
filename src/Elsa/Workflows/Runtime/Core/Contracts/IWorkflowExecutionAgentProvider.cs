namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Resolves provider-owned execution agents. Providers enforce one active mailbox per workflow execution id.
/// </summary>
public interface IWorkflowExecutionAgentProvider
{
    ValueTask<IWorkflowExecutionAgent> GetAgentAsync(string workflowExecutionId, CancellationToken cancellationToken = default);
}
