using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Entities;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Exceptions;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Stores;

/// <summary>Tenant-membership store coordinated with its user authority registry and revision.</summary>
public sealed class EfTenantMembershipStore(
    IdentityIamDbContext context,
    IPersistenceAccessContextAccessor accessContextAccessor,
    EfIdentityAuthorityRelationshipCoordinator? relationshipCoordinator = null)
    : ITenantMembershipStore, IRevisionAwareTenantMembershipStore
{
    private readonly EfIdentityAuthorityRelationshipCoordinator relationship = relationshipCoordinator ?? new(
        context,
        new EfIdentityAtomicWrite(context, accessContextAccessor: accessContextAccessor),
        accessContextAccessor);

    public async ValueTask<TenantMembershipRecord?> FindAsync(string tenantId, string userId, CancellationToken cancellationToken = default)
    {
        var row = await FindEntityAsync(tenantId, userId, cancellationToken);
        return row is null ? null : Map(row);
    }

    public ValueTask SaveAsync(TenantMembershipRecord membership, CancellationToken cancellationToken = default) =>
        new(SaveCoreAsync(membership, expectedVersion: null, createOnly: false, cancellationToken));

    public async ValueTask<IamRevisionedRecord<TenantMembershipRecord>?> FindWithRevisionAsync(string tenantId, string userId, CancellationToken cancellationToken = default)
    {
        var row = await FindEntityAsync(tenantId, userId, cancellationToken);
        return row is null ? null : new IamRevisionedRecord<TenantMembershipRecord>(Map(row), IdentityEntityFrameworkRevisionCodec.FromVersion(row.Revision));
    }

    public async ValueTask<IamRevisionSaveResult> SaveWithRevisionAsync(TenantMembershipRecord membership, string? expectedRevision, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(membership); Validate(membership.TenantId, nameof(membership.TenantId));
        long expected = 0;
        if (expectedRevision is not null && !IdentityEntityFrameworkRevisionCodec.TryGetVersion(expectedRevision, out expected)) return EfIdentityStoreSupport.InvalidRevision();
        var result = await SaveCoreAsync(membership, expectedRevision is null ? 0 : expected, expectedRevision is null, cancellationToken);
        return EfIdentityStoreSupport.ToRevisionResult(result);
    }

    private async Task<EfIdentityWriteResult> SaveCoreAsync(TenantMembershipRecord record, long? expectedVersion, bool createOnly, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record); Validate(record.TenantId, nameof(record.TenantId)); Validate(record.UserId, nameof(record.UserId)); ArgumentNullException.ThrowIfNull(record.RoleIds); ArgumentNullException.ThrowIfNull(record.DirectPermissions); Prepare(record.TenantId, cancellationToken);
        var row = new TenantMembershipEntity { TenantId = record.TenantId, UserId = record.UserId, Status = (int)record.Status, RoleIdsJson = EfIdentityStoreSupport.SerializeSet(record.RoleIds), DirectPermissionsJson = EfIdentityStoreSupport.SerializeSet(record.DirectPermissions) };
        var result = await relationship.SaveTenantMembershipAsync(row, createOnly || expectedVersion is not null ? expectedVersion : null, enforceMembershipVersion: createOnly || expectedVersion is not null, cancellationToken);
        if (!result.Succeeded && !createOnly && expectedVersion is null) throw new IdentityEntityFrameworkPersistenceException("Unable to save the tenant Identity membership.", new InvalidOperationException(result.Message));
        return result;
    }

    private async Task<TenantMembershipEntity?> FindEntityAsync(string tenantId, string userId, CancellationToken cancellationToken)
    {
        Prepare(tenantId, cancellationToken); Validate(userId, nameof(userId));
        return await EfIdentityStoreSupport.ReadAsync(
            context,
            "Unable to read the Identity tenant membership.",
            () => context.TenantMemberships.AsNoTracking().SingleOrDefaultAsync(x => x.Id == EfIdentityStoreSupport.RecordId(tenantId, userId), cancellationToken));
    }

    private static TenantMembershipRecord Map(TenantMembershipEntity row) => new(row.TenantId, row.UserId, (TenantMembershipStatus)row.Status, EfIdentityStoreSupport.DeserializeSet(row.RoleIdsJson), EfIdentityStoreSupport.DeserializeSet(row.DirectPermissionsJson));
    private void Prepare(string tenantId, CancellationToken cancellationToken) { Validate(tenantId, nameof(tenantId)); EfIdentityStoreSupport.EnsureTenant(accessContextAccessor, tenantId); context.EnsureProviderBinding(); cancellationToken.ThrowIfCancellationRequested(); }
    private static void Validate(string value, string parameter) { ArgumentNullException.ThrowIfNull(value); if (value.Length > IdentityProviderConfigurationCanonicalizer.MaximumIdentityLength) throw new ArgumentException($"Identity key values cannot exceed {IdentityProviderConfigurationCanonicalizer.MaximumIdentityLength} UTF-16 code units.", parameter); }
}
