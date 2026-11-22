using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;

namespace Elsa.Workflows.Publishing.Api.Services;

public sealed class InMemoryActivityDraftTestRunStore : IActivityDraftTestRunStore
{
    private readonly Dictionary<string, ActivityDraftTestRunReceipt> _receipts = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    public ValueTask<ActivityDraftTestRunCreateResult> TryCreateAsync(
        ActivityDraftTestRunReceipt receipt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        cancellationToken.ThrowIfCancellationRequested();
        ActivityDraftTestRunIdentity.EnsureOperationScope(receipt);
        lock (_gate)
        {
            if (_receipts.TryGetValue(receipt.TestRunId, out var existing))
                return ValueTask.FromResult(new ActivityDraftTestRunCreateResult(false, existing));

            _receipts.Add(receipt.TestRunId, receipt);
            return ValueTask.FromResult(new ActivityDraftTestRunCreateResult(true, receipt));
        }
    }

    public ValueTask<ActivityDraftTestRunReceipt?> FindAsync(
        string testRunId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(testRunId);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _receipts.TryGetValue(testRunId, out var receipt);
            return ValueTask.FromResult(receipt);
        }
    }

    public ValueTask<ActivityDraftTestRunReceipt?> FindByIdempotencyKeyAsync(
        string operationScope,
        string draftId,
        string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        FindAsync(ActivityDraftTestRunIdentity.CreateTestRunId(operationScope, draftId, idempotencyKey), cancellationToken);

    public ValueTask<bool> TryUpdateAsync(
        ActivityDraftTestRunReceipt receipt,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        cancellationToken.ThrowIfCancellationRequested();
        ActivityDraftTestRunIdentity.EnsureOperationScope(receipt);
        if (receipt.Revision != expectedRevision + 1)
            throw new ArgumentException("The next receipt revision must advance the expected revision exactly once.", nameof(receipt));

        lock (_gate)
        {
            if (!_receipts.TryGetValue(receipt.TestRunId, out var current) || current.Revision != expectedRevision)
                return ValueTask.FromResult(false);

            _receipts[receipt.TestRunId] = receipt;
            return ValueTask.FromResult(true);
        }
    }

    public ValueTask<int> DeleteExpiredAsync(
        DateTimeOffset asOf,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
            throw new ArgumentOutOfRangeException(nameof(limit));
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var ids = _receipts.Values
                .Where(x => x.ReceiptExpiresAt <= asOf)
                .OrderBy(x => x.ReceiptExpiresAt)
                .ThenBy(x => x.TestRunId, StringComparer.Ordinal)
                .Take(limit)
                .Select(x => x.TestRunId)
                .ToArray();
            foreach (var id in ids)
                _receipts.Remove(id);
            return ValueTask.FromResult(ids.Length);
        }
    }
}
