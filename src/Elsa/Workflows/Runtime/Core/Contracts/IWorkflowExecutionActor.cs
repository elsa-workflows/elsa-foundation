using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Active execution mailbox for one workflow execution id.
/// </summary>
/// <remarks>
/// The mailbox is the single-writer serialization point (RT-2): it admits one command at a time for its workflow
/// execution, so all dispatch MUST route through it. This is what makes at-most-one-drainer-per-execution hold for the
/// drainer, scheduler queue, and checkpoint committer downstream.
/// </remarks>
public interface IWorkflowExecutionActor
{
    WorkflowExecutionActorDescriptor Descriptor { get; }

    ValueTask<WorkflowExecutionCommandDispatchResult> EnqueueAsync(WorkflowExecutionCommandEnvelope envelope, CancellationToken cancellationToken = default);

    ValueTask<WorkflowExecutionCommandDispatchResult> EnqueueAsync(
        WorkflowExecutionCommandEnvelope envelope,
        WorkflowExecutionCommandDispatchOptions options,
        CancellationToken cancellationToken = default) =>
        EnqueueAsync(envelope, cancellationToken);
}
