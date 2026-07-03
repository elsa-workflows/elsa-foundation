using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Resolves provider-owned execution agents. Providers enforce one active mailbox per workflow execution id.
/// </summary>
/// <remarks>
/// This is the enforcement point of the single-writer ownership contract (RT-2): all command dispatch for a workflow
/// execution MUST route through its agent mailbox, which serializes entry so that at most one drain runs per execution
/// at a time. Downstream components (drainer, scheduler queue, checkpoint committer) rely on this guarantee; the
/// committer additionally fences checkpoint commits with an execution lease so a superseded writer that bypassed the
/// mailbox cannot persist state.
/// </remarks>
public interface IWorkflowExecutionAgentProvider
{
    WorkflowExecutionAgentCapabilities Capabilities { get; }

    ValueTask<IWorkflowExecutionAgent> GetAgentAsync(WorkflowExecutionAgentActivationRequest request, CancellationToken cancellationToken = default);

    ValueTask PassivateAsync(WorkflowExecutionAgentPassivationRequest request, CancellationToken cancellationToken = default);
}
