using System.Text.Json;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.Persistence.Groundwork.Documents;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Queries;
using Groundwork.Documents.Store;

namespace Elsa.Foundation.Identity.Persistence.Groundwork.Stores;

/// <summary>
/// Groundwork-backed <see cref="IClaimMappingStore"/>. Claim mapping rules are keyed by an escaped
/// <c>tenantId:provider:ruleId</c> document id and listed through a provider lookup projection.
/// </summary>
public sealed class GroundworkClaimMappingStore(
    IDocumentStore store,
    IPersistenceAccessContextAccessor accessContextAccessor,
    IBoundedDocumentStore? boundedStore = null) : IClaimMappingStore, IRevisionAwareClaimMappingStore
{
    private const int MaxMaterialization = 100_000;

    private readonly IBoundedDocumentStore? _boundedStore = boundedStore ?? store as IBoundedDocumentStore;

    public async ValueTask<IReadOnlyList<ClaimMappingRule>> ListForProviderAsync(
        string tenantId,
        string provider,
        CancellationToken cancellationToken = default)
    {
        accessContextAccessor.EnsureCurrentScope(tenantId);
        var envelopes = await IdentityBoundedDocumentQueryPager.ReadAllPagesAsync(
            BoundedStore,
            IdentityStorageManifest.IdentityClaimMappingDocumentKind,
            IdentityStorageManifest.ListClaimMappingsByProviderQuery,
            [DocumentQueryClause.Of(DocumentQueryComparison.Equal(
                IdentityStorageManifest.ProviderLookupKeyField,
                IdentityDocumentId.From(tenantId, provider)))],
            ElsaGroundworkQueryRoutes.MaximumResultCount,
            MaxMaterialization,
            cancellationToken);
        return envelopes.Select(Map).OrderBy(rule => rule.Order).ThenBy(rule => rule.Id, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public async ValueTask SaveAsync(ClaimMappingRule rule, CancellationToken cancellationToken = default)
    {
        await SaveCoreAsync(rule, expectedVersion: null, cancellationToken);
    }

    public async ValueTask<IamRevisionedRecord<ClaimMappingRule>?> FindWithRevisionAsync(
        string tenantId,
        string provider,
        string ruleId,
        CancellationToken cancellationToken = default)
    {
        accessContextAccessor.EnsureCurrentScope(tenantId);
        var envelope = await store.LoadAsync(
            IdentityStorageManifest.IdentityClaimMappingDocumentKind,
            IdentityCompositeDocumentId.From(tenantId, provider, ruleId),
            cancellationToken);

        return envelope is null ? null : new IamRevisionedRecord<ClaimMappingRule>(Map(envelope), GroundworkIamRevisionMapper.Revision(envelope));
    }

    public async ValueTask<IamRevisionSaveResult> SaveWithRevisionAsync(
        ClaimMappingRule rule,
        string? expectedRevision,
        CancellationToken cancellationToken = default)
    {
        if (!GroundworkIamRevisionMapper.TryExpectedVersion(expectedRevision, out var expectedVersion))
            return GroundworkIamRevisionMapper.InvalidRevision();

        var result = await SaveCoreAsync(rule, expectedVersion, cancellationToken);
        return GroundworkIamRevisionMapper.ToResult(result);
    }

    private async ValueTask<DocumentStoreWriteResult> SaveCoreAsync(
        ClaimMappingRule rule,
        long? expectedVersion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rule);
        accessContextAccessor.EnsureCurrentScope(rule.TenantId);

        var document = new IdentityClaimMappingDocument(
            IdentityCompositeDocumentId.Normalize(rule.TenantId),
            IdentityCompositeDocumentId.Normalize(rule.Provider),
            IdentityCompositeDocumentId.Normalize(rule.Id),
            IdentityDocumentId.From(rule.TenantId, rule.Provider),
            rule);
        var content = JsonSerializer.Serialize(document, IdentityGroundworkJson.Options);
        return await store.SaveAsync(
            new SaveDocumentRequest(
                IdentityStorageManifest.IdentityClaimMappingDocumentKind,
                IdentityCompositeDocumentId.From(rule.TenantId, rule.Provider, rule.Id),
                IdentityStorageManifest.SchemaVersion,
                content,
                expectedVersion),
            cancellationToken);
    }

    private IBoundedDocumentStore BoundedStore => _boundedStore
        ?? throw new InvalidOperationException("Claim mapping queries require an admitted bounded document-store runtime.");

    private static ClaimMappingRule Map(DocumentEnvelope envelope) =>
        JsonSerializer.Deserialize<IdentityClaimMappingDocument>(
            envelope.ContentJson,
            IdentityGroundworkJson.Options)!.Rule;
}
