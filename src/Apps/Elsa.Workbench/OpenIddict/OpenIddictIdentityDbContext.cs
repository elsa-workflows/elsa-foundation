using Microsoft.EntityFrameworkCore;

namespace Elsa.Workbench.OpenIddict;

/// <summary>
/// Companion EF Core context holding OpenIddict's entity sets (applications, authorizations, scopes,
/// tokens). By default it targets the same database file as the ASP.NET Core Identity module's
/// <c>ApplicationIdentityDbContext</c>, but with its own model and migrations history.
/// </summary>
/// <remarks>
/// A companion context was chosen over adding OpenIddict's entity sets to the shared
/// <c>ApplicationIdentityDbContext</c> deliberately: both features must stay independently toggleable in
/// shells.json, and a shared context means a single model snapshot + migration chain. With EF Core's
/// pending-model-changes check, running the identity feature <em>without</em> OpenIddict against a snapshot
/// that includes OpenIddict entities would fault <c>MigrateAsync</c> (and vice versa). Separate contexts with
/// separate history tables (<see cref="OpenIddictEntityFrameworkCoreDefaults.MigrationsHistoryTable"/>) let each feature
/// own and evolve its schema without knowing about the other — no backwards dependency, no migration drift.
/// </remarks>
public class OpenIddictIdentityDbContext(DbContextOptions<OpenIddictIdentityDbContext> options) : DbContext(options)
{
    /// <summary>The default schema, aligned with the identity context so the tables group together.</summary>
    public const string Schema = "Identity";

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema(Schema);
        builder.UseOpenIddict();
        base.OnModelCreating(builder);
    }
}
