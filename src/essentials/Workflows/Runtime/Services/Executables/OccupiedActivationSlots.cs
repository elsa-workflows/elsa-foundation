using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Executables;

/// <summary>
/// The shell-start sweep over every occupied slot: each slot that names an activation, of every definition with a live
/// Published activation reference.
/// </summary>
/// <remarks>
/// An activation always has a live Published source reference, minted before its switch, so these are all the slots an
/// activation can occupy. The runtime has nothing to complete in them, because a slot and its projections switch in one
/// commit (#2230); a feature that keeps its own record of activations outside that commit, such as Publishing's journal
/// (#2223), sweeps them to bring that record into line with a process that stopped after the switch. A failure never stops
/// the sweep or the shell from starting: the caller is told, and logs it in its own words.
/// </remarks>
public sealed class OccupiedActivationSlots(
    IWorkflowExecutableSourceReferenceStore sourceReferenceStore,
    IWorkflowActivationAuthority authority,
    TimeProvider timeProvider)
{
    /// <summary>Calls <paramref name="visit"/> once for each occupied slot, reporting rather than propagating any failure.</summary>
    /// <param name="visit">What to do with one slot.</param>
    /// <param name="sweepFailed">Told of a failure to list the slots, which ends the sweep.</param>
    /// <param name="visitFailed">Told of a slot <paramref name="visit"/> failed for; the sweep goes on with the next.</param>
    public async Task VisitAsync(
        Func<WorkflowActivationSlot, CancellationToken, ValueTask> visit,
        Action<Exception> sweepFailed,
        Action<WorkflowActivationSlot, Exception> visitFailed,
        CancellationToken cancellationToken)
    {
        try
        {
            var now = timeProvider.GetUtcNow();
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
                        await VisitSlotsAsync(reference.DefinitionId, visit, visitFailed, cancellationToken);
                }

                continuationToken = page.NextContinuationToken;
            } while (continuationToken is not null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            sweepFailed(exception);
        }
    }

    private async Task VisitSlotsAsync(
        string definitionId,
        Func<WorkflowActivationSlot, CancellationToken, ValueTask> visit,
        Action<WorkflowActivationSlot, Exception> visitFailed,
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
                visitFailed(slot, exception);
            }
        }
    }
}
