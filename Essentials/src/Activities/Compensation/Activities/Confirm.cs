using Elsa.Activities.Compensation.Internal;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Compensation.Activities;

/// <summary>
/// Confirms the work done so far: the entries of the nearest enclosing compensation scope that are not yet being
/// compensated stop being eligible, so no later recovery undoes them. They stay in the journal as confirmed evidence.
/// </summary>
/// <remarks>
/// <para>
/// With <see cref="Scope"/> set to an enclosing scope's node id, that scope's entries are confirmed as well as the
/// nearest scope's. Confirmation never crosses a reusable-activity boundary. A Confirm with no enclosing scope faults
/// with <c>runtime.ancestor-notification.target-not-found</c>; publishing refuses one that cannot reach its target.
/// </para>
/// <para>
/// Only a scope that is running forward confirms; during recovery the scope decides alone and a Confirm changes
/// nothing. See <c>specs/258-compensation-nesting</c>.
/// </para>
/// </remarks>
[ActivityPalette(AncestorActivityTypes = ["Elsa.Activities.Compensation.Activities.CompensationScope"], Description = "Confirm requires an enclosing Compensation Scope.")]
[Activity(Icon = "BadgeCheck")]
public sealed class Confirm : StructuralActivity, IRuntimeStructuralActivity
{
    /// <summary>Asks Studio for the target picker that lists the enclosing compensation scopes (spec 258 FR-320).</summary>
    public const string TargetUIHint = "compensation-confirm-target";

    /// <summary>The node id of an enclosing compensation scope to confirm too; blank confirms the nearest one only.</summary>
    [ActivityInput(Key = "scope", UIHint = TargetUIHint)]
    public string? Scope { get; set; }

    public ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext runtimeContext)
    {
        ArgumentNullException.ThrowIfNull(runtimeContext);
        var payload = CompensationMessages.Serialize(new CompensationConfirmRequest(runtimeContext.ActivityExecutionState.Execution.ActivityExecutionId));

        // Both are evaluated ahead of this node's completion. When the named scope is also the nearest, the second
        // delivery is a repeat of the same confirmation and changes nothing.
        runtimeContext.RequestAncestorNotification(CompensationScope.NearestScope(), CompensationScope.Notifications.Confirm, payload);
        if (!string.IsNullOrWhiteSpace(Scope))
            runtimeContext.RequestAncestorNotification(CompensationScope.NamedScope(Scope.Trim()), CompensationScope.Notifications.Confirm, payload);

        return ValueTask.FromResult(RuntimeStructuralContinuation.Complete());
    }
}
