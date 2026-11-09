using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executions;

namespace Elsa.Workflows.Runtime.Services.Alterations;

/// <summary>
/// Holds the target run's ownership lease while an alteration reads the run and commits its checkpoint, so the
/// checkpoint carries the lease's fence and the durable store refuses it once another owner has the run (#2667).
/// </summary>
/// <remarks>
/// On one node the actor mailbox already makes an alteration wait behind a drain. Across nodes only placement kept
/// them apart: an alteration that reached another node wrote the run's rows beside a live drain. Here it waits there
/// too. It acquires the run only while no live lease holds it, and otherwise does nothing, leaving its job claimed for
/// the pump to deliver again. It never takes a run from a live owner, so a migration is never checked for quiescence
/// against a lease it displaced itself. A lease that has expired is a dead owner's and is taken, as a drain takes it.
/// A replacement <see cref="IRuntimeExecutionOwnershipService"/> has no acquire-if-unheld operation; there the
/// alteration acquires as a drain does.
/// </remarks>
internal sealed class AlterationRunLease(
    IRuntimeExecutionOwnershipService ownershipService,
    IRuntimeExecutionOwnershipContextAccessor ownershipContext,
    IPersistenceOperationScopeFactory heartbeatScopes,
    TimeProvider timeProvider)
{
    private static readonly object Completed = new();
    private readonly RuntimeExecutionLeaseRunner _leaseRunner = new(ownershipService, ownershipContext, heartbeatScopes, timeProvider);

    /// <summary>
    /// Runs <paramref name="alteration"/> under the run's lease. Returns <see langword="false"/>, having run nothing,
    /// when a live owner holds the run.
    /// </summary>
    public async ValueTask<bool> TryRunAsync(
        WorkflowExecutionCommandEnvelope envelope,
        Func<CancellationToken, ValueTask> alteration,
        CancellationToken cancellationToken)
    {
        // A flow that already holds this run's lease runs its alteration under it: acquiring again would fence it out.
        if (ownershipContext.Current is { } held &&
            StringComparer.Ordinal.Equals(held.WorkflowExecutionId, envelope.WorkflowExecutionId))
        {
            await alteration(cancellationToken);
            return true;
        }

        var lease = ownershipService is RuntimeExecutionOwnershipService service
            ? await service.TryAcquireUnheldAsync(envelope.WorkflowExecutionId, cancellationToken)
            : await ownershipService.AcquireAsync(envelope.WorkflowExecutionId, cancellationToken);
        if (lease is null)
            return false;

        var committed = false;
        try
        {
            await _leaseRunner.RunAsync(
                lease,
                envelope.Partition,
                async leasedCancellation =>
                {
                    await alteration(leasedCancellation);
                    committed = true;
                    return Completed;
                },
                cancellationToken);
        }
        catch (RuntimeExecutionOwnershipLostException) when (committed)
        {
            // The alteration committed under its fence. A run lost after that leaves nothing to undo and no lease of
            // ours to release.
        }

        return true;
    }
}
