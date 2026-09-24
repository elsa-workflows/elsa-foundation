using System.Text.Json.Nodes;
using Npgsql;
using Xunit;

namespace Elsa.Persistence.EntityFrameworkCore.SharedResources.Tests;

[Collection(PostgreSqlTargetFixture.CollectionName)]
public sealed class SharedPersistenceFixtureTests(PostgreSqlTargetFixture targets)
{
    private static readonly string[] RuntimePersistenceFeatures =
    [
        "WorkflowsRuntimeEntityFrameworkCore",
        "WorkflowsRuntimeWorkflowExecutionEntityFrameworkCorePersistence",
        "WorkflowsRuntimeActivityExecutionEntityFrameworkCorePersistence",
        "WorkflowsRuntimeOperationalStateEntityFrameworkCorePersistence",
        "WorkflowsRuntimeAlterationEntityFrameworkCorePersistence",
        "WorkflowsRuntimeTestScopeEntityFrameworkCorePersistence",
        "WorkflowsRuntimeBookmarksEntityFrameworkCorePersistence",
        "WorkflowsRuntimeArtifactsEntityFrameworkCorePersistence"
    ];

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

        await using var host = new SharedPersistenceHostFixture(targets, SharedPersistenceHostFixture.PrimaryResourceSettings());
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

    [SkippableFact]
    public async Task Opt_in_runtime_persistence_features_activate_with_the_shared_target()
    {
        Skip.IfNot(targets.IsAvailable, targets.SkipReason ?? "Docker/PostgreSQL unavailable.");

        await using var host = new SharedPersistenceHostFixture(targets,
            SharedPersistenceHostFixture.PrimaryResourceSettings(), ConfigureOptInRuntimeShell);
        await host.StartAsync();

        var catalog = (await host.Process.ReadFeatureCatalogAsync()).ToDictionary(feature => feature.Id, StringComparer.Ordinal);
        foreach (var feature in RuntimePersistenceFeatures.Skip(1))
        {
            Assert.True(catalog.TryGetValue(feature, out var entry) && entry.Runs,
                $"{feature} did not activate in the shared-resource host.");
        }

        await using var connection = new NpgsqlConnection(targets.PrimaryConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM \"__EFMigrationsHistory_ElsaRuntime\"";
        Assert.True((long)(await command.ExecuteScalarAsync())! > 0);
    }

    private static void ConfigureOptInRuntimeShell(string contentRoot)
    {
        var path = Path.Combine(contentRoot, "shells.json");
        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        var features = root["CShells"]!["Shells"]!["default"]!["Features"]!.AsObject();
        var comprehensive = features[RuntimePersistenceFeatures[0]]!;
        var recoveryKey = (string)comprehensive["RecoveryContinuationSigningKey"]!;
        var hierarchyKey = (string)comprehensive["HierarchyCursorSigningKey"]!;
        features.Remove(RuntimePersistenceFeatures[0]);
        features.Remove("WorkflowsDashboardEntityFrameworkCore"); // Its dependency would re-enable the comprehensive store.
        foreach (var feature in RuntimePersistenceFeatures.Skip(1))
        {
            var configuration = new JsonObject();
            if (feature is not "WorkflowsRuntimeBookmarksEntityFrameworkCorePersistence")
                configuration["RecoveryContinuationSigningKey"] = recoveryKey;
            if (feature is "WorkflowsRuntimeActivityExecutionEntityFrameworkCorePersistence")
                configuration["HierarchyCursorSigningKey"] = hierarchyKey;
            features[feature] = configuration;
        }
        File.WriteAllText(path, root.ToJsonString());
    }
}
