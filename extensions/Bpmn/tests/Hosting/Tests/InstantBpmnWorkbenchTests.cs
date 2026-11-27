using System.Text.Json;
using System.Text.Json.Nodes;
using Bpmn.Model;
using Elsa.Activities.Bpmn.Core;
using Xunit;

namespace Elsa.Activities.Bpmn.Hosting.Tests;

public sealed class InstantBpmnWorkbenchTests
{
    [Theory]
    [InlineData("Root")]
    [InlineData("Sequence")]
    [InlineData("Flowchart")]
    [InlineData("CompensationScope")]
    public async Task First_decision_completion_returns_to_the_original_native_parent_after_restart(string parent)
    {
        await using var host = await OwnedWorkbench.StartAsync(directBpmn: true, recoverySweepSeconds: 300);
        await host.LoginAsync();
        var process = new BpmnProcessDefinition("instant-process", Elements:
            [new("start", BpmnElementTypes.StartEvent), new("end", BpmnElementTypes.EndEvent)],
            SequenceFlows: [new("finish", "start", "end")]);
        var definition = new BpmnModelDefinition(1, JsonSerializer.SerializeToElement(process, new JsonSerializerOptions(JsonSerializerDefaults.Web)), []);
        var published = await host.PostAsync("/bpmn/artifacts", new { requestId = "publish-instant", definition });
        var direct = await host.PostAsync("/bpmn/executions", new { sourceReferenceId = published["sourceReferenceId"]!.GetValue<string>(), requestId = "direct-instant" });
        var directId = direct["executionId"]!.GetValue<string>();
        var directFinal = await host.GetAsync("/bpmn/executions/" + directId);
        Assert.Equal("TerminalSettled", directFinal["status"]!.GetValue<string>());
        object root = new { nodeId = "run-bpmn", activityVersionId = await Version("Elsa.Activities.Bpmn.Activities.RunBpmn"), outputs = Array.Empty<object>(),
            inputs = new[] { new { referenceKey = "Model", value = new { value = published["sourceReferenceId"]!.GetValue<string>(), expressionType = "Literal" } } } };
        if (parent != "Root")
        {
            var after = new { nodeId = "after", activityVersionId = await Version("Elsa.Activities.Primitives.Activities.WriteLine"), outputs = Array.Empty<object>(),
                inputs = new[] { new { referenceKey = "text", value = new { value = "Instant BPMN returned.", expressionType = "Literal" } } } };
            object structure = parent switch
            {
                "Sequence" => new { kind = "elsa.sequence.structure", schemaVersion = "1.0.0", payload = new { activities = new[] { root, after } } },
                "Flowchart" => new { kind = "elsa.flowchart.structure", schemaVersion = "1.0.0", payload = new { startNodeId = "run-bpmn", activities = new[] { root, after },
                    connections = new[] { new { id = "after", source = new { nodeId = "run-bpmn", port = "Done" }, target = new { nodeId = "after" } } } } },
                "CompensationScope" => new { kind = "elsa.compensation-scope.structure", schemaVersion = "1.0.0", payload = new { steps = new[] { new { name = "process", activity = root }, new { name = "after", activity = (object)after } } } },
                _ => throw new InvalidOperationException("Unknown native parent.")
            };
            root = new { nodeId = "native-parent", activityVersionId = await Version("Elsa.Activities." + (parent == "CompensationScope" ? "Compensation" : parent) + ".Activities." + parent),
                inputs = Array.Empty<object>(), outputs = Array.Empty<object>(), structure };
        }
        var native = await host.PostAsync("/design/workflows/definitions/submit", new { name = "Instant BPMN native return", state = new
        { variables = Array.Empty<object>(), inputs = Array.Empty<object>(), outputs = Array.Empty<object>(), rootActivity = root } });
        var publication = await host.PostAsync($"/publishing/workflows/{native["version"]!["id"]!.GetValue<string>()}/publish", new { });
        var started = await host.PostAsync($"/runtime/workflows/executables/{publication["artifactId"]!.GetValue<string>()}/execute",
            new { sourceReferenceId = publication["sourceReferenceId"]!.GetValue<string>() });
        var execution = started["workflowExecutionId"]!.GetValue<string>();
        var nativePath = "/runtime/workflows/instances/" + execution;
        var before = await host.GetAsync(nativePath);
        var invocation = Assert.Single(before["activities"]!.AsArray(), item => item!["authoredActivityId"]!.GetValue<string>() == "run-bpmn")!["activityExecutionId"]!.GetValue<string>();
        var detail = await host.GetAsync(nativePath + "/activity-executions/" + invocation);
        var wait = Assert.Single(detail["bookmarks"]!.AsArray())!;
        var modelId = wait["stimulusHash"]!.GetValue<string>();
        var modelPath = "/bpmn/executions/" + modelId;
        Assert.Null(await host.FindAsync(modelPath));
        var caseName = "instant-" + parent.ToLowerInvariant();
        var evidence = Environment.GetEnvironmentVariable("ELSA_BPMN_HOST_EVIDENCE_DIRECTORY");
        if (evidence is not null) Directory.CreateDirectory(Path.Combine(evidence, caseName));
        var first = host.ProcessId;
        await host.StopAsync();
        var firstExit = host.LastStoppedExitCode;
        Assert.NotNull(firstExit); Assert.NotEqual(0, firstExit.Value);
        var prepared = await host.ReadEmbeddingAsync(modelId, execution, directId);
        Assert.Null(prepared["model"]);
        var waitingRuntime = await host.SnapshotAsync("waiting-runtime.sqlite.gz", caseName);
        var waitingDesign = await host.SnapshotAsync("waiting-design.sqlite.gz", caseName, design: true);
        await host.RestartAsync(recoverySweepSeconds: 0.2);
        await host.LoginAsync();
        var second = host.ProcessId;
        Assert.NotEqual(first, second);
        JsonNode settled = null!, final = null!, nativeFinal = null!;
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (true)
        {
            settled = await host.ReadEmbeddingAsync(modelId, execution, directId);
            nativeFinal = await host.GetAsync(nativePath);
            if (settled["model"]?["lifecycle"]?.GetValue<int>() == 3 && nativeFinal["instance"]!["status"]!.GetValue<string>() == "Completed" &&
                settled["sources"]!.AsArray().Count == 2 && settled["sources"]!.AsArray().All(item => item!["status"]!.GetValue<int>() == 2)) break;
            Assert.True(DateTimeOffset.UtcNow < deadline, "First-decision model completion did not return and settle against its original native wait.");
            await Task.Delay(100);
        }
        final = await host.GetAsync(modelPath);
        Assert.True(JsonNode.DeepEquals(directFinal["result"], final["result"]));
        Assert.Empty(nativeFinal["incidents"]!.AsArray());
        Assert.Equal("Completed", Assert.Single(nativeFinal["activities"]!.AsArray(), item => item!["authoredActivityId"]!.GetValue<string>() == "run-bpmn")!["status"]!.GetValue<string>());
        if (parent != "Root") Assert.Equal("Completed", Assert.Single(nativeFinal["activities"]!.AsArray(), item => item!["authoredActivityId"]!.GetValue<string>() == "after")!["status"]!.GetValue<string>());
        Assert.Empty((await host.GetAsync(modelPath + "/work"))["items"]!.AsArray());
        await host.StopAsync();
        var secondExit = host.LastStoppedExitCode;
        Assert.NotNull(secondExit); Assert.NotEqual(0, secondExit.Value);
        var completed = await host.ReadEmbeddingAsync(modelId, execution, directId);
        var completedRuntime = await host.SnapshotAsync("completed-runtime.sqlite.gz", caseName);
        var completedDesign = await host.SnapshotAsync("completed-design.sqlite.gz", caseName, design: true);
        await host.RestartAsync(); await host.LoginAsync();
        var third = host.ProcessId;
        Assert.NotEqual(second, third);
        Assert.True(JsonNode.DeepEquals(final, await host.GetAsync(modelPath)));
        Assert.True(JsonNode.DeepEquals(directFinal, await host.GetAsync("/bpmn/executions/" + directId)));
        Assert.Equal("Completed", (await host.GetAsync(nativePath))["instance"]!["status"]!.GetValue<string>());
        var retained = await host.ReadEmbeddingAsync(modelId, execution, directId);
        Assert.True(JsonNode.DeepEquals(completed, retained));
        await host.StopAsync();
        var thirdExit = host.LastStoppedExitCode;
        if (evidence is not null)
        {
            var source = Environment.GetEnvironmentVariable("ELSA_BPMN_HOST_SOURCE_REVISION") ?? throw new InvalidOperationException("Evidence requires exact source.");
            var receipt = new { kind = "actual-first-decision-bpmn-native-return", sourceRevision = source, parent, published, publication,
                nativeExecutionId = execution, modelExecutionId = modelId, directExecutionId = directId, first, second, third, firstExit, secondExit, thirdExit,
                termination = ".NET Process.Kill(entireProcessTree: true)", actualWait = wait,
                binaries = host.DescribeBinaries(source, includePublishing: true), before, directFinal, final, nativeFinal,
                snapshots = new[] { waitingRuntime, waitingDesign, completedRuntime, completedDesign },
                limits = new[] { "SQLite, built-in single-node host, supported start/end profile.", "Actual process loss before model admission and restart after settled return; no new provider/Studio/ETL acceptance." } };
            var folder = Path.Combine(evidence, caseName);
            foreach (var item in new[] { ("case.json", JsonSerializer.SerializeToNode(receipt, new JsonSerializerOptions(JsonSerializerDefaults.Web))),
                         ("prepared.json", prepared), ("completed.json", completed), ("retained.json", retained) })
                await File.WriteAllTextAsync(Path.Combine(folder, item.Item1), item.Item2!.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }

        async Task<string> Version(string type)
        {
            var catalog = await host.GetAsync("/design/activities/definitions?search=" + type);
            return Assert.Single(catalog["items"]!.AsArray(), item => item!["definition"]!["activityTypeKey"]!.GetValue<string>() == type)!["definition"]!["headVersionId"]!.GetValue<string>();
        }
    }
}
