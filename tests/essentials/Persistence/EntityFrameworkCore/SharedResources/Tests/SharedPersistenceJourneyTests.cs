using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Elsa.Workbench.Tests;
using Npgsql;
using Xunit;

namespace Elsa.Persistence.EntityFrameworkCore.SharedResources.Tests;

[Collection(PostgreSqlTargetFixture.CollectionName)]
public sealed class SharedPersistenceJourneyTests(PostgreSqlTargetFixture targets)
{
    [SkippableFact]
    public async Task Shared_resource_preserves_reusable_activity_published_workflow_and_execution_after_restart()
    {
        Skip.IfNot(targets.IsAvailable, targets.SkipReason ?? "Docker/PostgreSQL unavailable.");

        await using var host = new SharedPersistenceHostFixture(targets, SharedPersistenceHostFixture.PrimaryResourceSettings());
        await host.StartAsync();
        var client = host.Process.Client;
        await SignInAsync(client);

        var writeLineId = await ActivityVersionIdAsync(client, "Elsa.Activities.Primitives.Activities.WriteLine");
        var sequenceId = await ActivityVersionIdAsync(client, "Elsa.Activities.Sequence.Activities.Sequence");
        var marker = $"shared-resource-{Guid.NewGuid():N}";
        var reusable = await PublishReusableActivityAsync(client, marker, sequenceId, writeLineId);
        var submitted = await PostAsync(client, "design/workflows/definitions/submit", new
        {
            name = marker,
            description = "Shared persistence resource restart proof",
            state = new
            {
                variables = Array.Empty<object>(),
                inputs = Array.Empty<object>(),
                outputs = Array.Empty<object>(),
                workflowActivityOptions = (object?)null,
                strategyOptions = (object?)null,
                rootActivity = ActivityNode(reusable.VersionId)
            }
        });
        var versionId = Required(submitted, "version", "id");
        var published = await PostAsync(client, $"publishing/workflows/{versionId}/publish", new { });
        var artifactId = Required(published, "artifactId");
        var sourceReferenceId = Required(published, "sourceReferenceId");
        var started = await PostAsync(client, $"runtime/workflows/executables/{artifactId}/execute",
            new { sourceReferenceId });
        var executionId = Required(started, "workflowExecutionId");
        var completed = await WaitForCompletionAsync(client, executionId);
        Assert.Contains((string?)completed["instance"]?["status"], new[] { "Completed", "Finished" });
        Assert.Contains(completed["activities"]!.AsArray(), activity =>
            StringComparer.OrdinalIgnoreCase.Equals((string?)activity?["activityType"], reusable.TypeKey) &&
            (string?)activity?["status"] is "Completed" or "Finished");

        await host.RestartAsync();
        client = host.Process.Client;
        await SignInAsync(client);
        var persisted = await GetAsync(client, $"runtime/workflows/instances/{executionId}");
        Assert.Contains((string?)persisted["instance"]?["status"], new[] { "Completed", "Finished" });
        var persistedVersion = await GetAsync(client, $"design/workflows/versions/{versionId}");
        Assert.NotNull(persistedVersion);
        var activityCatalog = await GetAsync(client, $"design/activities/definitions?search={Uri.EscapeDataString(marker)}");
        Assert.Contains(activityCatalog["items"]!.AsArray(), item =>
            StringComparer.Ordinal.Equals((string?)item?["definition"]?["definitionId"], reusable.DefinitionId) &&
            StringComparer.Ordinal.Equals((string?)item?["definition"]?["headVersionId"], reusable.VersionId));

        await using var primary = new NpgsqlConnection(targets.PrimaryConnectionString);
        await primary.OpenAsync();
        foreach (var table in new[]
                 {
                     "elsa_activity_definitions",
                     "elsa_workflow_definitions_v2",
                     "elsa_publication_records",
                     "elsa_runtime_workflow_execution_state"
                 })
        {
            await using var count = primary.CreateCommand();
            count.CommandText = $"SELECT COUNT(*) FROM \"{table}\"";
            Assert.True((long)(await count.ExecuteScalarAsync())! > 0, $"{table} has no persisted rows on the shared target.");
        }
        foreach (var history in new[]
                 {
                     "__EFMigrationsHistory_ElsaActivitiesDesign",
                     "__EFMigrationsHistory_ElsaWorkflowsDesign",
                     "__EFMigrationsHistory_ElsaPublishingSnapshotReview",
                     "__EFMigrationsHistory_ElsaRuntime"
                 })
        {
            await using var count = primary.CreateCommand();
            count.CommandText = $"SELECT COUNT(*) FROM \"{history}\"";
            Assert.True((long)(await count.ExecuteScalarAsync())! > 0, $"{history} has no migration row on the shared target.");
        }

        var listed = await host.RunToolingAsync("list");
        AssertTooling(listed, 0, targets);
        Assert.Contains("4 module(s).", listed.Output, StringComparison.Ordinal);
        foreach (var module in new[] { "Activities.Design", "Workflows.Design", "Workflows.Publishing", "Workflows.Runtime" })
            Assert.Contains(module, listed.Output, StringComparison.Ordinal);

        var validated = await host.RunToolingAsync("validate");
        AssertTooling(validated, 0, targets);
        Assert.Contains("No pending migrations.", validated.Output, StringComparison.Ordinal);
        Assert.Contains("Target verification: matched", validated.Output, StringComparison.Ordinal);

        var wrongTarget = await host.RunToolingAsync("validate", targets.DiagnosticsConnectionString);
        AssertTooling(wrongTarget, 3, targets);
        Assert.Contains("connection-target-mismatch", wrongTarget.Output, StringComparison.Ordinal);

        await using var diagnostics = new NpgsqlConnection(targets.DiagnosticsConnectionString);
        await diagnostics.OpenAsync();
        await using var check = diagnostics.CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM pg_catalog.pg_tables WHERE schemaname = 'public' AND tablename IN ('__EFMigrationsHistory_ElsaRuntime', '__EFMigrationsHistory_ElsaWorkflowsDesign', '__EFMigrationsHistory_ElsaActivitiesDesign', '__EFMigrationsHistory_ElsaPublishingSnapshotReview', 'elsa_activity_definitions', 'elsa_workflow_definitions_v2', 'elsa_publication_records', 'elsa_runtime_workflow_execution_state')";
        Assert.Equal(0L, await check.ExecuteScalarAsync());
    }

    [SkippableFact]
    public async Task Authored_resource_reload_moves_the_target_and_failed_candidate_keeps_the_active_generation()
    {
        Skip.IfNot(targets.IsAvailable, targets.SkipReason ?? "Docker/PostgreSQL unavailable.");
        var (firstConnection, secondConnection) = await targets.CreateIsolatedTargetsAsync();
        var settings = SharedPersistenceHostFixture.PrimaryResourceSettings()
            .Where(pair => !pair.Key.StartsWith("Elsa:Persistence:Resources:primary:", StringComparison.Ordinal))
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        settings["ConnectionStrings:ReloadFirst"] = firstConnection;
        settings["ConnectionStrings:ReloadSecond"] = secondConnection;
        var shell = WorkbenchShell.Development with { Settings = settings };
        string? appsettings = null;
        await using var host = await WorkbenchProcess.StartAsync(shell, directory =>
        {
            appsettings = Path.Combine(directory, "appsettings.json");
            WriteAuthoredResource(appsettings, "PostgreSql", "ReloadFirst");
        });
        var initialGeneration = await ReadinessGenerationAsync(host.Client);
        Assert.True(await HasTableAsync(firstConnection, "__EFMigrationsHistory_ElsaActivitiesDesign"));
        Assert.False(await HasTableAsync(secondConnection, "__EFMigrationsHistory_ElsaActivitiesDesign"));

        WriteAuthoredResource(appsettings!, "PostgreSql", "ReloadSecond");
        await Task.Delay(TimeSpan.FromSeconds(1)); // Allow the host's JSON provider to observe the authored file change.
        var promoted = await ReloadAsync(host);
        Assert.True((bool?)promoted["success"] is true);
        var nextGeneration = (int?)promoted["newShell"]?["generation"];
        Assert.True(nextGeneration > initialGeneration);
        Assert.Equal(nextGeneration, await ReadinessGenerationAsync(host.Client));
        Assert.True(await HasTableAsync(secondConnection, "__EFMigrationsHistory_ElsaActivitiesDesign"));

        await SignInAsync(host.Client);
        var sequenceId = await ActivityVersionIdAsync(host.Client, "Elsa.Activities.Sequence.Activities.Sequence");
        var writeLineId = await ActivityVersionIdAsync(host.Client, "Elsa.Activities.Primitives.Activities.WriteLine");
        var marker = $"reload-target-{Guid.NewGuid():N}";
        var published = await PublishReusableActivityAsync(host.Client, marker, sequenceId, writeLineId);
        Assert.True(await HasActivityAsync(secondConnection, marker));
        Assert.False(await HasActivityAsync(firstConnection, marker));

        WriteAuthoredResource(appsettings!, "Oracle", "ReloadSecond");
        await Task.Delay(TimeSpan.FromSeconds(1));
        var refused = await ReloadAsync(host);
        Assert.True((bool?)refused["success"] is false);
        Assert.NotNull(refused["error"]);
        Assert.Null(refused["newShell"]);
        var refusal = refused.ToJsonString();
        Assert.DoesNotContain(firstConnection, refusal, StringComparison.Ordinal);
        Assert.DoesNotContain(secondConnection, refusal, StringComparison.Ordinal);
        Assert.DoesNotContain(PostgreSqlTargetFixture.SyntheticPassword, refusal, StringComparison.Ordinal);
        Assert.Equal(nextGeneration, await ReadinessGenerationAsync(host.Client));
        Assert.True(await HasActivityAsync(secondConnection, marker));
        var catalog = await GetAsync(host.Client, $"design/activities/definitions?search={Uri.EscapeDataString(marker)}");
        Assert.Contains(catalog["items"]!.AsArray(), item =>
            StringComparer.Ordinal.Equals((string?)item?["definition"]?["definitionId"], published.DefinitionId));
    }

    private static void WriteAuthoredResource(string appsettings, string provider, string connectionName)
    {
        var root = JsonNode.Parse(File.ReadAllText(appsettings))!.AsObject();
        root["Elsa"]!["Persistence"]!["Resources"]!["primary"] = new JsonObject
        {
            ["Provider"] = provider,
            ["ConnectionName"] = connectionName
        };
        var candidate = appsettings + ".candidate";
        File.WriteAllText(candidate, root.ToJsonString());
        File.Move(candidate, appsettings, overwrite: true);
    }

    private static async Task<JsonNode> ReloadAsync(WorkbenchProcess host)
    {
        using var response = await host.ManagementClient.PostAsync("/_admin/shells/reload/default", null);
        Assert.True(response.IsSuccessStatusCode, $"Shell reload returned {(int)response.StatusCode}.");
        return (await response.Content.ReadFromJsonAsync<JsonNode>())!;
    }

    private static async Task<int> ReadinessGenerationAsync(HttpClient client) =>
        (int)(await GetAsync(client, "health/ready"))["generation"]!;

    private static async Task<bool> HasTableAsync(string connectionString, string table)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS (SELECT 1 FROM pg_catalog.pg_tables WHERE schemaname = 'public' AND tablename = @table)";
        command.Parameters.AddWithValue("table", table);
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<bool> HasActivityAsync(string connectionString, string displayName)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS (SELECT 1 FROM \"elsa_activity_definitions\" WHERE \"DisplayName\" = @name)";
        command.Parameters.AddWithValue("name", displayName);
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private static void AssertTooling(ToolingRun run, int expectedExitCode, PostgreSqlTargetFixture targets)
    {
        Assert.False(run.Output.Contains(targets.PrimaryConnectionString, StringComparison.Ordinal) ||
                     run.Output.Contains(targets.DiagnosticsConnectionString, StringComparison.Ordinal) ||
                     run.Output.Contains(PostgreSqlTargetFixture.SyntheticPassword, StringComparison.Ordinal),
            "The tooling response exposed a connection value.");
        var safeOutput = run.Output.Replace(targets.PrimaryConnectionString, "<primary>", StringComparison.Ordinal)
            .Replace(targets.DiagnosticsConnectionString, "<diagnostics>", StringComparison.Ordinal);
        Assert.True(run.ExitCode == expectedExitCode, safeOutput);
    }

    private static object ActivityNode(string versionId, string? text = null) => new
    {
        nodeId = "root",
        activityVersionId = versionId,
        inputs = text is null ? Array.Empty<object>() : new object[]
        {
            new
            {
                referenceKey = "text",
                value = new { value = text, expressionType = "Literal" },
                autoEvaluate = (object?)null,
                evaluatorType = (object?)null,
                storageDriverType = (object?)null,
                isSensitive = (object?)null
            }
        },
        outputs = Array.Empty<object>()
    };

    private static async Task<ReusablePublication> PublishReusableActivityAsync(
        HttpClient client, string marker, string sequenceId, string writeLineId)
    {
        var manifest = new
        {
            variables = Array.Empty<object>(),
            rootActivity = new
            {
                nodeId = "graph-root",
                activityVersionId = sequenceId,
                inputs = Array.Empty<object>(),
                outputs = Array.Empty<object>(),
                structure = new
                {
                    kind = "elsa.sequence.structure",
                    schemaVersion = "1.0.0",
                    payload = new { activities = new[] { ActivityNode(writeLineId, marker) } }
                }
            },
            outputMappings = Array.Empty<object>()
        };
        var created = await PostAsync(client, "design/activities/definitions", new
        {
            category = "QaReusable",
            displayName = marker,
            description = "Shared persistence resource restart proof",
            provider = new { providerKey = "elsa.activity-graph", schemaVersion = "1", payload = manifest },
            contract = new
            {
                contractSchemaVersion = "1",
                inputs = Array.Empty<object>(),
                outputs = Array.Empty<object>(),
                outcomes = new[] { new { referenceKey = "done", name = "Done", isEmitted = true } }
            },
            layout = Array.Empty<object>()
        });
        var draftId = Required(created, "draft", "draftId");
        var revision = (long)created["draft"]!["revision"]!;
        var preflight = await PostAsync(client, $"design/activities/drafts/{draftId}/publication-preflight",
            new { expectedDraftRevision = revision, expectedDefinitionHeadVersionId = (string?)null });
        Assert.True((bool)preflight["isPublishable"]!);
        var published = await PostAsync(client, $"design/activities/drafts/{draftId}/publish", new
        {
            expectedDraftRevision = (long)preflight["draftRevision"]!,
            expectedDefinitionHeadVersionId = (string?)preflight["definitionHeadVersionId"],
            version = Required(preflight, "reviewedVersion"),
            reviewToken = Required(preflight, "reviewToken"),
            idempotencyKey = Guid.NewGuid().ToString()
        });
        Assert.Equal("Applied", (string?)published["status"]);
        return new ReusablePublication(
            Required(created, "definition", "definitionId"),
            Required(created, "definition", "activityTypeKey"),
            Required(published, "outcome", "definitionVersionId"));
    }

    private static async Task SignInAsync(HttpClient client)
    {
        await PostAsync(client, "_elsa/identity/login", new { username = "admin", password = "Password123!" });
    }

    private static async Task<string> ActivityVersionIdAsync(HttpClient client, string typeKey)
    {
        var catalog = await GetAsync(client, $"design/activities/definitions?search={Uri.EscapeDataString(typeKey)}");
        var item = catalog["items"]?.AsArray().FirstOrDefault(x =>
            StringComparer.Ordinal.Equals((string?)x?["definition"]?["activityTypeKey"], typeKey));
        return Required(item, "definition", "headVersionId");
    }

    private static async Task<JsonNode> WaitForCompletionAsync(HttpClient client, string executionId)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        do
        {
            var result = await GetAsync(client, $"runtime/workflows/instances/{executionId}");
            if ((string?)result["instance"]?["status"] is "Completed" or "Finished" or "Faulted" or "Cancelled")
                return result;
            await Task.Delay(500);
        } while (DateTimeOffset.UtcNow < deadline);
        throw new TimeoutException("The shared-resource workflow did not reach a terminal state.");
    }

    private static async Task<JsonNode> GetAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        Assert.True(response.IsSuccessStatusCode, $"GET {path} returned {(int)response.StatusCode}.");
        return (await response.Content.ReadFromJsonAsync<JsonNode>())!;
    }

    private static async Task<JsonNode> PostAsync(HttpClient client, string path, object body)
    {
        using var response = await client.PostAsJsonAsync(path, body);
        Assert.True(response.IsSuccessStatusCode, $"POST {path} returned {(int)response.StatusCode}.");
        return (await response.Content.ReadFromJsonAsync<JsonNode>())!;
    }

    private static string Required(JsonNode? node, params string[] path)
    {
        foreach (var segment in path)
            node = node?[segment];
        return (string?)node ?? throw new InvalidOperationException($"Response lacks {string.Join('.', path)}.");
    }

    private sealed record ReusablePublication(string DefinitionId, string TypeKey, string VersionId);
}
