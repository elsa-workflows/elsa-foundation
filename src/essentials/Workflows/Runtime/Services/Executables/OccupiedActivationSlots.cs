using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Runtime.Services.Executables;

/// <summary>
/// The shell-start sweep over every slot an interrupted activation can be in (#2193): each slot that names an activation,
/// of every definition with a live Published activation reference.
/// </summary>
/// <remarks>
/// A half-done activation always has a live Published source reference, minted before its slot transition, so these are
/// all the slots a dying process can have left half done. <see cref="CompleteInterruptedActivationsStartupTask"/> completes
/// the activations; a feature that keeps its own record of activations, such as Publishing's journal (#2223), sweeps the
/// same slots to bring that record into line. Failures are logged and never stop the shell from starting.
/// </remarks>
public static class OccupiedActivationSlots
{
    /// <summary>Calls <paramref name="visit"/> once for each occupied slot, logging rather than propagating any failure.</summary>
    public static async Task VisitAsync(
        IWorkflowExecutableSourceReferenceStore sourceReferenceStore,
        IWorkflowActivationAuthority authority,
        DateTimeOffset now,
        Func<WorkflowActivationSlot, CancellationToken, ValueTask> visit,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var definitions = new HashSet<string>(StringComparer.Ordinal);
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
                foreach (var reference in page.Items.Where(reference => reference.ActivationId is not null))
                {
                    if (definitions.Add(reference.DefinitionId))
                        await VisitSlotsAsync(authority, reference.DefinitionId, visit, logger, cancellationToken);
                }

                continuationToken = page.NextContinuationToken;
            } while (continuationToken is not null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogError(exception, "Interrupted workflow activations could not be completed at shell start");
        }
    }

    private static async Task VisitSlotsAsync(
        IWorkflowActivationAuthority authority,
        string definitionId,
        Func<WorkflowActivationSlot, CancellationToken, ValueTask> visit,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        foreach (var slot in (await authority.ListByDefinitionAsync(definitionId, cancellationToken)).Where(slot => slot.ActiveActivationId is not null))
        {
            try
            {
                await visit(slot, cancellationToken);
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
