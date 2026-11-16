using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore;

public sealed class IdentityProviderConfigurationSqliteDbContext(DbContextOptions<IdentityProviderConfigurationSqliteDbContext> options)
    : IdentityProviderConfigurationDbContext(options)
{
    public const string ExpectedProviderName = Elsa.Persistence.EntityFramework.EfProviderNames.Sqlite;
    protected override string ExpectedProviderNameValue => ExpectedProviderName;

    protected override void ConfigureProvider(ModelBuilder modelBuilder)
    {
        ConfigureText(modelBuilder, "TEXT");
        ApplyOrdinalCollation(modelBuilder, ExpectedProviderName);
    }

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
        ApplyOrdinalCollation(modelBuilder, ExpectedProviderName);
    }

    private static void ConfigureEntity<TEntity>(EntityTypeBuilder<TEntity> entity)
        where TEntity : ProviderConfigurationEntity =>
        entity.Property(record => record.SettingsJson).HasColumnType("nvarchar(max)");
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
        ApplyOrdinalCollation(modelBuilder, ExpectedProviderName);
    }

    private static void ConfigureEntity<TEntity>(EntityTypeBuilder<TEntity> entity)
        where TEntity : ProviderConfigurationEntity =>
        entity.Property(record => record.SettingsJson).HasColumnType("text");
}

public sealed class IdentityProviderConfigurationMySqlDbContext(DbContextOptions<IdentityProviderConfigurationMySqlDbContext> options)
    : IdentityProviderConfigurationDbContext(options)
{
    private const string CharacterSetAnnotation = "MySQL:Charset";

    public const string ExpectedProviderName = Elsa.Persistence.EntityFramework.EfProviderNames.MySql;
    public const string CharacterSet = "utf8mb4";
    protected override string ExpectedProviderNameValue => ExpectedProviderName;

    protected override void ConfigureProvider(ModelBuilder modelBuilder)
    {
        // The Oracle provider owns this annotation. Setting it by its stable metadata name keeps
        // the shipped module provider-neutral while ensuring generated DDL does not inherit an
        // incompatible database default charset.
        modelBuilder.Model.SetAnnotation(CharacterSetAnnotation, CharacterSet);
        ConfigureEntity(modelBuilder.Entity<TenantProviderConfigurationEntity>());
        ConfigureEntity(modelBuilder.Entity<GlobalProviderConfigurationEntity>());
        ApplyOrdinalCollation(modelBuilder, ExpectedProviderName);
    }

    private static void ConfigureEntity<TEntity>(EntityTypeBuilder<TEntity> entity)
        where TEntity : ProviderConfigurationEntity =>
        entity.Property(record => record.SettingsJson).HasColumnType("longtext");
}
