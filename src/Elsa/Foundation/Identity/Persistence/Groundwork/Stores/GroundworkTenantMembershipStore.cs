using System.Text.Json;
using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.Persistence.Groundwork.Documents;
using Elsa.Persistence.Core;
using Groundwork.Documents.Store;

namespace Elsa.Foundation.Identity.Persistence.Groundwork.Stores;

/// <summary>
/// Groundwork-backed <see cref="ITenantMembershipStore"/>. Memberships are keyed and loaded directly by an
/// escaped composite <c>tenantId:userId</c> document id.
/// </summary>
public sealed class GroundworkTenantMembershipStore(
    IDocumentStore store,
    IPersistenceAccessContextAccessor accessContextAccessor,
    GroundworkIdentityAuthorityRelationshipCoordinator? relationshipCoordinator = null) : ITenantMembershipStore, IRevisionAwareTenantMembershipStore
{
    private readonly GroundworkIdentityAuthorityRelationshipCoordinator _relationships =
        relationshipCoordinator ?? new GroundworkIdentityAuthorityRelationshipCoordinator(store);

    public async ValueTask<TenantMembershipRecord?> FindAsync(string tenantId, string userId, CancellationToken cancellationToken = default)
    {
        accessContextAccessor.EnsureCurrentScope(tenantId);
        var envelope = await store.LoadAsync(
            IdentityStorageManifest.IdentityTenantMembershipDocumentKind,
            IdentityCompositeDocumentId.From(tenantId, userId),
            cancellationToken);

        return envelope is null ? null : Map(envelope);
    }

    public async ValueTask<IamRevisionedRecord<TenantMembershipRecord>?> FindWithRevisionAsync(string tenantId, string userId, CancellationToken cancellationToken = default)
    {
        accessContextAccessor.EnsureCurrentScope(tenantId);
        var envelope = await store.LoadAsync(
            IdentityStorageManifest.IdentityTenantMembershipDocumentKind,
            IdentityCompositeDocumentId.From(tenantId, userId),
            cancellationToken);

        return envelope is null ? null : new IamRevisionedRecord<TenantMembershipRecord>(Map(envelope), GroundworkIamRevisionMapper.Revision(envelope));
    }

    public async ValueTask SaveAsync(TenantMembershipRecord membership, CancellationToken cancellationToken = default)
    {
        await SaveCoreAsync(membership, expectedVersion: null, enforceExpectedVersion: false, cancellationToken);
    }

    public async ValueTask<IamRevisionSaveResult> SaveWithRevisionAsync(TenantMembershipRecord membership, string? expectedRevision, CancellationToken cancellationToken = default)
    {
        if (!GroundworkIamRevisionMapper.TryExpectedVersion(expectedRevision, out var expectedVersion))
            return GroundworkIamRevisionMapper.InvalidRevision();

        var result = await SaveCoreAsync(membership, expectedVersion, enforceExpectedVersion: true, cancellationToken);
        return GroundworkIamRevisionMapper.ToResult(result);
    }

    private async ValueTask<DocumentStoreWriteResult> SaveCoreAsync(
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

    private static TenantMembershipRecord Map(DocumentEnvelope envelope) =>
        JsonSerializer.Deserialize<IdentityTenantMembershipDocument>(envelope.ContentJson, IdentityGroundworkJson.Options)!.TenantMembership;
}
