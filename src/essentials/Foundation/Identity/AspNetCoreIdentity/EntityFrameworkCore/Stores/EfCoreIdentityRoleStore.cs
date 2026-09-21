using System.Security.Claims;
using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.Core.Ownership;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Models;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Entities;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.Stores;

/// <summary>ASP.NET Core Identity role and role-claim store over the tenant-local EF authority.</summary>
public sealed class EfCoreIdentityRoleStore(
    IdentityIamDbContext db,
    IPersistenceAccessContextAccessor accessContextAccessor,
    EfIdentityAuthorityAggregateCoordinator? aggregateCoordinator = null,
    EfIdentityAuthorityRelationshipCoordinator? relationshipCoordinator = null) :
    IRoleStore<IdentityRole>,
    IRoleClaimStore<IdentityRole>
{
    private const int MaximumMaterializedRelationshipEntries = 512;
    private readonly EfIdentityAuthorityAggregateCoordinator aggregates =
        aggregateCoordinator ?? new(db, accessContextAccessor);
    private readonly EfIdentityAuthorityRelationshipCoordinator relationships =
        relationshipCoordinator ?? new(db, new EfIdentityAtomicWrite(db, accessContextAccessor: accessContextAccessor), accessContextAccessor);

    public void Dispose() { }

    public Task<string> GetRoleIdAsync(IdentityRole role, CancellationToken cancellationToken) => Task.FromResult(role.Id);
    public Task<string?> GetRoleNameAsync(IdentityRole role, CancellationToken cancellationToken) => Task.FromResult(role.Name);

    public Task SetRoleNameAsync(IdentityRole role, string? roleName, CancellationToken cancellationToken)
    {
        role.Name = roleName;
        return Task.CompletedTask;
    }

    public Task<string?> GetNormalizedRoleNameAsync(IdentityRole role, CancellationToken cancellationToken) => Task.FromResult(role.NormalizedName);

    public Task SetNormalizedRoleNameAsync(IdentityRole role, string? normalizedName, CancellationToken cancellationToken)
    {
        role.NormalizedName = normalizedName;
        return Task.CompletedTask;
    }

    public async Task<IdentityResult> CreateAsync(IdentityRole role, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(role);
        var tenantId = CurrentTenantId();
        EnsureTenant(tenantId);
        return await SaveFrameworkRoleAsync(role, tenantId, 0, createOnly: true, cancellationToken);
    }

    public async Task<IdentityResult> UpdateAsync(IdentityRole role, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(role);
        var tenantId = CurrentTenantId();
        EnsureTenant(tenantId);
        if (!IdentityEntityFrameworkRevisionSupport.TryGetRoleVersion(role.ConcurrencyStamp, tenantId, role.Id, out var revision))
            return ConcurrencyFailure();
        return await SaveFrameworkRoleAsync(role, tenantId, revision, createOnly: false, cancellationToken);
    }

    public async Task<IdentityResult> DeleteAsync(IdentityRole role, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(role);
        var tenantId = CurrentTenantId();
        EnsureTenant(tenantId);
        if (!IdentityEntityFrameworkRevisionSupport.TryGetRoleVersion(role.ConcurrencyStamp, tenantId, role.Id, out var revision))
            return ConcurrencyFailure();
        var result = await aggregates.DeleteRoleAsync(tenantId, role.Id, revision, cancellationToken);
        if (result.Succeeded)
            role.ConcurrencyStamp = null;
        return ToIdentityResult(result);
    }

    public async Task<IdentityRole?> FindByIdAsync(string roleId, CancellationToken cancellationToken)
    {
        var tenantId = CurrentTenantId();
        EnsureTenant(tenantId);
        var entity = await FindEntityAsync(tenantId, roleId, false, cancellationToken);
        return entity is null ? null : ToFrameworkRole(entity);
    }

    public async Task<IdentityRole?> FindByNameAsync(string normalizedRoleName, CancellationToken cancellationToken)
    {
        var tenantId = CurrentTenantId();
        EnsureTenant(tenantId);
        var entity = await IdentityEntityFrameworkAdapterSupport.ReadAsync(
            db,
            "Unable to find the ASP.NET Core Identity role by normalized name.",
            () => db.Roles.AsNoTracking().SingleOrDefaultAsync(x => x.TenantLookupKey == TenantKey(tenantId) && x.NormalizedNameKey == RoleKey(tenantId, normalizedRoleName), cancellationToken));
        return entity is null ? null : ToFrameworkRole(entity);
    }

    public async Task<IList<Claim>> GetClaimsAsync(IdentityRole role, CancellationToken cancellationToken = default)
    {
        var tenantId = CurrentTenantId();
        EnsureTenant(tenantId);
        var rows = await IdentityEntityFrameworkAdapterSupport.ReadAsync(
            db,
            "Unable to read ASP.NET Core Identity role claims.",
            () => db.RoleClaims.AsNoTracking().Where(x => x.TenantLookupKey == TenantKey(tenantId) && x.RoleLookupKey == RoleKey(tenantId, role.Id)).OrderBy(x => x.Id)
                .Take(MaximumMaterializedRelationshipEntries + 1).ToListAsync(cancellationToken));
        EnsureRelationshipMaterializationLimit(rows.Count, "role claims");
        return rows
            .Select(x => new Claim(x.ClaimType, x.ClaimValue ?? string.Empty)).ToList();
    }

    public async Task AddClaimAsync(IdentityRole role, Claim claim, CancellationToken cancellationToken = default)
    {
        var tenantId = CurrentTenantId();
        EnsureTenant(tenantId);
        var result = await relationships.SaveRoleClaimAsync(
            tenantId,
            role.Id,
            RequireRevision(role, tenantId),
            new RoleClaimEntity { TenantId = tenantId, RoleId = role.Id, ClaimType = claim.Type, ClaimValue = claim.Value, Revision = 1 },
            cancellationToken);
        ApplyRevisionStamp(role, tenantId, result);
        EnsureRelationshipSucceeded(result, "role-claim add");
    }

    public async Task ReplaceClaimAsync(IdentityRole role, Claim claim, Claim newClaim, CancellationToken cancellationToken = default)
    {
        var tenantId = CurrentTenantId();
        EnsureTenant(tenantId);
        var result = await relationships.ReplaceRoleClaimAsync(
            tenantId,
            role.Id,
            RequireRevision(role, tenantId),
            claim.Type,
            claim.Value,
            new RoleClaimEntity { TenantId = tenantId, RoleId = role.Id, ClaimType = newClaim.Type, ClaimValue = newClaim.Value, Revision = 1 },
            cancellationToken);
        ApplyRevisionStamp(role, tenantId, result);
        EnsureRelationshipSucceeded(result, "role-claim replacement");
    }

    public async Task RemoveClaimAsync(IdentityRole role, Claim claim, CancellationToken cancellationToken = default)
    {
        var tenantId = CurrentTenantId();
        EnsureTenant(tenantId);
        var result = await relationships.DeleteRoleClaimAsync(
            tenantId,
            role.Id,
            RequireRevision(role, tenantId),
            new RoleClaimEntity { TenantId = tenantId, RoleId = role.Id, ClaimType = claim.Type, ClaimValue = claim.Value },
            cancellationToken);
        ApplyRevisionStamp(role, tenantId, result);
        EnsureRelationshipSucceeded(result, "role-claim remove");
    }

    private async Task<IdentityResult> SaveFrameworkRoleAsync(IdentityRole role, string tenantId, long expectedRevision, bool createOnly, CancellationToken cancellationToken)
    {
        var existing = await FindEntityAsync(tenantId, role.Id, false, cancellationToken);
        var record = (existing is null
                ? new RoleRecord(role.Id, tenantId, role.Name ?? string.Empty, null,
                    new HashSet<string>(StringComparer.Ordinal), false)
                : ToRoleRecord(existing)) with
        {
            Name = role.Name ?? string.Empty
        };
        var result = await aggregates.SaveRoleAsync(
            record,
            createOnly ? 0 : expectedRevision,
            cancellationToken,
            Guid.NewGuid().ToString("N"),
            entity => ApplyFrameworkState(entity, role));
        if (!result.WriteResult.Succeeded)
            return ToIdentityResult(role, result);
        role.ConcurrencyStamp = IdentityEntityFrameworkRevisionSupport.FromRole(
            tenantId,
            role.Id,
            result.WriteResult.Version ?? throw new InvalidOperationException("The Identity role aggregate did not return a revision."));
        return IdentityResult.Success;
    }

    private async Task<RoleEntity?> FindEntityAsync(string tenantId, string roleId, bool forWrite, CancellationToken cancellationToken)
    {
        return await IdentityEntityFrameworkAdapterSupport.ReadAsync(
            db,
            "Unable to find the ASP.NET Core Identity role.",
            async () =>
            {
                var query = db.Roles.Where(x => x.Id == IdentityEntityFrameworkAdapterSupport.RecordId(tenantId, roleId) && x.TenantLookupKey == TenantKey(tenantId));
                return forWrite ? await query.SingleOrDefaultAsync(cancellationToken) : await query.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
            });
    }

    private static IdentityRole ToFrameworkRole(RoleEntity entity) => new() { Id = entity.RoleId, Name = entity.Name, NormalizedName = entity.NormalizedName, ConcurrencyStamp = IdentityEntityFrameworkRevisionSupport.FromRole(entity.TenantId, entity.RoleId, entity.Revision) };
    private static void ApplyFrameworkState(RoleEntity entity, IdentityRole role)
    {
        entity.NormalizedName = role.NormalizedName ?? (string.IsNullOrWhiteSpace(role.Name) ? null : IdentityEntityFrameworkAdapterSupport.Normalize(role.Name));
        entity.ConcurrencyStamp = IdentityEntityFrameworkRevisionSupport.FromRole(entity.TenantId, entity.RoleId, entity.Revision);
    }

    private static RoleRecord ToRoleRecord(RoleEntity entity) => new(entity.RoleId, entity.TenantId, entity.Name, entity.Description, IdentityEntityFrameworkAdapterSupport.DeserializeSet(entity.PermissionsJson), entity.System);
    private static void EnsureRelationshipMaterializationLimit(int count, string subject)
    {
        if (count > MaximumMaterializedRelationshipEntries)
            throw new InvalidOperationException($"The EF Identity {subject} exceed the {MaximumMaterializedRelationshipEntries}-entry materialization limit.");
    }

    private void EnsureTenant(string tenantId) => accessContextAccessor.Current.EnsureTenantScope(tenantId);
    private string CurrentTenantId() => accessContextAccessor.Current.Scope?.Value ?? PersistenceScope.DefaultValue;
    private static string TenantKey(string tenantId) => IdentityEntityFrameworkAdapterSupport.TenantLookup(tenantId);
    private static string RoleKey(string tenantId, string? value) => IdentityEntityFrameworkAdapterSupport.Lookup(tenantId, value);
    private static IdentityResult ConcurrencyFailure() => IdentityResult.Failed(new IdentityErrorDescriber().ConcurrencyFailure());
    private static long RequireRevision(IdentityRole role, string tenantId) =>
        IdentityEntityFrameworkRevisionSupport.TryGetRoleVersion(role.ConcurrencyStamp, tenantId, role.Id, out var version)
            ? version
            : throw new InvalidOperationException("The requested role has no valid EF revision stamp.");

    private static void ApplyRevisionStamp(IdentityRole role, string tenantId, EfIdentityWriteResult result)
    {
        if (result.Succeeded && result.Version is { } version)
            role.ConcurrencyStamp = IdentityEntityFrameworkRevisionSupport.FromRole(tenantId, role.Id, version);
    }

    private static void EnsureRelationshipSucceeded(EfIdentityWriteResult result, string operation)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException($"The EF Identity {operation} returned {result.Status}: {result.Message}");
    }

    private static IdentityResult ToIdentityResult(EfIdentityWriteResult result) => result.Status switch
    {
        EfIdentityWriteStatus.Inserted or EfIdentityWriteStatus.Updated or EfIdentityWriteStatus.Deleted => IdentityResult.Success,
        EfIdentityWriteStatus.Conflict => ConcurrencyFailure(),
        _ => IdentityResult.Failed(new IdentityError { Code = $"EfIdentity{result.Status}", Description = result.Message })
    };

    private static IdentityResult ToIdentityResult(IdentityRole role, EfIdentityAuthorityWriteResult result) => result.Conflict switch
    {
        EfIdentityAuthorityConflict.RoleName => IdentityResult.Failed(new IdentityErrorDescriber().DuplicateRoleName(role.NormalizedName ?? role.Name ?? role.Id)),
        _ => ToIdentityResult(result.WriteResult)
    };
}
