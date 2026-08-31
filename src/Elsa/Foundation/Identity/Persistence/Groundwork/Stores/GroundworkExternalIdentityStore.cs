using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.Persistence.Groundwork.Documents;
using Elsa.Persistence.Core;
using Groundwork.Store;

namespace Elsa.Foundation.Identity.Persistence.Groundwork.Stores;

/// <summary>
/// Groundwork-backed <see cref="IExternalIdentityStore"/>. External identities are keyed by an escaped
/// composite <c>tenantId:provider:providerSubject</c> document id (the natural subject lookup) and carry a
/// <c>userKey</c> index (<c>tenantId:userId</c>) so every external identity linked to a user resolves
/// through the declared index.
/// </summary>
public sealed class GroundworkExternalIdentityStore(
    GroundworkIdentityRowStore rows,
    IPersistenceAccessContextAccessor accessContextAccessor,
    GroundworkIdentityAuthorityRelationshipCoordinator? relationshipCoordinator = null) : IExternalIdentityStore, IRevisionAwareExternalIdentityStore, IPagedExternalIdentityStore
{
    private readonly GroundworkIdentityAuthorityRelationshipCoordinator _relationships =
        relationshipCoordinator ?? GroundworkIdentityAuthorityRelationshipCoordinator.ForRows(rows);

    public ValueTask<ExternalIdentityRecord?> FindBySubjectAsync(string tenantId, string provider, string providerSubject, CancellationToken cancellationToken = default)
    {
        accessContextAccessor.EnsureCurrentScope(tenantId);
        var row = rows.Read(
            IdentityStorageManifest.ExternalLoginDocumentKind,
            IdentityCompositeDocumentId.From(tenantId, provider, providerSubject),
            cancellationToken);

        return ValueTask.FromResult(row is null ? null : Map(row));
    }

    public ValueTask<IamRevisionedRecord<ExternalIdentityRecord>?> FindBySubjectWithRevisionAsync(string tenantId, string provider, string providerSubject, CancellationToken cancellationToken = default)
    {
        accessContextAccessor.EnsureCurrentScope(tenantId);
        var row = rows.Read(
            IdentityStorageManifest.ExternalLoginDocumentKind,
            IdentityCompositeDocumentId.From(tenantId, provider, providerSubject),
            cancellationToken);

        return ValueTask.FromResult(row is null ? null : new IamRevisionedRecord<ExternalIdentityRecord>(Map(row), GroundworkIamRevisionMapper.Revision(row)));
    }

    public ValueTask<IReadOnlyList<ExternalIdentityRecord>> ListForUserAsync(string tenantId, string userId, CancellationToken cancellationToken = default)
    {
        accessContextAccessor.EnsureCurrentScope(tenantId);
        var result = rows.QueryWithTotalCount(
            IdentityStorageManifest.ExternalLoginDocumentKind,
            new GroundworkIdentityRowQuery(
                IdentityStorageManifest.UserLookupKeyField,
                GroundworkIdentityRowComparison.Equal,
                IdentityDocumentId.From(tenantId, userId),
                IdentityV2StorageManifest.IdField,
                Take: IdentityStorageManifest.MaxAggregateRelationshipEntries,
                ExpectedIndex: IdentityV2StorageManifest.LoginByUserIndex),
            cancellationToken);
        GroundworkIdentityListGuard.EnsureWithinMaterializationLimit<IPagedExternalIdentityStore>(result.TotalCount);
        return ValueTask.FromResult<IReadOnlyList<ExternalIdentityRecord>>(result.Rows.Select(Map).ToArray());
    }

    public ValueTask<IamPage<ExternalIdentityRecord>> ListForUserPageAsync(
        string tenantId,
        string userId,
        IamPageRequest request,
        CancellationToken cancellationToken = default)
    {
        accessContextAccessor.EnsureCurrentScope(tenantId);
        var result = rows.QueryWithTotalCount(
            IdentityStorageManifest.ExternalLoginDocumentKind,
            new GroundworkIdentityRowQuery(
                IdentityStorageManifest.UserLookupKeyField,
                GroundworkIdentityRowComparison.Equal,
                IdentityDocumentId.From(tenantId, userId),
                IdentityV2StorageManifest.IdField,
                Take: request.Take,
                Skip: request.Skip,
                ExpectedIndex: IdentityV2StorageManifest.LoginByUserIndex),
            cancellationToken);
        return ValueTask.FromResult(new IamPage<ExternalIdentityRecord>(result.Rows.Select(Map).ToArray(), result.TotalCount));
    }

    public async ValueTask SaveAsync(ExternalIdentityRecord externalIdentity, CancellationToken cancellationToken = default)
    {
        var result = await SaveCoreAsync(
            externalIdentity,
            expectedVersion: null,
            enforceExpectedVersion: false,
            cancellationToken);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Groundwork external-identity save returned {result.Status}; use the revision-aware contract for an explicit owner rebind.");
        }
    }

    public async ValueTask<IamRevisionSaveResult> SaveWithRevisionAsync(ExternalIdentityRecord externalIdentity, string? expectedRevision, CancellationToken cancellationToken = default)
    {
        if (!GroundworkIamRevisionMapper.TryExpectedVersion(expectedRevision, out var expectedVersion))
            return GroundworkIamRevisionMapper.InvalidRevision();

        // The relationship coordinator must load both owners before it can stage an atomic rebind.
        // Preserve the revision contract's missing-login result before that owner validation runs.
        if (expectedVersion is > 0 &&
            await FindBySubjectWithRevisionAsync(
                externalIdentity.TenantId,
                externalIdentity.Provider,
                externalIdentity.ProviderSubject,
                cancellationToken) is null)
        {
            return new IamRevisionSaveResult(IamRevisionSaveStatus.NotFound);
        }

        var result = await SaveCoreAsync(externalIdentity, expectedVersion, enforceExpectedVersion: true, cancellationToken);
        return GroundworkIamRevisionMapper.ToResult(result);
    }

    private async ValueTask<GroundworkIdentityWriteResult> SaveCoreAsync(
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

    private static ExternalIdentityRecord Map(GroundworkIdentityRow row) =>
        GroundworkIdentityDocumentRows.Deserialize<IdentityExternalLoginDocument>(row).ExternalIdentity;
}
