using System.Text.Json;
using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.Persistence.Groundwork.Documents;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Queries;
using Groundwork.Documents.Store;

namespace Elsa.Foundation.Identity.Persistence.Groundwork.Stores;

/// <summary>
/// Groundwork-backed <see cref="IExternalIdentityStore"/>. External identities are keyed by an escaped
/// composite <c>tenantId:provider:providerSubject</c> document id (the natural subject lookup) and carry a
/// <c>userKey</c> index (<c>tenantId:userId</c>) so every external identity linked to a user resolves
/// through the declared index.
/// </summary>
public sealed class GroundworkExternalIdentityStore(
    IDocumentStore store,
    IPersistenceAccessContextAccessor accessContextAccessor,
    IBoundedDocumentStore? boundedStore = null,
    GroundworkIdentityAuthorityRelationshipCoordinator? relationshipCoordinator = null) : IExternalIdentityStore, IRevisionAwareExternalIdentityStore
{
    private const int MaxRelationshipMaterialization = 100_000;

    private readonly IBoundedDocumentStore? _boundedStore = boundedStore ?? store as IBoundedDocumentStore;
    private readonly GroundworkIdentityAuthorityRelationshipCoordinator _relationships =
        relationshipCoordinator ?? new GroundworkIdentityAuthorityRelationshipCoordinator(store);

    public async ValueTask<ExternalIdentityRecord?> FindBySubjectAsync(string tenantId, string provider, string providerSubject, CancellationToken cancellationToken = default)
    {
        accessContextAccessor.EnsureCurrentScope(tenantId);
        var envelope = await store.LoadAsync(
            IdentityStorageManifest.ExternalLoginDocumentKind,
            IdentityCompositeDocumentId.From(tenantId, provider, providerSubject),
            cancellationToken);

        return envelope is null ? null : Map(envelope);
    }

    public async ValueTask<IamRevisionedRecord<ExternalIdentityRecord>?> FindBySubjectWithRevisionAsync(string tenantId, string provider, string providerSubject, CancellationToken cancellationToken = default)
    {
        accessContextAccessor.EnsureCurrentScope(tenantId);
        var envelope = await store.LoadAsync(
            IdentityStorageManifest.ExternalLoginDocumentKind,
            IdentityCompositeDocumentId.From(tenantId, provider, providerSubject),
            cancellationToken);

        return envelope is null ? null : new IamRevisionedRecord<ExternalIdentityRecord>(Map(envelope), GroundworkIamRevisionMapper.Revision(envelope));
    }

    public async ValueTask<IReadOnlyList<ExternalIdentityRecord>> ListForUserAsync(string tenantId, string userId, CancellationToken cancellationToken = default)
    {
        accessContextAccessor.EnsureCurrentScope(tenantId);
        var envelopes = await IdentityBoundedDocumentQueryPager.ReadAllPagesAsync(
            BoundedStore,
            IdentityStorageManifest.ExternalLoginDocumentKind,
            IdentityStorageManifest.ListUserLoginsQuery,
            [DocumentQueryClause.Of(DocumentQueryComparison.Equal(
                IdentityStorageManifest.UserLookupKeyField,
                IdentityDocumentId.From(tenantId, userId)))],
            ElsaGroundworkQueryRoutes.MaximumResultCount,
            MaxRelationshipMaterialization,
            cancellationToken);
        return envelopes.Select(Map).ToArray();
    }

    public async ValueTask SaveAsync(ExternalIdentityRecord externalIdentity, CancellationToken cancellationToken = default)
    {
        var result = await SaveCoreAsync(
            externalIdentity,
            expectedVersion: null,
            enforceExpectedVersion: false,
            cancellationToken);
        if (result.Status is not DocumentStoreWriteStatus.Saved)
        {
            throw new InvalidOperationException(
                $"Groundwork external-identity save returned {result.Status}; use the revision-aware contract for an explicit owner rebind.");
        }
    }

    public async ValueTask<IamRevisionSaveResult> SaveWithRevisionAsync(ExternalIdentityRecord externalIdentity, string? expectedRevision, CancellationToken cancellationToken = default)
    {
        if (!GroundworkIamRevisionMapper.TryExpectedVersion(expectedRevision, out var expectedVersion))
            return GroundworkIamRevisionMapper.InvalidRevision();

        var result = await SaveCoreAsync(externalIdentity, expectedVersion, enforceExpectedVersion: true, cancellationToken);
        return GroundworkIamRevisionMapper.ToResult(result);
    }

    private async ValueTask<DocumentStoreWriteResult> SaveCoreAsync(
        ExternalIdentityRecord externalIdentity,
        long? expectedVersion,
        bool enforceExpectedVersion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(externalIdentity);
        accessContextAccessor.EnsureCurrentScope(externalIdentity.TenantId);

        var document = new IdentityExternalLoginDocument(
            IdentityCompositeDocumentId.Normalize(externalIdentity.TenantId),
            IdentityCompositeDocumentId.Normalize(externalIdentity.UserId),
            IdentityCompositeDocumentId.Normalize(externalIdentity.Provider),
            IdentityCompositeDocumentId.Normalize(externalIdentity.ProviderSubject),
            IdentityDocumentId.From(externalIdentity.TenantId, externalIdentity.Provider, externalIdentity.ProviderSubject),
            null,
            externalIdentity,
            IdentityDocumentId.From(externalIdentity.TenantId, externalIdentity.UserId));
        return await _relationships.SaveExternalLoginAsync(
            document,
            expectedNewOwnerVersion: null,
            expectedLoginVersion: expectedVersion,
            enforceLoginVersion: enforceExpectedVersion,
            ownershipPolicy: enforceExpectedVersion
                ? GroundworkExternalLoginOwnershipPolicy.RevisionEnforcedRebind
                : GroundworkExternalLoginOwnershipPolicy.CreateOrSameOwner,
            returnOwnerResult: false,
            cancellationToken);
    }

    private IBoundedDocumentStore BoundedStore => _boundedStore
        ?? throw new InvalidOperationException("External identity queries require an admitted bounded document-store runtime.");

    private static ExternalIdentityRecord Map(DocumentEnvelope envelope) =>
        JsonSerializer.Deserialize<IdentityExternalLoginDocument>(envelope.ContentJson, IdentityGroundworkJson.Options)!.ExternalIdentity;
}
