using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Executions;

/// <summary>
/// One borrow of a run's ownership lease, for a short write that is not a drain.
/// <see cref="RuntimeExecutionOwnershipService.ReturnAsync"/> ends it.
/// </summary>
/// <param name="Lease">The lease the borrower holds and commits under. It is in the borrower's own name.</param>
/// <param name="OwnersLeaseExpiresAt">
/// When the run had an owner (a drain that is running, or that stopped without releasing): when that owner's lease
/// expires. Null when the run had no owner.
/// </param>
internal sealed record ExecutionLeaseBorrow(RuntimeExecutionLease Lease, DateTimeOffset? OwnersLeaseExpiresAt);
