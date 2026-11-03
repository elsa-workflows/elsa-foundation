using System.Text.Json;
using System.Text.Json.Nodes;
using Bpmn.Model;
using Elsa.Activities.Bpmn.Core;
using Xunit;
using Microsoft.Data.Sqlite;

namespace Elsa.Activities.Bpmn.Hosting.Tests;

public sealed class RunBpmnWorkbenchTests
{
    [Theory]
    [InlineData("Root", false)]
    [InlineData("Sequence", false)]
    [InlineData("Flowchart", false)]
    [InlineData("CompensationScope", false)]
    [InlineData("Root", true)]
    [InlineData("Sequence", true)]
    [InlineData("Flowchart", true)]
    [InlineData("CompensationScope", true)]
    public async Task A_real_native_RunBpmn_wait_restarts_and_consumes_the_same_model_lifecycle(string parent, bool cancel)
    {
        await using var host = await OwnedWorkbench.StartAsync(directBpmn: true, recoverySweepSeconds: 0.2);
        await host.LoginAsync();
        var process = new BpmnProcessDefinition("embedded-human", Elements:
            [new("start", BpmnElementTypes.StartEvent), new("human", BpmnElementTypes.UserTask, bindingRef: "work"), new("end", BpmnElementTypes.EndEvent)],
            SequenceFlows: [new("to-work", "start", "human"), new("to-end", "human", "end")]);
        var definition = new BpmnModelDefinition(1, JsonSerializer.SerializeToElement(process, new JsonSerializerOptions(JsonSerializerDefaults.Web)), ["work"]);
        var published = await host.PostAsync("/bpmn/artifacts", new { requestId = "publish-embedded", definition });
        var catalog = await host.GetAsync("/design/activities/definitions?search=Elsa.Activities.Bpmn.Activities.RunBpmn");
        var activity = Assert.Single(catalog["items"]!.AsArray(), item => item!["definition"]!["activityTypeKey"]!.GetValue<string>() == "Elsa.Activities.Bpmn.Activities.RunBpmn");
        object rootNode = new { nodeId = "run-bpmn", activityVersionId = activity!["definition"]!["headVersionId"]!.GetValue<string>(), outputs = Array.Empty<object>(),
            inputs = new[] { new { referenceKey = "Model", value = new { value = published["sourceReferenceId"]!.GetValue<string>(), expressionType = "Literal" } } } };
        if (parent != "Root")
        {
            var after = new { nodeId = "after", activityVersionId = await Version("Elsa.Activities.Primitives.Activities.WriteLine"), outputs = Array.Empty<object>(),
                inputs = new[] { new { referenceKey = "text", value = new { value = "Actual native parent continued after RunBpmn.", expressionType = "Literal" } } } };
            object structure = parent switch
            {
                "Sequence" => new { kind = "elsa.sequence.structure", schemaVersion = "1.0.0", payload = new { activities = new object[] { rootNode, after } } },
                "Flowchart" => new { kind = "elsa.flowchart.structure", schemaVersion = "1.0.0", payload = new { startNodeId = "run-bpmn", activities = new object[] { rootNode, after },
                    connections = new[] { new { id = "after", source = new { nodeId = "run-bpmn", port = "Done" }, target = new { nodeId = "after" } } } } },
                "CompensationScope" => new { kind = "elsa.compensation-scope.structure", schemaVersion = "1.0.0", payload = new { steps = new[] { new { name = "process", activity = rootNode }, new { name = "after", activity = (object)after } } } },
                _ => throw new InvalidOperationException("Unknown actual native parent.")
            };
            rootNode = new { nodeId = "native-parent", activityVersionId = await Version("Elsa.Activities." + (parent == "CompensationScope" ? "Compensation" : parent) + ".Activities." + parent),
                inputs = Array.Empty<object>(), outputs = Array.Empty<object>(), structure };
        }
        var native = await host.PostAsync("/design/workflows/definitions/submit", new
        {
            name = "Actual native RunBpmn wait",
            state = new { variables = Array.Empty<object>(), inputs = Array.Empty<object>(), outputs = Array.Empty<object>(),
                rootActivity = rootNode }
        });
        var publication = await host.PostAsync($"/publishing/workflows/{native["version"]!["id"]!.GetValue<string>()}/publish", new { });
        var started = await host.PostAsync($"/runtime/workflows/executables/{publication["artifactId"]!.GetValue<string>()}/execute",
            new { sourceReferenceId = publication["sourceReferenceId"]!.GetValue<string>() });
        var execution = started["workflowExecutionId"]!.GetValue<string>();
        JsonNode view = null!; string? modelId = null; var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (modelId is null)
        {
            Assert.True(DateTimeOffset.UtcNow < deadline, "The actual RunBpmn native wait did not publish its model invocation.");
            view = await host.GetAsync("/runtime/workflows/instances/" + execution);
            Assert.Empty(view["incidents"]!.AsArray());
            // The actual typed bookmark exposes its stable stimulus identity through ordinary inspection.
            if (view["activities"]!.AsArray().SingleOrDefault(item => item!["activityType"]!.GetValue<string>() == "Elsa.Activities.Bpmn.Activities.RunBpmn") is { } root)
            {
                var detail = await host.GetAsync("/runtime/workflows/instances/" + execution + "/activity-executions/" + root["activityExecutionId"]!.GetValue<string>());
                if (detail["bookmarks"] is JsonArray waits && waits.Count == 1) modelId = waits[0]!["stimulusHash"]!.GetValue<string>();
            }
            if (modelId is null) await Task.Delay(100);
        }
        var modelPath = "/bpmn/executions/" + modelId;
        while (await host.FindAsync(modelPath) is null)
        {
            Assert.True(DateTimeOffset.UtcNow < deadline, "The real native source did not admit its model child.");
            await Task.Delay(100);
        }
        var work = (await host.GetAsync(modelPath + "/work"))["items"]!.AsArray();
        var waiting = Assert.Single(work)!;
        var before = await host.GetAsync(modelPath);
        Assert.Equal("Running", before["status"]!.GetValue<string>());
        string? directId = null; JsonNode? directWork = null;
        if (!cancel)
        {
            directId = (await host.PostAsync("/bpmn/executions", new { sourceReferenceId = published["sourceReferenceId"]!.GetValue<string>(), requestId = "direct-counterpart" }))["executionId"]!.GetValue<string>();
            directWork = Assert.Single((await host.GetAsync("/bpmn/executions/" + directId + "/work"))["items"]!.AsArray())!;
        }
        var evidenceDirectory = Environment.GetEnvironmentVariable("ELSA_BPMN_HOST_EVIDENCE_DIRECTORY");
        var caseName = parent.ToLowerInvariant() + (cancel ? "-cancel" : "-complete");
        if (evidenceDirectory is not null) Directory.CreateDirectory(Path.Combine(evidenceDirectory, caseName));
        var firstProcess = host.ProcessId;
        var nativeBefore = await host.GetAsync("/runtime/workflows/instances/" + execution);
        await host.StopAsync(); var predecessorExit = host.LastStoppedExitCode;
        var waitingPhysical = await host.ReadEmbeddingAsync(modelId, execution, directId);
        var waitingRuntime = await host.SnapshotAsync("waiting-runtime.sqlite.gz", caseName);
        var waitingDesign = await host.SnapshotAsync("waiting-design.sqlite.gz", caseName, design: true);
        await host.RestartAsync(); await host.LoginAsync();
        var secondProcess = host.ProcessId; Assert.NotEqual(firstProcess, secondProcess);
        Assert.True(JsonNode.DeepEquals(before, await host.GetAsync(modelPath)));
        if (directId is not null)
            await host.PostAsync("/bpmn/executions/" + directId + "/work/" + directWork!["invocationId"]!.GetValue<string>() + "/complete",
                new { requestId = "complete-direct", attemptId = directWork["attemptId"]!.GetValue<string>() });
        if (cancel)
            await host.PostAsync("/runtime/workflows/alteration-plans", new { target = new { executionIds = new[] { execution } },
                alterations = new[] { new { kind = "CancelWorkflow", schemaVersion = 1, payload = new { } } } }, idempotencyKey: "cancel-embedded");
        else
            await host.PostAsync(modelPath + "/work/" + waiting["invocationId"]!.GetValue<string>() + "/complete",
                new { requestId = "complete-embedded", attemptId = waiting["attemptId"]!.GetValue<string>() });
        deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (true)
        {
            view = await host.GetAsync("/runtime/workflows/instances/" + execution);
            var model = await host.GetAsync(modelPath);
            if (view["instance"]!["status"]!.GetValue<string>() == (cancel ? "Cancelled" : "Completed") && model["status"]!.GetValue<string>() == "TerminalSettled") break;
            if (DateTimeOffset.UtcNow >= deadline)
            {
                await host.StopAsync();
                var diagnostic = Path.Combine(Directory.CreateTempSubdirectory("elsa-runbpmn-failure-").FullName, "runtime.sqlite");
                await using var source = new SqliteConnection($"Data Source={host.RuntimeDatabase};Mode=ReadOnly;Pooling=False");
                await using var target = new SqliteConnection($"Data Source={diagnostic};Pooling=False");
                await source.OpenAsync(); await target.OpenAsync(); source.BackupDatabase(target);
                throw new InvalidOperationException($"The actual model/native return did not settle. Closed diagnostic: {diagnostic}. Native: {view.ToJsonString()}. Model: {model.ToJsonString()}.");
            }
            await Task.Delay(100);
        }
        Assert.Empty(view["incidents"]!.AsArray());
        Assert.Equal(cancel ? "Cancelled" : "Completed", Assert.Single(view["activities"]!.AsArray(), item => item!["authoredActivityId"]!.GetValue<string>() == "run-bpmn")!["status"]!.GetValue<string>());
        if (!cancel && parent != "Root") Assert.Equal("Completed", Assert.Single(view["activities"]!.AsArray(), item => item!["authoredActivityId"]!.GetValue<string>() == "after")!["status"]!.GetValue<string>());
        Assert.Single((await host.GetAsync("/runtime/workflows/instances")).AsArray());
        var final = await host.GetAsync(modelPath);
        Assert.Equal(cancel ? "Cancelled" : "Completed", final["result"]!["status"]!.GetValue<string>());
        if (cancel)
        {
            await host.PostAsync(modelPath + "/work/" + waiting["invocationId"]!.GetValue<string>() + "/complete",
                new { requestId = "late-embedded", attemptId = waiting["attemptId"]!.GetValue<string>() }, System.Net.HttpStatusCode.Conflict);
            Assert.True(JsonNode.DeepEquals(final, await host.GetAsync(modelPath)));
        }
        JsonNode checkedFinal;
        deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (true)
        {
            checkedFinal = await host.ReadEmbeddingAsync(modelId, execution, directId);
            if (checkedFinal["sources"]!.AsArray().Count == (cancel ? 3 : 2) && checkedFinal["sources"]!.AsArray().All(item => item!["status"]!.GetValue<int>() == 2)) break;
            Assert.True(DateTimeOffset.UtcNow < deadline, "The actual model/native source acknowledgement remains owed.");
            await Task.Delay(100);
        }
        await host.StopAsync();
        var finishedPhysical = await host.ReadEmbeddingAsync(modelId, execution, directId);
        var completedRuntime = await host.SnapshotAsync("completed-runtime.sqlite.gz", caseName);
        var completedDesign = await host.SnapshotAsync("completed-design.sqlite.gz", caseName, design: true);
        await host.RestartAsync(); await host.LoginAsync();
        var thirdProcess = host.ProcessId; Assert.NotEqual(secondProcess, thirdProcess);
        Assert.True(JsonNode.DeepEquals(final, await host.GetAsync(modelPath)));
        Assert.Equal(cancel ? "Cancelled" : "Completed", (await host.GetAsync("/runtime/workflows/instances/" + execution))["instance"]!["status"]!.GetValue<string>());
        var retainedPhysical = await host.ReadEmbeddingAsync(modelId, execution, directId);
        Assert.True(JsonNode.DeepEquals(finishedPhysical, retainedPhysical));
        if (evidenceDirectory is not null)
        {
            var sourceRevision = Environment.GetEnvironmentVariable("ELSA_BPMN_HOST_SOURCE_REVISION") ?? throw new InvalidOperationException("Embedding evidence requires its exact executing source.");
            var receipt = new { formatVersion = 1, kind = "actual-native-runbpmn-human-recovery", sourceRevision, parent, cancel,
                nativeExecutionId = execution, modelExecutionId = modelId, directCounterpartId = directId, published, publication,
                firstProcess, secondProcess, thirdProcess, predecessorExit, termination = ".NET Process.Kill(entireProcessTree: true)",
                binaries = host.DescribeBinaries(sourceRevision, includePublishing: true), before, nativeBefore, final, nativeFinal = view,
                actualHumanWait = waiting, snapshots = new[] { waitingRuntime, waitingDesign, completedRuntime, completedDesign },
                limits = new[] { "Model-owned human-work profile only; native-service/ETL embedding is not yet admitted.",
                    "Same-source process loss; no upgraded-host, pending-parent-result crash or cancellation-before-admission acceptance.",
                    "Direct trace counterpart is qualified for completion only; no new direct-cancel endpoint is claimed.",
                    "Native parent whole-workflow cancellation only; no subtree, held retry or final Studio acceptance." } };
            var output = Path.Combine(evidenceDirectory, caseName);
            foreach (var item in new[] { ("case.json", (object)receipt), ("waiting-checked.json", waitingPhysical),
                         ("completed-checked.json", finishedPhysical), ("retained-checked.json", retainedPhysical) })
                await File.WriteAllTextAsync(Path.Combine(output, item.Item1), JsonSerializer.Serialize(item.Item2, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }) + Environment.NewLine);
        }

        async Task<string> Version(string type)
        {
            var catalog = await host.GetAsync("/design/activities/definitions?search=" + type);
            return Assert.Single(catalog["items"]!.AsArray(), item => item!["definition"]!["activityTypeKey"]!.GetValue<string>() == type)!["definition"]!["headVersionId"]!.GetValue<string>();
        }
    }
}
