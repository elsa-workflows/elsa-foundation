using System.Text.Json;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Stores;
using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.Persistence.Groundwork;
using Elsa.Foundation.Identity.Persistence.Groundwork.Documents;
using Elsa.Foundation.Identity.Persistence.Groundwork.Stores;
using Elsa.Persistence.Core;
using ElsaExternalIdentityStore = Elsa.Foundation.Identity.Persistence.Groundwork.Stores.GroundworkExternalIdentityStore;
using ElsaRoleStore = Elsa.Foundation.Identity.Persistence.Groundwork.Stores.GroundworkRoleStore;
using ElsaTenantMembershipStore = Elsa.Foundation.Identity.Persistence.Groundwork.Stores.GroundworkTenantMembershipStore;
using ElsaUserStore = Elsa.Foundation.Identity.Persistence.Groundwork.Stores.GroundworkUserStore;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork.Tests.Fixtures;

internal sealed class AspNetCoreIdentityGroundworkStoreFixture(
    string tenantId,
    AspNetCoreIdentityTestPersistence? persistence = null,
    bool requireUniqueEmail = false)
{
    private readonly FixedAccessContextAccessor _accessor = new(
        PersistenceAccessContext.Scoped(new PersistenceScope(tenantId)));

    public AspNetCoreIdentityTestPersistence Persistence { get; } = persistence ?? new AspNetCoreIdentityTestPersistence();

    public GroundworkIdentityRowStore Rows => Persistence.Rows(_accessor);

    public IReadOnlyList<GroundworkIdentityRow> Snapshot(string unitId) => Persistence.Snapshot(unitId, tenantId);

    public GroundworkIdentityRow? Read(string unitId, string id) => Persistence.Read(unitId, id, tenantId);

    public Task<GroundworkIdentityRow?> LoadAsync(
        string unitId,
        string id,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Read(unitId, id));
    }

    public GroundworkIdentityWriteResult Save(
        string unitId,
        string id,
        string canonicalJson,
        long? expectedVersion = null) =>
        Rows.Save(new GroundworkIdentityRowWrite(
            unitId,
            id,
            canonicalJson,
            ProjectedValues(unitId, id, canonicalJson),
            expectedVersion is null
                ? GroundworkIdentityRowWriteCondition.Unconditional
                : expectedVersion == 0
                    ? GroundworkIdentityRowWriteCondition.CreateOnly
                    : GroundworkIdentityRowWriteCondition.IfVersion(expectedVersion.Value)));

    private IReadOnlyDictionary<string, object?> ProjectedValues(
        string unitId,
        string id,
        string canonicalJson)
    {
        var current = Read(unitId, id);
        if (current is not null)
            return current.ProjectedValues;

        return unitId switch
        {
            IdentityStorageManifest.UserClaimDocumentKind =>
                JsonSerializer.Deserialize<IdentityUserClaimDocument>(canonicalJson, IdentityGroundworkJson.Options) is { } userClaim
                    ? new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        [IdentityStorageManifest.UserLookupKeyField] = userClaim.UserLookupKey ??
                            IdentityDocumentId.From(userClaim.TenantId, userClaim.UserId),
                        [IdentityStorageManifest.ClaimKeyField] = userClaim.ClaimKey
                    }
                    : new Dictionary<string, object?>(),
            IdentityStorageManifest.RoleClaimDocumentKind =>
                JsonSerializer.Deserialize<IdentityRoleClaimDocument>(canonicalJson, IdentityGroundworkJson.Options) is { } roleClaim
                    ? new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        [IdentityStorageManifest.RoleLookupKeyField] = roleClaim.RoleLookupKey ??
                            IdentityDocumentId.From(roleClaim.TenantId, roleClaim.RoleId)
                    }
                    : new Dictionary<string, object?>(),
            IdentityStorageManifest.ExternalLoginDocumentKind =>
                JsonSerializer.Deserialize<IdentityExternalLoginDocument>(canonicalJson, IdentityGroundworkJson.Options) is { } login
                    ? new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        [IdentityStorageManifest.UserLookupKeyField] = login.UserLookupKey ??
                            IdentityDocumentId.From(login.TenantId, login.UserId)
                    }
                    : new Dictionary<string, object?>(),
            IdentityStorageManifest.UserRoleDocumentKind =>
                JsonSerializer.Deserialize<IdentityUserRoleDocument>(canonicalJson, IdentityGroundworkJson.Options) is { } userRole
                    ? new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        [IdentityStorageManifest.UserLookupKeyField] = userRole.UserLookupKey ??
                            IdentityDocumentId.From(userRole.TenantId, userRole.UserId),
                        [IdentityStorageManifest.RoleLookupKeyField] = userRole.RoleLookupKey ??
                            IdentityDocumentId.From(userRole.TenantId, userRole.RoleId)
                    }
                    : new Dictionary<string, object?>(),
            _ => new Dictionary<string, object?>()
        };
    }

    public GroundworkIdentityUserStore UserStore() => new(
        Rows,
        _accessor,
        emailUniquenessPolicy: new IdentityEmailUniquenessPolicy(requireUniqueEmail));

    public GroundworkIdentityRoleStore RoleStore() => new(Rows, _accessor);

    public ElsaUserStore ElsaUserStore() => new(
        Rows,
        _accessor,
        emailUniquenessPolicy: new IdentityEmailUniquenessPolicy(requireUniqueEmail));

    public ElsaRoleStore ElsaRoleStore() => new(Rows, _accessor);

    public ElsaExternalIdentityStore ElsaExternalIdentityStore() => new(Rows, _accessor);

    public ElsaTenantMembershipStore ElsaTenantMembershipStore() => new(Rows, _accessor);

    private sealed class FixedAccessContextAccessor(PersistenceAccessContext current)
        : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = current;
    }
}
