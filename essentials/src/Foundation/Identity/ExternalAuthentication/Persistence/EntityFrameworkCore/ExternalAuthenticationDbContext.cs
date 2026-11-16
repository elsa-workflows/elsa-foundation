using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Foundation.Identity.ExternalAuthentication.Persistence.EntityFrameworkCore;

public abstract class ExternalAuthenticationDbContext(DbContextOptions options) : DbContext(options)
{
    internal DbSet<AuthenticationArtifactEntity> Artifacts => Set<AuthenticationArtifactEntity>();
    internal DbSet<AuthenticationSessionEntity> Sessions => Set<AuthenticationSessionEntity>();
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
        var session = builder.Entity<AuthenticationSessionEntity>();
        session.ToTable("identity_authentication_sessions");
        session.HasKey(value => new { value.TenantKey, value.Id });
        session.Property(value => value.Id).HasMaxLength(64).IsUnicode(false).ValueGeneratedNever();
        session.Property(value => value.TenantKey).HasMaxLength(64).IsUnicode(false).IsRequired();
        session.Property(value => value.UserKey).HasMaxLength(64).IsUnicode(false).IsRequired();
        session.Property(value => value.Version).HasMaxLength(64).IsUnicode(false).IsRequired();
        session.Property(value => value.ProtectedPayload).IsRequired();
        session.Property(value => value.SchemaVersion).HasMaxLength(32).IsRequired();
        session.HasIndex(value => new { value.TenantKey, value.UserKey, value.CreatedAtMilliseconds, value.Id }).HasDatabaseName("ix_identity_auth_sessions_user");
        session.HasIndex(value => new { value.TenantKey, value.ExpiresAtMilliseconds }).HasDatabaseName("ix_identity_auth_sessions_expiry");
        builder.MapSchemaFinalization(ExternalAuthenticationEfModule.HistoryModuleName);
        ConfigureProvider(builder);
        builder.IndexSchemaVersionStamps();
    }
    protected abstract void ConfigureProvider(ModelBuilder builder);
    protected static void Collation(ModelBuilder builder, string provider) => EfOrdinalCollation.Apply(builder, provider,
        ["Id", "TenantKey", "UserKey", "BrowserBinding", "AdministratorBinding", "Version"]);
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

internal sealed class AuthenticationSessionEntity
{
    public string Id { get; set; } = "";
    public string TenantKey { get; set; } = "";
    public string UserKey { get; set; } = "";
    public long CreatedAtMilliseconds { get; set; }
    public long ExpiresAtMilliseconds { get; set; }
    public long? RevokedAtMilliseconds { get; set; }
    public string ProtectedPayload { get; set; } = "";
    public string Version { get; set; } = "";
    public string SchemaVersion { get; set; } = "";
}
