using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Stores;

/// <summary>EF adapter for immutable, single-use publication snapshot-review authorities.</summary>
public sealed class EfPublicationSnapshotReviewStore(
    PublishingSnapshotReviewDbContext context,
    IPersistenceAccessContextAccessor accessContextAccessor) : IPublicationSnapshotReviewStore
{
    public async ValueTask<bool> TryAddAsync(PublicationSnapshotReview review, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(review);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureAuthorizedTenant(accessContextAccessor, review.TenantId);
        ValidateForWrite(review);

        if (await context.SnapshotReviews.AsNoTracking().AnyAsync(row => row.PreflightToken == review.PreflightToken, cancellationToken))
            return false;

        context.SnapshotReviews.Add(ToEntity(review));
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            context.ChangeTracker.Clear();
            // A unique-token conflict is the expected create-only idempotency result. Do not hide
            // unrelated database failures: only return false when the token is now present.
            if (await context.SnapshotReviews.AsNoTracking().AnyAsync(row => row.PreflightToken == review.PreflightToken, cancellationToken))
                return false;
            throw;
        }
    }

    public async ValueTask<PublicationSnapshotReview?> FindAsync(string preflightToken, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(preflightToken);
        cancellationToken.ThrowIfCancellationRequested();
        var entity = await context.SnapshotReviews.AsNoTracking()
            .SingleOrDefaultAsync(row => row.PreflightToken == preflightToken, cancellationToken);
        if (entity is null)
            return null;

        var review = ToModel(entity);
        EnsureAuthorizedTenant(accessContextAccessor, review.TenantId);
        return review;
    }

    public async ValueTask<bool> TryConsumeAsync(string preflightToken, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(preflightToken);
        cancellationToken.ThrowIfCancellationRequested();
        var entity = await context.SnapshotReviews.AsNoTracking()
            .SingleOrDefaultAsync(row => row.PreflightToken == preflightToken, cancellationToken);
        if (entity is null)
            return false;

        var review = ToModel(entity);
        EnsureAuthorizedTenant(accessContextAccessor, review.TenantId);
        // The token is the immutable row identity. ExecuteDelete is one database-side operation,
        // so concurrent contexts get exactly one affected row without a tracked stale delete.
        return await context.SnapshotReviews
            .Where(row => row.PreflightToken == preflightToken)
            .ExecuteDeleteAsync(cancellationToken) == 1;
    }

    public async ValueTask<int> DeleteExpiredAsync(DateTimeOffset expiresAtOrBefore, int maxCount, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCount);
        cancellationToken.ThrowIfCancellationRequested();

        IQueryable<PublicationSnapshotReviewEntity> query = context.SnapshotReviews.AsNoTracking()
            .Where(row => row.ExpiresAt <= expiresAtOrBefore);
        if (accessContextAccessor.Current.Scope is { } scope)
            query = query.Where(row => row.TenantId == scope.Value);

        var candidates = await query.OrderBy(row => row.ExpiresAt).ThenBy(row => row.PreflightToken)
            .Take(maxCount).Select(row => row.PreflightToken).ToListAsync(cancellationToken);
        var deleted = 0;
        foreach (var token in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entity = await context.SnapshotReviews.AsNoTracking()
                .SingleOrDefaultAsync(row => row.PreflightToken == token, cancellationToken);
            if (entity is null)
                continue;

            var review = ToModel(entity);
            EnsureAuthorizedTenant(accessContextAccessor, review.TenantId);
            // Re-check the expiry in the delete predicate. A concurrent consumer wins by removing
            // the row first; a future same-token row cannot be inserted because identity is unique.
            deleted += await context.SnapshotReviews
                .Where(row => row.PreflightToken == token && row.ExpiresAt <= expiresAtOrBefore)
                .ExecuteDeleteAsync(cancellationToken) == 1 ? 1 : 0;
        }

        return deleted;
    }

    private static PublicationSnapshotReviewEntity ToEntity(PublicationSnapshotReview review) => new()
    {
        PreflightToken = review.PreflightToken,
        CandidateHash = review.CandidateHash,
        DefinitionId = review.DefinitionId,
        Action = review.Action.ToString(),
        SlotName = review.SlotName,
        PolicySource = review.PolicySource.ToString(),
        PolicyRevision = review.PolicyRevision,
        RequestedAction = review.RequestedAction?.ToString(),
        RequestedSlotName = review.RequestedSlotName,
        RequestedExpectedPublicationId = review.RequestedExpectedPublicationId,
        SlotRevision = review.SlotRevision,
        ActivePublicationId = review.ActivePublicationId,
        TenantId = review.TenantId,
        ExpiresAt = review.ExpiresAt
    };

    private static void ValidateForWrite(PublicationSnapshotReview review)
    {
        Required(review.PreflightToken, nameof(review.PreflightToken));
        Required(review.CandidateHash, nameof(review.CandidateHash));
        Required(review.DefinitionId, nameof(review.DefinitionId));
        Required(review.SlotName, nameof(review.SlotName));
        if (!Enum.IsDefined(review.Action) || !Enum.IsDefined(review.PolicySource) ||
            (review.RequestedAction is { } requested && !Enum.IsDefined(requested)))
            throw new ArgumentException("The publication snapshot-review contains an invalid enum value.", nameof(review));
    }

    private static PublicationSnapshotReview ToModel(PublicationSnapshotReviewEntity entity) => new(
        Required(entity.PreflightToken, nameof(entity.PreflightToken)),
        Required(entity.CandidateHash, nameof(entity.CandidateHash)),
        Required(entity.DefinitionId, nameof(entity.DefinitionId)),
        Parse<PublicationAction>(entity.Action, nameof(entity.Action)),
        Required(entity.SlotName, nameof(entity.SlotName)),
        Parse<PublicationPolicySource>(entity.PolicySource, nameof(entity.PolicySource)),
        entity.PolicyRevision,
        ParseNullable<PublicationAction>(entity.RequestedAction, nameof(entity.RequestedAction)),
        entity.RequestedSlotName,
        entity.RequestedExpectedPublicationId,
        entity.SlotRevision,
        entity.ActivePublicationId,
        entity.TenantId,
        entity.ExpiresAt);

    private static string Required(string? value, string field) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"Malformed publication snapshot-review row: {field} is missing.")
            : value;

    private static T Parse<T>(string? value, string field) where T : struct, Enum =>
        Enum.TryParse<T>(value, ignoreCase: false, out var result) && Enum.IsDefined(result)
            ? result
            : throw new InvalidOperationException($"Malformed publication snapshot-review row: {field} is invalid.");

    private static T? ParseNullable<T>(string? value, string field) where T : struct, Enum =>
        value is null ? null : Parse<T>(value, field);

    private static void EnsureAuthorizedTenant(IPersistenceAccessContextAccessor accessor, string? tenantId)
    {
        var current = accessor.Current;
        if (tenantId is null)
        {
            if (!current.IsGlobal)
                throw new InvalidOperationException("The requested resource requires explicit global persistence access.");
            return;
        }

        current.EnsureScope(new PersistenceScope(tenantId));
    }
}
