using System.Text.Json;
using System.Text.Json.Nodes;
using Bpmn.Model;
using Elsa.Activities.Bpmn.Core;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Elsa.Activities.Bpmn.Hosting.Tests;

public sealed class RuntimeOnlyParallelBpmnTests
{
    [Fact]
    public async Task Partial_fork_recovers_and_joins_in_the_actual_host_without_design_storage()
    {
        await using var host = await OwnedWorkbench.StartAsync(directBpmn: true, runtimeOnly: true);
        await host.LoginAsync();
        var process = new BpmnProcessDefinition("parallel-process", Elements:
            [new("start", BpmnElementTypes.StartEvent), new("fork", BpmnElementTypes.ParallelGateway),
                new("left", BpmnElementTypes.UserTask, bindingRef: "left-human"), new("right", BpmnElementTypes.UserTask, bindingRef: "right-human"),
                new("join", BpmnElementTypes.ParallelGateway), new("after-join", BpmnElementTypes.UserTask, bindingRef: "joined-human"), new("end", BpmnElementTypes.EndEvent)],
            SequenceFlows: [new("begin", "start", "fork"), new("fork-left", "fork", "left"), new("fork-right", "fork", "right"),
                new("left-join", "left", "join"), new("right-join", "right", "join"), new("joined", "join", "after-join"), new("finish", "after-join", "end")]);
        var definition = new BpmnModelDefinition(1, JsonSerializer.SerializeToElement(process, new JsonSerializerOptions(JsonSerializerDefaults.Web)), ["left-human", "right-human", "joined-human"]);
        var publication = await host.PostAsync("/bpmn/artifacts", new { requestId = "publish-parallel", definition });
        var start = await host.PostAsync("/bpmn/executions", new { sourceReferenceId = publication["sourceReferenceId"]!.GetValue<string>(), requestId = "start" });
        Assert.False(start["pending"]!.GetValue<bool>());
        var path = "/bpmn/executions/" + start["executionId"]!.GetValue<string>();
        var initialWork = (await host.GetAsync(path + "/work"))["items"]!.AsArray();
        Assert.Equal(2, initialWork.Count);
        var left = Assert.Single(initialWork, work => work!["elementId"]!.GetValue<string>() == "left")!;
        var right = Assert.Single(initialWork, work => work!["elementId"]!.GetValue<string>() == "right")!;
        await host.PostAsync(Completion(path, left), new { requestId = "left", attemptId = left["attemptId"]!.GetValue<string>() });
        var partial = await host.GetAsync(path);
        Assert.Equal("Running", partial["status"]!.GetValue<string>());
        Assert.Equal(2, partial["revision"]!.GetValue<long>());
        Assert.True(JsonNode.DeepEquals(right, Assert.Single((await host.GetAsync(path + "/work"))["items"]!.AsArray())));
        Assert.False(File.Exists(host.DesignDatabase));
        var predecessor = host.ProcessId;
        await host.StopAsync();
        var partialSnapshot = await host.SnapshotAsync("partial.sqlite.gz", "runtime-only-parallel");
        await host.RestartAsync();
        Assert.NotEqual(predecessor, host.ProcessId);
        var successor = host.ProcessId;
        await host.LoginAsync();
        var recovered = await host.GetAsync(path);
        Assert.True(JsonNode.DeepEquals(partial, recovered));
        Assert.True(JsonNode.DeepEquals(right, Assert.Single((await host.GetAsync(path + "/work"))["items"]!.AsArray())));
        Assert.True((await host.PostAsync(Completion(path, left), new { requestId = "left", attemptId = left["attemptId"]!.GetValue<string>() }))["replay"]!.GetValue<bool>());
        Assert.True(JsonNode.DeepEquals(partial, await host.GetAsync(path)));
        await host.PostAsync(Completion(path, right), new { requestId = "right", attemptId = right["attemptId"]!.GetValue<string>() });
        var joined = await host.GetAsync(path);
        Assert.Equal("Running", joined["status"]!.GetValue<string>());
        Assert.Equal(3, joined["revision"]!.GetValue<long>());
        var joinWork = Assert.Single((await host.GetAsync(path + "/work"))["items"]!.AsArray())!;
        Assert.Equal("after-join", joinWork["elementId"]!.GetValue<string>());
        Assert.True((await host.PostAsync(Completion(path, right), new { requestId = "right", attemptId = right["attemptId"]!.GetValue<string>() }))["replay"]!.GetValue<bool>());
        Assert.True(JsonNode.DeepEquals(joined, await host.GetAsync(path)));
        Assert.True(JsonNode.DeepEquals(joinWork, Assert.Single((await host.GetAsync(path + "/work"))["items"]!.AsArray())));
        await host.PostAsync(Completion(path, joinWork), new { requestId = "finish", attemptId = joinWork["attemptId"]!.GetValue<string>() });
        var completed = await host.GetAsync(path);
        Assert.Equal("TerminalSettled", completed["status"]!.GetValue<string>());
        Assert.Equal("Completed", completed["result"]!["status"]!.GetValue<string>());
        Assert.Equal(4, completed["revision"]!.GetValue<long>());
        Assert.True(JsonNode.DeepEquals(publication["executable"], completed["executable"]));
        Assert.Equal(publication["incarnationId"]!.GetValue<string>(), completed["materialIncarnationId"]!.GetValue<string>());
        Assert.Empty((await host.GetAsync(path + "/work"))["items"]!.AsArray());
        Assert.True((await host.PostAsync(Completion(path, right), new { requestId = "right", attemptId = right["attemptId"]!.GetValue<string>() }))["replay"]!.GetValue<bool>());
        Assert.True(JsonNode.DeepEquals(completed, await host.GetAsync(path)));
        await host.StopAsync();
        Assert.False(File.Exists(host.DesignDatabase));
        var stores = new List<object>();
        foreach (var file in Directory.EnumerateFiles(host.ContentRoot, "*", SearchOption.AllDirectories)
                     .Where(file => Path.GetExtension(file) is ".db" or ".sqlite"))
        {
            await using var database = new SqliteConnection($"Data Source={file};Mode=ReadOnly;Pooling=False"); await database.OpenAsync();
            await using var tables = database.CreateCommand(); tables.CommandText = "SELECT name FROM sqlite_master WHERE type='table'";
            await using var names = await tables.ExecuteReaderAsync();
            var tableNames = new List<string>();
            while (await names.ReadAsync())
            {
                var name = names.GetString(0); Assert.DoesNotContain("design", name, StringComparison.OrdinalIgnoreCase);
                tableNames.Add(name);
            }
            stores.Add(new { database = Path.GetRelativePath(host.ContentRoot, file), tables = tableNames.Order(StringComparer.Ordinal).ToArray() });
        }
        await using var runtime = new SqliteConnection($"Data Source={host.RuntimeDatabase};Mode=ReadOnly;Pooling=False"); await runtime.OpenAsync();
        foreach (var table in new[] { "elsa_runtime_workflow_execution_state", "elsa_runtime_activity_execution_state", "elsa_runtime_bookmark_state" })
        {
            await using var count = runtime.CreateCommand(); count.CommandText = $"SELECT COUNT(*) FROM \"{table}\"";
            Assert.Equal(0L, (long)(await count.ExecuteScalarAsync())!);
        }
        foreach (var table in new[] { "elsa_runtime_model_input", "elsa_runtime_checkpoint_commit" })
        {
            await using var count = runtime.CreateCommand(); count.CommandText = $"SELECT COUNT(*) FROM \"{table}\"";
            Assert.Equal(4L, (long)(await count.ExecuteScalarAsync())!);
        }
        var completeSnapshot = await host.SnapshotAsync("completed.sqlite.gz", "runtime-only-parallel");
        if (Environment.GetEnvironmentVariable("ELSA_BPMN_HOST_EVIDENCE_DIRECTORY") is { } evidenceRoot)
        {
            var sourceRevision = Environment.GetEnvironmentVariable("ELSA_BPMN_HOST_SOURCE_REVISION");
            var config = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(host.ContentRoot, "shells.json")))!;
            var requestedFeatures = config["CShells"]!["Shells"]!["default"]!["Features"]!.AsObject().Select(item => item.Key).Order(StringComparer.Ordinal).ToArray();
            Assert.DoesNotContain(requestedFeatures, feature => feature.Contains("Design", StringComparison.OrdinalIgnoreCase));
            var evidence = new { formatVersion = 1, sourceRevision, capturedAt = DateTimeOffset.UtcNow,
                profile = "foundation-direct-v1 / production Workbench / single-node mailbox / EF SQLite / no Design storage",
                originalProcessId = predecessor, restartedProcessId = successor, publication, start, left, right, partial, recovered, joined, joinWork, completed,
                requestedFeatures, stores, nativeWorkflowRows = 0, nativeActivityRows = 0, nativeBookmarkRows = 0,
                modelInputReceipts = 4, checkpointMarkers = 4, logicalJoinWaits = 1,
                binaries = host.DescribeBinaries(sourceRevision), snapshots = new[] { partialSnapshot, completeSnapshot },
                limits = new[] { "Same-source process restart; no changed-binary or legacy-upgrade acceptance.",
                    "Clustered placement, timer/broadcast/cancellation/settlement, native/ETL bindings and Studio remain open." } };
            var directory = Path.Combine(evidenceRoot, "runtime-only-parallel"); Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, "process.json"), JsonSerializer.Serialize(evidence,
                new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }) + Environment.NewLine);
        }
    }

    private static string Completion(string path, JsonNode work) => path + "/work/" + work["invocationId"]!.GetValue<string>() + "/complete";
}
