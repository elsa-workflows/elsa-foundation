using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Elsa.Activities.Bpmn.Hosting.Tests;

/// <summary>Actual authored API/host control. This does not substitute for the later Studio browser journey.</summary>
public sealed class DirectBpmnStudioPublicationTests
{
    [Fact]
    public async Task Saved_authored_version_publishes_directly_and_recovers_its_human_wait_after_actual_restart()
    {
        await using var host = await OwnedWorkbench.StartAsync(directBpmn: true, authoredDirectPublication: true);
        await host.PostAsync("/interchange/bpmn/versions/unknown/publish-direct", new { requestId = "not-authorized" }, HttpStatusCode.Unauthorized);
        await host.LoginAsync();
        await host.PostAsync("/interchange/bpmn/versions/unknown/publish-direct", new { requestId = "missing" }, HttpStatusCode.NotFound);
        var capabilities = await host.GetAsync("/capabilities");
        Assert.Contains("elsa.api.bpmn-publishing", capabilities.ToJsonString(), StringComparison.Ordinal);
        Assert.Contains("elsa.api.bpmn-catalog", capabilities.ToJsonString(), StringComparison.Ordinal);
        var catalog = await host.GetAsync("/design/activities/definitions?search=Elsa.Activities.Bpmn.Activities.BpmnProcess");
        var activity = Assert.Single(catalog["items"]!.AsArray(), item =>
            item!["definition"]!["activityTypeKey"]!.GetValue<string>() == "Elsa.Activities.Bpmn.Activities.BpmnProcess");
        var metadata = "{\"formatVersion\":1,\"kind\":\"human\"}";
        var state = new
        {
            variables = Array.Empty<object>(), inputs = Array.Empty<object>(), outputs = Array.Empty<object>(),
            rootActivity = new
            {
                nodeId = "direct-root", activityVersionId = activity!["definition"]!["headVersionId"]!.GetValue<string>(),
                inputs = Array.Empty<object>(), outputs = Array.Empty<object>(),
                structure = new
                {
                    kind = "elsa.bpmn.structure", schemaVersion = "1.0.0",
                    payload = new
                    {
                        activities = Array.Empty<object>(),
                        elements = new object[] { new { elementId = "start", elementType = "startEvent" },
                            new { elementId = "human", elementType = "userTask", name = "Review order", properties = new Dictionary<string, string> { ["elsa.direct.binding"] = metadata } },
                            new { elementId = "end", elementType = "endEvent" } },
                        sequenceFlows = new[] { new { flowId = "to-human", sourceRef = "start", targetRef = "human" }, new { flowId = "to-end", sourceRef = "human", targetRef = "end" } },
                        diagram = new { shapes = new { human = new { x = 240, y = 0, width = 100, height = 80 } } }
                    }
                }
            }
        };
        var submitted = await host.PostAsync("/design/workflows/definitions/submit", new { name = "Authored direct human process", state });
        var draftId = submitted["draftId"]!.GetValue<string>();
        var reopened = await host.GetAsync("/design/workflows/drafts/" + draftId);
        Assert.Equal(metadata, reopened["state"]!["rootActivity"]!["structure"]!["payload"]!["elements"]![1]!["properties"]!["elsa.direct.binding"]!.GetValue<string>());
        var promoted = await host.PostAsync("/design/workflows/drafts/" + draftId + "/promote", new { operationKey = "promote-direct" });
        var versionId = promoted["id"]!.GetValue<string>();
        var publicationPath = "/interchange/bpmn/versions/" + versionId + "/publish-direct";
        // The route and normalized principal choose the source/caller, never body identity lookalikes.
        var request = new { requestId = "publish-direct", versionId = "body-cannot-choose-source", actorId = "body-cannot-choose-author", tenantId = "body-cannot-choose-tenant" };
        var published = await host.PostAsync(publicationPath, request);
        Assert.True(JsonNode.DeepEquals(published, await host.PostAsync(publicationPath, request)));
        var publications = await host.GetAsync("/bpmn/publications?limit=500&artifactId=" + Uri.EscapeDataString(published["executable"]!["artifactId"]!.GetValue<string>()));
        Assert.Equal(published["sourceReferenceId"]!.GetValue<string>(), Assert.Single(publications["items"]!.AsArray())!["sourceReferenceId"]!.GetValue<string>());
        var exported = await host.PostAsync("/interchange/bpmn/export", new { processNode = promoted["state"]!["rootActivity"], processId = "round-trip" });
        var imported = await host.PostAsync("/interchange/bpmn/import", new { xml = exported["xml"]!.GetValue<string>(), processId = "round-trip" });
        Assert.Equal(metadata, imported["processNode"]!["structure"]!["payload"]!["elements"]![1]!["properties"]!["elsa.direct.binding"]!.GetValue<string>());
        var started = await host.PostAsync("/bpmn/executions", new { sourceReferenceId = published["sourceReferenceId"]!.GetValue<string>(), requestId = "start-direct" });
        var path = "/bpmn/executions/" + started["executionId"]!.GetValue<string>();
        var waiting = await host.GetAsync(path);
        var work = Assert.Single((await host.GetAsync(path + "/work"))["items"]!.AsArray())!;
        Assert.Equal("human", work["elementId"]!.GetValue<string>());
        var firstProcess = host.ProcessId;
        await host.StopAsync();
        var waitRuntime = await host.SnapshotAsync("authored-wait-runtime.sqlite.gz");
        var waitDesign = await host.SnapshotAsync("authored-wait-design.sqlite.gz", design: true);
        await host.RestartAsync();
        Assert.NotEqual(firstProcess, host.ProcessId);
        await host.LoginAsync();
        var executions = await host.GetAsync("/bpmn/executions?artifactId=" + Uri.EscapeDataString(published["executable"]!["artifactId"]!.GetValue<string>()));
        var discovered = Assert.Single(executions["items"]!.AsArray())!;
        Assert.Equal(started["executionId"]!.GetValue<string>(), discovered["executionId"]!.GetValue<string>());
        Assert.Equal(waiting["revision"]!.GetValue<long>(), discovered["revision"]!.GetValue<long>());
        Assert.True(JsonNode.DeepEquals(waiting, await host.GetAsync(path)));
        Assert.True(JsonNode.DeepEquals(published, await host.PostAsync(publicationPath, request)));
        var completionPath = path + "/work/" + work["invocationId"]!.GetValue<string>() + "/complete";
        var command = new { requestId = "complete-direct", attemptId = work["attemptId"]!.GetValue<string>() };
        var completed = await host.PostAsync(completionPath, command);
        var terminal = await host.GetAsync(path);
        Assert.Equal("TerminalSettled", terminal["status"]!.GetValue<string>());
        Assert.Equal("Completed", terminal["result"]!["status"]!.GetValue<string>());
        await host.RestartAsync();
        await host.LoginAsync();
        Assert.True((await host.PostAsync(completionPath, command))["replay"]!.GetValue<bool>());
        Assert.True(JsonNode.DeepEquals(terminal, await host.GetAsync(path)));
        await host.StopAsync();
        await using (var database = new SqliteConnection($"Data Source={host.RuntimeDatabase};Mode=ReadOnly;Pooling=False"))
        {
            await database.OpenAsync();
            foreach (var table in new[] { "elsa_runtime_workflow_execution_state", "elsa_runtime_activity_execution_state", "elsa_runtime_bookmark_state" })
            {
                await using var count = database.CreateCommand(); count.CommandText = $"SELECT COUNT(*) FROM \"{table}\"";
                Assert.Equal(0L, (long)(await count.ExecuteScalarAsync())!);
            }
        }
        var completeRuntime = await host.SnapshotAsync("authored-completed-runtime.sqlite.gz");
        var completeDesign = await host.SnapshotAsync("authored-completed-design.sqlite.gz", design: true);
        var directory = Environment.GetEnvironmentVariable("ELSA_BPMN_HOST_EVIDENCE_DIRECTORY");
        if (directory is not null)
            await File.WriteAllTextAsync(Path.Combine(directory, "authored-direct-publication.json"), JsonSerializer.Serialize(new
            {
                sourceRevision = Environment.GetEnvironmentVariable("ELSA_BPMN_HOST_SOURCE_REVISION"),
                profile = "saved/promoted authored version → optional direct publisher → actual Workbench restart and completion replay",
                studioBrowserProved = false, firstProcess, binaries = host.DescribeBinaries(Environment.GetEnvironmentVariable("ELSA_BPMN_HOST_SOURCE_REVISION"), includeBpmnAuthoring: true),
                submitted, reopened, promoted, published, publications, started, waiting, work, executions, completed, terminal,
                snapshots = new[] { waitRuntime, waitDesign, completeRuntime, completeDesign },
                nativeWorkflowRows = 0, nativeActivityRows = 0, nativeBookmarkRows = 0
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
    }
}
