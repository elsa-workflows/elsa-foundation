using System.Text.Json;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Applies private state and an optional root control emission staged by a structural continuation.
/// The returned state is still only a projection; callers own the atomic checkpoint that persists it.
/// </summary>
public static class RuntimeStructuralStateProjector
{
    public static ActivityExecutionState Apply(
        ActivityExecutionState state,
        RuntimeStructuralContinuation continuation,
        DateTimeOffset committedAt)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(continuation);

        if (continuation.StateUpdate is null && continuation.Control is null)
            return state;

        var attempt = state.Attempts?.SingleOrDefault(candidate => candidate.EndedAt is null)
            ?? throw new InvalidOperationException(
                $"Structural activity invocation '{state.InvocationId}' must have exactly one open attempt before staging private state or control.");

        if (continuation.Control is { } control)
        {
            if (state.ParentActivityExecutionId is not null || control.RootActivityExecutionId != state.InvocationId ||
                control.ChildWorkflowExecutionId != state.Execution.WorkflowExecutionId)
                throw new InvalidOperationException("Produced control must belong to this exact workflow root invocation.");
            var prior = state.ProducedControl;
            if (prior is null)
            {
                if (control.Ordinal != 0)
                    throw new InvalidOperationException("The first root control ordinal must be zero.");
            }
            else
            {
                if (prior.ConsumerKey != control.ConsumerKey || prior.ConsumerVersion != control.ConsumerVersion ||
                    prior.ChildWorkflowExecutionId != control.ChildWorkflowExecutionId || prior.RootActivityExecutionId != control.RootActivityExecutionId)
                    throw new InvalidOperationException("A root control stream cannot change its retained identity.");
                if (prior.SourceWorkItemId == control.SourceWorkItemId)
                {
                    if (!JsonElement.DeepEquals(JsonSerializer.SerializeToElement(prior), JsonSerializer.SerializeToElement(control)))
                        throw new InvalidOperationException("A repeated control source must preserve its exact batch.");
                }
                else if (prior.IsTerminal || prior.Ordinal == long.MaxValue || control.Ordinal != prior.Ordinal + 1)
                    throw new InvalidOperationException("Root control must advance exactly one ordinal before its terminal barrier.");
            }
        }

        return state with
        {
            ProducedControl = continuation.Control ?? state.ProducedControl,
            PrivateState = continuation.StateUpdate is { } update ? new ActivityPrivateState(
                state.InvocationId,
                update.StateVersion,
                update.Value,
                attempt.AttemptId,
                committedAt) : state.PrivateState
        };
    }
}
