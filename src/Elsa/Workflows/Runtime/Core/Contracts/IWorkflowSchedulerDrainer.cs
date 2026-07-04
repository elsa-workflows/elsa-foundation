using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Drains queued scheduler work for one workflow execution.
/// </summary>
/// <remarks>
/// Single-writer ownership contract (RT-2): at most one drain may run per workflow execution at a time. All dispatch
/// MUST route through the agent mailbox (<see cref="IWorkflowExecutionActor"/>), which serializes command entry per
/// execution and is the sole guarantor of this invariant. The drainer's peek → pause-gate → dequeue sequence and its
/// "read terminal status once, re-check after dispatch" optimization are only correct under that guarantee; the
/// drainer additionally trips a fail-fast assertion if a dequeue does not return the peeked head, and checkpoint
/// commits are fenced by an execution lease so a superseded writer cannot persist state.
/// </remarks>
public interface IWorkflowSchedulerDrainer
{
    ValueTask<RuntimeSchedulerDrainResult> DrainAsync(RuntimeSchedulerDrainRequest request, CancellationToken cancellationToken = default);
}
