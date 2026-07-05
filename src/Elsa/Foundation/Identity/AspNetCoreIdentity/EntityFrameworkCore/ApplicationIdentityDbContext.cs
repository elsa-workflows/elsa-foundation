using Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.Entities;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore;

/// <summary>
/// The EF Core context backing the ASP.NET Core Identity substrate. It derives from
/// <see cref="IdentityDbContext{TUser}"/> so the framework's user/role/claim stores work unchanged, while
/// Elsa's own IAM store contracts (<c>IUserStore</c>, <c>IRoleStore</c>, …) are projected over the same
/// tables by <c>EfCore*Store</c> adapters.
/// </summary>
/// <remarks>
/// Workstream B (OpenIddict) is expected to add its own entity sets to this same context via
/// <c>options.UseEntityFrameworkCore().UseDbContext&lt;ApplicationIdentityDbContext&gt;()</c> and
/// <see cref="OnModelCreating"/> is left overridable for that. A partial-friendly design was avoided so the
/// downstream owner can extend by configuration rather than editing this type.
/// </remarks>
public class ApplicationIdentityDbContext(DbContextOptions<ApplicationIdentityDbContext> options)
    : IdentityDbContext<AspNetCoreIdentityUser>(options)
{
    /// <summary>The default schema for identity tables. Public so downstream migrations can align.</summary>
    public const string Schema = "Identity";

    /// <summary>Tenant-membership records backing Elsa's <c>ITenantMembershipStore</c>.</summary>
    public DbSet<TenantMembershipEntity> TenantMemberships => Set<TenantMembershipEntity>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema(Schema);
        base.OnModelCreating(builder);

        builder.Entity<AspNetCoreIdentityUser>(user =>
        {
            user.Property(x => x.TenantId).HasMaxLength(256);
            user.Property(x => x.DisplayName).HasMaxLength(256);
            user.HasIndex(x => new { x.TenantId, x.NormalizedUserName });
            user.HasIndex(x => new { x.TenantId, x.NormalizedEmail });
        });

        builder.Entity<IdentityRole>(role =>
            role.HasIndex(x => x.NormalizedName));

        builder.Entity<TenantMembershipEntity>(membership =>
        {
            membership.ToTable("TenantMemberships");
            membership.HasKey(x => x.Id);
            membership.Property(x => x.TenantId).HasMaxLength(256);
            membership.Property(x => x.UserId).HasMaxLength(256);
            membership.HasIndex(x => new { x.TenantId, x.UserId }).IsUnique();
        });
    }
}
