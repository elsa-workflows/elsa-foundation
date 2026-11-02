using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
using static Elsa.Activities.Bpmn.Hosting.Tests.LegacyUpgradeInspection;

namespace Elsa.Activities.Bpmn.Hosting.Tests;

public sealed class LegacyControlsUpgradeTests
{
    [Fact]
    public async Task Actual_called_controls_and_compensation_recover_original_streams_and_continue_once()
    {
        var matrix = Environment.GetEnvironmentVariable("ELSA_BPMN_LEGACY_MATRIX_DIRECTORY");
        var fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var manifestPath = matrix is null ? Path.Combine(fixtures, "legacy-baseline-manifest.json") : Path.Combine(matrix, "manifest.json");
        var input = Path.Combine(matrix ?? fixtures, "legacy-controls"); var manifest = JsonNode.Parse(await File.ReadAllTextAsync(manifestPath))!;
        var names = new[] { "runtime.sqlite.gz", "design.sqlite.gz", "called-control-prepare.json", "concurrent-prepare.json", "compensation-handler-b.json", "escalations-checked.json", "concurrent-checked.json", "compensation-checked.json" };
        var inputs = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            var sha = Hash(await File.ReadAllBytesAsync(Path.Combine(input, name))); inputs.Add("legacy-controls/" + name, sha);
            Assert.Equal(Assert.Single(manifest["assets"]!.AsArray(), asset => asset!["path"]!.GetValue<string>() == "legacy-controls/" + name)!["sha256"]!.GetValue<string>(), sha);
        }
        var originalEscalations = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(input, "called-control-prepare.json")))!;
        var originalConcurrent = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(input, "concurrent-prepare.json")))!;
        var originalCompensation = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(input, "compensation-handler-b.json")))!;
        var escalations = Cases(originalEscalations); var concurrent = Cases(originalConcurrent);
        var compensation = new JsonObject { ["schemaVersion"] = 1, ["passed"] = true, ["case"] = Scalars(originalCompensation["case"]!) };
        var temporary = Directory.CreateTempSubdirectory("elsa-controls-upgrade-");
        try
        {
            foreach (var name in new[] { "runtime", "design" })
            {
                await using var source = File.OpenRead(Path.Combine(input, name + ".sqlite.gz")); await using var gzip = new GZipStream(source, CompressionMode.Decompress);
                await using var target = File.Create(Path.Combine(temporary.FullName, name + ".sqlite")); await gzip.CopyToAsync(target);
            }
            var artifacts = await ArtifactsAsync(Path.Combine(temporary.FullName, "runtime.sqlite"), immutable: true);
            var originalMigrations = await MigrationsAsync(Path.Combine(temporary.FullName, "runtime.sqlite"), immutable: true);
            await using var host = await OwnedWorkbench.StartAsync(directBpmn: true, legacySnapshotDirectory: temporary.FullName, recoverySweepSeconds: 1);
            AssertStandardHost(host); var processIds = new List<int> { host.ProcessId }; await host.LoginAsync();
            await RefreshAsync(host, escalations, concurrent, compensation);
            foreach (var original in originalEscalations["cases"]!.AsArray())
            {
                var current = Assert.Single(escalations["cases"]!.AsArray(), item => item!["parentWorkflowExecutionId"]!.GetValue<string>() == original!["parentWorkflowExecutionId"]!.GetValue<string>())!;
                SameView(original!["parent"]!, current["parent"]!); SameView(original["child"]!, current["child"]!);
            }
            foreach (var original in originalConcurrent["cases"]!.AsArray())
            {
                var current = Assert.Single(concurrent["cases"]!.AsArray(), item => item!["parentWorkflowExecutionId"]!.GetValue<string>() == original!["parentWorkflowExecutionId"]!.GetValue<string>())!;
                SameView(original!["parent"]!, current["parent"]!);
                foreach (var child in original["children"]!.AsArray()) SameView(child!, Assert.Single(current["children"]!.AsArray(), candidate => candidate!["instance"]!["workflowExecutionId"]!.GetValue<string>() == child!["instance"]!["workflowExecutionId"]!.GetValue<string>())!);
            }
            SameView(originalCompensation["workflow"]!, compensation["workflow"]!);
            var stages = new List<JsonNode>();
            var recovered = await CaptureAsync("recovered", host, temporary.FullName, escalations, concurrent, compensation, "ObserveEscalations", "Prepare", "HandlerB"); stages.Add(recovered);
            foreach (var family in new[] { ("escalations", "calledWorkflows"), ("concurrent", "calledWorkflows"), ("compensation", "compensation") })
            {
                var baseline = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(input, family.Item1 + "-checked.json")))!;
                SameView(baseline[family.Item2]!, recovered[family.Item1 + "Proof"]![family.Item2]!);
            }
            var choices = recovered["concurrentProof"]!["calledWorkflows"]!.DeepClone();
            SetSelections(concurrent, choices);
            await host.RestartAsync(); processIds.Add(host.ProcessId); await host.LoginAsync(); await SameObservedViewsAsync(host, recovered["views"]!);

            foreach (var item in escalations["cases"]!.AsArray())
            {
                var kind = item!["kind"]!.GetValue<string>(); var interrupting = item["interrupting"]!.GetValue<bool>();
                var parentId = item["parentWorkflowExecutionId"]!.GetValue<string>(); var childId = item["childWorkflowExecutionId"]!.GetValue<string>();
                if (kind != "SignalAndFailure" || interrupting)
                {
                    item["warningDelivery"] = await SendAsync(host, item["warningEventName"]!.GetValue<string>(), parentId);
                    if (kind == "RunningEscalation" && !interrupting) item["finishDelivery"] = await SendAsync(host, item["finishEventName"]!.GetValue<string>(), childId);
                }
                await WaitAsync(host, parentId, view => Status(view) == (kind == "SignalAndFailure" && !interrupting ? "Faulted" : "Completed"));
                await WaitAsync(host, childId, view => Status(view) == (kind == "SignalAndFailure" ? "Faulted" : kind == "RunningEscalation" && interrupting ? "Cancelled" : "Completed"));
            }
            await HandleConcurrentAsync(host, concurrent, choices, selected: true);
            var comp = compensation["case"]!; var compensationId = comp["workflowExecutionId"]!.GetValue<string>();
            compensation["delivery"] = await SendAsync(host, comp["eventB"]!.GetValue<string>(), compensationId);
            await WaitAsync(host, compensationId, view => HasNode(view, "node-handler-a", "Suspended"));
            var intermediate = await CaptureAsync("intermediate", host, temporary.FullName, escalations, concurrent, compensation, "FinishEscalations", "HandleSelected", "HandlerA"); stages.Add(intermediate);
            await host.RestartAsync(); processIds.Add(host.ProcessId); await host.LoginAsync(); await SameObservedViewsAsync(host, intermediate["views"]!);
            var resumed = await CaptureAsync("resumed", host, temporary.FullName, escalations, concurrent, compensation, "FinishEscalations", "ObserveSelected", "HandlerA"); stages.Add(resumed);
            SameView(intermediate["compensationProof"]!["compensation"]!, resumed["compensationProof"]!["compensation"]!);
            SameView(intermediate["concurrentProof"]!["calledWorkflows"]!, resumed["concurrentProof"]!["calledWorkflows"]!);

            await HandleConcurrentAsync(host, concurrent, choices, selected: false);
            compensation["delivery"] = await SendAsync(host, comp["eventA"]!.GetValue<string>(), compensationId);
            await WaitAsync(host, compensationId, view => HasNode(view, "node-hold", "Suspended"));
            stages.Add(await CaptureAsync("settled", host, temporary.FullName, escalations, concurrent, compensation, "FinishEscalations", "HandleOther", "Settled"));
            compensation["delivery"] = await SendAsync(host, comp["holdEvent"]!.GetValue<string>(), compensationId);
            await WaitAsync(host, compensationId, view => Status(view) == "Completed");
            var finished = await CaptureAsync("finished", host, temporary.FullName, escalations, concurrent, compensation, "FinishEscalations", "HandleOther", "Finished"); stages.Add(finished);
            await host.StopAsync(); var designBackup = Path.Combine(temporary.FullName, "finished-design.sqlite"); await BackupAsync(host.DesignDatabase, designBackup);
            var designSnapshot = await ExportSnapshotAsync(designBackup, "finished-design.sqlite.gz", "legacy-controls");
            var migrations = await MigrationsAsync(host.RuntimeDatabase); Assert.All(originalMigrations, migration => Assert.Contains(migration, migrations)); Assert.Equal(artifacts, await ArtifactsAsync(host.RuntimeDatabase));
            await host.RestartAsync(); processIds.Add(host.ProcessId); await host.LoginAsync(); await SameObservedViewsAsync(host, finished["views"]!); await host.StopAsync();
            Assert.Equal(4, processIds.Distinct().Count());
            if (Environment.GetEnvironmentVariable("ELSA_BPMN_HOST_EVIDENCE_DIRECTORY") is { } evidence)
            {
                var source = Environment.GetEnvironmentVariable("ELSA_BPMN_HOST_SOURCE_REVISION");
                if (source is not null) Assert.EndsWith("+" + source, FileVersionInfo.GetVersionInfo(host.ReaderAssemblyPath).ProductVersion);
                var output = Path.Combine(evidence, "legacy-controls"); Directory.CreateDirectory(output);
                await File.WriteAllTextAsync(Path.Combine(output, "process.json"), JsonSerializer.Serialize(new {
                    formatVersion = 1, kind = "actual-legacy-controls-upgrade", capturedAt = DateTimeOffset.UtcNow, sourceRevision = source,
                    baselineSourceRevision = originalEscalations["sourceRevision"]!.GetValue<string>(), baselineManifestSha256 = Hash(await File.ReadAllBytesAsync(manifestPath)), baselineAssets = inputs,
                    testOnlyRecoveryProbePresent = false, processIds, originalMigrations, migrations, retainedArtifacts = artifacts, stages, designSnapshot,
                    binaries = host.DescribeBinaries(source), reader = new { file = Path.GetFileName(host.ReaderAssemblyPath), productVersion = FileVersionInfo.GetVersionInfo(host.ReaderAssemblyPath).ProductVersion, sha256 = Hash(await File.ReadAllBytesAsync(host.ReaderAssemblyPath)) },
                    limits = new[] { "Actual legacy native/BpmnProcess control streams, same-binding siblings and compensation only; no new direct-model parent/worker settlement is proved.", "Active MI and actual Studio remain separate compatibility qualifications before real native/ETL integration." }
                }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }) + Environment.NewLine);
            }
        }
        finally { temporary.Delete(recursive: true); }
        foreach (var pair in inputs) Assert.Equal(pair.Value, Hash(await File.ReadAllBytesAsync(Path.Combine(input, Path.GetFileName(pair.Key)))));
    }

    private static JsonObject Scalars(JsonNode item) => new(item.AsObject().Where(pair => pair.Value is JsonValue).Select(pair => new KeyValuePair<string, JsonNode?>(pair.Key, pair.Value!.DeepClone())));
    private static JsonObject Cases(JsonNode original) => new() { ["schemaVersion"] = 1, ["passed"] = true,
        ["cases"] = new JsonArray(original["cases"]!.AsArray().Select(item => (JsonNode)Scalars(item!)).ToArray()) };
    private static string Status(JsonNode view) => view["instance"]!["status"]!.GetValue<string>();
    private static bool HasNode(JsonNode view, string node, string status) => view["activities"]!.AsArray().Any(item => item!["executableNodeId"]!.GetValue<string>() == node && item["status"]!.GetValue<string>() == status);

    private static async Task<JsonNode> WaitAsync(OwnedWorkbench host, string id, Func<JsonNode, bool> ready)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(40);
        do
        {
            var view = await host.GetAsync("/runtime/workflows/instances/" + id); if (ready(view)) return view;
            Assert.True(DateTimeOffset.UtcNow < deadline, "Original control continuation did not reach its expected boundary: " + id + ", " + Status(view)); await Task.Delay(100);
        } while (true);
    }
    private static async Task<JsonNode> SendAsync(OwnedWorkbench host, string name, string id)
    {
        var result = await host.PostAsync("/runtime/workflows/stimuli", new { stimulusType = "Event", stimulusHash = "sha256:" + Hash(Encoding.UTF8.GetBytes(name)), mode = "ResumeOnly", correlationId = id, input = new { eventName = name } });
        Assert.Equal(0, result["startedCount"]!.GetValue<int>()); Assert.Equal(1, result["resumedCount"]!.GetValue<int>()); return result;
    }
    private static void SetSelections(JsonNode receipt, JsonNode choices)
    {
        foreach (var item in receipt["cases"]!.AsArray())
        {
            var choice = Assert.Single(choices.AsArray(), choice => choice!["parentWorkflowExecutionId"]!.GetValue<string>() == item!["parentWorkflowExecutionId"]!.GetValue<string>())!;
            item!["selection"] = new JsonObject {
                ["selectedCallActivityExecutionId"] = choice["selected"]!["call"]!["invocationId"]!.DeepClone(), ["otherCallActivityExecutionId"] = choice["other"]!["call"]!["invocationId"]!.DeepClone(),
                ["selectedChildWorkflowExecutionId"] = choice["selected"]!["child"]!["workflowExecutionId"]!.DeepClone(), ["otherChildWorkflowExecutionId"] = choice["other"]!["child"]!["workflowExecutionId"]!.DeepClone(),
                ["selectedSemanticTokenId"] = choice["selected"]!["call"]!["provenance"]!["executionPathId"]!.DeepClone(), ["otherSemanticTokenId"] = choice["other"]!["call"]!["provenance"]!["executionPathId"]!.DeepClone() };
        }
    }
    private static async Task HandleConcurrentAsync(OwnedWorkbench host, JsonNode receipt, JsonNode choices, bool selected)
    {
        var handled = selected ? 1 : 2;
        foreach (var item in receipt["cases"]!.AsArray())
        {
            var parentId = item!["parentWorkflowExecutionId"]!.GetValue<string>(); var choice = Assert.Single(choices.AsArray(), choice => choice!["parentWorkflowExecutionId"]!.GetValue<string>() == parentId)!;
            var target = choice[selected ? "selected" : "other"]!; var childId = target["child"]!["workflowExecutionId"]!.GetValue<string>(); var interrupting = item["interrupting"]!.GetValue<bool>();
            item["targetChildWorkflowExecutionId"] = JsonValue.Create(childId); item["targetCallActivityExecutionId"] = target["call"]!["invocationId"]!.DeepClone(); item["targetSemanticTokenId"] = target["call"]!["provenance"]!["executionPathId"]!.DeepClone();
            item["emitDelivery"] = await SendAsync(host, item["emitEventName"]!.GetValue<string>(), childId);
            await WaitAsync(host, parentId, view => view["activities"]!.AsArray().Count(activity => activity!["executableNodeId"]!.GetValue<string>() == "node-warning") == handled && HasNode(view, "node-warning", "Suspended"));
            if (interrupting) await WaitAsync(host, childId, view => Status(view) == "Cancelled");
            else await WaitAsync(host, childId, view => HasNode(view, "node-child-finish", "Suspended"));
            item["warningDelivery"] = await SendAsync(host, item["warningEventName"]!.GetValue<string>(), parentId);
            if (!interrupting) item["finishDelivery"] = await SendAsync(host, item["finishEventName"]!.GetValue<string>(), childId);
            await WaitAsync(host, parentId, view => view["activities"]!.AsArray().Count(activity => activity!["executableNodeId"]!.GetValue<string>() == "node-warning" && activity["status"]!.GetValue<string>() == "Completed") == handled && (!selected ? Status(view) == "Completed" : Status(view) == "Running"));
            await WaitAsync(host, childId, view => Status(view) == (interrupting ? "Cancelled" : "Completed"));
        }
    }
    private static async Task<JsonObject> RefreshAsync(OwnedWorkbench host, JsonNode escalations, JsonNode concurrent, JsonNode compensation)
    {
        var views = new JsonObject();
        async Task<JsonNode> ReadView(string id) { var value = await host.GetAsync("/runtime/workflows/instances/" + id); views[id] = value.DeepClone(); return value; }
        foreach (var item in escalations["cases"]!.AsArray())
        {
            item!["parent"] = await ReadView(item["parentWorkflowExecutionId"]!.GetValue<string>()); item["child"] = await ReadView(item["childWorkflowExecutionId"]!.GetValue<string>());
        }
        foreach (var item in concurrent["cases"]!.AsArray())
        {
            item!["parent"] = await ReadView(item["parentWorkflowExecutionId"]!.GetValue<string>());
            var dispatches = await host.GetAsync("/runtime/workflows/dispatches?parentWorkflowExecutionId=" + item["parentWorkflowExecutionId"]!.GetValue<string>());
            var children = new JsonArray();
            foreach (var dispatch in dispatches.AsArray().OrderBy(dispatch => dispatch!["childWorkflowExecutionId"]!.GetValue<string>(), StringComparer.Ordinal)) children.Add(await ReadView(dispatch!["childWorkflowExecutionId"]!.GetValue<string>()));
            item["children"] = children;
        }
        compensation["workflow"] = await ReadView(compensation["case"]!["workflowExecutionId"]!.GetValue<string>()); return views;
    }
    private static async Task SameObservedViewsAsync(OwnedWorkbench host, JsonNode views)
    {
        foreach (var pair in views.AsObject()) SameView(pair.Value!, await host.GetAsync("/runtime/workflows/instances/" + pair.Key));
    }
    private static async Task<JsonNode> CaptureAsync(string name, OwnedWorkbench host, string directory, JsonNode escalations, JsonNode concurrent, JsonNode compensation, string escalationPhase, string concurrentPhase, string compensationStage)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(40);
        while (true)
        {
            await using var connection = Read(host.RuntimeDatabase); await connection.OpenAsync(); await using var command = connection.CreateCommand(); command.CommandText = "SELECT COUNT(*) FROM elsa_runtime_post_commit_outbox WHERE Status <> 2";
            if ((long)(await command.ExecuteScalarAsync())! == 0) break;
            Assert.True(DateTimeOffset.UtcNow < deadline, "A real control source still owes delivery at " + name); await Task.Delay(100);
        }
        var views = await RefreshAsync(host, escalations, concurrent, compensation);
        var source = FileVersionInfo.GetVersionInfo(host.AssemblyPath).ProductVersion!.Split('+').Last();
        foreach (var receipt in new[] { escalations, concurrent, compensation }) receipt["sourceRevision"] = source;
        escalations["phase"] = escalationPhase; concurrent["phase"] = concurrentPhase; compensation["phase"] = name; compensation["expectedStage"] = compensationStage;
        var database = Path.Combine(directory, name + ".sqlite"); await BackupAsync(host.RuntimeDatabase, database);
        var escalationPath = Path.Combine(directory, name + "-escalations.json"); await File.WriteAllTextAsync(escalationPath, escalations.ToJsonString());
        var concurrentPath = Path.Combine(directory, name + "-concurrent.json"); await File.WriteAllTextAsync(concurrentPath, concurrent.ToJsonString());
        var compensationPath = Path.Combine(directory, name + "-compensation.json"); await File.WriteAllTextAsync(compensationPath, compensation.ToJsonString());
        var escalationProof = await CheckedSnapshotAsync(host.ReaderAssemblyPath, database, "--inspect-opted-called", escalationPath, escalationPhase == "ObserveEscalations" ? "escalation-recovered" : "escalation-finished");
        var concurrentMode = concurrentPhase switch { "Prepare" => "concurrent-prepared", "HandleSelected" => "concurrent-selected", "ObserveSelected" => "concurrent-recovered", _ => "concurrent-finished" };
        var concurrentProof = await CheckedSnapshotAsync(host.ReaderAssemblyPath, database, "--inspect-opted-called", concurrentPath, concurrentMode);
        var compensationProof = await CheckedSnapshotAsync(host.ReaderAssemblyPath, database, "--inspect-compensation", compensationPath, compensationStage);
        var snapshot = await ExportSnapshotAsync(database, name + ".sqlite.gz", "legacy-controls");
        return JsonSerializer.SerializeToNode(new { name, views, snapshot, escalationsApi = escalations.DeepClone(), escalationsProof = escalationProof,
            concurrentApi = concurrent.DeepClone(), concurrentProof, compensationApi = compensation.DeepClone(), compensationProof }, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }
}
