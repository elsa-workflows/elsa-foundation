using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore;

public sealed class IdentityProviderConfigurationSqliteDbContext(DbContextOptions<IdentityProviderConfigurationSqliteDbContext> options)
    : IdentityProviderConfigurationDbContext(options)
{
    public const string ExpectedProviderName = Elsa.Persistence.EntityFramework.EfProviderNames.Sqlite;
    protected override string ExpectedProviderNameValue => ExpectedProviderName;

    protected override void ConfigureProvider(ModelBuilder modelBuilder) => ConfigureText(modelBuilder, "TEXT");

    private static void ConfigureText(ModelBuilder modelBuilder, string type)
    {
        ConfigureText(modelBuilder.Entity<TenantProviderConfigurationEntity>(), type);
        ConfigureText(modelBuilder.Entity<GlobalProviderConfigurationEntity>(), type);
    }

    private static void ConfigureText<TEntity>(EntityTypeBuilder<TEntity> entity, string type)
        where TEntity : ProviderConfigurationEntity
    {
        entity.Property(record => record.SettingsJson).HasColumnType(type);
        entity.Property(record => record.TenantId).HasColumnType(type);
        entity.Property(record => record.TenantLookupKey).HasColumnType(type);
        entity.Property(record => record.Provider).HasColumnType(type);
        entity.Property(record => record.ProviderLookupKey).HasColumnType(type);
    }
}

public sealed class IdentityProviderConfigurationSqlServerDbContext(DbContextOptions<IdentityProviderConfigurationSqlServerDbContext> options)
    : IdentityProviderConfigurationDbContext(options)
{
    public const string ExpectedProviderName = Elsa.Persistence.EntityFramework.EfProviderNames.SqlServer;
    protected override string ExpectedProviderNameValue => ExpectedProviderName;

    protected override void ConfigureProvider(ModelBuilder modelBuilder)
    {
        ConfigureEntity(modelBuilder.Entity<TenantProviderConfigurationEntity>());
        ConfigureEntity(modelBuilder.Entity<GlobalProviderConfigurationEntity>());
    }

    private static void ConfigureEntity<TEntity>(EntityTypeBuilder<TEntity> entity)
        where TEntity : ProviderConfigurationEntity
    {
        entity.Property(record => record.SettingsJson).HasColumnType("nvarchar(max)");
        entity.Property(record => record.TenantId).UseCollation("Latin1_General_BIN2");
        entity.Property(record => record.TenantLookupKey).UseCollation("Latin1_General_BIN2");
        entity.Property(record => record.Provider).UseCollation("Latin1_General_BIN2");
        entity.Property(record => record.ProviderLookupKey).UseCollation("Latin1_General_BIN2");
    }
}

public sealed class IdentityProviderConfigurationPostgreSqlDbContext(DbContextOptions<IdentityProviderConfigurationPostgreSqlDbContext> options)
    : IdentityProviderConfigurationDbContext(options)
{
    public const string ExpectedProviderName = Elsa.Persistence.EntityFramework.EfProviderNames.PostgreSql;
    protected override string ExpectedProviderNameValue => ExpectedProviderName;

    protected override void ConfigureProvider(ModelBuilder modelBuilder)
    {
        modelBuilder.Model.RemoveAnnotation("Npgsql:ValueGenerationStrategy");
        ConfigureEntity(modelBuilder.Entity<TenantProviderConfigurationEntity>());
        ConfigureEntity(modelBuilder.Entity<GlobalProviderConfigurationEntity>());
    }

    private static void ConfigureEntity<TEntity>(EntityTypeBuilder<TEntity> entity)
        where TEntity : ProviderConfigurationEntity
    {
        entity.Property(record => record.SettingsJson).HasColumnType("text");
        entity.Property(record => record.TenantLookupKey).UseCollation("C");
        entity.Property(record => record.ProviderLookupKey).UseCollation("C");
    }
}

public sealed class IdentityProviderConfigurationMySqlDbContext(DbContextOptions<IdentityProviderConfigurationMySqlDbContext> options)
    : IdentityProviderConfigurationDbContext(options)
{
    private const string CharacterSetAnnotation = "MySQL:Charset";
    private const string CollationAnnotation = "MySQL:Collation";

    public const string ExpectedProviderName = Elsa.Persistence.EntityFramework.EfProviderNames.MySql;
    public const string CharacterSet = "utf8mb4";
    public const string Collation = "utf8mb4_0900_bin";
    protected override string ExpectedProviderNameValue => ExpectedProviderName;

    protected override void ConfigureProvider(ModelBuilder modelBuilder)
    {
        // The Oracle provider owns this annotation. Setting it by its stable metadata name keeps
        // the shipped module provider-neutral while ensuring generated DDL does not inherit an
        // incompatible database default charset.
        modelBuilder.Model.SetAnnotation(CharacterSetAnnotation, CharacterSet);
        modelBuilder.UseCollation(Collation);
        ConfigureEntity(modelBuilder.Entity<TenantProviderConfigurationEntity>());
        ConfigureEntity(modelBuilder.Entity<GlobalProviderConfigurationEntity>());
    }

    private static void ConfigureEntity<TEntity>(EntityTypeBuilder<TEntity> entity)
        where TEntity : ProviderConfigurationEntity
    {
        entity.Metadata.SetAnnotation(CollationAnnotation, Collation);
        entity.Property(record => record.SettingsJson).HasColumnType("longtext");
        // NO PAD keeps trailing spaces distinct in canonical projections. IDs are still the
        // authoritative binary identity, so this collation is a defensive provider projection.
        entity.Property(record => record.TenantLookupKey).Metadata.SetAnnotation(CollationAnnotation, Collation);
        entity.Property(record => record.ProviderLookupKey).Metadata.SetAnnotation(CollationAnnotation, Collation);
    }
}
