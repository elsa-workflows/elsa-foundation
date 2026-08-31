using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.Persistence.Groundwork.Documents;
using Elsa.Persistence.Core;

namespace Elsa.Foundation.Identity.Persistence.Groundwork.Stores;

/// <summary>
/// Groundwork-backed <see cref="IRoleStore"/>. Roles are keyed by an escaped composite
/// <c>tenantId:roleId</c> document id and carry a <c>tenantKey</c> index so a tenant's roles can be
/// listed through the declared index.
/// </summary>
public sealed class GroundworkRoleStore(
    GroundworkIdentityRowStore rows,
    IPersistenceAccessContextAccessor accessContextAccessor,
    GroundworkIdentityAuthorityAggregateCoordinator? aggregateCoordinator = null) : IRoleStore, IRevisionAwareRoleStore, IPagedRoleStore
{
    private readonly GroundworkIdentityAuthorityAggregateCoordinator _aggregates =
        aggregateCoordinator ?? GroundworkIdentityAuthorityAggregateCoordinator.ForRows(rows);

    public ValueTask<RoleRecord?> FindAsync(string tenantId, string roleId, CancellationToken cancellationToken = default)
    {
        accessContextAccessor.EnsureCurrentScope(tenantId);
        var row = rows.Read(
            IdentityStorageManifest.IdentityRoleDocumentKind,
            IdentityCompositeDocumentId.From(tenantId, roleId),
            cancellationToken);

        return ValueTask.FromResult(row is null ? null : Map(row));
    }

    public ValueTask<IamRevisionedRecord<RoleRecord>?> FindWithRevisionAsync(string tenantId, string roleId, CancellationToken cancellationToken = default)
    {
        accessContextAccessor.EnsureCurrentScope(tenantId);
        var row = rows.Read(
            IdentityStorageManifest.IdentityRoleDocumentKind,
            IdentityCompositeDocumentId.From(tenantId, roleId),
            cancellationToken);

        return ValueTask.FromResult(row is null ? null : new IamRevisionedRecord<RoleRecord>(Map(row), GroundworkIamRevisionMapper.Revision(row)));
    }

    public ValueTask<IReadOnlyList<RoleRecord>> ListAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        accessContextAccessor.EnsureCurrentScope(tenantId);
        var result = rows.QueryAllPages(
            IdentityStorageManifest.IdentityRoleDocumentKind,
            new GroundworkIdentityRowQuery(
                IdentityStorageManifest.TenantIdField,
                GroundworkIdentityRowComparison.Equal,
                IdentityCompositeDocumentId.Normalize(tenantId),
                IdentityV2StorageManifest.IdField,
                ExpectedIndex: IdentityV2StorageManifest.RoleByTenantIndex),
            IdentityStorageManifest.MaxMaterializedListEntries,
            cancellationToken);
        GroundworkIdentityListGuard.EnsureWithinMaterializationLimit<IPagedRoleStore>(result.Rows.Count);
        return ValueTask.FromResult<IReadOnlyList<RoleRecord>>(result.Rows.Select(Map).ToArray());
    }

    public ValueTask<IamPage<RoleRecord>> ListPageAsync(
        string tenantId,
        IamPageRequest request,
        CancellationToken cancellationToken = default)
    {
        accessContextAccessor.EnsureCurrentScope(tenantId);
        var result = rows.QueryWithTotalCount(
            IdentityStorageManifest.IdentityRoleDocumentKind,
            new GroundworkIdentityRowQuery(
                IdentityStorageManifest.TenantIdField,
                GroundworkIdentityRowComparison.Equal,
                IdentityCompositeDocumentId.Normalize(tenantId),
                IdentityV2StorageManifest.IdField,
                Take: request.Take,
                Skip: request.Skip,
                ExpectedIndex: IdentityV2StorageManifest.RoleByTenantIndex),
            cancellationToken);
        return ValueTask.FromResult(new IamPage<RoleRecord>(result.Rows.Select(Map).ToArray(), result.TotalCount));
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

    private async ValueTask<GroundworkIdentityWriteResult> SaveCoreAsync(RoleRecord role, long? expectedVersion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(role);
        accessContextAccessor.EnsureCurrentScope(role.TenantId);

        var documentId = IdentityCompositeDocumentId.From(role.TenantId, role.Id);
        var existing = rows.Read(
            IdentityStorageManifest.IdentityRoleDocumentKind,
            documentId,
            cancellationToken);
        var existingDocument = existing is null ? null : GroundworkIdentityDocumentRows.Deserialize<IdentityRoleDocument>(existing);

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

    private static RoleRecord Map(GroundworkIdentityRow row) =>
        GroundworkIdentityDocumentRows.Deserialize<IdentityRoleDocument>(row).Role;

    private static string? ScopedLookupKey(string tenantId, string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : IdentityDocumentId.From(tenantId, value);

}
