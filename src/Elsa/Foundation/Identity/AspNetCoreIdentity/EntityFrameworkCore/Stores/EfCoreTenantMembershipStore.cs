using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.Stores;

/// <summary>
/// EF Core-backed <see cref="ITenantMembershipStore"/> over the dedicated <see cref="TenantMembershipEntity"/>
/// table. Role ids and direct permissions are stored as newline-separated values.
/// </summary>
public sealed class EfCoreTenantMembershipStore(ApplicationIdentityDbContext db) : ITenantMembershipStore
{
    private static readonly char[] Separator = ['\n'];

    public async ValueTask<TenantMembershipRecord?> FindAsync(string tenantId, string userId, CancellationToken cancellationToken = default)
    {
        var entity = await db.TenantMemberships.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.UserId == userId, cancellationToken);
        return entity is null ? null : Map(entity);
    }

    public async ValueTask SaveAsync(TenantMembershipRecord membership, CancellationToken cancellationToken = default)
    {
        var entity = await db.TenantMemberships.FirstOrDefaultAsync(x => x.TenantId == membership.TenantId && x.UserId == membership.UserId, cancellationToken);
        if (entity is null)
        {
            entity = new TenantMembershipEntity { Id = Guid.NewGuid().ToString("n"), TenantId = membership.TenantId, UserId = membership.UserId };
            db.TenantMemberships.Add(entity);
        }

        entity.Status = (int)membership.Status;
        entity.RoleIds = Join(membership.RoleIds);
        entity.DirectPermissions = Join(membership.DirectPermissions);

        await db.SaveChangesAsync(cancellationToken);
    }

    private static TenantMembershipRecord Map(TenantMembershipEntity entity) =>
        new(
            entity.TenantId,
            entity.UserId,
            (TenantMembershipStatus)entity.Status,
            Split(entity.RoleIds),
            Split(entity.DirectPermissions));

    private static string Join(IEnumerable<string> values) => string.Join('\n', values);

    private static IReadOnlySet<string> Split(string value) =>
        value.Split(Separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
