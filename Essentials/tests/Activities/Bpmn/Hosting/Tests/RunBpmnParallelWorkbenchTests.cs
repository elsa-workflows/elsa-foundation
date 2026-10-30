using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bpmn.Model;
using Elsa.Activities.Bpmn.Core;
using Xunit;

namespace Elsa.Activities.Bpmn.Hosting.Tests;

public sealed class RunBpmnParallelWorkbenchTests
{
    [Theory]
    [InlineData("Root")]
    [InlineData("Sequence")]
    [InlineData("Flowchart")]
    [InlineData("CompensationScope")]
    public async Task The_same_partial_fork_recovers_and_joins_once_directly_and_under_a_real_native_parent(string parent)
    {
        await using var host = await OwnedWorkbench.StartAsync(directBpmn: true, recoverySweepSeconds: 0.2);
        await host.LoginAsync();
        var process = new BpmnProcessDefinition("parallel-embedding", Elements:
            [new("start", BpmnElementTypes.StartEvent), new("fork", BpmnElementTypes.ParallelGateway),
                new("left", BpmnElementTypes.UserTask, bindingRef: "left-human"), new("right", BpmnElementTypes.UserTask, bindingRef: "right-human"),
                new("join", BpmnElementTypes.ParallelGateway), new("after-join", BpmnElementTypes.UserTask, bindingRef: "joined-human"), new("end", BpmnElementTypes.EndEvent)],
            SequenceFlows: [new("begin", "start", "fork"), new("fork-left", "fork", "left"), new("fork-right", "fork", "right"),
                new("left-join", "left", "join"), new("right-join", "right", "join"), new("joined", "join", "after-join"), new("finish", "after-join", "end")]);
        var definition = new BpmnModelDefinition(1, JsonSerializer.SerializeToElement(process, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            ["left-human", "right-human", "joined-human"]);
        var published = await host.PostAsync("/bpmn/artifacts", new { requestId = "publish-parallel", definition });
        object root = new { nodeId = "run-bpmn", activityVersionId = await Version("Elsa.Activities.Bpmn.Activities.RunBpmn"), outputs = Array.Empty<object>(),
            inputs = new[] { new { referenceKey = "Model", value = new { value = published["sourceReferenceId"]!.GetValue<string>(), expressionType = "Literal" } } } };
        if (parent != "Root")
        {
            object after = new { nodeId = "after", activityVersionId = await Version("Elsa.Activities.Primitives.Activities.WriteLine"), outputs = Array.Empty<object>(),
                inputs = new[] { new { referenceKey = "text", value = new { value = "The partial fork joined once.", expressionType = "Literal" } } } };
            object structure = parent switch
            {
                "Sequence" => new { kind = "elsa.sequence.structure", schemaVersion = "1.0.0", payload = new { activities = new[] { root, after } } },
                "Flowchart" => new { kind = "elsa.flowchart.structure", schemaVersion = "1.0.0", payload = new { startNodeId = "run-bpmn", activities = new[] { root, after },
                    connections = new[] { new { id = "after", source = new { nodeId = "run-bpmn", port = "Done" }, target = new { nodeId = "after" } } } } },
                "CompensationScope" => new { kind = "elsa.compensation-scope.structure", schemaVersion = "1.0.0", payload = new { steps = new[] { new { name = "process", activity = root }, new { name = "after", activity = after } } } },
                _ => throw new InvalidOperationException("Unknown actual native parent.")
            };
            root = new { nodeId = "native-parent", activityVersionId = await Version("Elsa.Activities." + (parent == "CompensationScope" ? "Compensation" : parent) + ".Activities." + parent),
                inputs = Array.Empty<object>(), outputs = Array.Empty<object>(), structure };
        }
        var draft = await host.PostAsync("/design/workflows/definitions/submit", new { name = "Actual parallel RunBpmn " + parent,
            state = new { variables = Array.Empty<object>(), inputs = Array.Empty<object>(), outputs = Array.Empty<object>(), rootActivity = root } });
        var publication = await host.PostAsync($"/publishing/workflows/{draft["version"]!["id"]!.GetValue<string>()}/publish", new { });
        var nativeId = (await host.PostAsync($"/runtime/workflows/executables/{publication["artifactId"]!.GetValue<string>()}/execute",
            new { sourceReferenceId = publication["sourceReferenceId"]!.GetValue<string>() }))["workflowExecutionId"]!.GetValue<string>();
        string? modelId = null;
        await Until(async () =>
        {
            var native = await host.GetAsync("/runtime/workflows/instances/" + nativeId);
            Assert.Empty(native["incidents"]!.AsArray());
            var activity = native["activities"]!.AsArray().SingleOrDefault(item => item!["authoredActivityId"]!.GetValue<string>() == "run-bpmn");
            if (activity is null) return false;
            var detail = await host.GetAsync("/runtime/workflows/instances/" + nativeId + "/activity-executions/" + activity["activityExecutionId"]!.GetValue<string>());
            if (detail["bookmarks"] is not JsonArray { Count: 1 } waits) return false;
            modelId = waits[0]!["stimulusHash"]!.GetValue<string>();
            return await host.FindAsync("/bpmn/executions/" + modelId) is not null;
        });
        var directId = (await host.PostAsync("/bpmn/executions", new { sourceReferenceId = published["sourceReferenceId"]!.GetValue<string>(), requestId = "start-direct" }))["executionId"]!.GetValue<string>();
        var ids = new[] { directId, modelId! };
        var commands = new Dictionary<string, object>();
        var views = new Dictionary<string, object>();
        var checkedStages = new Dictionary<string, JsonNode>();
        var snapshots = new List<object?>();
        var processes = new List<int> { host.ProcessId };
        var ownedProcesses = new List<object> { DescribeOwnedProcess() };
        var exits = new List<int?>();
        var directory = Environment.GetEnvironmentVariable("ELSA_BPMN_HOST_EVIDENCE_DIRECTORY");
        var caseName = "parallel-" + parent.ToLowerInvariant();
        var initial = new Dictionary<string, JsonNode[]>();
        var knownWork = new Dictionary<string, List<JsonNode>>();
        foreach (var id in ids)
        {
            var work = (await host.GetAsync(Path(id) + "/work"))["items"]!.AsArray();
            Assert.Equal(2, work.Count);
            initial[id] = [Work(work, "left"), Work(work, "right")];
            knownWork[id] = initial[id].ToList();
        }
        checkedStages["bothWaiting"] = await Checked();
        foreach (var id in ids) commands[id + ":left"] = await Complete(id, initial[id][0], "left");
        var partial = new Dictionary<string, JsonNode>();
        foreach (var id in ids)
        {
            partial[id] = await host.GetAsync(Path(id));
            Assert.Equal(2, partial[id]["revision"]!.GetValue<long>());
            Assert.True(JsonNode.DeepEquals(initial[id][1], Assert.Single((await host.GetAsync(Path(id) + "/work"))["items"]!.AsArray())));
        }
        views["partial"] = partial;
        await Until(async () => (await Checked())["sources"]!.AsArray() is { Count: 1 } sources && sources.All(item => item!["status"]!.GetValue<int>() == 2));
        checkedStages["partial"] = await Checked();
        await Capture("partial");
        await host.RestartAsync(); await host.LoginAsync(); processes.Add(host.ProcessId); ownedProcesses.Add(DescribeOwnedProcess());
        foreach (var id in ids)
        {
            Assert.True(JsonNode.DeepEquals(partial[id], await host.GetAsync(Path(id))));
            Assert.True(JsonNode.DeepEquals(initial[id][1], Assert.Single((await host.GetAsync(Path(id) + "/work"))["items"]!.AsArray())));
            var replay = await Complete(id, initial[id][0], "left"); commands[id + ":left-replay"] = replay;
            AssertReplay(replay, id, "left");
            Assert.True(JsonNode.DeepEquals(partial[id], await host.GetAsync(Path(id))));
        }
        checkedStages["recovered"] = await Checked();
        var joined = new Dictionary<string, JsonNode>();
        var afterJoin = new Dictionary<string, JsonNode>();
        foreach (var id in ids)
        {
            commands[id + ":right"] = await Complete(id, initial[id][1], "right");
            joined[id] = await host.GetAsync(Path(id));
            Assert.Equal(3, joined[id]["revision"]!.GetValue<long>());
            afterJoin[id] = Assert.Single((await host.GetAsync(Path(id) + "/work"))["items"]!.AsArray())!;
            knownWork[id].Add(afterJoin[id]);
            Assert.Equal("after-join", afterJoin[id]["elementId"]!.GetValue<string>());
            var replay = await Complete(id, initial[id][1], "right"); commands[id + ":right-replay"] = replay;
            AssertReplay(replay, id, "right");
            Assert.True(JsonNode.DeepEquals(joined[id], await host.GetAsync(Path(id))));
            Assert.True(JsonNode.DeepEquals(afterJoin[id], Assert.Single((await host.GetAsync(Path(id) + "/work"))["items"]!.AsArray())));
        }
        views["joined"] = joined; checkedStages["joined"] = await Checked();
        foreach (var id in ids) commands[id + ":finish"] = await Complete(id, afterJoin[id], "finish");
        await Until(async () => (await host.GetAsync(Path(modelId!)))["status"]!.GetValue<string>() == "TerminalSettled" &&
            (await host.GetAsync("/runtime/workflows/instances/" + nativeId))["instance"]!["status"]!.GetValue<string>() == "Completed");
        await Until(async () => (await Checked())["sources"]!.AsArray() is { Count: 2 } sources && sources.All(item => item!["status"]!.GetValue<int>() == 2));
        var final = new Dictionary<string, JsonNode>();
        foreach (var id in ids)
        {
            final[id] = await host.GetAsync(Path(id));
            Assert.Equal("TerminalSettled", final[id]["status"]!.GetValue<string>());
            Assert.Equal("Completed", final[id]["result"]!["status"]!.GetValue<string>());
            Assert.Equal(id == directId ? 4 : 5, final[id]["revision"]!.GetValue<long>());
            Assert.Empty((await host.GetAsync(Path(id) + "/work"))["items"]!.AsArray());
            var replay = await Complete(id, initial[id][1], "right"); commands[id + ":late-right-replay"] = replay;
            AssertReplay(replay, id, "right");
            Assert.True(JsonNode.DeepEquals(final[id], await host.GetAsync(Path(id))));
        }
        var nativeFinal = await host.GetAsync("/runtime/workflows/instances/" + nativeId);
        Assert.Empty(nativeFinal["incidents"]!.AsArray());
        Assert.Equal("Completed", Assert.Single(nativeFinal["activities"]!.AsArray(), item => item!["authoredActivityId"]!.GetValue<string>() == "run-bpmn")!["status"]!.GetValue<string>());
        if (parent != "Root") Assert.Equal("Completed", Assert.Single(nativeFinal["activities"]!.AsArray(), item => item!["authoredActivityId"]!.GetValue<string>() == "after")!["status"]!.GetValue<string>());
        Assert.Single((await host.GetAsync("/runtime/workflows/instances")).AsArray());
        views["final"] = final; checkedStages["final"] = await Checked();
        Assert.Equal(parent == "Root" ? 1 : 3, checkedStages["final"]["activities"]!.AsArray().Count);
        await Capture("final");
        await host.RestartAsync(); await host.LoginAsync(); processes.Add(host.ProcessId); ownedProcesses.Add(DescribeOwnedProcess());
        foreach (var id in ids) Assert.True(JsonNode.DeepEquals(final[id], await host.GetAsync(Path(id))));
        Assert.True(JsonNode.DeepEquals(nativeFinal, await host.GetAsync("/runtime/workflows/instances/" + nativeId)));
        checkedStages["retained"] = await Checked();
        Assert.True(JsonNode.DeepEquals(checkedStages["final"], checkedStages["retained"]));
        await Capture("retained");
        Assert.Equal(3, processes.Distinct().Count());
        Assert.All(exits, value => Assert.True(value is not null and not 0));
        if (directory is not null)
        {
            var source = Environment.GetEnvironmentVariable("ELSA_BPMN_HOST_SOURCE_REVISION") ?? throw new InvalidOperationException("Actual parallel evidence requires its exact source.");
            var receipt = new { schemaVersion = 1, kind = "actual-direct-embedded-partial-fork-recovery", sourceRevision = source, parent,
                nativeExecutionId = nativeId, modelExecutionId = modelId, directCounterpartId = directId, published, publication, initial, afterJoin,
                commands, views, nativeFinal, processes, ownedProcesses, exits, snapshots, binaries = host.DescribeBinaries(source, includePublishing: true),
                termination = ".NET Process.Kill(entireProcessTree: true)", executionStateEdited = false, claimEdited = false, clockEdited = false,
                normalization = "Only tokens.producingWorkHandle is normalized: the owning model ID becomes $model; an observed local human invocation becomes its exact element/binding/token/iteration tuple.",
                limits = new[] { "Actual SQLite and built-in single-node actor; model-owned human work and existing native parents only.",
                    "No cancellation, ETL, timer, broadcast or upgraded-host result is inferred from this partial-fork witness." } };
            var output = System.IO.Path.Combine(directory, caseName);
            await File.WriteAllTextAsync(System.IO.Path.Combine(output, "case.json"), JsonSerializer.Serialize(receipt, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }) + Environment.NewLine);
            foreach (var pair in checkedStages) await File.WriteAllTextAsync(System.IO.Path.Combine(output, pair.Key + "-checked.json"), pair.Value.ToJsonString(new() { WriteIndented = true }) + Environment.NewLine);
        }
        object DescribeOwnedProcess()
        {
            using var owned = Process.GetProcessById(host.ProcessId);
            return new { processId = owned.Id, controllerProcessId = Environment.ProcessId, startedAtUtc = owned.StartTime.ToUniversalTime(),
                executable = owned.MainModule!.FileName, contentRoot = host.ContentRoot, role = "standard" };
        }
        string Path(string id) => "/bpmn/executions/" + id;
        static JsonNode Work(JsonArray items, string element) => Assert.Single(items, item => item!["elementId"]!.GetValue<string>() == element)!;
        async Task<JsonNode> Complete(string id, JsonNode work, string request) => await host.PostAsync(Path(id) + "/work/" + work["invocationId"]!.GetValue<string>() + "/complete",
            new { requestId = request, attemptId = work["attemptId"]!.GetValue<string>() });
        void AssertReplay(JsonNode replay, string id, string request)
        {
            var original = (JsonNode)commands[id + ":" + request];
            Assert.True(replay["replay"]!.GetValue<bool>());
            Assert.False(replay["pending"]!.GetValue<bool>());
            Assert.Equal(id, replay["executionId"]!.GetValue<string>());
            Assert.True(JsonNode.DeepEquals(original["inputId"], replay["inputId"]));
            Assert.True(JsonNode.DeepEquals(original["result"], replay["result"]));
        }
        async Task<JsonNode> Checked()
        {
            var value = await host.ReadEmbeddingAsync(modelId!, nativeId, directId);
            var same = JsonNode.DeepEquals(Normalize(value["model"]!), Normalize(value["directCounterpart"]!));
            if (!same && directory is not null)
            {
                var diagnostic = System.IO.Path.Combine("diagnostics", caseName);
                var output = System.IO.Path.Combine(directory, diagnostic);
                Directory.CreateDirectory(output);
                await File.WriteAllTextAsync(System.IO.Path.Combine(output, "observed-checked.json"), value.ToJsonString(new() { WriteIndented = true }));
                await host.StopAsync();
                var closed = await host.ReadEmbeddingAsync(modelId!, nativeId, directId);
                await File.WriteAllTextAsync(System.IO.Path.Combine(output, "closed-checked.json"), closed.ToJsonString(new() { WriteIndented = true }));
                var stores = new[] { await host.SnapshotAsync("refused-runtime.sqlite.gz", diagnostic),
                    await host.SnapshotAsync("refused-design.sqlite.gz", diagnostic, design: true) };
                var receipt = new { sourceRevision = Environment.GetEnvironmentVariable("ELSA_BPMN_HOST_SOURCE_REVISION"), parent,
                    nativeExecutionId = nativeId, modelExecutionId = modelId, directCounterpartId = directId,
                    published, publication, initial, knownWork, commands, views, ownedProcesses, exitCode = host.LastStoppedExitCode, stores,
                    binaries = host.DescribeBinaries(Environment.GetEnvironmentVariable("ELSA_BPMN_HOST_SOURCE_REVISION"), includePublishing: true),
                    executionStateEdited = false, claimEdited = false, clockEdited = false, qualifyingJourney = false };
                await File.WriteAllTextAsync(System.IO.Path.Combine(output, "refused-case.json"), JsonSerializer.Serialize(receipt, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
            }
            Assert.True(same, "The same accepted partial-fork inputs produced different released-interpreter states.");
            return value;
        }
        JsonNode Normalize(JsonNode model)
        {
            var state = model["payload"]!["state"]!.DeepClone();
            var execution = model["executionId"]!.GetValue<string>();
            var handles = knownWork[execution].ToDictionary(work => work["invocationId"]!.GetValue<string>(), work => "$human:" +
                new JsonArray(work["elementId"]!.DeepClone(), work["bindingId"]!.DeepClone(), work["tokenId"]!.DeepClone(), work["iterationId"]?.DeepClone()).ToJsonString());
            foreach (var token in state["tokens"]!.AsArray())
            {
                var handle = token!["producingWorkHandle"]?.GetValue<string>();
                if (handle == execution) token["producingWorkHandle"] = "$model";
                else if (handle is not null && handles.TryGetValue(handle, out var local)) token["producingWorkHandle"] = local;
            }
            return state;
        }
        async Task Capture(string stage)
        {
            await host.StopAsync(); exits.Add(host.LastStoppedExitCode);
            snapshots.Add(await host.SnapshotAsync(stage + "-runtime.sqlite.gz", caseName));
            snapshots.Add(await host.SnapshotAsync(stage + "-design.sqlite.gz", caseName, design: true));
        }
        static async Task Until(Func<Task<bool>> predicate)
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(45);
            while (!await predicate())
            {
                Assert.True(DateTimeOffset.UtcNow < deadline, "The actual native/model parallel journey did not reach its required durable boundary.");
                await Task.Delay(100);
            }
        }
        async Task<string> Version(string type)
        {
            var catalog = await host.GetAsync("/design/activities/definitions?search=" + type);
            return Assert.Single(catalog["items"]!.AsArray(), item => item!["definition"]!["activityTypeKey"]!.GetValue<string>() == type)!["definition"]!["headVersionId"]!.GetValue<string>();
        }
    }
}
