using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.Persistence.Groundwork.Documents;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Foundation.Identity.Persistence.Groundwork.Stores;

/// <summary>
/// Groundwork-backed <see cref="IClaimMappingStore"/>. Claim mapping rules are keyed by an escaped
/// <c>tenantId:provider:ruleId</c> document id and listed through a provider lookup projection.
/// </summary>
public sealed class GroundworkClaimMappingStore(
    GroundworkIdentityRowStore rows,
    IPersistenceAccessContextAccessor accessContextAccessor) : IClaimMappingStore, IRevisionAwareClaimMappingStore, IPagedClaimMappingStore
{
    public ValueTask<IReadOnlyList<ClaimMappingRule>> ListForProviderAsync(
        string tenantId,
        string provider,
        CancellationToken cancellationToken = default)
    {
        accessContextAccessor.EnsureCurrentScope(tenantId);
        var result = rows.QueryAllPages(
            IdentityStorageManifest.IdentityClaimMappingDocumentKind,
            new GroundworkIdentityRowQuery(
                IdentityStorageManifest.ProviderLookupKeyField,
                GroundworkIdentityRowComparison.Equal,
                IdentityDocumentId.From(tenantId, provider),
                IdentityStorageManifest.ClaimMappingOrderField,
                ExpectedIndex: IdentityV2StorageManifest.ClaimMappingByProviderIndex),
            IdentityStorageManifest.MaxMaterializedListEntries,
            cancellationToken);
        GroundworkIdentityListGuard.EnsureWithinMaterializationLimit<IPagedClaimMappingStore>(result.Rows.Count);
        return ValueTask.FromResult<IReadOnlyList<ClaimMappingRule>>(result.Rows
            .Select(Map)
            .ToArray());
    }

    public ValueTask<IamPage<ClaimMappingRule>> ListForProviderPageAsync(
        string tenantId,
        string provider,
        IamPageRequest request,
        CancellationToken cancellationToken = default)
    {
        accessContextAccessor.EnsureCurrentScope(tenantId);
        var result = rows.QueryWithTotalCount(
            IdentityStorageManifest.IdentityClaimMappingDocumentKind,
            new GroundworkIdentityRowQuery(
                IdentityStorageManifest.ProviderLookupKeyField,
                GroundworkIdentityRowComparison.Equal,
                IdentityDocumentId.From(tenantId, provider),
                IdentityStorageManifest.ClaimMappingOrderField,
                Take: request.Take,
                Skip: request.Skip,
                ExpectedIndex: IdentityV2StorageManifest.ClaimMappingByProviderIndex),
            cancellationToken);
        return ValueTask.FromResult(new IamPage<ClaimMappingRule>(
            result.Rows
                .Select(Map)
                .ToArray(),
            result.TotalCount));
    }

    public async ValueTask SaveAsync(ClaimMappingRule rule, CancellationToken cancellationToken = default)
    {
        await SaveCoreAsync(rule, expectedVersion: null, cancellationToken);
    }

    public ValueTask<IamRevisionedRecord<ClaimMappingRule>?> FindWithRevisionAsync(
        string tenantId,
        string provider,
        string ruleId,
        CancellationToken cancellationToken = default)
    {
        accessContextAccessor.EnsureCurrentScope(tenantId);
        var row = rows.Read(
            IdentityStorageManifest.IdentityClaimMappingDocumentKind,
            IdentityCompositeDocumentId.From(tenantId, provider, ruleId),
            cancellationToken);

        return ValueTask.FromResult(row is null ? null : new IamRevisionedRecord<ClaimMappingRule>(Map(row), GroundworkIamRevisionMapper.Revision(row)));
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

    private ValueTask<GroundworkIdentityWriteResult> SaveCoreAsync(
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
        return ValueTask.FromResult(rows.Save(
            GroundworkIdentityDocumentRows.Write(
                IdentityStorageManifest.IdentityClaimMappingDocumentKind,
                IdentityCompositeDocumentId.From(rule.TenantId, rule.Provider, rule.Id),
                document,
                expectedVersion,
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    [IdentityStorageManifest.ProviderLookupKeyField] = document.ProviderLookupKey,
                    [IdentityStorageManifest.ClaimMappingOrderField] = rule.Order
                }),
            cancellationToken));
    }

    private static ClaimMappingRule Map(GroundworkIdentityRow row) =>
        GroundworkIdentityDocumentRows.Deserialize<IdentityClaimMappingDocument>(row).Rule;
}
