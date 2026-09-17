using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Persistence.EntityFramework.Tests;

public sealed class EfRelationalProviderBindingTests
{
    [Theory]
    [InlineData("Sqlite", "sqlite")]
    [InlineData("SQLServer", "sqlserver")]
    [InlineData("PostgreSql", "postgresql")]
    [InlineData("Npgsql.EntityFrameworkCore.PostgreSQL", "postgresql")]
    [InlineData("MySql", "mysql")]
    [InlineData("MySql.EntityFrameworkCore", "mysql")]
    public void Normalize_accepts_the_default_pack_aliases(string input, string expected)
    {
        Assert.Equal(expected, EfRelationalProviderBinding.Normalize(input));
    }

    [Fact]
    public void MySql_alias_resolves_the_Oracle_provider_name()
    {
        Assert.Equal("MySql.EntityFrameworkCore", EfRelationalProviderBinding.ExpectedProviderName("mysql"));
    }

    [Fact]
    public void Normalize_rejects_an_unknown_provider()
    {
        Assert.Equal("oracle", EfRelationalProviderBinding.Normalize("oracle"));
        Assert.Throws<ArgumentException>(() => EfRelationalProviderBinding.ExpectedProviderName("oracle"));
        Assert.Throws<ArgumentException>(() =>
            EfRelationalProviderBinding.Use(new DbContextOptionsBuilder(), "oracle", "x", "__EFMigrationsHistory_X"));
    }

    [Theory]
    [InlineData("Sqlite", "sqlite")]
    [InlineData("mssql", "sqlserver")]
    [InlineData("Postgres", "postgresql")]
    [InlineData("MySql.EntityFrameworkCore", "mysql")]
    public void Select_picks_the_value_registered_for_every_alias_of_a_provider(string provider, string expected)
    {
        Assert.Equal(expected, EfRelationalProviderBinding.Select(provider, "Test", "sqlite", "sqlserver", "postgresql", "mysql"));
    }

    [Fact]
    public void Select_refuses_an_unknown_provider_naming_the_module()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            EfRelationalProviderBinding.Select("oracle", "Test", 1, 2, 3, 4));

        Assert.StartsWith("Unknown Test EF provider 'oracle'. Expected Sqlite, SqlServer, PostgreSql, or MySql.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UseSqlite_sets_the_provider_history_table_and_migrations_assembly()
    {
        var builder = new DbContextOptionsBuilder();
        EfRelationalProviderBinding.UseSqlite(
            builder,
            "Data Source=:memory:",
            "__EFMigrationsHistory_ElsaSecrets",
            "Elsa.Secrets.Persistence.EntityFrameworkCore");

        var relational = builder.Options.Extensions.OfType<RelationalOptionsExtension>().SingleOrDefault()
                         ?? throw new InvalidOperationException("Sqlite binding did not install a relational extension.");
        Assert.Equal("__EFMigrationsHistory_ElsaSecrets", relational.MigrationsHistoryTableName);
        Assert.Equal("Elsa.Secrets.Persistence.EntityFrameworkCore", relational.MigrationsAssembly);
        Assert.Contains("Sqlite", relational.GetType().Name, StringComparison.Ordinal);
    }

    [Fact]
    public void Module_binding_applies_the_resolved_connection_history_table_and_migrations_assembly()
    {
        var builder = new DbContextOptionsBuilder();
        using var services = new ServiceCollection().BuildServiceProvider();

        new EfModuleBinding("Test", "__EFMigrationsHistory_Test", "Test.Migrations", "Test", "Data Source=test-module.db")
            .Apply(builder, services, "Sqlite", null, null);

        var relational = builder.Options.Extensions.OfType<RelationalOptionsExtension>().Single();
        Assert.Equal("Data Source=test-module.db", relational.ConnectionString);
        Assert.Equal("__EFMigrationsHistory_Test", relational.MigrationsHistoryTableName);
        Assert.Equal("Test.Migrations", relational.MigrationsAssembly);
    }

    [Fact]
    public void UseSqlServer_fails_closed_when_the_engine_is_not_loaded()
    {
        var builder = new DbContextOptionsBuilder();
        var exception = Assert.Throws<InvalidOperationException>(() =>
            EfRelationalProviderBinding.UseSqlServer(
                builder,
                "Server=localhost;Database=x;TrustServerCertificate=True",
                "__EFMigrationsHistory_ElsaSecrets"));
        Assert.Contains("Microsoft.EntityFrameworkCore.SqlServer", exception.Message, StringComparison.Ordinal);
    }
}
