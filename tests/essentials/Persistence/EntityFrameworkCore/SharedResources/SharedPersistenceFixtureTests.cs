using Npgsql;
using Xunit;

namespace Elsa.Persistence.EntityFrameworkCore.SharedResources.Tests;

[Collection(PostgreSqlTargetFixture.CollectionName)]
public sealed class SharedPersistenceFixtureTests(PostgreSqlTargetFixture targets)
{
    [SkippableFact]
    public async Task Targets_are_separate_databases_and_writes_do_not_cross()
    {
        Skip.IfNot(targets.IsAvailable, targets.SkipReason ?? "Docker/PostgreSQL unavailable.");

        await using var primary = new NpgsqlConnection(targets.PrimaryConnectionString);
        await using var diagnostics = new NpgsqlConnection(targets.DiagnosticsConnectionString);
        await primary.OpenAsync();
        await diagnostics.OpenAsync();

        Assert.NotEqual(primary.Database, diagnostics.Database);

        await using (var create = primary.CreateCommand())
        {
            create.CommandText = "CREATE TABLE shared_fixture_probe (id integer PRIMARY KEY)";
            await create.ExecuteNonQueryAsync();
        }

        await using var inspect = diagnostics.CreateCommand();
        inspect.CommandText = "SELECT EXISTS (SELECT 1 FROM pg_catalog.pg_tables WHERE schemaname = 'public' AND tablename = 'shared_fixture_probe')";
        Assert.Equal(false, await inspect.ExecuteScalarAsync());
    }

    [SkippableFact]
    public async Task Workbench_can_start_and_restart_with_target_connections_available()
    {
        Skip.IfNot(targets.IsAvailable, targets.SkipReason ?? "Docker/PostgreSQL unavailable.");

        await using var host = new SharedPersistenceHostFixture(targets);
        await host.StartAsync();
        using (var initialResponse = await host.Process.Client.GetAsync("/"))
            Assert.True(initialResponse.IsSuccessStatusCode);

        await host.RestartAsync();
        using var restartedResponse = await host.Process.Client.GetAsync("/");
        Assert.True(restartedResponse.IsSuccessStatusCode);
    }
}
