using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Stores;

/// <summary>
/// EF adapter for create-only publication records and their lifecycle status compare-and-swap.
/// </summary>
/// <remarks>
/// A slot's exclusive activation authority is Runtime's (<c>IWorkflowActivationAuthority</c>); this store is
/// Publishing's journal of the candidates that competed for it. A record's identity never changes after it is
/// created, and a lifecycle transition succeeds only from the status the caller observed.
/// </remarks>
public sealed class EfPublicationRecordStore(
    PublishingSnapshotReviewDbContext context,
    IPersistenceAccessContextAccessor accessContextAccessor) : IPublicationRecordStore
{
    public async ValueTask SaveAsync(PublicationRecord publication, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(publication);
        cancellationToken.ThrowIfCancellationRequested();
        Validate(publication);

        var tenantId = EfPublishingStoreSupport.TenantValue(accessContextAccessor);
        var row = await FindEntityAsync(tenantId, publication.PublicationId, cancellationToken);
        if (row is not null)
        {
            if (ToModel(row) != publication)
                throw new InvalidOperationException($"Publication '{publication.PublicationId}' already exists.");
            return;
        }

        var created = ToEntity(publication, tenantId);
        context.PublicationRecords.Add(created);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            context.ChangeTracker.Clear();
            // The unique identity is the authority for a create race: an identical winner is this same
            // publication saved twice, anything else is a different publication claiming the id.
            var winner = await FindAsync(publication.PublicationId, cancellationToken);
            if (winner is null)
                throw;
            if (winner != publication)
                throw new InvalidOperationException($"Publication '{publication.PublicationId}' already exists.");
        }
        finally
        {
            EfPublishingStoreSupport.Detach(context, created);
        }
    }

    public async ValueTask<PublicationRecord?> FindAsync(string publicationId, CancellationToken cancellationToken = default)
    {
        EfPublishingStoreSupport.EnsureIdentity(publicationId, nameof(publicationId));
        cancellationToken.ThrowIfCancellationRequested();
        var row = await FindEntityAsync(EfPublishingStoreSupport.TenantValue(accessContextAccessor), publicationId, cancellationToken);
        return row is null ? null : ToModel(row);
    }

    public async ValueTask<IReadOnlyCollection<PublicationRecord>> ListBySlotAsync(string slotId, CancellationToken cancellationToken = default)
    {
        EfPublishingStoreSupport.EnsureIdentity(slotId, nameof(slotId));
        cancellationToken.ThrowIfCancellationRequested();
        var tenantId = EfPublishingStoreSupport.TenantValue(accessContextAccessor);
        var tenantHash = EfPublishingStoreSupport.TenantHash(tenantId);
        var slotHash = EfPublishingStoreSupport.Hash(slotId);
        var encodedTenantId = EfPublishingStoreSupport.EncodeNullable(tenantId);
        var encodedSlotId = EfPublishingStoreSupport.Encode(slotId);

        // Each page is bounded and keyed by the persisted unique publication hash, so the walk is exhaustive
        // over the caller's own slot without an unbounded read. The contract's order is applied afterwards.
        var records = new List<PublicationRecord>();
        string? after = null;
        for (var page = 0; ; page++)
        {
            if (page >= PublishingLedgerEfModule.MaximumSlotListPages)
                throw new InvalidOperationException("Publication record listing exceeded its page safety bound.");
            var query = context.PublicationRecords.AsNoTracking()
                .Where(row => row.TenantIdHash == tenantHash && row.SlotIdHash == slotHash);
            if (after is not null)
                query = query.Where(row => row.PublicationIdHash.CompareTo(after) > 0);
            var rows = await query
                .OrderBy(row => row.PublicationIdHash)
                .Take(PublishingLedgerEfModule.SlotListPageSize)
                .ToArrayAsync(cancellationToken);
            if (rows.Any(row => !StringComparer.Ordinal.Equals(row.TenantId, encodedTenantId) ||
                                !StringComparer.Ordinal.Equals(row.SlotId, encodedSlotId)))
                throw new InvalidOperationException("A publication-record hash candidate did not match its encoded identity residual.");
            records.AddRange(rows.Select(ToModel));
            if (rows.Length < PublishingLedgerEfModule.SlotListPageSize)
                break;
            var next = rows[^1].PublicationIdHash;
            if (after is not null && StringComparer.Ordinal.Compare(next, after) <= 0)
                throw new InvalidOperationException("Publication record listing did not advance its keyset.");
            after = next;
        }

        return records
            .OrderBy(record => record.CreatedAt)
            .ThenBy(record => record.PublicationId, StringComparer.Ordinal)
            .ToArray();
    }

    public async ValueTask<bool> TryTransitionAsync(PublicationRecord publication, PublicationStatus expectedStatus, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(publication);
        if (!Enum.IsDefined(expectedStatus))
            throw new ArgumentException("The expected publication status is invalid.", nameof(expectedStatus));
        cancellationToken.ThrowIfCancellationRequested();
        Validate(publication);

        var tenantId = EfPublishingStoreSupport.TenantValue(accessContextAccessor);
        var row = await FindEntityForUpdateAsync(tenantId, publication.PublicationId, cancellationToken);
        if (row is null)
            return false;

        try
        {
            var current = ToModel(row);
            if (current.Status != expectedStatus)
                return false;
            EnsureSameIdentity(current, publication);
            CopyMutable(row, publication);
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            // The status the caller observed was replaced concurrently; the compare-and-swap lost.
            return false;
        }
        finally
        {
            EfPublishingStoreSupport.Detach(context, row);
        }
    }

    private async Task<PublicationRecordEntity?> FindEntityAsync(string? tenantId, string publicationId, CancellationToken cancellationToken)
    {
        // Hashes are the only indexed lookup keys. The encoded residuals are checked after a bounded
        // candidate read so a hash collision can never silently alias another publication.
        var candidates = await context.PublicationRecords.AsNoTracking()
            .Where(row => row.TenantIdHash == EfPublishingStoreSupport.TenantHash(tenantId) &&
                          row.PublicationIdHash == EfPublishingStoreSupport.Hash(publicationId))
            .OrderBy(row => row.Id)
            .Take(2)
            .ToArrayAsync(cancellationToken);
        if (candidates.Length == 0)
            return null;

        if (candidates.Length != 1 ||
            !StringComparer.Ordinal.Equals(candidates[0].TenantId, EfPublishingStoreSupport.EncodeNullable(tenantId)) ||
            !StringComparer.Ordinal.Equals(candidates[0].PublicationId, EfPublishingStoreSupport.Encode(publicationId)))
            throw new InvalidOperationException("A publication-record hash candidate did not match its encoded identity residual.");
        return candidates[0];
    }

    private async Task<PublicationRecordEntity?> FindEntityForUpdateAsync(string? tenantId, string publicationId, CancellationToken cancellationToken)
    {
        // A tracking query can return an older instance already held by this DbContext. Read a fresh snapshot
        // first, then attach it with its database revision as the concurrency original value.
        var row = await FindEntityAsync(tenantId, publicationId, cancellationToken);
        if (row is null)
            return null;

        var tracked = context.ChangeTracker.Entries<PublicationRecordEntity>()
            .FirstOrDefault(entry => StringComparer.Ordinal.Equals(entry.Entity.Id, row.Id));
        tracked?.State = EntityState.Detached;
        context.PublicationRecords.Attach(row);
        return row;
    }

    private static PublicationRecordEntity ToEntity(PublicationRecord publication, string? tenantId)
    {
        var created = EfPublishingStoreSupport.DateTimeOffsetParts(publication.CreatedAt);
        var row = new PublicationRecordEntity
        {
            Id = EfPublishingStoreSupport.PhysicalId(tenantId, publication.PublicationId),
            PublicationId = EfPublishingStoreSupport.Encode(publication.PublicationId),
            PublicationIdHash = EfPublishingStoreSupport.Hash(publication.PublicationId),
            SlotId = EfPublishingStoreSupport.Encode(publication.SlotId),
            SlotIdHash = EfPublishingStoreSupport.Hash(publication.SlotId),
            SlotName = EfPublishingStoreSupport.Encode(publication.SlotName),
            WorkflowDefinitionId = EfPublishingStoreSupport.Encode(publication.WorkflowDefinitionId),
            WorkflowDefinitionVersionId = EfPublishingStoreSupport.Encode(publication.WorkflowDefinitionVersionId),
            ArtifactId = EfPublishingStoreSupport.Encode(publication.ArtifactId),
            ExpectedSlotRevision = publication.ExpectedSlotRevision,
            CreatedAtUtcTicks = created.UtcTicks,
            CreatedAtOffsetMinutes = created.OffsetMinutes,
            TenantId = EfPublishingStoreSupport.EncodeNullable(tenantId),
            TenantIdHash = EfPublishingStoreSupport.TenantHash(tenantId),
            Revision = 0
        };
        CopyMutable(row, publication);
        return row;
    }

    /// <summary>Copies the lifecycle members a transition may change and advances the row revision.</summary>
    private static void CopyMutable(PublicationRecordEntity row, PublicationRecord publication)
    {
        row.SourceReferenceId = EfPublishingStoreSupport.EncodeNullable(publication.SourceReferenceId);
        row.Status = publication.Status.ToString();
        (row.ActivatedAtUtcTicks, row.ActivatedAtOffsetMinutes) = NullableParts(publication.ActivatedAt);
        (row.RetiredAtUtcTicks, row.RetiredAtOffsetMinutes) = NullableParts(publication.RetiredAt);
        // Failure details may legitimately be empty, so they are encoded directly rather than as identities.
        row.FailureCode = publication.Failure is null ? null : EfRelationalIdentity.Encode(publication.Failure.Code);
        row.FailureMessage = publication.Failure is null ? null : EfRelationalIdentity.Encode(publication.Failure.Message);
        row.Revision = checked(row.Revision + 1);
    }

    private static PublicationRecord ToModel(PublicationRecordEntity row)
    {
        var publicationId = EfPublishingStoreSupport.DecodeIdentity(row.PublicationId, nameof(row.PublicationId));
        var tenantId = EfPublishingStoreSupport.DecodeNullableIdentity(row.TenantId, nameof(row.TenantId));
        if (!StringComparer.Ordinal.Equals(row.TenantIdHash, EfPublishingStoreSupport.TenantHash(tenantId)) ||
            !StringComparer.Ordinal.Equals(row.Id, EfPublishingStoreSupport.PhysicalId(tenantId, publicationId)))
            throw new InvalidOperationException("The persisted publication-record scope projection is corrupt.");
        EfPublishingStoreSupport.EnsureHash(publicationId, row.PublicationIdHash, nameof(row.PublicationId));
        var slotId = EfPublishingStoreSupport.DecodeIdentity(row.SlotId, nameof(row.SlotId));
        EfPublishingStoreSupport.EnsureHash(slotId, row.SlotIdHash, nameof(row.SlotId));
        if (row.Revision < 1 || row.ExpectedSlotRevision < 0)
            throw new InvalidOperationException("Malformed persisted publication record: revision or expected slot revision is invalid.");
        if (!Enum.TryParse<PublicationStatus>(row.Status, ignoreCase: false, out var status) ||
            !Enum.IsDefined(status) || !StringComparer.Ordinal.Equals(row.Status, status.ToString()))
            throw new InvalidOperationException("Malformed persisted publication record: status is invalid.");
        if ((row.FailureCode is null) != (row.FailureMessage is null))
            throw new InvalidOperationException("Malformed persisted publication record: failure details are incomplete.");

        return new PublicationRecord(
            publicationId,
            slotId,
            EfPublishingStoreSupport.DecodeIdentity(row.WorkflowDefinitionId, nameof(row.WorkflowDefinitionId)),
            EfPublishingStoreSupport.DecodeIdentity(row.WorkflowDefinitionVersionId, nameof(row.WorkflowDefinitionVersionId)),
            EfPublishingStoreSupport.DecodeIdentity(row.ArtifactId, nameof(row.ArtifactId)),
            EfPublishingStoreSupport.DecodeNullableIdentity(row.SourceReferenceId, nameof(row.SourceReferenceId)),
            row.ExpectedSlotRevision,
            status,
            EfPublishingStoreSupport.DateTimeOffset(row.CreatedAtUtcTicks, row.CreatedAtOffsetMinutes),
            NullableDateTimeOffset(row.ActivatedAtUtcTicks, row.ActivatedAtOffsetMinutes, "activation"),
            NullableDateTimeOffset(row.RetiredAtUtcTicks, row.RetiredAtOffsetMinutes, "retirement"),
            row.FailureCode is null ? null : new PublicationFailure(DecodeText(row.FailureCode, "failure code"), DecodeText(row.FailureMessage!, "failure message")),
            EfPublishingStoreSupport.DecodeIdentity(row.SlotName, nameof(row.SlotName)));
    }

    private static (long? UtcTicks, int? OffsetMinutes) NullableParts(DateTimeOffset? value)
    {
        if (value is not { } timestamp)
            return (null, null);
        var parts = EfPublishingStoreSupport.DateTimeOffsetParts(timestamp);
        return (parts.UtcTicks, parts.OffsetMinutes);
    }

    private static DateTimeOffset? NullableDateTimeOffset(long? utcTicks, int? offsetMinutes, string subject)
    {
        if ((utcTicks is null) != (offsetMinutes is null))
            throw new InvalidOperationException($"Malformed persisted publication record: the {subject} timestamp is incomplete.");
        return utcTicks is null ? null : EfPublishingStoreSupport.DateTimeOffset(utcTicks.Value, offsetMinutes!.Value);
    }

    private static string DecodeText(string encoded, string subject)
    {
        try
        {
            return EfRelationalIdentity.Decode(encoded);
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidOperationException($"Malformed persisted publication record: the {subject} is not a valid encoded value.", exception);
        }
    }

    private static void EnsureSameIdentity(PublicationRecord current, PublicationRecord next)
    {
        if (current.PublicationId != next.PublicationId || current.SlotId != next.SlotId || current.SlotName != next.SlotName ||
            current.WorkflowDefinitionId != next.WorkflowDefinitionId || current.WorkflowDefinitionVersionId != next.WorkflowDefinitionVersionId ||
            current.ArtifactId != next.ArtifactId || current.ExpectedSlotRevision != next.ExpectedSlotRevision || current.CreatedAt != next.CreatedAt)
            throw new InvalidOperationException("A publication lifecycle transition cannot change immutable publication identity.");
    }

    private static void Validate(PublicationRecord publication)
    {
        EfPublishingStoreSupport.EnsureIdentity(publication.PublicationId, nameof(publication.PublicationId));
        EfPublishingStoreSupport.EnsureIdentity(publication.SlotId, nameof(publication.SlotId));
        EfPublishingStoreSupport.EnsureIdentity(publication.SlotName, nameof(publication.SlotName));
        EfPublishingStoreSupport.EnsureIdentity(publication.WorkflowDefinitionId, nameof(publication.WorkflowDefinitionId));
        EfPublishingStoreSupport.EnsureIdentity(publication.WorkflowDefinitionVersionId, nameof(publication.WorkflowDefinitionVersionId));
        EfPublishingStoreSupport.EnsureIdentity(publication.ArtifactId, nameof(publication.ArtifactId));
        if (publication.SourceReferenceId is not null)
            EfPublishingStoreSupport.EnsureIdentity(publication.SourceReferenceId, nameof(publication.SourceReferenceId));
        ArgumentOutOfRangeException.ThrowIfNegative(publication.ExpectedSlotRevision);
        if (!Enum.IsDefined(publication.Status))
            throw new ArgumentException("The publication status is invalid.", nameof(publication));
        if (publication.Failure is { } failure)
        {
            ArgumentNullException.ThrowIfNull(failure.Code, nameof(publication));
            ArgumentNullException.ThrowIfNull(failure.Message, nameof(publication));
        }
    }
}
