using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bpmn.Model;
using Elsa.Activities.Bpmn.Core;
using Xunit;

namespace Elsa.Activities.Bpmn.Hosting.Tests;

public sealed class RunBpmnAdmissionCancellationTests
{
    [Fact]
    public async Task A_real_parent_cancel_before_model_admission_recovers_without_starting_semantics()
    {
        // Normal host configuration delays the next recovery sweep. The native command still commits
        // and retains its real start source in the durable outbox; no pump/store hook is used.
        await using var host = await OwnedWorkbench.StartAsync(directBpmn: true, recoverySweepSeconds: 300);
        await host.LoginAsync();
        var process = new BpmnProcessDefinition("cancel-before-admission", Elements:
            [new("start", BpmnElementTypes.StartEvent), new("human", BpmnElementTypes.UserTask, bindingRef: "work"), new("end", BpmnElementTypes.EndEvent)],
            SequenceFlows: [new("to-work", "start", "human"), new("to-end", "human", "end")]);
        var definition = new BpmnModelDefinition(1, JsonSerializer.SerializeToElement(process, new JsonSerializerOptions(JsonSerializerDefaults.Web)), ["work"]);
        var published = await host.PostAsync("/bpmn/artifacts", new { requestId = "publish-before-cancel", definition });
        var catalog = await host.GetAsync("/design/activities/definitions?search=Elsa.Activities.Bpmn.Activities.RunBpmn");
        var activity = Assert.Single(catalog["items"]!.AsArray(), item => item!["definition"]!["activityTypeKey"]!.GetValue<string>() == "Elsa.Activities.Bpmn.Activities.RunBpmn");
        var native = await host.PostAsync("/design/workflows/definitions/submit", new
        {
            name = "Actual cancel before model admission",
            state = new { variables = Array.Empty<object>(), inputs = Array.Empty<object>(), outputs = Array.Empty<object>(),
                rootActivity = new { nodeId = "run-bpmn", activityVersionId = activity!["definition"]!["headVersionId"]!.GetValue<string>(), outputs = Array.Empty<object>(),
                    inputs = new[] { new { referenceKey = "Model", value = new { value = published["sourceReferenceId"]!.GetValue<string>(), expressionType = "Literal" } } } } }
        });
        var publication = await host.PostAsync($"/publishing/workflows/{native["version"]!["id"]!.GetValue<string>()}/publish", new { });
        var started = await host.PostAsync($"/runtime/workflows/executables/{publication["artifactId"]!.GetValue<string>()}/execute",
            new { sourceReferenceId = publication["sourceReferenceId"]!.GetValue<string>() });
        var execution = started["workflowExecutionId"]!.GetValue<string>();
        var nativePath = "/runtime/workflows/instances/" + execution;
        var before = await host.GetAsync(nativePath);
        var invocation = Assert.Single(before["activities"]!.AsArray())!["activityExecutionId"]!.GetValue<string>();
        var detail = await host.GetAsync(nativePath + "/activity-executions/" + invocation);
        var wait = Assert.Single(detail["bookmarks"]!.AsArray())!;
        var modelId = wait["stimulusHash"]!.GetValue<string>();
        var modelPath = "/bpmn/executions/" + modelId;
        Assert.Null(await host.FindAsync(modelPath));
        var prepared = await host.ReadEmbeddingAsync(modelId, execution);
        Assert.Null(prepared["model"]); Assert.Empty(prepared["inputs"]!.AsArray());
        Assert.Empty(prepared["queuedModelInputs"]!.AsArray());
        Assert.Equal("Elsa.Bpmn.StartEmbedded.v1", Assert.Single(prepared["sources"]!.AsArray())!["intent"]!["kind"]!.GetValue<string>());
        const string caseName = "root-cancel-before-admission";
        var evidenceDirectory = Environment.GetEnvironmentVariable("ELSA_BPMN_HOST_EVIDENCE_DIRECTORY");
        if (evidenceDirectory is not null) Directory.CreateDirectory(Path.Combine(evidenceDirectory, caseName));
        var firstProcess = host.ProcessId;
        await host.PostAsync("/runtime/workflows/alteration-plans", new { target = new { executionIds = new[] { execution } },
            alterations = new[] { new { kind = "CancelWorkflow", schemaVersion = 1, payload = new { } } } }, idempotencyKey: "before-model-admission");
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while ((await host.GetAsync(nativePath))["instance"]!["status"]!.GetValue<string>() != "Cancelled")
        {
            Assert.Null(await host.FindAsync(modelPath));
            Assert.True(DateTimeOffset.UtcNow < deadline, "The normal alteration pump did not commit native cancellation before model admission.");
            await Task.Delay(100);
        }
        Assert.Null(await host.FindAsync(modelPath));
        await host.StopAsync(); var predecessorExit = host.LastStoppedExitCode;
        var cancelled = await host.ReadEmbeddingAsync(modelId, execution);
        Assert.Null(cancelled["model"]); Assert.Empty(cancelled["bookmarks"]!.AsArray());
        Assert.Empty(cancelled["queuedModelInputs"]!.AsArray());
        Assert.Equal(new[] { "Elsa.Bpmn.CancelEmbedded.v1", "Elsa.Bpmn.StartEmbedded.v1" }, cancelled["sources"]!.AsArray()
            .Select(item => item!["intent"]!["kind"]!.GetValue<string>()).Order(StringComparer.Ordinal));
        var cancelledRuntime = await host.SnapshotAsync("cancelled-runtime.sqlite.gz", caseName);
        var cancelledDesign = await host.SnapshotAsync("cancelled-design.sqlite.gz", caseName, design: true);
        await host.RestartAsync(recoverySweepSeconds: 0.2); await host.LoginAsync();
        var secondProcess = host.ProcessId; JsonNode settled = null!;
        deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (true)
        {
            settled = await host.ReadEmbeddingAsync(modelId, execution);
            if (settled["model"]?["lifecycle"]?.GetValue<int>() == 3 && settled["sources"]!.AsArray().Count == 3 &&
                settled["sources"]!.AsArray().All(item => item!["status"]!.GetValue<int>() == 2)) break;
            Assert.True(DateTimeOffset.UtcNow < deadline, "Pre-admission cancellation did not recover its actual model/native responsibility.");
            await Task.Delay(100);
        }
        var final = await host.GetAsync(modelPath);
        Assert.Equal("Cancelled", final["result"]!["status"]!.GetValue<string>());
        Assert.Empty((await host.GetAsync(modelPath + "/work"))["items"]!.AsArray());
        var state = settled["model"]!["payload"]!["state"]!;
        Assert.Equal(0, state["sequence"]!.GetValue<int>());
        Assert.Empty(state["tokens"]!.AsArray()); Assert.Empty(state["activeWork"]!.AsArray()); Assert.Empty(state["diagnostics"]!.AsArray());
        await host.StopAsync();
        var completed = await host.ReadEmbeddingAsync(modelId, execution);
        var completedRuntime = await host.SnapshotAsync("completed-runtime.sqlite.gz", caseName);
        var completedDesign = await host.SnapshotAsync("completed-design.sqlite.gz", caseName, design: true);
        await host.RestartAsync(); await host.LoginAsync(); var thirdProcess = host.ProcessId;
        var retained = await host.ReadEmbeddingAsync(modelId, execution);
        Assert.True(JsonNode.DeepEquals(completed, retained));
        Assert.True(JsonNode.DeepEquals(final, await host.GetAsync(modelPath)));
        Assert.Equal("Cancelled", (await host.GetAsync(nativePath))["instance"]!["status"]!.GetValue<string>());
        if (evidenceDirectory is not null)
        {
            var sourceRevision = Environment.GetEnvironmentVariable("ELSA_BPMN_HOST_SOURCE_REVISION") ?? throw new InvalidOperationException("Evidence requires its exact executing source.");
            var receipt = new { formatVersion = 1, kind = "actual-runbpmn-cancellation-before-admission", sourceRevision, nativeExecutionId = execution,
                modelExecutionId = modelId, published, publication, firstProcess, secondProcess, thirdProcess, predecessorExit,
                preparedRecoverySweepSeconds = 300, recoveredSweepSeconds = 0.2, actualWait = wait, actualNativeActivityDetail = detail,
                binaries = host.DescribeBinaries(sourceRevision, includePublishing: true),
                snapshots = new[] { cancelledRuntime, cancelledDesign, completedRuntime, completedDesign } };
            var output = Path.Combine(evidenceDirectory, caseName);
            await File.WriteAllTextAsync(Path.Combine(output, "case.json"), JsonSerializer.Serialize(receipt, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
            foreach (var (name, value) in new[] { ("prepared-checked", prepared), ("cancelled-checked", cancelled), ("completed-checked", completed), ("retained-checked", retained) })
                await File.WriteAllTextAsync(Path.Combine(output, name + ".json"), value.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
