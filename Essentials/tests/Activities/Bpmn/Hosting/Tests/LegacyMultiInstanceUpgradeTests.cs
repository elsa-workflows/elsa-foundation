using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
using static Elsa.Activities.Bpmn.Hosting.Tests.LegacyUpgradeInspection;

namespace Elsa.Activities.Bpmn.Hosting.Tests;

public sealed class LegacyMultiInstanceUpgradeTests
{
    [Fact]
    public async Task Actual_partial_multi_instance_calls_preserve_iterations_and_finish_after_restart()
    {
        var matrix = Environment.GetEnvironmentVariable("ELSA_BPMN_LEGACY_MATRIX_DIRECTORY"); var fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var manifestPath = matrix is null ? Path.Combine(fixtures, "legacy-baseline-manifest.json") : Path.Combine(matrix, "manifest.json");
        var input = Path.Combine(matrix ?? fixtures, "multi-instance-and-studio"); var manifest = JsonNode.Parse(await File.ReadAllTextAsync(manifestPath))!;
        var inputs = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in new[] { "runtime.sqlite.gz", "design.sqlite.gz", "api.json", "physical-checked.json" })
        {
            var hash = Hash(await File.ReadAllBytesAsync(Path.Combine(input, name))); inputs.Add("multi-instance-and-studio/" + name, hash);
            Assert.Equal(Assert.Single(manifest["assets"]!.AsArray(), asset => asset!["path"]!.GetValue<string>() == "multi-instance-and-studio/" + name)!["sha256"]!.GetValue<string>(), hash);
        }
        var baseline = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(input, "api.json")))!;
        var physical = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(input, "physical-checked.json")))!;
        Assert.True(baseline["passed"]!.GetValue<bool>()); Assert.True(physical["passed"]!.GetValue<bool>());
        var api = new JsonObject { ["schemaVersion"] = 1, ["passed"] = true, ["phase"] = "ActualUpgradedMultiInstanceCalls", ["cases"] = new JsonArray() };
        foreach (var original in baseline["cases"]!.AsArray())
        {
            var item = new JsonObject(original!.AsObject().Where(pair => pair.Value is JsonValue).Select(pair => new KeyValuePair<string, JsonNode?>(pair.Key, pair.Value!.DeepClone())));
            item["originalLoopId"] = Assert.Single(physical["cases"]!.AsArray(), item => item!["parentWorkflowExecutionId"]!.GetValue<string>() == original["parentWorkflowExecutionId"]!.GetValue<string>())!["loopId"]!.DeepClone();
            item["originalChildPin"] = new JsonObject(new[] { "artifactId", "artifactHash", "artifactVersion", "definitionId", "definitionVersionId" }
                .Select(field => new KeyValuePair<string, JsonNode?>(field, original["children"]![0]!["instance"]![field]!.DeepClone())));
            api["cases"]!.AsArray().Add(item);
        }
        var temporary = Directory.CreateTempSubdirectory("elsa-mi-upgrade-");
        try
        {
            foreach (var name in new[] { "runtime", "design" })
            {
                await using var source = File.OpenRead(Path.Combine(input, name + ".sqlite.gz")); await using var gzip = new GZipStream(source, CompressionMode.Decompress);
                await using var target = File.Create(Path.Combine(temporary.FullName, name + ".sqlite")); await gzip.CopyToAsync(target);
            }
            var artifacts = await ArtifactsAsync(Path.Combine(temporary.FullName, "runtime.sqlite"), immutable: true); var originalMigrations = await MigrationsAsync(Path.Combine(temporary.FullName, "runtime.sqlite"), immutable: true);
            await using var host = await OwnedWorkbench.StartAsync(directBpmn: true, legacySnapshotDirectory: temporary.FullName, recoverySweepSeconds: 1);
            AssertStandardHost(host); var processIds = new List<int> { host.ProcessId }; await host.LoginAsync();
            var originalViews = await RefreshAsync(host, api);
            foreach (var item in baseline["cases"]!.AsArray())
            {
                SameView(item!["parent"]!, originalViews[item["parentWorkflowExecutionId"]!.GetValue<string>()]!);
                foreach (var child in item["children"]!.AsArray()) SameView(child!, originalViews[child!["instance"]!["workflowExecutionId"]!.GetValue<string>()]!);
            }
            var stages = new List<JsonNode>(); var recovered = await CaptureAsync("recovered", 1, host, api, temporary.FullName); stages.Add(recovered);
            foreach (var original in physical["cases"]!.AsArray())
            {
                var current = Assert.Single(recovered["proof"]!["multiInstance"]!.AsArray(), item => item!["parentWorkflowExecutionId"]!.GetValue<string>() == original!["parentWorkflowExecutionId"]!.GetValue<string>())!;
                Assert.Equal(original!["liveIterationIds"]!.AsArray().Select(item => item!.GetValue<string>()).Order(StringComparer.Ordinal), current["calls"]!.AsArray().Where(call => call!["call"]!["status"]!.GetValue<int>() == 3).Select(call => call!["call"]!["iterationId"]!.GetValue<string>()).Order(StringComparer.Ordinal));
                foreach (var call in original["calls"]!.AsArray())
                {
                    var observed = Assert.Single(current["calls"]!.AsArray(), item => item!["call"]!["invocationId"]!.GetValue<string>() == call!["callActivityExecutionId"]!.GetValue<string>())!;
                    Assert.Equal(call!["iterationId"]!.GetValue<string>(), observed["call"]!["iterationId"]!.GetValue<string>()); Assert.Equal(call["childWorkflowExecutionId"]!.GetValue<string>(), observed["child"]!["workflowExecutionId"]!.GetValue<string>());
                    foreach (var wait in call["physicalBookmarks"]!.AsArray())
                    {
                        var actual = Assert.Single(observed["physicalBookmarks"]!.AsArray(), item => item!["state"]!["bookmarkId"]!.GetValue<string>() == wait!["bookmarkId"]!.GetValue<string>())!;
                        Assert.Equal(wait!["rowRevision"]!.GetValue<long>(), actual["generation"]!.GetValue<long>());
                    }
                }
            }
            await host.RestartAsync(); processIds.Add(host.ProcessId); await host.LoginAsync(); await SameViewsAsync(host, recovered["views"]!);
            var firstDeliveries = await CompleteOneAsync(host, api, recovered["proof"]!, 2);
            var progressed = await CaptureAsync("progressed", 2, host, api, temporary.FullName); stages.Add(progressed);
            foreach (var item in baseline["cases"]!.AsArray()) SameView(originalViews[item!["completedChildWorkflowExecutionId"]!.GetValue<string>()]!, progressed["views"]![item["completedChildWorkflowExecutionId"]!.GetValue<string>()]!);
            await host.RestartAsync(); processIds.Add(host.ProcessId); await host.LoginAsync(); await SameViewsAsync(host, progressed["views"]!);
            var resumed = await CaptureAsync("resumed", 2, host, api, temporary.FullName); stages.Add(resumed);
            SameView(progressed["proof"]!["multiInstance"]!, resumed["proof"]!["multiInstance"]!);
            var lastDeliveries = await CompleteOneAsync(host, api, resumed["proof"]!, 3);
            var finished = await CaptureAsync("finished", 3, host, api, temporary.FullName); stages.Add(finished);
            foreach (var item in baseline["cases"]!.AsArray()) SameView(originalViews[item!["completedChildWorkflowExecutionId"]!.GetValue<string>()]!, finished["views"]![item["completedChildWorkflowExecutionId"]!.GetValue<string>()]!);
            Assert.Equal(16, finished["views"]!.AsObject().Count);
            await host.StopAsync(); var designBackup = Path.Combine(temporary.FullName, "finished-design.sqlite"); await BackupAsync(host.DesignDatabase, designBackup);
            var designSnapshot = await ExportSnapshotAsync(designBackup, "finished-design.sqlite.gz", "legacy-multi-instance");
            var migrations = await MigrationsAsync(host.RuntimeDatabase); Assert.All(originalMigrations, migration => Assert.Contains(migration, migrations)); Assert.Equal(artifacts, await ArtifactsAsync(host.RuntimeDatabase));
            await host.RestartAsync(); processIds.Add(host.ProcessId); await host.LoginAsync(); await SameViewsAsync(host, finished["views"]!); await host.StopAsync(); Assert.Equal(4, processIds.Distinct().Count());
            if (Environment.GetEnvironmentVariable("ELSA_BPMN_HOST_EVIDENCE_DIRECTORY") is { } evidence)
            {
                var source = Environment.GetEnvironmentVariable("ELSA_BPMN_HOST_SOURCE_REVISION"); if (source is not null) Assert.EndsWith("+" + source, FileVersionInfo.GetVersionInfo(host.ReaderAssemblyPath).ProductVersion);
                var output = Path.Combine(evidence, "legacy-multi-instance"); Directory.CreateDirectory(output);
                await File.WriteAllTextAsync(Path.Combine(output, "process.json"), JsonSerializer.Serialize(new { formatVersion = 1, kind = "actual-legacy-multi-instance-upgrade", capturedAt = DateTimeOffset.UtcNow, sourceRevision = source,
                    baselineSourceRevision = baseline["sourceRevision"]!.GetValue<string>(), baselineManifestSha256 = Hash(await File.ReadAllBytesAsync(manifestPath)), baselineAssets = inputs, originalPhysical = physical["cases"],
                    testOnlyRecoveryProbePresent = false, processIds, originalMigrations, migrations, retainedArtifacts = artifacts, stages, firstDeliveries, lastDeliveries, designSnapshot, binaries = host.DescribeBinaries(source),
                    reader = new { file = Path.GetFileName(host.ReaderAssemblyPath), productVersion = FileVersionInfo.GetVersionInfo(host.ReaderAssemblyPath).ProductVersion, sha256 = Hash(await File.ReadAllBytesAsync(host.ReaderAssemblyPath)) },
                    limits = new[] { "Actual legacy native/BpmnProcess MI recovery only; no direct-model MI or native/ETL bridge is proved.", "Actual Studio and final-source qualification remain separate gates." }
                }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }) + Environment.NewLine);
            }
        }
        finally { temporary.Delete(recursive: true); }
        foreach (var pair in inputs) Assert.Equal(pair.Value, Hash(await File.ReadAllBytesAsync(Path.Combine(input, Path.GetFileName(pair.Key)))));
    }

    private static async Task SameViewsAsync(OwnedWorkbench host, JsonNode views) { foreach (var item in views.AsObject()) SameView(item.Value!, await host.GetAsync("/runtime/workflows/instances/" + item.Key)); }
    private static async Task<JsonObject> RefreshAsync(OwnedWorkbench host, JsonNode api)
    {
        var views = new JsonObject();
        foreach (var item in api["cases"]!.AsArray())
        {
            var parentId = item!["parentWorkflowExecutionId"]!.GetValue<string>(); item["parent"] = await host.GetAsync("/runtime/workflows/instances/" + parentId); views[parentId] = item["parent"]!.DeepClone();
            var children = new JsonArray(); var links = await host.GetAsync("/runtime/workflows/dispatches?parentWorkflowExecutionId=" + parentId);
            foreach (var link in links.AsArray().OrderBy(link => link!["childWorkflowExecutionId"]!.GetValue<string>(), StringComparer.Ordinal))
            {
                var id = link!["childWorkflowExecutionId"]!.GetValue<string>(); var child = await host.GetAsync("/runtime/workflows/instances/" + id); views[id] = child.DeepClone(); children.Add(child);
            }
            item["children"] = children;
        }
        return views;
    }
    private static async Task<JsonNode> CompleteOneAsync(OwnedWorkbench host, JsonNode api, JsonNode proof, int completed)
    {
        var deliveries = new JsonArray();
        foreach (var item in api["cases"]!.AsArray())
        {
            var parentId = item!["parentWorkflowExecutionId"]!.GetValue<string>(); var choice = Assert.Single(proof["multiInstance"]!.AsArray(), choice => choice!["parentWorkflowExecutionId"]!.GetValue<string>() == parentId)!;
            var target = choice["calls"]!.AsArray().First(call => call!["call"]!["status"]!.GetValue<int>() == 3)!; var childId = target["child"]!["workflowExecutionId"]!.GetValue<string>();
            var name = item["eventName"]!.GetValue<string>(); var delivery = await host.PostAsync("/runtime/workflows/stimuli", new { stimulusType = "Event", stimulusHash = "sha256:" + Hash(Encoding.UTF8.GetBytes(name)), mode = "ResumeOnly", correlationId = childId, input = new { eventName = name } });
            Assert.Equal(0, delivery["startedCount"]!.GetValue<int>()); Assert.Equal(1, delivery["resumedCount"]!.GetValue<int>());
            var deadline = DateTimeOffset.UtcNow.AddSeconds(40);
            do
            {
                var view = await host.GetAsync("/runtime/workflows/instances/" + parentId); var calls = view["activities"]!.AsArray().Where(activity => activity!["executableNodeId"]!.GetValue<string>() == "node-call").ToArray();
                if (calls.Count(call => call!["status"]!.GetValue<string>() == "Completed") == completed && calls.Count(call => call!["status"]!.GetValue<string>() == "Suspended") == 3 - completed && view["instance"]!["status"]!.GetValue<string>() == (completed == 3 ? "Completed" : "Running")) break;
                Assert.True(DateTimeOffset.UtcNow < deadline, "The original MI loop did not complete exactly one additional iteration: " + parentId); await Task.Delay(100);
            } while (true);
            deliveries.Add(new JsonObject { ["parentWorkflowExecutionId"] = parentId, ["childWorkflowExecutionId"] = childId, ["callActivityExecutionId"] = target["call"]!["invocationId"]!.DeepClone(), ["iterationId"] = target["call"]!["iterationId"]!.DeepClone(), ["delivery"] = delivery });
        }
        return deliveries;
    }
    private static async Task<JsonNode> CaptureAsync(string name, int completed, OwnedWorkbench host, JsonNode api, string directory)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(40);
        while (true)
        {
            await using var connection = Read(host.RuntimeDatabase); await connection.OpenAsync(); await using var command = connection.CreateCommand(); command.CommandText = "SELECT COUNT(*) FROM elsa_runtime_post_commit_outbox WHERE Status <> 2";
            if ((long)(await command.ExecuteScalarAsync())! == 0) break;
            Assert.True(DateTimeOffset.UtcNow < deadline, "An actual MI source still owes delivery: " + name); await Task.Delay(100);
        }
        var views = await RefreshAsync(host, api); api["completedCount"] = completed; api["sourceRevision"] = FileVersionInfo.GetVersionInfo(host.AssemblyPath).ProductVersion!.Split('+').Last();
        var receipt = Path.Combine(directory, name + "-api.json"); await File.WriteAllTextAsync(receipt, api.ToJsonString()); var database = Path.Combine(directory, name + ".sqlite"); await BackupAsync(host.RuntimeDatabase, database);
        var proof = await CheckedSnapshotAsync(host.ReaderAssemblyPath, database, "--inspect-multi-instance", receipt, completed.ToString(CultureInfo.InvariantCulture));
        var snapshot = await ExportSnapshotAsync(database, name + ".sqlite.gz", "legacy-multi-instance");
        return JsonSerializer.SerializeToNode(new { name, completedCount = completed, views, api = api.DeepClone(), proof, snapshot }, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }
}
