using System.Text.Json;
using Elsa.Foundation.Identity.Abstractions.Iam;
using Groundwork.Documents.Store;

namespace Elsa.Foundation.Identity.Persistence.Groundwork.Stores;

/// <summary>
/// Groundwork-backed <see cref="IRoleStore"/>. Roles are keyed by an escaped composite
/// <c>tenantId:roleId</c> document id and carry a <c>tenantKey</c> index so a tenant's roles can be
/// listed through the declared index.
/// </summary>
public sealed class GroundworkRoleStore(IDocumentStore store) : IRoleStore
{
    public async ValueTask<RoleRecord?> FindAsync(string tenantId, string roleId, CancellationToken cancellationToken = default)
    {
        var envelope = await store.LoadAsync(
            IdentityStorageManifest.RoleDocumentKind,
            IdentityCompositeDocumentId.From(tenantId, roleId),
            cancellationToken);

        return envelope is null ? null : Map(envelope);
    }

    public async ValueTask<IReadOnlyList<RoleRecord>> ListAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        var envelopes = await store.QueryAsync(
            new DocumentStoreQuery(
                IdentityStorageManifest.RoleDocumentKind,
                IdentityStorageManifest.ByTenantIndex,
                IdentityCompositeDocumentId.Normalize(tenantId)),
            cancellationToken);

        return envelopes.Select(Map).ToArray();
    }

    public async ValueTask SaveAsync(RoleRecord role, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(role);

        var document = new RoleDocument(IdentityCompositeDocumentId.Normalize(role.TenantId), role);
        var content = JsonSerializer.Serialize(document, IdentityGroundworkJson.Options);

        await store.SaveAsync(
            new SaveDocumentRequest(
                IdentityStorageManifest.RoleDocumentKind,
                IdentityCompositeDocumentId.From(role.TenantId, role.Id),
                IdentityStorageManifest.SchemaVersion,
                content),
            cancellationToken);
    }

    private static RoleRecord Map(DocumentEnvelope envelope) =>
        JsonSerializer.Deserialize<RoleDocument>(envelope.ContentJson, IdentityGroundworkJson.Options)!.Role;

    private sealed record RoleDocument(string TenantKey, RoleRecord Role);
}
