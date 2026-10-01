using Elsa.Tasks.Core;
using Elsa.Tasks.Core.Attributes;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
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
/// reconciler never offers it, and republishing the same version stops in Publishing before it reaches the
/// coordinator, because the publication already names that artifact.
/// </para>
/// <para>
/// A half-done activation always has a live Published source reference, minted before its slot transition, so the
/// pass visits the slots of every definition that has one. Failures are logged and never stop the shell from starting.
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
/// reference a completion retired. The one remaining window, a first activation completed after another writer has
/// already replaced it, is described on the coordinator.
/// </para>
/// </remarks>
[Order(4)]
public sealed class CompleteInterruptedActivationsStartupTask(
    IWorkflowExecutableSourceReferenceStore sourceReferenceStore,
    IWorkflowActivationAuthority authority,
    IWorkflowActivationCoordinator coordinator,
    TimeProvider timeProvider,
    ILogger<CompleteInterruptedActivationsStartupTask> logger) : IStartupTask
{
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        try
        {
            var definitions = new HashSet<string>(StringComparer.Ordinal);
            var now = timeProvider.GetUtcNow();
            string? continuationToken = null;
            do
            {
                var page = await sourceReferenceStore.ListPageAsync(
                    new WorkflowExecutableSourceReferencePageQuery(
                        WorkflowExecutableReferenceScope.Published,
                        liveOnly: true,
                        now,
                        continuationToken: continuationToken),
                    cancellationToken);
                foreach (var reference in page.Items)
                {
                    if (reference.ActivationId is not null && definitions.Add(reference.DefinitionId))
                        await CompleteSlotsAsync(reference.DefinitionId, cancellationToken);
                }

                continuationToken = page.NextContinuationToken;
            } while (continuationToken is not null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogError(exception, "Interrupted workflow activations could not be completed at shell start");
        }
    }

    private async Task CompleteSlotsAsync(string definitionId, CancellationToken cancellationToken)
    {
        foreach (var slot in await authority.ListByDefinitionAsync(definitionId, cancellationToken))
        {
            if (slot.ActiveActivationId is null)
                continue;

            // The coordinator logs a completion and a failure itself, with the activation's identity.
            try
            {
                await coordinator.CompleteAsync(definitionId, slot.SlotName, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                logger.LogError(
                    exception,
                    "Activation {ActivationId} of definition {DefinitionId} slot {SlotName} could not be checked for an interrupted activation",
                    slot.ActiveActivationId,
                    definitionId,
                    slot.SlotName);
            }
        }
    }
}
