using Elsa.Workflows.Publishing.Core.Models;

namespace Elsa.Workflows.Publishing.Core.Contracts;

/// <summary>Shared, atomic storage for short-lived publication review authorities.</summary>
public interface IPublicationSnapshotReviewStore
{
    ValueTask<bool> TryAddAsync(PublicationSnapshotReview review, CancellationToken cancellationToken = default);
    ValueTask<PublicationSnapshotReview?> FindAsync(string preflightToken, CancellationToken cancellationToken = default);
    ValueTask<bool> TryConsumeAsync(string preflightToken, CancellationToken cancellationToken = default);
    ValueTask<int> DeleteExpiredAsync(DateTimeOffset expiresAtOrBefore, int maxCount, CancellationToken cancellationToken = default);
}
