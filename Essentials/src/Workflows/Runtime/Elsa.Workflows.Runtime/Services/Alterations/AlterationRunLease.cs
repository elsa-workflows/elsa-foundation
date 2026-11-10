using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elsa.Workflows.Runtime.Services.Alterations;

/// <summary>
/// Holds the target run's ownership lease while an alteration reads the run and commits its checkpoint, so the
/// checkpoint carries the lease's fence and the durable store refuses it once another owner has the run (#2667).
/// </summary>
/// <remarks>
/// <para>On one node the actor mailbox serializes an alteration with drains, and the alteration finds no lease on
/// the run. On another node nothing but this lease stands between an alteration and a live drain. The alteration
/// acquires the run as a drain does, so a drain it takes the run from is refused at its next heartbeat or commit.</para>
/// <para>The lease is borrowed (<see cref="RuntimeExecutionOwnershipService.BorrowAsync"/>): an alteration is not a
/// drain, and the ownership record must not say otherwise once the alteration is done. When the run had an owner,
/// that owner's lease is given back under a new fencing token, so recovery and host reclaim find a run whose owner
/// stopped exactly as they would have, and a migration is refused while the borrowed lease stands in for it.</para>
/// <para>Borrowing needs the runtime's own <see cref="RuntimeExecutionOwnershipService"/>. With a replacement
/// <see cref="IRuntimeExecutionOwnershipService"/> the alteration runs without a lease.</para>
/// </remarks>
internal sealed class AlterationRunLease(
    IRuntimeExecutionOwnershipService ownershipService,
    IRuntimeExecutionOwnershipContextAccessor ownershipContext,
    IPersistenceOperationScopeFactory? heartbeatScopes,
    TimeProvider timeProvider,
    ILogger<AlterationRunLease>? logger = null)
{
    private static readonly object Completed = new();
    private readonly RuntimeExecutionLeaseRunner _leaseRunner = new(ownershipService, ownershipContext, heartbeatScopes, timeProvider);
    private readonly ILogger _logger = logger ?? NullLogger<AlterationRunLease>.Instance;

    /// <summary>Runs <paramref name="alteration"/> under the run's lease.</summary>
    public async ValueTask RunAsync(
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
            return;
        }

        var borrowed = await service.BorrowAsync(envelope.WorkflowExecutionId, cancellationToken);
        var committed = false;
        try
        {
            await _leaseRunner.RunAsync(
                borrowed.Lease,
                () => service.ReturnAsync(borrowed, CancellationToken.None),
                envelope.Partition,
                async leasedCancellation =>
                {
                    await alteration(leasedCancellation);
                    committed = true;
                    return Completed;
                },
                cancellationToken);
        }
        catch (RuntimeExecutionOwnershipLostException exception) when (committed)
        {
            // The alteration committed under its fence, so the job is done. Either the run has a new owner, or the
            // lease could not be returned and is left to expire.
            _logger.LogWarning(
                exception,
                "The alteration of workflow execution {WorkflowExecutionId} committed, but its run lease was not returned.",
                envelope.WorkflowExecutionId);
        }
    }
}
