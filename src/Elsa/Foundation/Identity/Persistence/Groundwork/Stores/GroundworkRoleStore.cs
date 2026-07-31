using System.Text.Json;
using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.Persistence.Groundwork.Documents;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Stores;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Queries;
using Groundwork.Documents.Store;

namespace Elsa.Foundation.Identity.Persistence.Groundwork.Stores;

/// <summary>
/// Groundwork-backed <see cref="IRoleStore"/>. Roles are keyed by an escaped composite
/// <c>tenantId:roleId</c> document id and carry a <c>tenantKey</c> index so a tenant's roles can be
/// listed through the declared index.
/// </summary>
public sealed class GroundworkRoleStore(
    IDocumentStore store,
    IPersistenceAccessContextAccessor accessContextAccessor,
    IBoundedDocumentStore? boundedStore = null,
    GroundworkIdentityAuthorityAggregateCoordinator? aggregateCoordinator = null) : IRoleStore, IRevisionAwareRoleStore
{
    private const int MaxRelationshipMaterialization = 100_000;

    private readonly IBoundedDocumentStore? _boundedStore = boundedStore ?? store as IBoundedDocumentStore;
    private readonly GroundworkIdentityAuthorityAggregateCoordinator _aggregates =
        aggregateCoordinator ?? GroundworkIdentityAuthorityAggregateCoordinator.ForDirectStore(store);

    public async ValueTask<RoleRecord?> FindAsync(string tenantId, string roleId, CancellationToken cancellationToken = default)
    {
        accessContextAccessor.EnsureCurrentScope(tenantId);
        var envelope = await store.LoadAsync(
            IdentityStorageManifest.IdentityRoleDocumentKind,
            IdentityCompositeDocumentId.From(tenantId, roleId),
            cancellationToken);

        return envelope is null ? null : Map(envelope);
    }

    public async ValueTask<IamRevisionedRecord<RoleRecord>?> FindWithRevisionAsync(string tenantId, string roleId, CancellationToken cancellationToken = default)
    {
        accessContextAccessor.EnsureCurrentScope(tenantId);
        var envelope = await store.LoadAsync(
            IdentityStorageManifest.IdentityRoleDocumentKind,
            IdentityCompositeDocumentId.From(tenantId, roleId),
            cancellationToken);

        return envelope is null ? null : new IamRevisionedRecord<RoleRecord>(Map(envelope), GroundworkIamRevisionMapper.Revision(envelope));
    }

    public async ValueTask<IReadOnlyList<RoleRecord>> ListAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        accessContextAccessor.EnsureCurrentScope(tenantId);
        var documents = await BoundedDocumentQueryPager.QueryAllAsync(
            BoundedStore,
            IdentityStorageManifest.IdentityRoleDocumentKind,
            IdentityStorageManifest.ListRolesByTenantQuery,
            [DocumentQueryClause.Of(DocumentQueryComparison.Equal(
                IdentityStorageManifest.TenantIdField,
                IdentityCompositeDocumentId.Normalize(tenantId)))],
            cancellationToken,
            MaxRelationshipMaterialization);
        return documents.Select(Map).ToArray();
    }

    public async ValueTask SaveAsync(RoleRecord role, CancellationToken cancellationToken = default)
    {
        await SaveCoreAsync(role, expectedVersion: null, cancellationToken);
    }

    public async ValueTask<IamRevisionSaveResult> SaveWithRevisionAsync(RoleRecord role, string? expectedRevision, CancellationToken cancellationToken = default)
    {
        if (!GroundworkIamRevisionMapper.TryExpectedVersion(expectedRevision, out var expectedVersion))
            return GroundworkIamRevisionMapper.InvalidRevision();

        var result = await SaveCoreAsync(role, expectedVersion, cancellationToken);
        return GroundworkIamRevisionMapper.ToResult(result);
    }

    private async ValueTask<DocumentStoreWriteResult> SaveCoreAsync(RoleRecord role, long? expectedVersion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(role);
        accessContextAccessor.EnsureCurrentScope(role.TenantId);

        var documentId = IdentityCompositeDocumentId.From(role.TenantId, role.Id);
        var existing = await store.LoadAsync(
            IdentityStorageManifest.IdentityRoleDocumentKind,
            documentId,
            cancellationToken);
        var existingDocument = existing is null
            ? null
            : JsonSerializer.Deserialize<IdentityRoleDocument>(existing.ContentJson, IdentityGroundworkJson.Options);

        var document = new IdentityRoleDocument(
            IdentityCompositeDocumentId.Normalize(role.TenantId),
            role.Id,
            IdentityCompositeDocumentId.Normalize(role.Name),
            ScopedLookupKey(role.TenantId, role.Name),
            role,
            existingDocument?.FrameworkState);
        var result = await _aggregates.SaveRoleAsync(document, expectedVersion, cancellationToken);
        return result.WriteResult;
    }

    private static RoleRecord Map(DocumentEnvelope envelope) =>
        JsonSerializer.Deserialize<IdentityRoleDocument>(envelope.ContentJson, IdentityGroundworkJson.Options)!.Role;

    private static string? ScopedLookupKey(string tenantId, string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : IdentityDocumentId.From(tenantId, value);

    private IBoundedDocumentStore BoundedStore => _boundedStore
        ?? throw new InvalidOperationException("Identity role queries require an admitted bounded document-store runtime.");

}
