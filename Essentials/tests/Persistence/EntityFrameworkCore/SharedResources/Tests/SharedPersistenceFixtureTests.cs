using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Elsa.Workbench.Tests;
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
    public async Task Explicit_diagnostics_bindings_place_both_histories_on_the_second_target_after_restart()
    {
        Skip.IfNot(targets.IsAvailable, targets.SkipReason ?? "Docker/PostgreSQL unavailable.");

        await using var host = new SharedPersistenceHostFixture(targets,
            SharedPersistenceHostFixture.DiagnosticsResourceSettings(), SharedPersistenceHostFixture.RemoveLegacyDiagnosticsTargets);
        await host.StartAsync();
        using (var request = new ByteArrayContent(TracePayload()))
        {
            request.Headers.ContentType = new MediaTypeHeaderValue("application/x-protobuf");
            using var response = await host.Process.Client.PostAsync("/elsa/otlp/v1/traces", request);
            Assert.Equal(204, (int)response.StatusCode);
        }
        using (var response = await host.Process.Client.GetAsync("/"))
            Assert.True(response.IsSuccessStatusCode);
        await AssertDiagnosticHistoriesAsync();
        await host.RestartAsync();
        await AssertDiagnosticHistoriesAsync();

        var listed = await host.RunToolingAsync("list", resource: "diagnostics", stageAuthoredConfig: true);
        AssertTooling(listed, 0);
        Assert.Contains("Diagnostics.OpenTelemetry", listed.Output, StringComparison.Ordinal);
        Assert.Contains("Diagnostics.StructuredLogs", listed.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("Workflows.Runtime", listed.Output, StringComparison.Ordinal);

        var validated = await host.RunToolingAsync("validate", resource: "diagnostics", stageAuthoredConfig: true);
        AssertTooling(validated, 0);
        Assert.Contains("No pending migrations.", validated.Output, StringComparison.Ordinal);
        Assert.Contains("Target verification: matched", validated.Output, StringComparison.Ordinal);

        var wrongTarget = await host.RunToolingAsync("validate", targets.PrimaryConnectionString,
            resource: "diagnostics", stageAuthoredConfig: true);
        AssertTooling(wrongTarget, 3);
        Assert.Contains("connection-target-mismatch", wrongTarget.Output, StringComparison.Ordinal);

        async Task AssertDiagnosticHistoriesAsync()
        {
            await using var primary = new NpgsqlConnection(targets.PrimaryConnectionString);
            await using var diagnostics = new NpgsqlConnection(targets.DiagnosticsConnectionString);
            await primary.OpenAsync();
            await diagnostics.OpenAsync();
            foreach (var history in new[]
                     {
                         "__EFMigrationsHistory_ElsaStructuredLogs",
                         "__EFMigrationsHistory_ElsaOpenTelemetry"
                     })
            {
                Assert.False(await HasTableAsync(primary, history), $"{history} reached the primary target.");
                Assert.True(await HasTableAsync(diagnostics, history), $"{history} is absent from diagnostics.");
            }
            Assert.True(await HasTableAsync(primary, "__EFMigrationsHistory_ElsaRuntime"));
            Assert.False(await HasTableAsync(diagnostics, "__EFMigrationsHistory_ElsaRuntime"));
            Assert.True(await CountAsync(diagnostics, "elsa_otel_traces") > 0,
                "The accepted OTLP trace did not reach the diagnostics target.");
            Assert.Equal(0, await CountAsync(primary, "elsa_otel_traces", missingIsZero: true));
            Assert.True(await CountAsync(diagnostics, "elsa_structured_log_records") > 0,
                "The running shell did not persist a structured log on the diagnostics target.");
            Assert.Equal(0, await CountAsync(primary, "elsa_structured_log_records", missingIsZero: true));
        }

        void AssertTooling(ToolingRun run, int expectedExitCode)
        {
            Assert.DoesNotContain(targets.PrimaryConnectionString, run.Output, StringComparison.Ordinal);
            Assert.DoesNotContain(targets.DiagnosticsConnectionString, run.Output, StringComparison.Ordinal);
            Assert.DoesNotContain(PostgreSqlTargetFixture.SyntheticPassword, run.Output, StringComparison.Ordinal);
            Assert.True(run.ExitCode == expectedExitCode,
                run.Output.Replace(targets.PrimaryConnectionString, "<primary>", StringComparison.Ordinal)
                    .Replace(targets.DiagnosticsConnectionString, "<diagnostics>", StringComparison.Ordinal)
                    .Replace(PostgreSqlTargetFixture.SyntheticPassword, "<password>", StringComparison.Ordinal));
        }
    }

    [SkippableFact]
    public async Task Split_diagnostics_targets_refuse_before_any_database_migration()
    {
        Skip.IfNot(targets.IsAvailable, targets.SkipReason ?? "Docker/PostgreSQL unavailable.");
        var (primary, diagnostics) = await targets.CreateIsolatedTargetsAsync();
        var settings = SharedPersistenceHostFixture.DiagnosticsResourceSettings()
            .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase);
        settings["ConnectionStrings:Shared"] = primary;
        settings["ConnectionStrings:Diagnostics"] = diagnostics;
        settings["CShells:Shells:default:Configuration:Elsa:Persistence:Bindings:DiagnosticsOpenTelemetryEntityFrameworkCore"] = "primary";
        var shell = WorkbenchShell.Development with { Settings = settings };

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            WorkbenchProcess.StartAsync(shell, SharedPersistenceHostFixture.RemoveLegacyDiagnosticsTargets));
        Assert.Contains("resource-context-conflict", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(primary, failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(diagnostics, failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(PostgreSqlTargetFixture.SyntheticPassword, failure.Message, StringComparison.Ordinal);

        foreach (var connectionString in new[] { primary, diagnostics })
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM pg_catalog.pg_tables WHERE schemaname = 'public'";
            Assert.Equal(0L, await command.ExecuteScalarAsync());
        }
    }

    private static async Task<bool> HasTableAsync(NpgsqlConnection connection, string table)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS (SELECT 1 FROM pg_catalog.pg_tables WHERE schemaname = 'public' AND tablename = @table)";
        command.Parameters.AddWithValue("table", table);
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<long> CountAsync(NpgsqlConnection connection, string table, bool missingIsZero = false)
    {
        if (missingIsZero && !await HasTableAsync(connection, table))
            return 0;
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM \"{table}\"";
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static byte[] TracePayload()
    {
        // One OTLP ExportTraceServiceRequest with a resource, scope and completed span.
        var span = Join(Bytes(1, Convert.FromHexString("00112233445566778899aabbccddeeff")),
            Bytes(2, Convert.FromHexString("0011223344556677")),
            Message(5, Encoding.UTF8.GetBytes("composition-proof")),
            Field(7, 1_700_000_000_000_000_000), Field(8, 1_700_000_000_025_000_000));
        var resource = Message(1, Join(Message(1, Encoding.UTF8.GetBytes("service.name")),
            Message(2, Message(1, Encoding.UTF8.GetBytes("composition-proof")))));
        return Message(1, Join(Message(1, resource), Message(2, Message(2, span))));
    }

    private static byte[] Bytes(int number, byte[] value) => Message(number, value);
    private static byte[] Message(int number, byte[] value) => Join(Field((number << 3) | 2), Field(value.Length), value);
    private static byte[] Field(int number, ulong value) => Join(Field(number << 3), Field(value));
    private static byte[] Field(int number) => Field((ulong)number);
    private static byte[] Field(ulong value)
    {
        var bytes = new List<byte>();
        while (value >= 0x80)
        {
            bytes.Add((byte)(value | 0x80));
            value >>= 7;
        }
        bytes.Add((byte)value);
        return bytes.ToArray();
    }

    private static byte[] Join(params byte[][] parts) => parts.SelectMany(part => part).ToArray();

    [SkippableFact]
    public async Task Partial_opt_in_runtime_stores_activate_without_the_comprehensive_execution_profile()
    {
        Skip.IfNot(targets.IsAvailable, targets.SkipReason ?? "Docker/PostgreSQL unavailable.");

        await using var host = new SharedPersistenceHostFixture(targets,
            SharedPersistenceHostFixture.PrimaryResourceSettings(), ConfigureOptInRuntimeShell);
        await host.StartAsync();

        var catalog = (await host.Process.ReadFeatureCatalogAsync()).ToDictionary(feature => feature.Id, StringComparer.Ordinal);
        Assert.False(catalog.TryGetValue(RuntimePersistenceFeatures[0], out var comprehensive) && comprehensive.Runs,
            "The comprehensive Runtime feature would mask an opt-in activation failure.");
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
