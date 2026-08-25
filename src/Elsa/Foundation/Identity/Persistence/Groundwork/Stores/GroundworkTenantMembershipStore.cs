using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.Persistence.Groundwork.Documents;
using Elsa.Persistence.Core;

namespace Elsa.Foundation.Identity.Persistence.Groundwork.Stores;

/// <summary>
/// Groundwork-backed <see cref="ITenantMembershipStore"/>. Memberships are keyed and loaded directly by an
/// escaped composite <c>tenantId:userId</c> document id.
/// </summary>
public sealed class GroundworkTenantMembershipStore(
    GroundworkIdentityRowStore rows,
    IPersistenceAccessContextAccessor accessContextAccessor,
    GroundworkIdentityAuthorityRelationshipCoordinator? relationshipCoordinator = null) : ITenantMembershipStore, IRevisionAwareTenantMembershipStore
{
    private readonly GroundworkIdentityAuthorityRelationshipCoordinator _relationships =
        relationshipCoordinator ?? GroundworkIdentityAuthorityRelationshipCoordinator.ForRows(rows);

    public ValueTask<TenantMembershipRecord?> FindAsync(string tenantId, string userId, CancellationToken cancellationToken = default)
    {
        accessContextAccessor.EnsureCurrentScope(tenantId);
        var row = rows.Read(
            IdentityStorageManifest.IdentityTenantMembershipDocumentKind,
            IdentityCompositeDocumentId.From(tenantId, userId),
            cancellationToken);

        return ValueTask.FromResult(row is null ? null : Map(row));
    }

    public ValueTask<IamRevisionedRecord<TenantMembershipRecord>?> FindWithRevisionAsync(string tenantId, string userId, CancellationToken cancellationToken = default)
    {
        accessContextAccessor.EnsureCurrentScope(tenantId);
        var row = rows.Read(
            IdentityStorageManifest.IdentityTenantMembershipDocumentKind,
            IdentityCompositeDocumentId.From(tenantId, userId),
            cancellationToken);

        return ValueTask.FromResult(row is null ? null : new IamRevisionedRecord<TenantMembershipRecord>(Map(row), GroundworkIamRevisionMapper.Revision(row)));
    }

    public async ValueTask SaveAsync(TenantMembershipRecord membership, CancellationToken cancellationToken = default)
    {
        await SaveCoreAsync(membership, expectedVersion: null, enforceExpectedVersion: false, cancellationToken);
    }

    public async ValueTask<IamRevisionSaveResult> SaveWithRevisionAsync(TenantMembershipRecord membership, string? expectedRevision, CancellationToken cancellationToken = default)
    {
        if (!GroundworkIamRevisionMapper.TryExpectedVersion(expectedRevision, out var expectedVersion))
            return GroundworkIamRevisionMapper.InvalidRevision();

        // The relationship coordinator requires the owner user before staging. A revision-aware
        // update of an absent membership is NotFound even when that owner is also absent.
        if (expectedVersion is > 0 &&
            await FindWithRevisionAsync(membership.TenantId, membership.UserId, cancellationToken) is null)
        {
            return new IamRevisionSaveResult(IamRevisionSaveStatus.NotFound);
        }

        var result = await SaveCoreAsync(membership, expectedVersion, enforceExpectedVersion: true, cancellationToken);
        return GroundworkIamRevisionMapper.ToResult(result);
    }

    private async ValueTask<GroundworkIdentityWriteResult> SaveCoreAsync(
        TenantMembershipRecord membership,
        long? expectedVersion,
        bool enforceExpectedVersion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(membership);
        accessContextAccessor.EnsureCurrentScope(membership.TenantId);

        var document = new IdentityTenantMembershipDocument(
            IdentityCompositeDocumentId.Normalize(membership.TenantId),
            IdentityCompositeDocumentId.Normalize(membership.UserId),
            IdentityDocumentId.From(membership.TenantId, membership.UserId),
            membership.Status.ToString(),
            membership);
        return await _relationships.SaveTenantMembershipAsync(
            document,
            expectedMembershipVersion: expectedVersion,
            enforceMembershipVersion: enforceExpectedVersion,
            cancellationToken);
    }

    private static TenantMembershipRecord Map(GroundworkIdentityRow row) =>
        GroundworkIdentityDocumentRows.Deserialize<IdentityTenantMembershipDocument>(row).TenantMembership;
}
