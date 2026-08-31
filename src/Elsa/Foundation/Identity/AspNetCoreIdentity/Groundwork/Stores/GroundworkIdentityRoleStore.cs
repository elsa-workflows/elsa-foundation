using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.Persistence.Groundwork;
using Elsa.Foundation.Identity.Persistence.Groundwork.Documents;
using Elsa.Foundation.Identity.Persistence.Groundwork.Stores;
using Elsa.Persistence.Core;
using Groundwork.Store;
using Microsoft.AspNetCore.Identity;
using System.Security.Claims;
using System.Text.Json;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Stores;

public sealed class GroundworkIdentityRoleStore(
    GroundworkIdentityRowStore rows,
    IPersistenceAccessContextAccessor accessContextAccessor,
    GroundworkIdentityAuthorityRelationshipCoordinator? relationshipCoordinator = null,
    GroundworkIdentityAuthorityAggregateCoordinator? aggregateCoordinator = null) : IRoleClaimStore<IdentityRole>
{
    private const int SingleResultTake = 1;
    private const int MaxRelationshipMaterialization = IdentityStorageManifest.MaxAggregateRelationshipEntries;

    private readonly GroundworkIdentityAuthorityRelationshipCoordinator _relationships =
        relationshipCoordinator ?? GroundworkIdentityAuthorityRelationshipCoordinator.ForRows(rows);
    private readonly GroundworkIdentityAuthorityAggregateCoordinator _aggregates =
        aggregateCoordinator ?? GroundworkIdentityAuthorityAggregateCoordinator.ForRows(rows);

    public void Dispose()
    {
    }

    public async Task<IdentityResult> CreateAsync(IdentityRole role, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(role);
        var result = await SaveRoleAsync(role, expectedVersion: 0, cancellationToken);
        return ToIdentityResult(role, result);
    }

    public async Task<IdentityResult> UpdateAsync(IdentityRole role, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(role);
        if (!TryExpectedVersion(role, out var expectedVersion))
            return ConcurrencyFailure();

        var result = await SaveRoleAsync(role, expectedVersion, cancellationToken);
        return ToIdentityResult(role, result);
    }

    public async Task<IdentityResult> DeleteAsync(IdentityRole role, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(role);
        if (!TryExpectedVersion(role, out var expectedVersion) || expectedVersion is not { } version)
            return ConcurrencyFailure();

        var result = await _aggregates.DeleteRoleAsync(CurrentTenantId(), role.Id, version, cancellationToken);
        return ToIdentityResult(result);
    }

    public Task<string> GetRoleIdAsync(IdentityRole role, CancellationToken cancellationToken) =>
        Task.FromResult(role.Id);

    public Task<string?> GetRoleNameAsync(IdentityRole role, CancellationToken cancellationToken) =>
        Task.FromResult(role.Name);

    public Task SetRoleNameAsync(IdentityRole role, string? roleName, CancellationToken cancellationToken)
    {
        role.Name = roleName;
        return Task.CompletedTask;
    }

    public Task<string?> GetNormalizedRoleNameAsync(IdentityRole role, CancellationToken cancellationToken) =>
        Task.FromResult(role.NormalizedName);

    public Task SetNormalizedRoleNameAsync(IdentityRole role, string? normalizedName, CancellationToken cancellationToken)
    {
        role.NormalizedName = normalizedName;
        return Task.CompletedTask;
    }

    public async Task<IdentityRole?> FindByIdAsync(string roleId, CancellationToken cancellationToken)
    {
        var envelope = rows.Read(
            IdentityStorageManifest.IdentityRoleDocumentKind,
            RoleDocumentId(CurrentTenantId(), roleId),
            cancellationToken);
        return envelope is null ? null : MapRole(envelope);
    }

    public async Task<IdentityRole?> FindByNameAsync(string normalizedRoleName, CancellationToken cancellationToken)
    {
        var tenantId = CurrentTenantId();
        var envelope = First(
            IdentityStorageManifest.IdentityRoleDocumentKind,
            IdentityStorageManifest.FindRoleByNormalizedNameQuery,
            (IdentityStorageManifest.NormalizedRoleNameKeyField, ScopedLookupKey(tenantId, normalizedRoleName)!),
            cancellationToken);
        if (envelope is null)
            return null;

        var document = Deserialize<IdentityRoleDocument>(envelope);
        return string.Equals(document.TenantId, Normalize(tenantId), StringComparison.Ordinal)
            ? MapRole(envelope)
            : null;
    }

    public async Task<IList<Claim>> GetClaimsAsync(IdentityRole role, CancellationToken cancellationToken)
    {
        var envelopes = Query(
            IdentityStorageManifest.RoleClaimDocumentKind,
            IdentityStorageManifest.ListRoleClaimsQuery,
            (IdentityStorageManifest.RoleLookupKeyField, RoleLookupKey(CurrentTenantId(), role.Id)),
            cancellationToken);
        return envelopes
            .Select(MapRoleClaim)
            .Cast<Claim>()
            .ToArray();
    }

    public async Task AddClaimAsync(IdentityRole role, Claim claim, CancellationToken cancellationToken)
    {
        if (!TryExpectedVersion(role, out var expectedVersion) || expectedVersion is not { } roleVersion)
            throw new InvalidOperationException("The requested role has no valid Groundwork revision stamp.");
        var document = new IdentityRoleClaimDocument(
            Normalize(CurrentTenantId()),
            Normalize(role.Id),
            claim.Type,
            claim.Value,
            RoleLookupKey(CurrentTenantId(), role.Id));
        var result = await _relationships.SaveRoleClaimAsync(
            CurrentTenantId(), role.Id, roleVersion, document, cancellationToken);
        ApplyRevisionStamp(role, result);
        if (!result.Succeeded)
            throw new InvalidOperationException($"Groundwork Identity role-claim add returned {result.Status}.");
    }

    public async Task RemoveClaimAsync(IdentityRole role, Claim claim, CancellationToken cancellationToken)
    {
        if (!TryExpectedVersion(role, out var expectedVersion) || expectedVersion is not { } roleVersion)
            throw new InvalidOperationException("The requested role has no valid Groundwork revision stamp.");
        var document = new IdentityRoleClaimDocument(
            Normalize(CurrentTenantId()), Normalize(role.Id), claim.Type, claim.Value,
            RoleLookupKey(CurrentTenantId(), role.Id));
        var result = await _relationships.DeleteRoleClaimAsync(
            CurrentTenantId(), role.Id, roleVersion, document, cancellationToken);
        ApplyRevisionStamp(role, result);
        if (!result.Succeeded)
            throw new InvalidOperationException($"Groundwork Identity role-claim remove returned {result.Status}.");
    }

    private async Task<GroundworkIdentityAuthorityWriteResult> SaveRoleAsync(
        IdentityRole role,
        long? expectedVersion,
        CancellationToken cancellationToken)
    {
        var tenantId = CurrentTenantId();
        var documentId = RoleDocumentId(tenantId, role.Id);
        var existingEnvelope = rows.Read(
            IdentityStorageManifest.IdentityRoleDocumentKind,
            documentId,
            cancellationToken);
        var existingDocument = existingEnvelope is null ? null : Deserialize<IdentityRoleDocument>(existingEnvelope);
        var document = new IdentityRoleDocument(
            Normalize(tenantId),
            Normalize(role.Id),
            Normalize(role.NormalizedName),
            ScopedLookupKey(tenantId, role.NormalizedName),
            MergeRoleRecord(existingDocument?.Role, role, tenantId),
            AspNetCoreIdentityAuthorityMapper.ToFrameworkState(role),
            existingDocument?.ClaimIds,
            existingDocument?.UserLinkIds);
        var result = await _aggregates.SaveRoleAsync(
            document,
            expectedVersion,
            cancellationToken,
            Guid.NewGuid().ToString("N"));
        ApplyRevisionStamp(role, result.WriteResult);
        return result;
    }

    private GroundworkIdentityRow? First(
        string documentKind,
        string queryIdentity,
        (string Field, string Value) comparison,
        CancellationToken cancellationToken) =>
        Query(documentKind, queryIdentity, comparison, SingleResultTake, cancellationToken, includeVersions: true).FirstOrDefault();

    private IReadOnlyList<GroundworkIdentityRow> Query(
        string documentKind,
        string queryIdentity,
        (string Field, string Value) comparison,
        CancellationToken cancellationToken) =>
        rows.QueryAllPages(
            documentKind,
            new GroundworkIdentityRowQuery(
                comparison.Field,
                GroundworkIdentityRowComparison.Equal,
                comparison.Value,
                IdentityV2StorageManifest.IdField,
                ExpectedIndex: IdentityV2StorageManifest.IndexForQuery(queryIdentity)),
            MaxRelationshipMaterialization,
            cancellationToken).Rows;

    private IReadOnlyList<GroundworkIdentityRow> Query(
        string documentKind,
        string queryIdentity,
        (string Field, string Value) comparison,
        int take,
        CancellationToken cancellationToken,
        bool includeVersions = false) =>
        rows.Query(
            documentKind,
            new GroundworkIdentityRowQuery(
                comparison.Field,
                GroundworkIdentityRowComparison.Equal,
                comparison.Value,
                IdentityV2StorageManifest.IdField,
                Take: take,
                IncludeVersions: includeVersions,
                ExpectedIndex: IdentityV2StorageManifest.IndexForQuery(queryIdentity)),
            cancellationToken);

    private static IdentityRole MapRole(GroundworkIdentityRow envelope)
    {
        var document = Deserialize<IdentityRoleDocument>(envelope);
        var role = AspNetCoreIdentityAuthorityMapper.ToRole(document);
        role.ConcurrencyStamp = IdentityRevisionStamp.FromRole(document.TenantId, role.Id, envelope.Version).ToString();
        return role;
    }

    private static Claim MapRoleClaim(GroundworkIdentityRow envelope)
    {
        var document = Deserialize<IdentityRoleClaimDocument>(envelope);
        return new Claim(document.ClaimType, document.ClaimValue ?? string.Empty);
    }

    private static TDocument Deserialize<TDocument>(GroundworkIdentityRow row) =>
        JsonSerializer.Deserialize<TDocument>(row.CanonicalJson, IdentityGroundworkJson.Options)!;

    private static IdentityResult ToIdentityResult(GroundworkIdentityWriteResult result) =>
        result.Status switch
        {
            _ when result.Succeeded => IdentityResult.Success,
            WriteOutcomeStatus.ConcurrencyConflict => IdentityResult.Failed(new IdentityErrorDescriber().ConcurrencyFailure()),
            _ => IdentityResult.Failed(new IdentityError
            {
                Code = $"GroundworkIdentity{result.Status}",
                Description = $"Groundwork Identity write returned {result.Status}."
            })
        };

    private static IdentityResult ToIdentityResult(
        IdentityRole role,
        GroundworkIdentityAuthorityWriteResult result) =>
        result.Conflict is GroundworkIdentityAuthorityConflict.RoleName
            ? IdentityResult.Failed(new IdentityErrorDescriber().DuplicateRoleName(role.NormalizedName ?? role.Name ?? role.Id))
            : ToIdentityResult(result.WriteResult);

    private bool TryExpectedVersion(IdentityRole role, out long? expectedVersion)
    {
        if (IdentityRevisionStamp.TryGetRoleVersion(role.ConcurrencyStamp, CurrentTenantId(), role.Id, out var version))
        {
            expectedVersion = version;
            return true;
        }

        expectedVersion = null;
        return false;
    }

    private static void ApplyRevisionStamp(IdentityRole role, GroundworkIdentityWriteResult result)
    {
        if (result.Succeeded && result.Row is not null)
        {
            var document = Deserialize<IdentityRoleDocument>(result.Row);
            role.ConcurrencyStamp = IdentityRevisionStamp.FromRole(document.TenantId, role.Id, result.Row.Version).ToString();
        }
    }

    private static IdentityResult ConcurrencyFailure() =>
        IdentityResult.Failed(new IdentityErrorDescriber().ConcurrencyFailure());

    private static RoleRecord MergeRoleRecord(RoleRecord? existing, IdentityRole role, string tenantId)
    {
        var next = AspNetCoreIdentityAuthorityMapper.ToRoleRecord(role, tenantId);
        return existing is null
            ? next
            : existing with
            {
                Name = next.Name
            };
    }

    private string CurrentTenantId() =>
        accessContextAccessor.Current.Scope?.Value ?? PersistenceScope.DefaultValue;

    private static string RoleDocumentId(string tenantId, string roleId) =>
        IdentityCompositeDocumentId.From(tenantId, roleId);

    private static string? ScopedLookupKey(string tenantId, string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : IdentityDocumentId.From(tenantId, value);

    private static string RoleLookupKey(string tenantId, string roleId) =>
        IdentityDocumentId.From(tenantId, roleId);

    private static string Normalize(string? value) => IdentityCompositeDocumentId.Normalize(value);
}
