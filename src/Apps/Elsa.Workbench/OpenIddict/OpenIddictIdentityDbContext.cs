using Microsoft.EntityFrameworkCore;

namespace Elsa.Workbench.OpenIddict;

/// <summary>
/// Companion EF Core context holding OpenIddict's entity sets (applications, authorizations, scopes,
/// tokens). By default it targets the Workbench persistence database, with its own model and migrations history.
/// </summary>
/// <remarks>
/// A companion context was chosen over adding OpenIddict's entity sets to an Identity model deliberately:
/// the two features remain independently toggleable in shells.json, and separate contexts provide separate
/// model snapshots and migration histories. With EF Core's pending-model-changes check, separate contexts
/// let each feature own and evolve its schema without knowing about the other — no backwards dependency,
/// no migration drift.
/// </remarks>
public class OpenIddictIdentityDbContext(DbContextOptions<OpenIddictIdentityDbContext> options) : DbContext(options)
{
    /// <summary>The default schema used to group the vendor tables.</summary>
    public const string Schema = "Identity";

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema(Schema);
        builder.UseOpenIddict();
        base.OnModelCreating(builder);
    }
}
