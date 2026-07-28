using System.Text.Json;
using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.Persistence.Groundwork.Documents;
using Elsa.Persistence.Core;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Queries;
using Groundwork.Documents.Store;

namespace Elsa.Foundation.Identity.Persistence.Groundwork.Stores;

/// <summary>
/// Groundwork-backed <see cref="IUserStore"/>. Users are keyed by an escaped composite
/// <c>tenantId:userId</c> document id and carry an <c>emailKey</c> index (<c>tenantId:email</c>) so
/// email lookups resolve through the declared index rather than a scan.
/// </summary>
public sealed class GroundworkUserStore(
    IDocumentStore store,
    IPersistenceAccessContextAccessor accessContextAccessor,
    IBoundedDocumentStore? boundedStore = null,
    GroundworkIdentityAuthorityAggregateCoordinator? aggregateCoordinator = null,
    IIdentityEmailUniquenessPolicy? emailUniquenessPolicy = null) : IUserStore, IRevisionAwareUserStore
{
    private const int AmbiguousEmailTake = 2;

    private readonly IBoundedDocumentStore? _boundedStore = boundedStore ?? store as IBoundedDocumentStore;
    private readonly GroundworkIdentityAuthorityAggregateCoordinator _aggregates =
        aggregateCoordinator ?? GroundworkIdentityAuthorityAggregateCoordinator.ForDirectStore(store);
    private readonly IIdentityEmailUniquenessPolicy _emailUniquenessPolicy =
        emailUniquenessPolicy ?? IdentityEmailUniquenessPolicy.NonUnique;

    public async ValueTask<UserRecord?> FindAsync(string tenantId, string userId, CancellationToken cancellationToken = default)
    {
        accessContextAccessor.EnsureCurrentScope(tenantId);
        var envelope = await store.LoadAsync(
            IdentityStorageManifest.IdentityUserDocumentKind,
            IdentityCompositeDocumentId.From(tenantId, userId),
            cancellationToken);

        return envelope is null ? null : Map(envelope);
    }

    public async ValueTask<IamRevisionedRecord<UserRecord>?> FindWithRevisionAsync(string tenantId, string userId, CancellationToken cancellationToken = default)
    {
        accessContextAccessor.EnsureCurrentScope(tenantId);
        var envelope = await store.LoadAsync(
            IdentityStorageManifest.IdentityUserDocumentKind,
            IdentityCompositeDocumentId.From(tenantId, userId),
            cancellationToken);

        return envelope is null ? null : new IamRevisionedRecord<UserRecord>(Map(envelope), GroundworkIamRevisionMapper.Revision(envelope));
    }

    public async ValueTask<UserRecord?> FindByEmailAsync(string tenantId, string email, CancellationToken cancellationToken = default)
    {
        accessContextAccessor.EnsureCurrentScope(tenantId);
        var envelopes = (await BoundedStore.QueryAsync(
            new DocumentQuery(
                IdentityStorageManifest.IdentityUserDocumentKind,
                IdentityStorageManifest.FindUserByNormalizedEmailQuery,
                [DocumentQueryClause.Of(DocumentQueryComparison.Equal(
                    IdentityStorageManifest.NormalizedEmailKeyField,
                    ScopedLookupKey(tenantId, email)!))],
                [],
                null,
                AmbiguousEmailTake,
                null,
                null,
                BoundedQueryResultOperation.Documents),
            cancellationToken)).Documents;

        return envelopes.Count == 1 ? Map(envelopes[0]) : null;
    }

    public async ValueTask SaveAsync(UserRecord user, CancellationToken cancellationToken = default)
    {
        await SaveCoreAsync(user, expectedVersion: null, cancellationToken);
    }

    public async ValueTask<IamRevisionSaveResult> SaveWithRevisionAsync(UserRecord user, string? expectedRevision, CancellationToken cancellationToken = default)
    {
        if (!GroundworkIamRevisionMapper.TryExpectedVersion(expectedRevision, out var expectedVersion))
            return GroundworkIamRevisionMapper.InvalidRevision();

        var result = await SaveCoreAsync(user, expectedVersion, cancellationToken);
        return GroundworkIamRevisionMapper.ToResult(result);
    }

    private async ValueTask<DocumentStoreWriteResult> SaveCoreAsync(UserRecord user, long? expectedVersion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        accessContextAccessor.EnsureCurrentScope(user.TenantId);

        var documentId = IdentityCompositeDocumentId.From(user.TenantId, user.Id);
        var existing = await store.LoadAsync(
            IdentityStorageManifest.IdentityUserDocumentKind,
            documentId,
            cancellationToken);
        var existingDocument = existing is null
            ? null
            : JsonSerializer.Deserialize<IdentityUserDocument>(existing.ContentJson, IdentityGroundworkJson.Options);

        var document = new IdentityUserDocument(
            user.TenantId,
            user.Id,
            Normalize(user.UserName),
            Normalize(user.Email),
            ScopedLookupKey(user.TenantId, user.UserName),
            ScopedLookupKey(user.TenantId, user.Email),
            user,
            existingDocument?.FrameworkState);
        var result = await _aggregates.SaveUserAsync(
            document,
            expectedVersion,
            _emailUniquenessPolicy.RequireUniqueEmail,
            cancellationToken);
        return result.WriteResult;
    }

    private static string Normalize(string? value) => IdentityCompositeDocumentId.Normalize(value);

    private static string? ScopedLookupKey(string tenantId, string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : IdentityDocumentId.From(tenantId, value);

    private IBoundedDocumentStore BoundedStore => _boundedStore
        ?? throw new InvalidOperationException("Identity user queries require an admitted bounded document-store runtime.");

    private static UserRecord Map(DocumentEnvelope envelope) =>
        JsonSerializer.Deserialize<IdentityUserDocument>(envelope.ContentJson, IdentityGroundworkJson.Options)!.User;
}
