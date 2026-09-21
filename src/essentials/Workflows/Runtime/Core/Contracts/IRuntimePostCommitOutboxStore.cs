using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

public interface IRuntimePostCommitOutboxStore
{
    ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxItem>> GetDeliverableAsync(RuntimePostCommitOutboxQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a claim-less delivery result and reports which outcome was persisted.
    /// </summary>
    /// <returns>
    /// <see cref="RuntimePostCommitOutboxClaimCompletionOutcome.Persisted"/> when the result was written as presented, or
    /// <see cref="RuntimePostCommitOutboxClaimCompletionOutcome.SupersededByOtherOwner"/> when another deliverer owns the
    /// item — in which case NOTHING is written and the owning deliverer's completion governs.
    /// </returns>
    /// <remarks>
    /// Implementations MUST:
    /// <list type="bullet">
    /// <item>return <see cref="RuntimePostCommitOutboxClaimCompletionOutcome.SupersededByOtherOwner"/> when the item is
    /// <see cref="RuntimePostCommitOutboxStatus.Delivering"/> under another owner, or carries a fencing token the caller
    /// does not hold;</item>
    /// <item>write nothing at all in that case — no status, owner, fencing token, attempt count, failure message or
    /// availability change — and leave the item recoverable by claim expiry and the resumption sweep;</item>
    /// <item>continue to throw when the item is already terminal (that is double completion, not contention) or when it
    /// does not exist;</item>
    /// <item>propagate the inner outcome unchanged when decorating another store.</item>
    /// </list>
    /// </remarks>
    ValueTask<RuntimePostCommitOutboxClaimCompletionOutcome> RecordDeliveryResultAsync(RuntimePostCommitOutboxDeliveryResult result, CancellationToken cancellationToken = default);
}
