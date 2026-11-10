using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elsa.Workflows.Runtime.Services.Alterations;

/// <summary>
/// Runs an alteration while it holds the target run's ownership lease, so the checkpoint it commits carries the
/// lease's fence and the durable store refuses it once another owner has the run (#2667).
/// </summary>
/// <remarks>
/// <para>On one node the actor mailbox serializes an alteration with drains, and the alteration finds no lease on
/// the run. On another node nothing but this lease stands between an alteration and a live drain. The alteration
/// acquires the run as a drain does, so a drain it takes the run from is refused at its next heartbeat or commit.</para>
/// <para>The lease is borrowed (<see cref="RuntimeExecutionOwnershipService.BorrowAsync"/>), because an alteration
/// is not a drain and the ownership record must not present it as one. When the run has an owner, the borrowed lease
/// stands in for that owner: the run stays listed under the owner for recovery and host reclaim, a migration is
/// refused, and the owner's lease is given back under a new fencing token when the alteration is done.</para>
/// <para>Borrowing needs the runtime's own <see cref="RuntimeExecutionOwnershipService"/>. With a replacement
/// <see cref="IRuntimeExecutionOwnershipService"/> the alteration runs without a lease.</para>
/// </remarks>
internal sealed class AlterationLeaseRunner(
    IRuntimeExecutionOwnershipService ownershipService,
    IRuntimeExecutionOwnershipContextAccessor ownershipContext,
    IPersistenceOperationScopeFactory? heartbeatScopes,
    TimeProvider timeProvider,
    ILogger<AlterationLeaseRunner>? logger = null)
{
    private static readonly object Completed = new();
    private readonly RuntimeExecutionLeaseRunner _leaseRunner = new(ownershipService, ownershipContext, heartbeatScopes, timeProvider);
    private readonly ILogger _logger = logger ?? NullLogger<AlterationLeaseRunner>.Instance;

    /// <summary>Runs <paramref name="alteration"/> under the run's lease.</summary>
    public async ValueTask RunAsync(
        WorkflowExecutionCommandEnvelope envelope,
        Func<CancellationToken, ValueTask> alteration,
        CancellationToken cancellationToken)
    {
        // A flow that already holds this run's lease runs its alteration under it: acquiring again would fence it out.
        if (ownershipContext.Current is { } held &&
            StringComparer.Ordinal.Equals(held.WorkflowExecutionId, envelope.WorkflowExecutionId))
        {
            await alteration(cancellationToken);
            return;
        }

        if (ownershipService is not RuntimeExecutionOwnershipService service)
        {
            _logger.LogWarning(
                "The alteration of workflow execution {WorkflowExecutionId} runs without the run's ownership lease, because the registered ownership service {OwnershipService} cannot lend one. Its checkpoint is not fenced.",
                envelope.WorkflowExecutionId,
                ownershipService.GetType().Name);
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
            // The alteration committed under its fence, so the job is done. The exception says where the lease was
            // lost: at a renewal, or when it was returned. A lease that was not returned is left to expire.
            _logger.LogWarning(
                exception,
                "The alteration of workflow execution {WorkflowExecutionId} committed, but its ownership lease was lost before it was returned.",
                envelope.WorkflowExecutionId);
        }
    }
}
