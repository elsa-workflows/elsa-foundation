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
/// too. It acquires the run only while the run's ownership record holds no lease, and otherwise runs nothing, so its
/// job stays claimed and is delivered again. It never replaces a lease. A live one belongs to a drain whose work it
/// must not throw away. An expired one is the evidence that an owner stopped mid-drain: recovery reads it to drive
/// the run again, and the migration quiescence probe reads it to refuse the run until then.
/// Acquiring only when unheld needs the runtime's own <see cref="RuntimeExecutionOwnershipService"/>. With a
/// replacement <see cref="IRuntimeExecutionOwnershipService"/> the alteration runs without a lease, as it did
/// before, because the contract's acquisition always takes the run over.
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
    /// when the run's ownership record holds a lease.
    /// </summary>
    public async ValueTask<bool> TryRunAsync(
        WorkflowExecutionCommandEnvelope envelope,
        Func<CancellationToken, ValueTask> alteration,
        CancellationToken cancellationToken)
    {
        // A flow that already holds this run's lease runs its alteration under it: acquiring again would fence it out.
        var runsUnderHeldLease = ownershipContext.Current is { } held &&
            StringComparer.Ordinal.Equals(held.WorkflowExecutionId, envelope.WorkflowExecutionId);
        if (runsUnderHeldLease || ownershipService is not RuntimeExecutionOwnershipService service)
        {
            await alteration(cancellationToken);
            return true;
        }

        var lease = await service.TryAcquireUnheldAsync(envelope.WorkflowExecutionId, cancellationToken);
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
