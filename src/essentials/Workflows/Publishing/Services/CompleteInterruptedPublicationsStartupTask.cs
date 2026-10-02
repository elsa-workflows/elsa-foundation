using Elsa.Tasks.Core;
using Elsa.Tasks.Core.Attributes;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Runtime.Services.Executables;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Publishing.Services;

/// <summary>
/// At shell start, brings the publication journal of every slot publishing owns into line with the slot (#2223): a
/// process that stopped after the runtime's switch left the slot's publication serving but a candidate, and the one it
/// replaced active.
/// </summary>
/// <remarks>
/// <para>
/// A workflow published from the designer is published again only when someone does, so without this pass its journal
/// would stay behind until then. The pass visits every occupied slot (<see cref="OccupiedActivationSlots"/>) and calls
/// <see cref="IPublicationActivator.CompleteAsync"/> for each one publishing owns, which checks with the runtime that the
/// slot's activation serves first. The runtime has nothing to complete itself, because the slot and its projections
/// switch in one commit (#2230). Failures are logged and never stop the shell from starting.
/// </para>
/// <para>
/// <b>Ordered after the startup reconcilers</b> (<c>[Order(5)]</c>): the design-side reconcilers and export (orders 1 to
/// 3) publish through the same journal, so this pass finds only what nothing else brought into line. It runs on every
/// node: every transition is a compare-and-swap, so two nodes converging one slot write it once.
/// </para>
/// </remarks>
[Order(5)]
public sealed class CompleteInterruptedPublicationsStartupTask(
    OccupiedActivationSlots occupiedSlots,
    IPublicationActivator activator,
    ILogger<CompleteInterruptedPublicationsStartupTask> logger) : IStartupTask
{
    public Task ExecuteAsync(CancellationToken cancellationToken) =>
        occupiedSlots.VisitAsync(
            async (slot, cancellation) =>
            {
                if (slot.Source is { } source && source.IsSameOwnerAs(PublicationActivator.Source))
                    await activator.CompleteAsync(slot.WorkflowDefinitionId, slot.SlotName, cancellation);
            },
            exception => logger.LogError(exception, "The publication journal could not be brought into line with the slots at shell start"),
            (slot, exception) => logger.LogError(
                exception,
                "The publication journal of definition {DefinitionId} slot {SlotName} could not be brought into line with publication {PublicationId}",
                slot.WorkflowDefinitionId,
                slot.SlotName,
                slot.ActiveActivationId),
            cancellationToken);
}
