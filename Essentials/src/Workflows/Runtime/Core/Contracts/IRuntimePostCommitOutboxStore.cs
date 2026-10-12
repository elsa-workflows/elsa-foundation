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
    /// item or this caller lacks its retained positive fence — in which case NOTHING is written and the fenced owner's
    /// completion governs, even if that completion already made the item terminal.
    /// </returns>
    /// <remarks>
    /// Implementations MUST:
    /// <list type="bullet">
    /// <item>return <see cref="RuntimePostCommitOutboxClaimCompletionOutcome.SupersededByOtherOwner"/> when the item is
    /// <see cref="RuntimePostCommitOutboxStatus.Delivering"/> under another owner, or carries a positive fencing token the
    /// claim-less caller does not hold, including after that fenced owner has completed the item;</item>
    /// <item>write nothing at all in that case — no status, owner, fencing token, attempt count, failure message or
    /// availability change. A superseded nonterminal item remains recoverable by claim expiry and the resumption sweep; an
    /// item already completed by its fenced owner remains terminal;</item>
    /// <item>continue to throw for an unfenced terminal item (a double completion, not contention) or when the item does
    /// not exist. A positive foreign fence takes precedence over terminal state, as specified above;</item>
    /// <item>propagate the inner outcome unchanged when decorating another store.</item>
    /// </list>
    /// </remarks>
    ValueTask<RuntimePostCommitOutboxClaimCompletionOutcome> RecordDeliveryResultAsync(RuntimePostCommitOutboxDeliveryResult result, CancellationToken cancellationToken = default);
}
