using System.Collections.Concurrent;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;

namespace Elsa.Workflows.Publishing.Core.Services;

/// <summary>A bounded single-process fallback. Durable hosts replace it with a shared provider.</summary>
public sealed class InMemoryPublicationSnapshotReviewStore(int capacity = 4096) : IPublicationSnapshotReviewStore
{
    private readonly ConcurrentDictionary<string, PublicationSnapshotReview> _reviews = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    public ValueTask<bool> TryAddAsync(PublicationSnapshotReview review, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(review);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            return ValueTask.FromResult(_reviews.Count < capacity && _reviews.TryAdd(review.PreflightToken, review));
    }

    public ValueTask<PublicationSnapshotReview?> FindAsync(string preflightToken, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_reviews.GetValueOrDefault(preflightToken));
    }

    public ValueTask<bool> TryConsumeAsync(string preflightToken, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_reviews.TryRemove(preflightToken, out _));
    }

    public ValueTask<int> DeleteExpiredAsync(
        DateTimeOffset expiresAtOrBefore,
        int maxCount,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCount);
        cancellationToken.ThrowIfCancellationRequested();
        var deleted = 0;
        foreach (var review in _reviews.Values
                     .Where(x => x.ExpiresAt <= expiresAtOrBefore)
                     .OrderBy(x => x.ExpiresAt)
                     .Take(maxCount))
        {
            if (_reviews.TryRemove(review.PreflightToken, out _))
                deleted++;
        }
        return ValueTask.FromResult(deleted);
    }
}
