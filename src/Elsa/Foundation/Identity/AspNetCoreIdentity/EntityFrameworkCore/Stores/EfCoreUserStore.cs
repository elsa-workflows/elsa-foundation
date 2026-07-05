using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.Stores;

/// <summary>
/// Projects Elsa's tenant-scoped <see cref="IUserStore"/> contract over the ASP.NET Core Identity user,
/// user-role, and user-claim tables in <see cref="ApplicationIdentityDbContext"/>. Role memberships map to
/// <see cref="UserRecord.RoleIds"/> (Identity role ids) and permission claims map to
/// <see cref="UserRecord.DirectPermissions"/>.
/// </summary>
public sealed class EfCoreUserStore(ApplicationIdentityDbContext db) : IUserStore
{
    public async ValueTask<UserRecord?> FindAsync(string tenantId, string userId, CancellationToken cancellationToken = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == userId, cancellationToken);
        return user is null ? null : await MapAsync(user, cancellationToken);
    }

    public async ValueTask<UserRecord?> FindByEmailAsync(string tenantId, string email, CancellationToken cancellationToken = default)
    {
        var normalized = email.ToUpperInvariant();
        var user = await db.Users.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.NormalizedEmail == normalized, cancellationToken);
        return user is null ? null : await MapAsync(user, cancellationToken);
    }

    public async ValueTask SaveAsync(UserRecord user, CancellationToken cancellationToken = default)
    {
        var entity = await db.Users.FirstOrDefaultAsync(x => x.Id == user.Id, cancellationToken);
        if (entity is null)
        {
            entity = new AspNetCoreIdentityUser { Id = user.Id };
            db.Users.Add(entity);
        }

        entity.TenantId = user.TenantId;
        entity.UserName = user.UserName;
        entity.NormalizedUserName = user.UserName.ToUpperInvariant();
        entity.Email = user.Email;
        entity.NormalizedEmail = user.Email?.ToUpperInvariant();
        entity.DisplayName = user.DisplayName;

        await SaveDirectPermissionsAsync(user, cancellationToken);
        await SaveRoleMembershipsAsync(user, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task SaveDirectPermissionsAsync(UserRecord user, CancellationToken cancellationToken)
    {
        var existing = await db.UserClaims
            .Where(x => x.UserId == user.Id && x.ClaimType == IdentityStoreClaimTypes.Permission)
            .ToListAsync(cancellationToken);

        db.UserClaims.RemoveRange(existing);

        foreach (var permission in user.DirectPermissions)
            db.UserClaims.Add(new IdentityUserClaim<string> { UserId = user.Id, ClaimType = IdentityStoreClaimTypes.Permission, ClaimValue = permission });
    }

    private async Task SaveRoleMembershipsAsync(UserRecord user, CancellationToken cancellationToken)
    {
        var existing = await db.UserRoles.Where(x => x.UserId == user.Id).ToListAsync(cancellationToken);
        db.UserRoles.RemoveRange(existing);

        foreach (var roleId in user.RoleIds)
            db.UserRoles.Add(new IdentityUserRole<string> { UserId = user.Id, RoleId = roleId });
    }

    private async Task<UserRecord> MapAsync(AspNetCoreIdentityUser user, CancellationToken cancellationToken)
    {
        var roleIds = await db.UserRoles.Where(x => x.UserId == user.Id).Select(x => x.RoleId).ToListAsync(cancellationToken);
        var permissions = await db.UserClaims
            .Where(x => x.UserId == user.Id && x.ClaimType == IdentityStoreClaimTypes.Permission)
            .Select(x => x.ClaimValue!)
            .ToListAsync(cancellationToken);

        return new UserRecord(
            user.Id,
            user.TenantId,
            user.UserName ?? user.Id,
            user.Email,
            user.DisplayName,
            UserStatus.Active,
            ResourceOwnership.Foundation,
            roleIds.ToHashSet(StringComparer.OrdinalIgnoreCase),
            permissions.ToHashSet(StringComparer.OrdinalIgnoreCase));
    }
}
