using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Executions;

/// <summary>
/// A run's ownership lease held for one short write that is not a drain, with what the ownership record said about
/// the run's owner when the lease was taken. <see cref="RuntimeExecutionOwnershipService.ReturnAsync"/> ends it.
/// </summary>
/// <param name="Lease">The lease the borrower holds and commits under.</param>
/// <param name="OwnersLease">
/// The lease of the run's owner, live or expired: a drain that is running, or that stopped without releasing. Null
/// when the run had no owner.
/// </param>
/// <param name="OwnersHeartbeat">The heartbeat recorded with <paramref name="OwnersLease"/>.</param>
internal sealed record BorrowedExecutionLease(
    RuntimeExecutionLease Lease,
    RuntimeExecutionLease? OwnersLease,
    RuntimeHeartbeat? OwnersHeartbeat);
