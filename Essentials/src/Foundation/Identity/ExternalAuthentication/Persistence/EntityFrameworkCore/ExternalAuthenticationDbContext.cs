using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Foundation.Identity.ExternalAuthentication.Persistence.EntityFrameworkCore;

public abstract class ExternalAuthenticationDbContext(DbContextOptions options) : DbContext(options)
{
    internal DbSet<AuthenticationArtifactEntity> Artifacts => Set<AuthenticationArtifactEntity>();
    protected abstract string ExpectedProvider { get; }
    public void EnsureProviderBinding() => EfProviderGuard.Ensure(this, ExpectedProvider);
    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasElsaDefaultSchema(this);
        var row = builder.Entity<AuthenticationArtifactEntity>();
        row.ToTable(ExternalAuthenticationEfModule.TableName);
        row.HasKey(value => value.Id);
        row.Property(value => value.Id).HasMaxLength(64).IsUnicode(false).ValueGeneratedNever();
        row.Property(value => value.TenantKey).HasMaxLength(64).IsUnicode(false).IsRequired();
        row.Property(value => value.BrowserBinding).HasMaxLength(64).IsUnicode(false).IsRequired();
        row.Property(value => value.AdministratorBinding).HasMaxLength(64).IsUnicode(false);
        row.Property(value => value.Version).HasMaxLength(64).IsUnicode(false).IsRequired();
        row.Property(value => value.ProtectedPayload).IsRequired();
        row.Property(value => value.SchemaVersion).HasMaxLength(32).IsRequired();
        row.HasIndex(value => new { value.TenantKey, value.ExpiresAtMilliseconds }).HasDatabaseName("ix_identity_auth_artifacts_expiry");
        builder.MapSchemaFinalization(ExternalAuthenticationEfModule.HistoryModuleName);
        ConfigureProvider(builder);
        builder.IndexSchemaVersionStamps();
    }
    protected abstract void ConfigureProvider(ModelBuilder builder);
    protected static void Collation(ModelBuilder builder, string provider) => EfOrdinalCollation.Apply(builder, provider,
        ["Id", "TenantKey", "BrowserBinding", "AdministratorBinding", "Version"]);
}

internal sealed class AuthenticationArtifactEntity
{
    public string Id { get; set; } = "";
    public string TenantKey { get; set; } = "";
    public int Purpose { get; set; }
    public string BrowserBinding { get; set; } = "";
    public string? AdministratorBinding { get; set; }
    public long CreatedAtMilliseconds { get; set; }
    public long ExpiresAtMilliseconds { get; set; }
    public string ProtectedPayload { get; set; } = "";
    public string Version { get; set; } = "";
    public string SchemaVersion { get; set; } = "";
}
