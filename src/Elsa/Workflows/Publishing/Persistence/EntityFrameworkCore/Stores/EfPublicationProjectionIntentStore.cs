using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Stores;

/// <summary>EF adapter for create-only publication projection intents and status CAS.</summary>
public sealed class EfPublicationProjectionIntentStore(
    PublishingSnapshotReviewDbContext context,
    IPersistenceAccessContextAccessor accessContextAccessor) : IPublicationProjectionIntentStore
{
    public async ValueTask SaveAsync(PublicationProjectionIntent intent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        cancellationToken.ThrowIfCancellationRequested();
        Validate(intent);

        var tenantId = EfPublishingStoreSupport.TenantValue(accessContextAccessor);
        var row = await FindEntityAsync(tenantId, intent.IntentId, cancellationToken);
        if (row is not null)
        {
            if (ToModel(row) != intent)
                throw new InvalidOperationException($"Publication projection intent '{intent.IntentId}' already exists.");
            return;
        }

        context.ProjectionIntents.Add(ToEntity(intent, tenantId));
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            context.ChangeTracker.Clear();
            var winner = await FindAsync(intent.IntentId, cancellationToken);
            if (winner is null)
                throw;
            if (winner != intent)
                throw new InvalidOperationException($"Publication projection intent '{intent.IntentId}' already exists.");
        }
    }

    public async ValueTask<PublicationProjectionIntent?> FindAsync(string intentId, CancellationToken cancellationToken = default)
    {
        EfPublishingStoreSupport.EnsureIdentity(intentId, nameof(intentId));
        cancellationToken.ThrowIfCancellationRequested();
        var row = await FindEntityAsync(EfPublishingStoreSupport.TenantValue(accessContextAccessor), intentId, cancellationToken);
        return row is null ? null : ToModel(row);
    }

    public async ValueTask<IReadOnlyCollection<PublicationProjectionIntent>> ListByPublicationAsync(
        string publicationId,
        CancellationToken cancellationToken = default)
    {
        EfPublishingStoreSupport.EnsureIdentity(publicationId, nameof(publicationId));
        cancellationToken.ThrowIfCancellationRequested();
        var tenantId = EfPublishingStoreSupport.TenantValue(accessContextAccessor);
        var publicationHash = EfPublishingStoreSupport.Hash(publicationId);
        var tenantHash = EfPublishingStoreSupport.TenantHash(tenantId);
        var rows = await context.ProjectionIntents.AsNoTracking()
            .Where(row => row.TenantIdHash == tenantHash && row.PublicationIdHash == publicationHash)
            .OrderBy(row => row.IntentIdOrderKey)
            .ThenBy(row => row.IntentIdHash)
            .Take(PublishingPolicyProjectionEfModule.MaximumMaterializedListEntries + 1)
            .ToArrayAsync(cancellationToken);
        if (rows.Length > PublishingPolicyProjectionEfModule.MaximumMaterializedListEntries)
            throw new InvalidOperationException($"Publication projection intents exceeded the bounded list limit of {PublishingPolicyProjectionEfModule.MaximumMaterializedListEntries}.");

        var encodedTenantId = EfPublishingStoreSupport.EncodeNullable(tenantId);
        var encodedPublicationId = EfPublishingStoreSupport.Encode(publicationId);
        if (rows.Any(row => !StringComparer.Ordinal.Equals(row.TenantId, encodedTenantId) ||
                            !StringComparer.Ordinal.Equals(row.PublicationId, encodedPublicationId)))
            throw new InvalidOperationException("A publication projection-intent hash candidate did not match its encoded identity residual.");

        return rows.Select(ToModel).ToArray();
    }

    public async ValueTask<PublicationProjectionIntentTransitionResult> TryTransitionAsync(
        PublicationProjectionIntent intent,
        PublicationProjectionIntentStatus expectedStatus,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        Validate(intent);
        if (!Enum.IsDefined(expectedStatus))
            throw new ArgumentException("Projection-intent expected status is invalid.", nameof(expectedStatus));
        cancellationToken.ThrowIfCancellationRequested();

        var tenantId = EfPublishingStoreSupport.TenantValue(accessContextAccessor);
        var row = await FindEntityForUpdateAsync(tenantId, intent.IntentId, cancellationToken);
        if (row is null)
            return new PublicationProjectionIntentTransitionResult(false, intent);

        var current = ToModel(row);
        if (current.Status != expectedStatus)
            return new PublicationProjectionIntentTransitionResult(false, current);
        EnsureImmutableIdentity(current, intent);

        CopyMutable(row, intent);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return new PublicationProjectionIntentTransitionResult(true, intent);
        }
        catch (DbUpdateConcurrencyException)
        {
            context.ChangeTracker.Clear();
            var winner = await FindAsync(intent.IntentId, cancellationToken);
            return new PublicationProjectionIntentTransitionResult(false, winner ?? intent);
        }
    }

    private async Task<PublicationProjectionIntentEntity?> FindEntityAsync(
        string? tenantId,
        string intentId,
        CancellationToken cancellationToken)
    {
        // Hashes are the only indexed lookup keys. The encoded residuals are checked after a
        // bounded candidate read so a hash collision can never silently alias another intent.
        var candidates = await context.ProjectionIntents.AsNoTracking()
            .Where(row => row.TenantIdHash == EfPublishingStoreSupport.TenantHash(tenantId) &&
                          row.IntentIdHash == EfPublishingStoreSupport.Hash(intentId))
            .Take(2)
            .ToArrayAsync(cancellationToken);
        if (candidates.Length == 0)
            return null;

        var encodedTenantId = EfPublishingStoreSupport.EncodeNullable(tenantId);
        var encodedIntentId = EfPublishingStoreSupport.Encode(intentId);
        if (candidates.Length != 1 ||
            !StringComparer.Ordinal.Equals(candidates[0].TenantId, encodedTenantId) ||
            !StringComparer.Ordinal.Equals(candidates[0].IntentId, encodedIntentId))
            throw new InvalidOperationException("A publication projection-intent hash candidate did not match its encoded identity residual.");

        return candidates[0];
    }

    private async Task<PublicationProjectionIntentEntity?> FindEntityForUpdateAsync(
        string? tenantId,
        string intentId,
        CancellationToken cancellationToken)
    {
        // A tracking query can return an older instance already held by this DbContext. Read a fresh
        // snapshot first, then attach it with its database revision as the concurrency original value.
        var row = await FindEntityAsync(tenantId, intentId, cancellationToken);
        if (row is null)
            return null;

        var tracked = context.ChangeTracker.Entries<PublicationProjectionIntentEntity>()
            .FirstOrDefault(entry => StringComparer.Ordinal.Equals(entry.Entity.Id, row.Id));
        tracked?.State = EntityState.Detached;
        context.ProjectionIntents.Attach(row);
        return row;
    }

    private static PublicationProjectionIntentEntity ToEntity(PublicationProjectionIntent intent, string? tenantId)
    {
        long? nextAttemptAtUtcTicks = null;
        int? nextAttemptAtOffsetMinutes = null;
        if (intent.NextAttemptAt is { } nextAttemptAt)
        {
            var parts = EfPublishingStoreSupport.DateTimeOffsetParts(nextAttemptAt);
            nextAttemptAtUtcTicks = parts.UtcTicks;
            nextAttemptAtOffsetMinutes = parts.OffsetMinutes;
        }
        return new PublicationProjectionIntentEntity
        {
            Id = EfPublishingStoreSupport.PhysicalId(tenantId, intent.IntentId),
            IntentId = EfPublishingStoreSupport.Encode(intent.IntentId),
            IntentIdHash = EfPublishingStoreSupport.Hash(intent.IntentId),
            IntentIdOrderKey = EfPublishingStoreSupport.OrderKey(intent.IntentId),
            PublicationId = EfPublishingStoreSupport.Encode(intent.PublicationId),
            PublicationIdHash = EfPublishingStoreSupport.Hash(intent.PublicationId),
            ProjectionKind = EfPublishingStoreSupport.Encode(intent.ProjectionKind),
            ProjectionKindHash = EfPublishingStoreSupport.Hash(intent.ProjectionKind),
            Operation = intent.Operation.ToString(),
            Status = intent.Status.ToString(),
            AttemptCount = intent.AttemptCount,
            NextAttemptAtUtcTicks = nextAttemptAtUtcTicks,
            NextAttemptAtOffsetMinutes = nextAttemptAtOffsetMinutes,
            LastFailureCode = intent.LastFailure is null ? null : EfPublishingStoreSupport.Encode(intent.LastFailure.Code),
            LastFailureMessage = intent.LastFailure is null ? null : EfPublishingStoreSupport.Encode(intent.LastFailure.Message),
            TenantId = EfPublishingStoreSupport.EncodeNullable(tenantId),
            TenantIdHash = EfPublishingStoreSupport.TenantHash(tenantId),
            Revision = 1
        };
    }

    private static void CopyMutable(PublicationProjectionIntentEntity row, PublicationProjectionIntent intent)
    {
        var next = ToEntity(intent, EfPublishingStoreSupport.DecodeNullableIdentity(row.TenantId, nameof(row.TenantId)));
        row.Status = next.Status;
        row.AttemptCount = next.AttemptCount;
        row.NextAttemptAtUtcTicks = next.NextAttemptAtUtcTicks;
        row.NextAttemptAtOffsetMinutes = next.NextAttemptAtOffsetMinutes;
        row.LastFailureCode = next.LastFailureCode;
        row.LastFailureMessage = next.LastFailureMessage;
        row.Revision = checked(row.Revision + 1);
    }

    private static PublicationProjectionIntent ToModel(PublicationProjectionIntentEntity row)
    {
        var intentId = EfPublishingStoreSupport.DecodeIdentity(row.IntentId, nameof(row.IntentId));
        var publicationId = EfPublishingStoreSupport.DecodeIdentity(row.PublicationId, nameof(row.PublicationId));
        var projectionKind = EfPublishingStoreSupport.DecodeIdentity(row.ProjectionKind, nameof(row.ProjectionKind));
        var tenantId = EfPublishingStoreSupport.DecodeNullableIdentity(row.TenantId, nameof(row.TenantId));
        if (!StringComparer.Ordinal.Equals(row.TenantIdHash, EfPublishingStoreSupport.TenantHash(tenantId)) ||
            !StringComparer.Ordinal.Equals(row.Id, EfPublishingStoreSupport.PhysicalId(tenantId, intentId)))
            throw new InvalidOperationException("The persisted publication projection-intent scope projection is corrupt.");
        EfPublishingStoreSupport.EnsureProjection(intentId, row.IntentIdHash, row.IntentIdOrderKey, nameof(row.IntentId));
        EfPublishingStoreSupport.EnsureHash(publicationId, row.PublicationIdHash, nameof(row.PublicationId));
        EfPublishingStoreSupport.EnsureHash(projectionKind, row.ProjectionKindHash, nameof(row.ProjectionKind));
        if (row.Revision < 1 || row.AttemptCount < 0)
            throw new InvalidOperationException("Malformed persisted publication projection intent: revision or attempt count is invalid.");
        if (!Enum.TryParse<PublicationProjectionOperation>(row.Operation, ignoreCase: false, out var operation) ||
            !Enum.IsDefined(operation) || !StringComparer.Ordinal.Equals(row.Operation, operation.ToString()))
            throw new InvalidOperationException("Malformed persisted publication projection intent: operation is invalid.");
        if (!Enum.TryParse<PublicationProjectionIntentStatus>(row.Status, ignoreCase: false, out var status) ||
            !Enum.IsDefined(status) || !StringComparer.Ordinal.Equals(row.Status, status.ToString()))
            throw new InvalidOperationException("Malformed persisted publication projection intent: status is invalid.");
        var nextAttempt = row.NextAttemptAtUtcTicks is null
            ? (DateTimeOffset?)null
            : EfPublishingStoreSupport.DateTimeOffset(row.NextAttemptAtUtcTicks.Value, row.NextAttemptAtOffsetMinutes ?? throw new InvalidOperationException("Malformed persisted publication projection intent: retry offset is missing."));
        if (row.NextAttemptAtUtcTicks is null && row.NextAttemptAtOffsetMinutes is not null)
            throw new InvalidOperationException("Malformed persisted publication projection intent: retry offset is unexpected.");
        if ((row.LastFailureCode is null) != (row.LastFailureMessage is null))
            throw new InvalidOperationException("Malformed persisted publication projection intent: failure details are incomplete.");
        if (row.LastFailureCode is not null)
        {
            var failureCode = EfPublishingStoreSupport.DecodeValue(row.LastFailureCode, PublishingPolicyProjectionEfModule.FailureCodeMaximumLength, nameof(row.LastFailureCode));
            var failureMessage = EfPublishingStoreSupport.DecodeValue(row.LastFailureMessage!, PublishingPolicyProjectionEfModule.FailureMessageMaximumLength, nameof(row.LastFailureMessage));
            var failure = new PublicationFailure(failureCode, failureMessage);
            return new PublicationProjectionIntent(intentId, publicationId, projectionKind, operation, status, row.AttemptCount, nextAttempt, failure);
        }
        return new PublicationProjectionIntent(intentId, publicationId, projectionKind, operation, status, row.AttemptCount, nextAttempt, null);
    }

    private static void EnsureImmutableIdentity(PublicationProjectionIntent current, PublicationProjectionIntent next)
    {
        if (current.IntentId != next.IntentId || current.PublicationId != next.PublicationId ||
            current.ProjectionKind != next.ProjectionKind || current.Operation != next.Operation)
            throw new InvalidOperationException("A projection-intent transition cannot change immutable delivery identity.");
    }

    private static void Validate(PublicationProjectionIntent intent)
    {
        EfPublishingStoreSupport.EnsureIdentity(intent.IntentId, nameof(intent.IntentId));
        EfPublishingStoreSupport.EnsureIdentity(intent.PublicationId, nameof(intent.PublicationId));
        EfPublishingStoreSupport.EnsureIdentity(intent.ProjectionKind, nameof(intent.ProjectionKind));
        if (!Enum.IsDefined(intent.Operation) || !Enum.IsDefined(intent.Status))
            throw new ArgumentException("Projection intent contains an invalid enum value.", nameof(intent));
        if (intent.AttemptCount < 0)
            throw new ArgumentOutOfRangeException(nameof(intent.AttemptCount));
        if (intent.LastFailure is { } failure)
        {
            EfPublishingStoreSupport.EnsureValue(failure.Code, PublishingPolicyProjectionEfModule.FailureCodeMaximumLength, nameof(failure.Code));
            EfPublishingStoreSupport.EnsureValue(failure.Message, PublishingPolicyProjectionEfModule.FailureMessageMaximumLength, nameof(failure.Message));
        }
    }
}
