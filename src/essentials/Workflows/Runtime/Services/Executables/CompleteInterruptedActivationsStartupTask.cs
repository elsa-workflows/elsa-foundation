using Elsa.Tasks.Core;
using Elsa.Tasks.Core.Attributes;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Runtime.Services.Executables;

/// <summary>
/// At shell start, completes every activation that an interrupted call left half done (#2193): the slot names it, but
/// the process died before its trigger bindings and recurring schedules were switched on, or before the reference of
/// the activation it replaced was retired.
/// </summary>
/// <remarks>
/// <para>
/// The coordinator also completes a slot's activation whenever that slot is activated or deactivated again. This pass
/// covers the slots nothing touches again on its own. A workflow published from the designer is one: the artifact
/// reconciler never offers it, and nothing publishes it again unless someone does.
/// </para>
/// <para>
/// The pass visits the slots <see cref="OccupiedActivationSlots"/> names: those of every definition with a live
/// Published activation reference, which a half-done activation always has. Failures are logged and never stop the
/// shell from starting.
/// </para>
/// <para>
/// <b>Ordered after the startup reconcilers</b> (<c>[Order(4)]</c>): the artifact reconciler
/// (<c>WorkflowArtifactReconcilerStartupTask</c>, unordered) and the design-side reconcilers and export (orders 1 to 3).
/// Their activations complete their own slots first, so this pass finds only what nothing else touched. A task
/// dependency cannot express the order, because the artifact reconciler lives in an assembly that references this one.
/// </para>
/// <para>
/// <b>Every node runs it; it is deliberately not <c>[SingleNodeTask]</c>.</b> That attribute takes a node-local lock
/// and skips when the lock is taken, so a node could skip work no other node does. Completion is idempotent and its
/// races are benign: two completions, or a completion and the activation's own sequence, make the same switch, which
/// the projection stores accept as a no-op whichever comes second, and a sequence that then fails restores the
/// reference a completion retired. The two windows that remain until the slot and the projections switch in one
/// transaction (#2230) are described on the coordinator.
/// </para>
/// </remarks>
[Order(4)]
public sealed class CompleteInterruptedActivationsStartupTask(
    OccupiedActivationSlots occupiedSlots,
    IWorkflowActivationCoordinator coordinator,
    ILogger<CompleteInterruptedActivationsStartupTask> logger) : IStartupTask
{
    // The coordinator logs a completion and a failure itself, with the activation's identity.
    public Task ExecuteAsync(CancellationToken cancellationToken) =>
        occupiedSlots.VisitAsync(
            async (slot, cancellation) => await coordinator.CompleteAsync(slot.WorkflowDefinitionId, slot.SlotName, cancellation),
            exception => logger.LogError(exception, "Interrupted workflow activations could not be completed at shell start"),
            (slot, exception) => logger.LogError(
                exception,
                "Activation {ActivationId} of definition {DefinitionId} slot {SlotName} could not be checked for an interrupted activation",
                slot.ActiveActivationId,
                slot.WorkflowDefinitionId,
                slot.SlotName),
            cancellationToken);
}
