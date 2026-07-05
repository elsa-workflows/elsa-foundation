using Elsa.Foundation.Identity.Abstractions.Iam;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.Stores;

/// <summary>
/// Projects Elsa's tenant-scoped <see cref="IRoleStore"/> contract over the ASP.NET Core Identity role and
/// role-claim tables. Roles are not natively tenant-scoped in ASP.NET Core Identity, so the tenant is
/// encoded in the role name as <c>{tenantId}:{name}</c>; <see cref="RoleRecord.Name"/> reports the bare
/// name and <see cref="RoleRecord.Permissions"/> is read from role claims.
/// </summary>
public sealed class EfCoreRoleStore(ApplicationIdentityDbContext db) : IRoleStore
{
    public async ValueTask<RoleRecord?> FindAsync(string tenantId, string roleId, CancellationToken cancellationToken = default)
    {
        var role = await db.Roles.FirstOrDefaultAsync(x => x.Id == roleId, cancellationToken);
        if (role is null || TenantOf(role.Name) != tenantId)
            return null;

        return await MapAsync(tenantId, role, cancellationToken);
    }

    public async ValueTask<IReadOnlyList<RoleRecord>> ListAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        var prefix = $"{tenantId}:";
        var roles = await db.Roles.Where(x => x.Name!.StartsWith(prefix)).ToListAsync(cancellationToken);

        var records = new List<RoleRecord>(roles.Count);
        foreach (var role in roles)
            records.Add(await MapAsync(tenantId, role, cancellationToken));

        return records;
    }

    public async ValueTask SaveAsync(RoleRecord role, CancellationToken cancellationToken = default)
    {
        var qualifiedName = QualifiedName(role.TenantId, role.Name);
        var entity = await db.Roles.FirstOrDefaultAsync(x => x.Id == role.Id, cancellationToken);
        if (entity is null)
        {
            entity = new IdentityRole { Id = role.Id };
            db.Roles.Add(entity);
        }

        entity.Name = qualifiedName;
        entity.NormalizedName = qualifiedName.ToUpperInvariant();

        var existing = await db.RoleClaims
            .Where(x => x.RoleId == role.Id && x.ClaimType == IdentityStoreClaimTypes.Permission)
            .ToListAsync(cancellationToken);

        db.RoleClaims.RemoveRange(existing);

        foreach (var permission in role.Permissions)
            db.RoleClaims.Add(new IdentityRoleClaim<string> { RoleId = role.Id, ClaimType = IdentityStoreClaimTypes.Permission, ClaimValue = permission });

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<RoleRecord> MapAsync(string tenantId, IdentityRole role, CancellationToken cancellationToken)
    {
        var permissions = await db.RoleClaims
            .Where(x => x.RoleId == role.Id && x.ClaimType == IdentityStoreClaimTypes.Permission)
            .Select(x => x.ClaimValue!)
            .ToListAsync(cancellationToken);

        return new RoleRecord(
            role.Id,
            tenantId,
            BareName(role.Name),
            null,
            permissions.ToHashSet(StringComparer.OrdinalIgnoreCase),
            System: false);
    }

    private static string QualifiedName(string tenantId, string name) => $"{tenantId}:{name}";

    private static string? TenantOf(string? qualifiedName)
    {
        if (string.IsNullOrEmpty(qualifiedName))
            return null;

        var separator = qualifiedName.IndexOf(':');
        return separator < 0 ? null : qualifiedName[..separator];
    }

    private static string BareName(string? qualifiedName)
    {
        if (string.IsNullOrEmpty(qualifiedName))
            return string.Empty;

        var separator = qualifiedName.IndexOf(':');
        return separator < 0 ? qualifiedName : qualifiedName[(separator + 1)..];
    }
}
