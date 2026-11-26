using Elsa.Workflows.Publishing.Core.Models;

namespace Elsa.Workflows.Publishing.Core.Contracts;

public interface IActivityDraftTestRunStore
{
    ValueTask<ActivityDraftTestRunCreateResult> TryCreateAsync(
        ActivityDraftTestRunReceipt receipt,
        CancellationToken cancellationToken = default);

    ValueTask<ActivityDraftTestRunReceipt?> FindAsync(
        string testRunId,
        CancellationToken cancellationToken = default);

    ValueTask<ActivityDraftTestRunReceipt?> FindByIdempotencyKeyAsync(
        string operationScope,
        string draftId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    ValueTask<bool> TryUpdateAsync(
        ActivityDraftTestRunReceipt receipt,
        long expectedRevision,
        CancellationToken cancellationToken = default);

    ValueTask<int> DeleteExpiredAsync(
        DateTimeOffset asOf,
        int limit,
        CancellationToken cancellationToken = default);
}
