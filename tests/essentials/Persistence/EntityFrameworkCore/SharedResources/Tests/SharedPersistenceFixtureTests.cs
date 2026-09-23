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

    [SkippableFact]
    public async Task Selected_resource_places_the_four_enrolled_module_histories_on_the_shared_target()
    {
        Skip.IfNot(targets.IsAvailable, targets.SkipReason ?? "Docker/PostgreSQL unavailable.");

        var settings = new Dictionary<string, string>
        {
            ["Elsa:Persistence:Resources:primary:Provider"] = "PostgreSql",
            ["Elsa:Persistence:Resources:primary:ConnectionName"] = "Shared"
        };
        foreach (var feature in new[]
                 {
                     "WorkflowsRuntimeEntityFrameworkCore",
                     "WorkflowsDesignEntityFrameworkCore",
                     "ActivitiesDesignEntityFrameworkCore",
                     "WorkflowsPublishingEntityFrameworkCore"
                 })
            settings[$"CShells:Shells:default:Configuration:Elsa:Persistence:Bindings:{feature}"] = "primary";

        await using var host = new SharedPersistenceHostFixture(targets, settings);
        await host.StartAsync();
        using (var response = await host.Process.Client.GetAsync("/"))
            Assert.True(response.IsSuccessStatusCode);

        await using var connection = new NpgsqlConnection(targets.PrimaryConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT tablename FROM pg_catalog.pg_tables WHERE schemaname = 'public' AND tablename LIKE '__EFMigrationsHistory_%'";
        var historyTables = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync())
            while (await reader.ReadAsync())
                historyTables.Add(reader.GetString(0));

        Assert.Contains("__EFMigrationsHistory_ElsaRuntime", historyTables);
        Assert.Contains("__EFMigrationsHistory_ElsaWorkflowsDesign", historyTables);
        Assert.Contains("__EFMigrationsHistory_ElsaActivitiesDesign", historyTables);
        Assert.Contains("__EFMigrationsHistory_ElsaPublishingSnapshotReview", historyTables);
    }
}
