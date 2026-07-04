using System.Text.Json;
using Elsa.Foundation.Identity.Abstractions.Iam;
using Groundwork.Documents.Store;

namespace Elsa.Foundation.Identity.Persistence.Groundwork.Stores;

/// <summary>
/// Groundwork-backed <see cref="ITenantMembershipStore"/>. Memberships are keyed by an escaped composite
/// <c>tenantId:userId</c> document id and carry a <c>tenantKey</c> index so a tenant's memberships remain
/// enumerable through the declared index alongside the primary find-by-user lookup.
/// </summary>
public sealed class GroundworkTenantMembershipStore(IDocumentStore store) : ITenantMembershipStore
{
    public async ValueTask<TenantMembershipRecord?> FindAsync(string tenantId, string userId, CancellationToken cancellationToken = default)
    {
        var envelope = await store.LoadAsync(
            IdentityStorageManifest.TenantMembershipDocumentKind,
            IdentityCompositeDocumentId.From(tenantId, userId),
            cancellationToken);

        return envelope is null ? null : Map(envelope);
    }

    public async ValueTask SaveAsync(TenantMembershipRecord membership, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(membership);

        var document = new TenantMembershipDocument(IdentityCompositeDocumentId.Normalize(membership.TenantId), membership);
        var content = JsonSerializer.Serialize(document, IdentityGroundworkJson.Options);

        await store.SaveAsync(
            new SaveDocumentRequest(
                IdentityStorageManifest.TenantMembershipDocumentKind,
                IdentityCompositeDocumentId.From(membership.TenantId, membership.UserId),
                IdentityStorageManifest.SchemaVersion,
                content),
            cancellationToken);
    }

    private static TenantMembershipRecord Map(DocumentEnvelope envelope) =>
        JsonSerializer.Deserialize<TenantMembershipDocument>(envelope.ContentJson, IdentityGroundworkJson.Options)!.TenantMembership;

    private sealed record TenantMembershipDocument(string TenantKey, TenantMembershipRecord TenantMembership);
}
