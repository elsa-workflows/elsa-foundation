using Elsa.Activities.Compensation.Internal;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Compensation.Activities;

/// <summary>
/// Starts recovery explicitly: the nearest enclosing compensation scope stops its running step, which holds this
/// activity, and undoes the completed work newest first, as after a fault. The scope completes <c>Compensated</c>.
/// </summary>
/// <remarks>
/// <para>
/// The stopped step is journaled as uncertain, because it may have had an effect: it is undone when it has a handler.
/// With <see cref="Scope"/> set to an outer scope's node id, every scope in between compensates itself first, then
/// faults with <c>compensation.scope.compensate-requested</c>, until the named scope recovers.
/// </para>
/// <para>
/// A Compensate during recovery, or inside a handler, changes nothing: there is no recovery of recovery. Publishing
/// refuses one inside a handler or one that cannot reach its target. See <c>specs/258-compensation-nesting</c>.
/// </para>
/// </remarks>
[Activity(Icon = "Undo")]
public sealed class Compensate : StructuralActivity, IRuntimeStructuralActivity
{
    /// <summary>The node id of the enclosing compensation scope that should recover; blank means the nearest one.</summary>
    [ActivityInput(Key = "scope")]
    public string? Scope { get; set; }

    /// <summary>Why recovery was asked for, kept with the request.</summary>
    [ActivityInput(Key = "reason")]
    public string? Reason { get; set; }

    public ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext runtimeContext)
    {
        ArgumentNullException.ThrowIfNull(runtimeContext);

        // The nearest scope always hears first, so each scope in between undoes its own work before passing it on.
        runtimeContext.RequestAncestorNotification(
            CompensationScope.NearestScope(),
            CompensationScope.Notifications.Compensate,
            CompensationMessages.Serialize(new CompensationCompensateRequest(
                runtimeContext.ActivityExecutionState.Execution.ActivityExecutionId,
                string.IsNullOrWhiteSpace(Reason) ? null : Reason,
                string.IsNullOrWhiteSpace(Scope) ? null : Scope)));
        return ValueTask.FromResult(RuntimeStructuralContinuation.Complete());
    }
}
