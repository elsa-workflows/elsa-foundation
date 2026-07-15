using System.Text.Json;
using Elsa.Foundation.Identity.Abstractions.Iam;
using Groundwork.Core.Queries;
using Groundwork.Documents.Store;

namespace Elsa.Foundation.Identity.Persistence.Groundwork.Stores;

/// <summary>
/// Groundwork-backed <see cref="IUserStore"/>. Users are keyed by an escaped composite
/// <c>tenantId:userId</c> document id and carry an <c>emailKey</c> index (<c>tenantId:email</c>) so
/// email lookups resolve through the declared index rather than a scan.
/// </summary>
public sealed class GroundworkUserStore(IDocumentStore store, IBoundedDocumentStore? boundedStore = null) : IUserStore
{
    private readonly IBoundedDocumentStore? _boundedStore = boundedStore ?? store as IBoundedDocumentStore;

    public async ValueTask<UserRecord?> FindAsync(string tenantId, string userId, CancellationToken cancellationToken = default)
    {
        var envelope = await store.LoadAsync(
            IdentityStorageManifest.UserDocumentKind,
            IdentityCompositeDocumentId.From(tenantId, userId),
            cancellationToken);

        return envelope is null ? null : Map(envelope);
    }

    public async ValueTask<UserRecord?> FindByEmailAsync(string tenantId, string email, CancellationToken cancellationToken = default)
    {
        var envelopes = (await BoundedStore.QueryAsync(
            new DocumentQuery(
                IdentityStorageManifest.UserDocumentKind,
                IdentityStorageManifest.FindUserByEmailQuery,
                [DocumentQueryClause.Of(DocumentQueryComparison.Equal(
                    IdentityStorageManifest.EmailKeyField,
                    EmailKey(tenantId, email)))]),
            cancellationToken)).Documents;

        var match = envelopes.Select(Map).FirstOrDefault();
        return match;
    }

    public async ValueTask SaveAsync(UserRecord user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        var document = new UserDocument(EmailKey(user.TenantId, user.Email), user);
        var content = JsonSerializer.Serialize(document, IdentityGroundworkJson.Options);

        await store.SaveAsync(
            new SaveDocumentRequest(
                IdentityStorageManifest.UserDocumentKind,
                IdentityCompositeDocumentId.From(user.TenantId, user.Id),
                IdentityStorageManifest.SchemaVersion,
                content),
            cancellationToken);
    }

    private static string EmailKey(string tenantId, string? email) =>
        $"{IdentityCompositeDocumentId.Normalize(tenantId)}:{IdentityCompositeDocumentId.Normalize(email)}";

    private IBoundedDocumentStore BoundedStore => _boundedStore
        ?? throw new InvalidOperationException("Identity user queries require an admitted bounded document-store runtime.");

    private static UserRecord Map(DocumentEnvelope envelope) =>
        JsonSerializer.Deserialize<UserDocument>(envelope.ContentJson, IdentityGroundworkJson.Options)!.User;

    private sealed record UserDocument(string EmailKey, UserRecord User);
}
