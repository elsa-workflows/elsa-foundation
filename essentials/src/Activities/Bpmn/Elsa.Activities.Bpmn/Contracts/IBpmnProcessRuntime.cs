using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Bpmn.Contracts;

/// <summary>Foundation's hosting boundary for a BPMN process invocation and its child deliveries.</summary>
/// <remarks>
/// Single-implementation replacement contract: a host may supply one adapter for its runtime context.
/// Composition rejects multiple registrations at shell startup; delivery is never a fan-out operation.
/// </remarks>
/// <exception cref="Exceptions.BpmnHostException">The pinned graph, checkpoint or delivery cannot be safely bound to the host.</exception>
/// <exception cref="Elsa.Workflows.Runtime.Core.Exceptions.RuntimeSchedulerWorkUnavailableException">An admitted control dependency is temporarily unavailable; the scheduler retains its source for retry.</exception>
public interface IBpmnProcessRuntime
{
    ValueTask<RuntimeStructuralContinuation> StartAsync(IRuntimeActivityExecutionContext context);
    ValueTask<RuntimeStructuralContinuation> CompleteAsync(IRuntimeActivityExecutionContext context, ActivityChildCompletedContext completion);
    ValueTask<RuntimeStructuralContinuation> FaultAsync(IRuntimeActivityExecutionContext context, ActivityChildFaultedContext fault);
    ValueTask<RuntimeStructuralContinuation> SignalAsync(IRuntimeActivityExecutionContext context, ActivityChildNotifiedContext signal);
    ValueTask<RuntimeStructuralContinuation> ControlAsync(IRuntimeActivityExecutionContext context, RuntimeChildControlContext control) =>
        ValueTask.FromException<RuntimeStructuralContinuation>(new Exceptions.BpmnHostException("This BPMN adapter does not implement declared called-workflow control."));
    ValueTask<RuntimeStructuralContinuation> OutcomeAsync(IRuntimeActivityExecutionContext context, ActivityChildOutcomeContext outcome);
}
