using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Decisions.Persistence.EntityFrameworkCore;

/// <summary>Optional Decisions-owned context. Applications can install the model in their own context.</summary>
internal abstract class DecisionsDbContext : DbContext
{
    protected DecisionsDbContext(DbContextOptions options) : base(options) { }
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasElsaDefaultSchema(this);
        modelBuilder.AddDecisionsModel(Database.ProviderName ?? "");
        modelBuilder.MapSchemaFinalization(DecisionsEfModule.HistoryModuleName);
        ConfigureProvider(modelBuilder);
        modelBuilder.IndexSchemaVersionStamps();
        EfOrdinalCollation.Apply(modelBuilder, Database.ProviderName!);
    }
    protected abstract void ConfigureProvider(ModelBuilder modelBuilder);
}

/// <summary>Installs only the Decisions-owned records into a consumer-owned context.</summary>
public static class DecisionsModelBuilderExtensions
{
    public static ModelBuilder AddDecisionsModel(this ModelBuilder modelBuilder, string providerName)
    {
        var entity = modelBuilder.Entity<DecisionRecord>();
        entity.ToTable("elsa_decisions");
        entity.HasKey(row => new { row.TenantKey, row.Kind, row.Id });
        entity.Property(row => row.TenantKey).HasMaxLength(64);
        entity.Property(row => row.Kind).HasMaxLength(24);
        entity.Property(row => row.Id).HasMaxLength(128);
        entity.Property(row => row.OwnerId).HasMaxLength(128);
        entity.Property(row => row.Revision).HasMaxLength(64).IsConcurrencyToken();
        entity.Property(row => row.Json).IsRequired();
        entity.Property(row => row.SchemaVersion).HasMaxLength(32).IsRequired().IsConcurrencyToken();
        entity.HasIndex(row => row.SchemaVersion);
        entity.HasIndex(row => new { row.TenantKey, row.Kind, row.OwnerId });
        entity.Property(row => row.Json).HasColumnType(providerName switch
        {
            EfProviderNames.Sqlite => "TEXT", EfProviderNames.SqlServer => "nvarchar(max)",
            EfProviderNames.PostgreSql => "jsonb", EfProviderNames.MySql => "json",
            _ => throw new ArgumentOutOfRangeException(nameof(providerName))
        });
        EfOrdinalCollation.ApplyToEntities(modelBuilder, providerName, new[] { typeof(DecisionRecord) });
        return modelBuilder;
    }
}

internal sealed class DecisionRecord
{
    public string TenantKey { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Id { get; set; } = "";
    public string OwnerId { get; set; } = "";
    public string Revision { get; set; } = "";
    public string Json { get; set; } = "";
    public string SchemaVersion { get; set; } = DecisionsEfModule.SchemaVersion;
}
