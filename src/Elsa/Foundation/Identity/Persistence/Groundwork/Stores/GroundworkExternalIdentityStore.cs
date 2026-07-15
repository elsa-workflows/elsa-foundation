using System.Text.Json;
using Elsa.Foundation.Identity.Abstractions.Iam;
using Groundwork.Core.Queries;
using Groundwork.Documents.Store;

namespace Elsa.Foundation.Identity.Persistence.Groundwork.Stores;

/// <summary>
/// Groundwork-backed <see cref="IExternalIdentityStore"/>. External identities are keyed by an escaped
/// composite <c>tenantId:provider:providerSubject</c> document id (the natural subject lookup) and carry a
/// <c>userKey</c> index (<c>tenantId:userId</c>) so every external identity linked to a user resolves
/// through the declared index.
/// </summary>
public sealed class GroundworkExternalIdentityStore(IDocumentStore store, IBoundedDocumentStore? boundedStore = null) : IExternalIdentityStore
{
    private readonly IBoundedDocumentStore? _boundedStore = boundedStore ?? store as IBoundedDocumentStore;

    public async ValueTask<ExternalIdentityRecord?> FindBySubjectAsync(string tenantId, string provider, string providerSubject, CancellationToken cancellationToken = default)
    {
        var envelope = await store.LoadAsync(
            IdentityStorageManifest.ExternalIdentityDocumentKind,
            IdentityCompositeDocumentId.From(tenantId, provider, providerSubject),
            cancellationToken);

        return envelope is null ? null : Map(envelope);
    }

    public async ValueTask<IReadOnlyList<ExternalIdentityRecord>> ListForUserAsync(string tenantId, string userId, CancellationToken cancellationToken = default)
    {
        var envelopes = (await BoundedStore.QueryAsync(
            new DocumentQuery(
                IdentityStorageManifest.ExternalIdentityDocumentKind,
                IdentityStorageManifest.ListExternalIdentitiesByUserQuery,
                [DocumentQueryClause.Of(DocumentQueryComparison.Equal(
                    IdentityStorageManifest.UserKeyField,
                    UserKey(tenantId, userId)))]),
            cancellationToken)).Documents;

        return envelopes.Select(Map).ToArray();
    }

    public async ValueTask SaveAsync(ExternalIdentityRecord externalIdentity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(externalIdentity);

        var document = new ExternalIdentityDocument(UserKey(externalIdentity.TenantId, externalIdentity.UserId), externalIdentity);
        var content = JsonSerializer.Serialize(document, IdentityGroundworkJson.Options);

        await store.SaveAsync(
            new SaveDocumentRequest(
                IdentityStorageManifest.ExternalIdentityDocumentKind,
                IdentityCompositeDocumentId.From(externalIdentity.TenantId, externalIdentity.Provider, externalIdentity.ProviderSubject),
                IdentityStorageManifest.SchemaVersion,
                content),
            cancellationToken);
    }

    private static string UserKey(string tenantId, string userId) =>
        $"{IdentityCompositeDocumentId.Normalize(tenantId)}:{IdentityCompositeDocumentId.Normalize(userId)}";

    private IBoundedDocumentStore BoundedStore => _boundedStore
        ?? throw new InvalidOperationException("External identity queries require an admitted bounded document-store runtime.");

    private static ExternalIdentityRecord Map(DocumentEnvelope envelope) =>
        JsonSerializer.Deserialize<ExternalIdentityDocument>(envelope.ContentJson, IdentityGroundworkJson.Options)!.ExternalIdentity;

    private sealed record ExternalIdentityDocument(string UserKey, ExternalIdentityRecord ExternalIdentity);
}
