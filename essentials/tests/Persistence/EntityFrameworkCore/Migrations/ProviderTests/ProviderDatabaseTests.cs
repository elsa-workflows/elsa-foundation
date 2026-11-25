using Microsoft.Data.SqlClient;
using MySql.Data.MySqlClient;
using Npgsql;
using Xunit;

namespace Elsa.Persistence.EntityFrameworkCore.Migrations.ProviderTests;

public sealed class ProviderDatabaseTests
{
    [Theory]
    [InlineData("PostgreSql", "ELSA_MIGRATIONS_EF_POSTGRESQL_TEST_CONNECTION_STRING")]
    [InlineData("SqlServer", "ELSA_MIGRATIONS_EF_SQLSERVER_TEST_CONNECTION_STRING")]
    [InlineData("MySql", "ELSA_MIGRATIONS_EF_MYSQL_TEST_CONNECTION_STRING")]
    public void Explicit_provider_configuration_has_its_own_named_contract(string provider, string variable) =>
        Assert.Equal(variable, ProviderDatabase.ConnectionEnvironmentVariable(provider));

    [Theory]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    [InlineData("MySql")]
    public void An_external_plan_uses_a_unique_owned_database_and_only_run_local_unpooled_connections(string provider)
    {
        var configured = Connection(provider);
        var plan = ProviderDatabase.CreateExternalPlan(provider, configured);
        var other = ProviderDatabase.CreateExternalPlan(provider, configured);
        Assert.NotEqual(plan.Name, other.Name);
        var identifier = ProviderDatabase.OwnedDatabaseIdentifier(provider, plan.Name);
        Assert.StartsWith("CREATE DATABASE " + identifier, plan.CreateSql, StringComparison.Ordinal);
        Assert.Equal("DROP DATABASE " + identifier, plan.DropSql);
        Assert.DoesNotContain("admin_database", plan.CreateSql, StringComparison.Ordinal);
        Assert.DoesNotContain("admin_database", plan.DropSql, StringComparison.Ordinal);
        using var admin = plan.CreateAdminConnection();
        switch (provider)
        {
            case "PostgreSql":
                var postgresAdmin = new NpgsqlConnectionStringBuilder(admin.ConnectionString);
                var postgresTarget = new NpgsqlConnectionStringBuilder(plan.ConnectionString);
                Assert.Equal("admin_database", postgresAdmin.Database);
                Assert.Equal(plan.Name, postgresTarget.Database);
                Assert.Equal(postgresAdmin.Host, postgresTarget.Host);
                Assert.False(postgresAdmin.Pooling); Assert.False(postgresTarget.Pooling);
                break;
            case "SqlServer":
                var sqlAdmin = new SqlConnectionStringBuilder(admin.ConnectionString);
                var sqlTarget = new SqlConnectionStringBuilder(plan.ConnectionString);
                Assert.Equal("admin_database", sqlAdmin.InitialCatalog);
                Assert.Equal(plan.Name, sqlTarget.InitialCatalog);
                Assert.Equal(sqlAdmin.DataSource, sqlTarget.DataSource);
                Assert.False(sqlAdmin.Pooling); Assert.False(sqlTarget.Pooling);
                break;
            case "MySql":
                var mySqlAdmin = new MySqlConnectionStringBuilder(admin.ConnectionString);
                var mySqlTarget = new MySqlConnectionStringBuilder(plan.ConnectionString);
                Assert.Equal("admin_database", mySqlAdmin.Database);
                Assert.Equal(plan.Name, mySqlTarget.Database);
                Assert.Equal(mySqlAdmin.Server, mySqlTarget.Server);
                Assert.False(mySqlAdmin.Pooling); Assert.False(mySqlTarget.Pooling);
                break;
        }
        Assert.Equal(plan.Name, plan.ToString());
    }

    [Theory]
    [InlineData("shared_database")]
    [InlineData("elsa_migrations_00000000000000000000000000000000")]
    [InlineData("elsa_migrations_ABCDEFABCDEFABCDEFABCDEFABCDEFAB")]
    [InlineData("elsa_migrations_12345678901234567890123456789012;DROP DATABASE shared")]
    public void Cleanup_identifiers_refuse_ungenerated_or_injected_names(string name) =>
        Assert.Throws<ArgumentException>(() => ProviderDatabase.OwnedDatabaseIdentifier("PostgreSql", name));

    [Fact]
    public async Task A_successful_run_creates_then_passes_only_the_isolated_connection_then_drops_its_captured_name()
    {
        var plan = ProviderDatabase.CreateExternalPlan("PostgreSql", Connection("PostgreSql"));
        var events = new List<string>();
        await ProviderDatabase.RunExternalAsync(plan, connection =>
        {
            Assert.Equal(plan.Name, new NpgsqlConnectionStringBuilder(connection).Database);
            events.Add("run"); return Task.CompletedTask;
        }, (sql, confirmed) => { events.Add(sql); confirmed(); return Task.CompletedTask; });
        Assert.Equal(new[] { plan.CreateSql, "run", plan.DropSql }, events);
    }

    [Fact]
    public async Task A_failed_create_never_runs_the_test_or_drops_an_unconfirmed_database()
    {
        var plan = ProviderDatabase.CreateExternalPlan("PostgreSql", Connection("PostgreSql"));
        var calls = new List<string>();
        var failure = new InvalidOperationException("create failed");
        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => ProviderDatabase.RunExternalAsync(plan,
            _ => throw new InvalidOperationException("The test must not run."),
            (sql, _) => { calls.Add(sql); throw failure; }));
        Assert.Same(failure, actual);
        Assert.Equal(new[] { plan.CreateSql }, calls);
    }

    [Fact]
    public async Task A_failed_test_still_drops_only_its_database_and_keeps_the_original_exception()
    {
        var plan = ProviderDatabase.CreateExternalPlan("PostgreSql", Connection("PostgreSql"));
        var calls = new List<string>();
        var failure = new InvalidOperationException("test failed");
        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => ProviderDatabase.RunExternalAsync(plan,
            _ => throw failure, (sql, confirmed) => { calls.Add(sql); confirmed(); return Task.CompletedTask; }));
        Assert.Same(failure, actual);
        Assert.Equal(new[] { plan.CreateSql, plan.DropSql }, calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cleanup_failure_is_reported_without_losing_a_test_failure(bool testFails)
    {
        var plan = ProviderDatabase.CreateExternalPlan("PostgreSql", Connection("PostgreSql"));
        var failure = new InvalidOperationException("test failed");
        var cleanup = new IOException("cleanup failed");
        Task Run() => ProviderDatabase.RunExternalAsync(plan,
            _ => testFails ? Task.FromException(failure) : Task.CompletedTask,
            (sql, confirmed) =>
            {
                if (sql == plan.DropSql) return Task.FromException(cleanup);
                confirmed(); return Task.CompletedTask;
            });
        if (testFails)
        {
            var actual = await Assert.ThrowsAsync<AggregateException>(Run);
            Assert.Equal(new Exception[] { failure, cleanup }, actual.InnerExceptions);
        }
        else Assert.Same(cleanup, await Assert.ThrowsAsync<IOException>(Run));
    }

    [Fact]
    public async Task Cancellation_still_cleans_up_and_keeps_the_cancellation_failure()
    {
        var plan = ProviderDatabase.CreateExternalPlan("PostgreSql", Connection("PostgreSql"));
        var calls = new List<string>();
        var failure = new OperationCanceledException(new CancellationToken(canceled: true));
        var actual = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ProviderDatabase.RunExternalAsync(plan,
            _ => throw failure, (sql, confirmed) => { calls.Add(sql); confirmed(); return Task.CompletedTask; }));
        Assert.Same(failure, actual);
        Assert.Equal(new[] { plan.CreateSql, plan.DropSql }, calls);
    }

    [Fact]
    public async Task An_acknowledged_create_is_cleaned_up_even_if_disposing_the_admin_operation_fails()
    {
        var plan = ProviderDatabase.CreateExternalPlan("PostgreSql", Connection("PostgreSql"));
        var calls = new List<string>();
        var failure = new IOException("admin disposal failed");
        var actual = await Assert.ThrowsAsync<IOException>(() => ProviderDatabase.RunExternalAsync(plan,
            _ => throw new InvalidOperationException("The test must not run."), (sql, confirmed) =>
            {
                calls.Add(sql); confirmed();
                if (sql == plan.CreateSql) throw failure;
                return Task.CompletedTask;
            }));
        Assert.Same(failure, actual);
        Assert.Equal(new[] { plan.CreateSql, plan.DropSql }, calls);
    }

    private static string Connection(string provider) => provider == "PostgreSql"
        ? "Host=example.invalid;Database=admin_database;Username=test_owner;Pooling=true"
        : "Server=example.invalid;Database=admin_database;User ID=test_owner;Pooling=true";
}
