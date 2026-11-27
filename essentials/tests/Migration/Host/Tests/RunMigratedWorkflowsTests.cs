using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Activities.Http.Activities;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Sequence.Activities;
using Elsa.Migration.Host.Tests.Support;
using Xunit;

namespace Elsa.Migration.Host.Tests;

/// <summary>
/// Spec 1236 US1 (run my migrated workflows), in process: each test starts the host on a temporary data folder and migrated
/// folder. The runs share no state, but they start one at a time to keep the machine's load down.
/// </summary>
[Collection(HostRunsCollection.Name)]
public sealed class RunMigratedWorkflowsTests
{
    [Fact]
    public async Task The_sample_is_published_and_its_HTTP_trigger_answers()
    {
        var folder = InProcessRun.NewMigratedFolder(withSample: true);
        await using var host = await InProcessRun.StartAsync(folder);

        await AssertReady(host);
        using var response = await host.Client.GetAsync("/workflows/http/hello");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        Assert.Equal("Hello from the Elsa 3 migration host", (string?)body["message"]);
    }

    [Fact]
    public async Task A_composite_from_the_activities_file_that_uses_another_activity_runs_inside_a_migrated_workflow()
    {
        var folder = InProcessRun.NewMigratedFolder(withSample: false);
        const string compositeKey = "Acme.Greeting";
        File.WriteAllText(folder.ActivitiesFile, CompositeCatalog(compositeKey, CatalogIds.VersionIdOf(typeof(WriteLine))));
        File.WriteAllText(Path.Combine(folder.WorkflowsFolder, "acme-greeting.json"), WorkflowUsing(CatalogIds.VersionIdOf(compositeKey, "1.0.0")));
        await using var host = await InProcessRun.StartAsync(folder);

        await AssertReady(host);
        using var response = await host.Client.GetAsync("/workflows/http/acme-greeting");

        Assert.True(response.StatusCode == HttpStatusCode.OK,
            $"Expected 200 from the migrated workflow, got {(int)response.StatusCode}. Log:{Environment.NewLine}{host.Logs.Text}");
        Assert.Contains("composite ran", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var executed = await ExecutedActivityTypes(host, "wfdef_acme-greeting");
        Assert.True(executed.Contains(compositeKey) && executed.Contains(typeof(WriteLine).FullName!),
            $"Expected the run to execute {compositeKey} and its WriteLine body; it executed {string.Join(", ", executed)}.");
    }

    [Fact]
    public void An_environment_variable_overrides_a_committed_setting_and_leaves_the_others()
    {
        const string modeKey = "CShells:Shells:default:Features:WorkflowsRuntimeCheckpointPersistence:Mode";
        const string variable = "CShells__Shells__default__Features__WorkflowsRuntimeCheckpointPersistence__Mode";
        var root = InProcessRun.NewRoot();
        Environment.SetEnvironmentVariable(variable, "Balanced");
        try
        {
            var builder = MigrationHost.CreateBuilder(["--environment=Development", $"--contentRoot={InProcessRun.CommittedContentRoot(root)}"]);

            Assert.Equal("Balanced", builder.Configuration[modeKey]);
            Assert.Equal("50", builder.Configuration["CShells:Shells:default:Features:WorkflowsRuntimeCheckpointPersistence:MaxActivitiesPerCommit"]);
            Assert.Equal("elsa3-migration", builder.Configuration["CShells:Shells:default:Features:JsonWorkflowReconciliation:Options:SourceId"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
            InProcessRun.DeleteRoot(root);
        }
    }

    [Fact]
    public async Task An_unknown_feature_keeps_the_host_from_becoming_ready_and_the_log_names_it()
    {
        var folder = InProcessRun.NewMigratedFolder(withSample: false);
        await using var host = await InProcessRun.StartAsync(folder, new Dictionary<string, string>
        {
            ["CShells:Shells:default:Features:AcmeMissingActivities:Enabled"] = "true"
        });

        await AssertNotReady(host);
        Assert.Contains(host.Logs.Lines, line => line.Contains("could not activate shell 'default'", StringComparison.Ordinal)
                                                  && line.Contains("AcmeMissingActivities", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_missing_workflows_folder_keeps_the_host_from_becoming_ready_and_the_log_names_it()
    {
        var folder = InProcessRun.NewMigratedFolder(withSample: false);
        var missing = Path.Combine(folder.Root, "migrated", "no-such-workflows");
        await using var host = await InProcessRun.StartAsync(folder, new Dictionary<string, string>
        {
            [InProcessRun.WorkflowsFolderKey] = missing
        });

        await AssertNotReady(host);
        Assert.Contains(host.Logs.Lines, line => line.Contains("could not activate shell 'default'", StringComparison.Ordinal)
                                                  && line.Contains(missing, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_missing_activities_file_keeps_the_host_from_becoming_ready_and_the_log_names_it()
    {
        var folder = InProcessRun.NewMigratedFolder(withSample: false);
        File.Delete(folder.ActivitiesFile);
        await using var host = await InProcessRun.StartAsync(folder);

        await AssertNotReady(host);
        Assert.Contains(host.Logs.Lines, line => line.Contains("could not activate shell 'default'", StringComparison.Ordinal)
                                                  && line.Contains(folder.ActivitiesFile, StringComparison.Ordinal));
    }

    [Fact]
    public void The_sample_workflow_names_the_catalog_versions_of_the_activities_it_uses()
    {
        var sample = File.ReadAllText(Path.Combine(HostPaths.ProjectFolder, "migrated", "workflows", "hello.json"));

        Assert.Contains(CatalogIds.VersionIdOf(typeof(Sequence)), sample, StringComparison.Ordinal);
        Assert.Contains(CatalogIds.VersionIdOf(typeof(HttpEndpoint)), sample, StringComparison.Ordinal);
        Assert.Contains(CatalogIds.VersionIdOf(typeof(WriteHttpResponse)), sample, StringComparison.Ordinal);
    }

    /// <summary>Signs in as the development administrator, as the JTest template does, and reads the run's activity types.</summary>
    private static async Task<HashSet<string>> ExecutedActivityTypes(InProcessRun host, string definitionId)
    {
        var token = await host.SignInAsync();
        using var page = await host.GetJsonAsync($"/runtime/workflows/instances/page?definitionId={definitionId}&take=10", token);
        var instance = page.RootElement.GetProperty("items").EnumerateArray().Single();
        var executionId = instance.GetProperty("workflowExecutionId").GetString();
        using var executions = await host.GetJsonAsync($"/runtime/workflows/instances/{executionId}/activity-executions?limit=500", token);
        return executions.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("execution").GetProperty("activityType").GetString()!)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static async Task AssertReady(InProcessRun host)
    {
        using var ready = await host.Client.GetAsync("/health/ready");
        Assert.True(ready.StatusCode == HttpStatusCode.OK,
            $"The host is not ready: {await ready.Content.ReadAsStringAsync()}{Environment.NewLine}{host.Logs.Text}");
    }

    private static async Task AssertNotReady(InProcessRun host)
    {
        using var ready = await host.Client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
    }

    /// <summary>A JSON activity catalog with one Graph composite whose body is a WriteLine, as the CLI writes a reusable.</summary>
    private static string CompositeCatalog(string activityTypeKey, string writeLineVersionId) =>
        new JsonArray(new JsonObject
        {
            ["version"] = "1.0.0",
            ["activityTypeKey"] = activityTypeKey,
            ["displayName"] = "Acme greeting",
            ["category"] = "Acme",
            ["providerKey"] = "elsa.activity-graph",
            ["providerSchemaVersion"] = "1",
            ["consumerKey"] = "elsa.graph-activity",
            ["consumerSchemaVersion"] = "1",
            ["descriptor"] = new JsonObject
            {
                ["rootActivity"] = Node("body", writeLineVersionId, Literal("text", "Acme greeting from the composite")),
                ["variables"] = new JsonArray(),
                ["outputMappings"] = new JsonArray(),
                ["outcomeMappings"] = new JsonArray()
            },
            ["inputs"] = new JsonArray(),
            ["outputs"] = new JsonArray(),
            ["designFacets"] = new JsonArray(new JsonObject
            {
                ["kind"] = "elsa.outcomes",
                ["schemaVersion"] = "1",
                ["payload"] = new JsonObject
                {
                    ["ports"] = new JsonArray(new JsonObject { ["referenceKey"] = "done", ["name"] = "Done", ["type"] = "outcome" })
                }
            })
        }).ToJsonString();

    /// <summary>A workflow catalog: an HTTP trigger, the composite, then the response.</summary>
    private static string WorkflowUsing(string compositeVersionId)
    {
        var root = Node("acme-root", CatalogIds.VersionIdOf(typeof(Sequence)));
        root["structure"] = new JsonObject
        {
            ["kind"] = "elsa.sequence.structure",
            ["schemaVersion"] = "1.0.0",
            ["payload"] = new JsonObject
            {
                ["activities"] = new JsonArray(
                    Node("acme-endpoint", CatalogIds.VersionIdOf(typeof(HttpEndpoint)),
                        Literal("Path", "acme-greeting"), Literal("SupportedMethods", new JsonArray("GET")),
                        Literal("CanStartWorkflow", true), Literal("ResponseMode", "Sync")),
                    Node("acme-greeting", compositeVersionId),
                    Node("acme-response", CatalogIds.VersionIdOf(typeof(WriteHttpResponse)),
                        Literal("StatusCode", 200), Literal("ContentType", "text/plain"), Literal("Body", "composite ran")))
            }
        };

        return new JsonArray(new JsonObject
        {
            ["definitionId"] = "wfdef_acme-greeting",
            ["name"] = "acme-greeting",
            ["version"] = "1.0.0",
            ["state"] = new JsonObject
            {
                ["rootActivity"] = root,
                ["variables"] = new JsonArray(),
                ["inputs"] = new JsonArray(),
                ["outputs"] = new JsonArray()
            },
            ["sourceCreatedAt"] = "2026-11-27T00:00:00Z",
            ["deleted"] = false
        }).ToJsonString();
    }

    private static JsonObject Node(string nodeId, string activityVersionId, params JsonObject[] inputs) => new()
    {
        ["nodeId"] = nodeId,
        ["activityVersionId"] = activityVersionId,
        ["inputs"] = new JsonArray(inputs.Cast<JsonNode>().ToArray()),
        ["outputs"] = new JsonArray()
    };

    private static JsonObject Literal(string referenceKey, JsonNode? value) => new()
    {
        ["referenceKey"] = referenceKey,
        ["value"] = new JsonObject { ["value"] = value, ["expressionType"] = "Literal" }
    };
}

/// <summary>Host runs start one at a time.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class HostRunsCollection
{
    public const string Name = "Migration host runs";
}
