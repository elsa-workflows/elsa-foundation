using Elsa.Tasks.Core;
using Elsa.Tasks.Core.Attributes;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Services.Executables;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Publishing.Services;

/// <summary>
/// At shell start, brings the publication journal of every slot publishing owns into line with the slot (#2223): a
/// process that stopped after the slot transition left the slot's publication a candidate and the one it replaced active.
/// </summary>
/// <remarks>
/// <para>
/// A workflow published from the designer is published again only when someone does, so without this pass its journal
/// would stay behind until then. The pass visits the slots the runtime's own pass does (<see cref="OccupiedActivationSlots"/>)
/// and calls <see cref="IPublicationActivator.CompleteAsync"/> for each one publishing owns, which completes the
/// activation through the runtime first. Failures are logged and never stop the shell from starting.
/// </para>
/// <para>
/// <b>Ordered after</b> <see cref="CompleteInterruptedActivationsStartupTask"/> (<c>[Order(5)]</c>), so the runtime's
/// pass has normally completed the activation already and this one finds only the journal to update. Like that pass, it
/// runs on every node: every transition is a compare-and-swap, so two nodes converging one slot write it once.
/// </para>
/// </remarks>
[Order(5)]
public sealed class CompleteInterruptedPublicationsStartupTask(
    IWorkflowExecutableSourceReferenceStore sourceReferenceStore,
    IWorkflowActivationAuthority authority,
    IPublicationActivator activator,
    TimeProvider timeProvider,
    ILogger<CompleteInterruptedPublicationsStartupTask> logger) : IStartupTask
{
    public Task ExecuteAsync(CancellationToken cancellationToken) =>
        OccupiedActivationSlots.VisitAsync(
            sourceReferenceStore,
            authority,
            timeProvider.GetUtcNow(),
            async (slot, cancellation) =>
            {
                if (slot.Source is { } source && source.IsSameOwnerAs(PublicationActivator.Source))
                    await activator.CompleteAsync(slot.WorkflowDefinitionId, slot.SlotName, cancellation);
            },
            logger,
            cancellationToken);
}
