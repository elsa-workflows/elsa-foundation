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
/// <para>On one node the actor mailbox already serializes an alteration with drains, and it finds no lease on the
/// run. Across nodes only placement kept them apart: an alteration that reached another node wrote the run's rows
/// beside a live drain, with nothing to stop either from overwriting the other. The alteration now acquires the run
/// as a drain does, so whichever owner it replaced is refused at its next commit.</para>
/// <para>A lease it replaced carried information, and the alteration keeps it. It tells the job that it replaced
/// one, so a migration still refuses a run another owner was on. And it leaves its own lease to expire instead of
/// releasing it, because an expired lease is how recovery finds a run whose owner stopped mid-drain: releasing would
/// erase the trace of the owner it replaced.</para>
/// <para>Knowing whether a lease was replaced needs the runtime's own <see cref="RuntimeExecutionOwnershipService"/>.
/// With a replacement <see cref="IRuntimeExecutionOwnershipService"/> the alteration runs without a lease, as it did
/// before.</para>
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
    /// Runs <paramref name="alteration"/> under the run's lease, telling it whether acquiring the lease replaced one.
    /// </summary>
    public async ValueTask RunAsync(
        WorkflowExecutionCommandEnvelope envelope,
        Func<bool, CancellationToken, ValueTask> alteration,
        CancellationToken cancellationToken)
    {
        // A flow that already holds this run's lease runs its alteration under it: acquiring again would fence it out.
        var runsUnderHeldLease = ownershipContext.Current is { } held &&
            StringComparer.Ordinal.Equals(held.WorkflowExecutionId, envelope.WorkflowExecutionId);
        if (runsUnderHeldLease || ownershipService is not RuntimeExecutionOwnershipService service)
        {
            await alteration(false, cancellationToken);
            return;
        }

        var (lease, replacedALease) = await service.AcquireNotingReplacedLeaseAsync(envelope.WorkflowExecutionId, cancellationToken);
        var committed = false;
        try
        {
            await _leaseRunner.RunAsync(
                lease,
                releaseWhenDone: !replacedALease,
                envelope.Partition,
                async leasedCancellation =>
                {
                    await alteration(replacedALease, leasedCancellation);
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
    }
}

/// <summary>
/// In a job's projected state when the alteration's lease replaced one already on the run: another owner was on the
/// run, or stopped on it without releasing. A handler that needs the run quiescent reads it.
/// </summary>
internal sealed class ReplacedRunLease
{
    public static readonly ReplacedRunLease Instance = new();

    private ReplacedRunLease()
    {
    }
}
