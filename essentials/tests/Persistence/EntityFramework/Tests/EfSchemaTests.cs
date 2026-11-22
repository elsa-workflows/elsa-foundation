using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Persistence.EntityFramework.Tests;

public sealed class EfSchemaTests
{
    [Fact]
    public void An_explicit_schema_wins_over_the_host_wide_key()
    {
        Assert.Equal("module", EfSchema.Resolve(Services(("Elsa:Persistence:EntityFramework:Schema", "host")), "Test", "PostgreSql", "module"));
    }

    [Fact]
    public void The_host_wide_key_puts_a_module_that_names_no_schema_in_one()
    {
        Assert.Equal("host", EfSchema.Resolve(Services(("Elsa:Persistence:EntityFramework:Schema", "host")), "Test", "PostgreSql", null));
    }

    [Fact]
    public void No_module_setting_and_no_key_is_no_schema_at_all()
    {
        Assert.Null(EfSchema.Resolve(Services(), "Test", "PostgreSql", null));
        Assert.Null(EfSchema.Resolve(Services(), "Test", "PostgreSql", "   "));
    }

    /// <summary>A container that composes modules on a bare ServiceCollection has no IConfiguration at all.</summary>
    [Fact]
    public void A_container_without_configuration_keeps_the_module_setting()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        Assert.Equal("module", EfSchema.Resolve(services, "Test", "SqlServer", "module"));
        Assert.Null(EfSchema.Resolve(services, "Test", "SqlServer", null));
    }

    [Theory]
    [InlineData("Sqlite")]
    [InlineData("microsoft.entityframeworkcore.sqlite")]
    public void Sqlite_ignores_a_schema_rather_than_refusing_it(string provider)
    {
        Assert.Null(EfSchema.Resolve(Services(("Elsa:Persistence:EntityFramework:Schema", "host")), "Test", provider, "module"));
        Assert.Null(EfSchema.Normalize("Test", provider, "module"));
    }

    /// <summary>
    /// A MySQL schema is a database, so the setting would not mean there what it means elsewhere, and the
    /// provider's history repository writes SQL the server rejects for one. Refused with the way to say it instead.
    /// </summary>
    [Theory]
    [InlineData("MySql")]
    [InlineData("mysql.entityframeworkcore")]
    public void MySql_refuses_a_schema_and_names_the_connection_string_instead(string provider)
    {
        var failure = Assert.Throws<InvalidOperationException>(() => EfSchema.Normalize("Test", provider, "elsa_alt"));
        Assert.Contains("a schema is a database", failure.Message, StringComparison.Ordinal);
        Assert.Contains("Database=elsa_alt", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_host_wide_key_is_refused_on_MySql_too_rather_than_half_applied()
    {
        Assert.Throws<InvalidOperationException>(() =>
            EfSchema.Resolve(Services(("Elsa:Persistence:EntityFramework:Schema", "elsa_alt")), "Test", "MySql", null));
    }

    [Theory]
    [InlineData("elsa runtime")]
    [InlineData("elsa.runtime")]
    [InlineData("elsa\"; DROP TABLE x; --")]
    [InlineData("[elsa]")]
    public void A_schema_that_is_not_a_plain_identifier_is_refused(string schema)
    {
        var failure = Assert.Throws<InvalidOperationException>(() => EfSchema.Normalize("Test", "PostgreSql", schema));
        Assert.Contains("is not a plain identifier", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_schema_longer_than_the_matrix_accepts_is_refused()
    {
        Assert.Throws<InvalidOperationException>(() => EfSchema.Normalize("Test", "PostgreSql", new string('a', EfSchema.MaxLength + 1)));
        Assert.Equal(new string('a', EfSchema.MaxLength), EfSchema.Normalize("Test", "PostgreSql", new string('a', EfSchema.MaxLength)));
    }

    [Fact]
    public void Surrounding_whitespace_is_trimmed_rather_than_refused()
    {
        Assert.Equal("elsa_alt", EfSchema.Normalize("Test", "PostgreSql", "  elsa_alt\t"));
    }

    [Fact]
    public void Binding_sqlite_with_a_schema_leaves_the_history_table_and_the_model_unqualified()
    {
        var builder = new DbContextOptionsBuilder();
        EfRelationalProviderBinding.UseSqlite(builder, "Data Source=:memory:", "__EFMigrationsHistory_Test", null, "elsa_alt");

        Assert.Null(builder.Options.Extensions.OfType<RelationalOptionsExtension>().Single().MigrationsHistoryTableSchema);
        Assert.Null(EfSchemaOptionsExtension.Find(builder.Options));
    }

    [Fact]
    public void A_module_binding_on_sqlite_ignores_the_host_wide_key_too()
    {
        var builder = new DbContextOptionsBuilder();
        using var services = Services(("Elsa:Persistence:EntityFramework:Schema", "host"));

        new EfModuleBinding("Test", "__EFMigrationsHistory_Test", null, "Test", "Data Source=test.db")
            .Apply(builder, services, "Sqlite", null, null);

        Assert.Null(EfSchemaOptionsExtension.Find(builder.Options));
    }

    /// <summary>
    /// EF caches one model per internal service provider. Two schemas that agreed here would share a model, and the
    /// second context would read and write the first one's schema.
    /// </summary>
    [Fact]
    public void Two_schemas_do_not_share_an_internal_service_provider()
    {
        var one = new EfSchemaOptionsExtension("elsa_one").Info;
        var same = new EfSchemaOptionsExtension("elsa_one").Info;
        var other = new EfSchemaOptionsExtension("elsa_two").Info;

        Assert.True(one.ShouldUseSameServiceProvider(same));
        Assert.Equal(one.GetServiceProviderHashCode(), same.GetServiceProviderHashCode());
        Assert.False(one.ShouldUseSameServiceProvider(other));
        Assert.NotEqual(one.GetServiceProviderHashCode(), other.GetServiceProviderHashCode());
    }

    [Fact]
    public void Qualifying_fills_in_a_create_table_and_everything_nested_in_it()
    {
        var create = new CreateTableOperation
        {
            Name = "elsa_test",
            Columns = { new AddColumnOperation { Name = "Id", Table = "elsa_test", ClrType = typeof(string) } },
            PrimaryKey = new AddPrimaryKeyOperation { Name = "PK_elsa_test", Table = "elsa_test", Columns = ["Id"] },
            ForeignKeys =
            {
                new AddForeignKeyOperation
                {
                    Name = "FK_elsa_test_parent",
                    Table = "elsa_test",
                    Columns = ["Id"],
                    PrincipalTable = "elsa_parent",
                    PrincipalColumns = ["Id"]
                }
            }
        };

        EfSchemaMigrationsAssembly.Qualify([create], "elsa_alt", "Initial");

        Assert.Equal("elsa_alt", create.Schema);
        Assert.Equal("elsa_alt", create.Columns.Single().Schema);
        Assert.Equal("elsa_alt", create.PrimaryKey!.Schema);
        Assert.Equal("elsa_alt", create.ForeignKeys.Single().Schema);
        Assert.Equal("elsa_alt", create.ForeignKeys.Single().PrincipalSchema);
    }

    [Fact]
    public void Qualifying_fills_in_the_old_shape_an_alter_is_diffed_against()
    {
        var alter = new AlterColumnOperation
        {
            Name = "Payload",
            Table = "elsa_test",
            ClrType = typeof(string),
            OldColumn = new AddColumnOperation { Name = "Payload", Table = "elsa_test", ClrType = typeof(string) }
        };

        EfSchemaMigrationsAssembly.Qualify([alter], "elsa_alt", "Widen");

        Assert.Equal("elsa_alt", alter.Schema);
        Assert.Equal("elsa_alt", alter.OldColumn.Schema);
    }

    [Fact]
    public void A_schema_a_migration_named_itself_is_left_alone()
    {
        var create = new CreateTableOperation { Name = "elsa_test", Schema = "chosen" };

        EfSchemaMigrationsAssembly.Qualify([create], "elsa_alt", "Initial");

        Assert.Equal("chosen", create.Schema);
    }

    [Fact]
    public void Raw_sql_in_a_migration_is_refused_rather_than_run_against_the_wrong_schema()
    {
        var failure = Assert.Throws<NotSupportedException>(() =>
            EfSchemaMigrationsAssembly.Qualify([new SqlOperation { Sql = "UPDATE elsa_test SET x = 1" }], "elsa_alt", "Backfill"));

        Assert.Contains("runs raw SQL", failure.Message, StringComparison.Ordinal);
        Assert.Contains("elsa_alt", failure.Message, StringComparison.Ordinal);
    }

    private static ServiceProvider Services(params (string Key, string Value)[] settings) => new ServiceCollection()
        .AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(setting => new KeyValuePair<string, string?>(setting.Key, setting.Value)))
            .Build())
        .BuildServiceProvider();
}
