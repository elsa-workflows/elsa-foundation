using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bpmn.Model;
using Elsa.Activities.Bpmn.Core;
using Xunit;

namespace Elsa.Activities.Bpmn.Hosting.Tests;

/// <summary>
/// Cross-model G2.4 evidence: a native Root RunBpmn parent waits on a direct BPMN model. An operator
/// cancel of that model (POST /bpmn/executions/{id}/cancel) is a child-initiated terminal, not a
/// parent CancelWorkflow. Spec 1017 T040's three native parent terminal mappings, implemented by
/// BpmnModelActivityRuntime.ResumeAsync, map model Cancelled to ActivityTransition.Cancel. The
/// engine's ActivityCancellationCheckpointService then terminates the surrounding run. For Root,
/// that is the parent workflow becoming Cancelled — an observed mapping, not the child issuing
/// CancelWorkflow. Cancellation authority stays parent-owned: cancel-embedded still requires the
/// parent to already be Cancelled/terminal (BpmnEmbeddingBoundary.ValidateInputAsync).
/// SQLite Owned Workbench, one in-process host. Reuses the existing BpmnEmbeddingDelivery hold
/// (ELSA_BPMN_HOLD_NATIVE_PARENT_RESUME); no new call site.
/// </summary>
public sealed class RunBpmnChildCancelTests
{
    [Fact]
    public async Task A_child_model_cancel_maps_to_native_parent_Cancelled_once_and_survives_a_pending_parent_resume_crash()
    {
        await using var host = await OwnedWorkbench.StartAsync(directBpmn: true, recoverySweepSeconds: 0.2);
        await host.LoginAsync();
        var process = new BpmnProcessDefinition("child-cancel-human", Elements:
            [new("start", BpmnElementTypes.StartEvent), new("human", BpmnElementTypes.UserTask, bindingRef: "work"), new("end", BpmnElementTypes.EndEvent)],
            SequenceFlows: [new("to-work", "start", "human"), new("to-end", "human", "end")]);
        var definition = new BpmnModelDefinition(1, JsonSerializer.SerializeToElement(process, new JsonSerializerOptions(JsonSerializerDefaults.Web)), ["work"]);
        var published = await host.PostAsync("/bpmn/artifacts", new { requestId = "publish-child-cancel", definition });
        var catalog = await host.GetAsync("/design/activities/definitions?search=Elsa.Activities.Bpmn.Activities.RunBpmn");
        var activity = Assert.Single(catalog["items"]!.AsArray(), item => item!["definition"]!["activityTypeKey"]!.GetValue<string>() == "Elsa.Activities.Bpmn.Activities.RunBpmn");
        var native = await host.PostAsync("/design/workflows/definitions/submit", new
        {
            name = "Actual RunBpmn child-to-parent cancel",
            state = new { variables = Array.Empty<object>(), inputs = Array.Empty<object>(), outputs = Array.Empty<object>(),
                rootActivity = new { nodeId = "run-bpmn", activityVersionId = activity!["definition"]!["headVersionId"]!.GetValue<string>(), outputs = Array.Empty<object>(),
                    inputs = new[] { new { referenceKey = "Model", value = new { value = published["sourceReferenceId"]!.GetValue<string>(), expressionType = "Literal" } } } } }
        });
        var publication = await host.PostAsync($"/publishing/workflows/{native["version"]!["id"]!.GetValue<string>()}/publish", new { });
        var started = await host.PostAsync($"/runtime/workflows/executables/{publication["artifactId"]!.GetValue<string>()}/execute",
            new { sourceReferenceId = publication["sourceReferenceId"]!.GetValue<string>() });
        var execution = started["workflowExecutionId"]!.GetValue<string>();
        var nativePath = "/runtime/workflows/instances/" + execution;
        JsonNode view = null!; string? modelId = null; var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (modelId is null)
        {
            Assert.True(DateTimeOffset.UtcNow < deadline, "The actual RunBpmn native wait did not publish its model invocation.");
            view = await host.GetAsync(nativePath);
            Assert.Empty(view["incidents"]!.AsArray());
            if (view["activities"]!.AsArray().SingleOrDefault(item => item!["activityType"]!.GetValue<string>() == "Elsa.Activities.Bpmn.Activities.RunBpmn") is { } root)
            {
                var detail = await host.GetAsync(nativePath + "/activity-executions/" + root["activityExecutionId"]!.GetValue<string>());
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
        Assert.Equal("Running", (await host.GetAsync(modelPath))["status"]!.GetValue<string>());
        Assert.Equal("Running", (await host.GetAsync(nativePath))["instance"]!["status"]!.GetValue<string>());
        const string caseName = "root-child-model-cancel";
        var evidenceDirectory = Environment.GetEnvironmentVariable("ELSA_BPMN_HOST_EVIDENCE_DIRECTORY");
        if (evidenceDirectory is not null) Directory.CreateDirectory(Path.Combine(evidenceDirectory, caseName));
        var cancel = new { requestId = "cancel-embedded-child", reason = "Operator cancelled the embedded model." };
        host.HoldNativeParentResume();
        var cancelling = host.PostAsync(modelPath + "/cancel", cancel);
        JsonNode pendingModel = null!; JsonNode pendingNative = null!;
        deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (true)
        {
            pendingModel = await host.GetAsync(modelPath);
            pendingNative = await host.GetAsync(nativePath);
            if (pendingModel["status"]!.GetValue<string>() == "TerminalPendingSettlement" &&
                pendingModel["result"]!["status"]!.GetValue<string>() == "Cancelled" &&
                pendingNative["instance"]!["status"]!.GetValue<string>() == "Running")
                break;
            Assert.True(DateTimeOffset.UtcNow < deadline, "The child model cancel did not reach a terminal result while the native parent was still waiting.");
            await Task.Delay(100);
        }
        Assert.Empty(pendingNative["incidents"]!.AsArray());
        var pendingActivity = Assert.Single(pendingNative["activities"]!.AsArray(), item => item!["authoredActivityId"]!.GetValue<string>() == "run-bpmn")!;
        Assert.Equal("Suspended", pendingActivity["status"]!.GetValue<string>());
        var pendingDetail = await host.GetAsync(nativePath + "/activity-executions/" + pendingActivity["activityExecutionId"]!.GetValue<string>());
        Assert.Single(pendingDetail["bookmarks"]!.AsArray());
        Assert.Equal("Operator cancelled the embedded model.", pendingModel["result"]!["reason"]!.GetValue<string>());
        var firstProcess = host.ProcessId;
        await host.StopAsync();
        var predecessorExit = host.LastStoppedExitCode;
        Assert.NotNull(predecessorExit); Assert.NotEqual(0, predecessorExit.Value);
        try { await cancelling; }
        catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException or TaskCanceledException or IOException) { }
        var pendingPhysical = await host.ReadEmbeddingAsync(modelId, execution);
        Assert.Equal(2, pendingPhysical["model"]!["lifecycle"]!.GetValue<int>());
        Assert.Equal("Cancelled", pendingPhysical["model"]!["terminalResult"]!["status"]!.GetValue<string>());
        Assert.False(pendingPhysical["nativeParent"]!["status"]!.GetValue<int>() is 3 or 4 or 5,
            "The native parent must still be waiting when the host is killed.");
        Assert.Single(pendingPhysical["bookmarks"]!.AsArray());
        var pendingResult = Assert.Single(pendingPhysical["sources"]!.AsArray(),
            item => item!["intent"]!["kind"]!.GetValue<string>() == "Elsa.Bpmn.ResumeNativeParent.v1")!;
        Assert.NotEqual(2, pendingResult["status"]!.GetValue<int>());
        Assert.DoesNotContain(pendingPhysical["sources"]!.AsArray(),
            item => item!["intent"]!["kind"]!.GetValue<string>() == "Elsa.Bpmn.CancelEmbedded.v1");
        var pendingRuntime = await host.SnapshotAsync("pending-runtime.sqlite.gz", caseName);
        var pendingDesign = await host.SnapshotAsync("pending-design.sqlite.gz", caseName, design: true);
        host.ReleaseNativeParentResumeHold();
        await host.RestartAsync(); await host.LoginAsync();
        var secondProcess = host.ProcessId; Assert.NotEqual(firstProcess, secondProcess);
        JsonNode settled = null!; JsonNode nativeFinal = null!; JsonNode final = null!;
        deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (true)
        {
            settled = await host.ReadEmbeddingAsync(modelId, execution);
            nativeFinal = await host.GetAsync(nativePath);
            final = await host.GetAsync(modelPath);
            if (final["status"]!.GetValue<string>() == "TerminalSettled" && nativeFinal["instance"]!["status"]!.GetValue<string>() == "Cancelled" &&
                settled["sources"]!.AsArray().Count >= 2 && settled["sources"]!.AsArray().All(item => item!["status"]!.GetValue<int>() == 2))
                break;
            Assert.True(DateTimeOffset.UtcNow < deadline, "The restarted host did not map the child Cancelled result to exactly one native parent Cancel.");
            await Task.Delay(100);
        }
        Assert.Empty(nativeFinal["incidents"]!.AsArray());
        Assert.NotEqual("Running", nativeFinal["instance"]!["status"]!.GetValue<string>());
        Assert.NotEqual("Completed", nativeFinal["instance"]!["status"]!.GetValue<string>());
        Assert.NotEqual("Faulted", nativeFinal["instance"]!["status"]!.GetValue<string>());
        var cancelledActivities = nativeFinal["activities"]!.AsArray()
            .Where(item => item!["authoredActivityId"]!.GetValue<string>() == "run-bpmn").ToArray();
        var cancelledRun = Assert.Single(cancelledActivities)!;
        Assert.Equal("Cancelled", cancelledRun["status"]!.GetValue<string>());
        Assert.DoesNotContain(cancelledRun["outcomeNames"]?.AsArray() ?? [], name => name!.GetValue<string>() == "Done");
        Assert.Equal("Cancelled", final["result"]!["status"]!.GetValue<string>());
        Assert.Equal("Operator cancelled the embedded model.", final["result"]!["reason"]!.GetValue<string>());
        Assert.Single((await host.GetAsync("/runtime/workflows/instances")).AsArray());
        Assert.Contains(settled["sources"]!.AsArray(), item => item!["intent"]!["kind"]!.GetValue<string>() == "Elsa.Bpmn.StartEmbedded.v1");
        Assert.Contains(settled["sources"]!.AsArray(), item => item!["intent"]!["kind"]!.GetValue<string>() == "Elsa.Bpmn.ResumeNativeParent.v1");
        var replayed = await host.PostAsync(modelPath + "/cancel", cancel);
        Assert.True(replayed["replay"]!.GetValue<bool>());
        await host.PostAsync(modelPath + "/work/" + waiting["invocationId"]!.GetValue<string>() + "/complete",
            new { requestId = "late-embedded", attemptId = waiting["attemptId"]!.GetValue<string>() }, HttpStatusCode.Conflict);
        var lateCancel = await host.PostAsync(modelPath + "/cancel",
            new { requestId = "late-child-cancel", reason = "too late" }, HttpStatusCode.Conflict);
        Assert.Equal("execution-terminal", lateCancel["result"]!["code"]!.GetValue<string>());
        Assert.True(JsonNode.DeepEquals(final, await host.GetAsync(modelPath)));
        Assert.Equal("Cancelled", (await host.GetAsync(nativePath))["instance"]!["status"]!.GetValue<string>());
        Assert.Single((await host.GetAsync(nativePath))["activities"]!.AsArray(), item => item!["authoredActivityId"]!.GetValue<string>() == "run-bpmn");
        await host.StopAsync();
        var finishedPhysical = await host.ReadEmbeddingAsync(modelId, execution);
        var completedRuntime = await host.SnapshotAsync("completed-runtime.sqlite.gz", caseName);
        var completedDesign = await host.SnapshotAsync("completed-design.sqlite.gz", caseName, design: true);
        await host.RestartAsync(); await host.LoginAsync();
        var thirdProcess = host.ProcessId; Assert.NotEqual(secondProcess, thirdProcess);
        Assert.True(JsonNode.DeepEquals(final, await host.GetAsync(modelPath)));
        var retainedNative = await host.GetAsync(nativePath);
        Assert.Equal("Cancelled", retainedNative["instance"]!["status"]!.GetValue<string>());
        Assert.Empty(retainedNative["incidents"]!.AsArray());
        Assert.Single(retainedNative["activities"]!.AsArray(), item => item!["authoredActivityId"]!.GetValue<string>() == "run-bpmn");
        Assert.Equal("Cancelled", Assert.Single(retainedNative["activities"]!.AsArray(),
            item => item!["authoredActivityId"]!.GetValue<string>() == "run-bpmn")!["status"]!.GetValue<string>());
        var retainedPhysical = await host.ReadEmbeddingAsync(modelId, execution);
        Assert.True(JsonNode.DeepEquals(finishedPhysical, retainedPhysical));
        if (evidenceDirectory is not null)
        {
            var sourceRevision = Environment.GetEnvironmentVariable("ELSA_BPMN_HOST_SOURCE_REVISION") ?? throw new InvalidOperationException("Embedding evidence requires its exact executing source.");
            var receipt = new { formatVersion = 1, kind = "actual-native-runbpmn-child-model-cancel", sourceRevision, parent = "Root",
                nativeExecutionId = execution, modelExecutionId = modelId, published, publication,
                firstProcess, secondProcess, thirdProcess, predecessorExit, termination = ".NET Process.Kill(entireProcessTree: true)",
                binaries = host.DescribeBinaries(sourceRevision, includePublishing: true), pendingModel, pendingNative, final, nativeFinal,
                actualHumanWait = waiting, snapshots = new[] { pendingRuntime, pendingDesign, completedRuntime, completedDesign },
                limits = new[] { "SQLite Owned Workbench only; no PostgreSQL/SQL Server/MySQL cell.",
                    "G2.4 child-to-parent cancel: operator cancel of the embedded model while Root RunBpmn waits. Parent-to-child whole-workflow cancel, admission cancel, and pending-parent-result crash are separate tests.",
                    "Root native parent only; Sequence/Flowchart/CompensationScope not claimed.",
                    "Human-work profile only; no native-service/ETL embedding, Studio, R3, or generic-kernel extraction." } };
            var output = Path.Combine(evidenceDirectory, caseName);
            foreach (var item in new[] { ("case.json", (object)receipt), ("pending-checked.json", pendingPhysical),
                         ("completed-checked.json", finishedPhysical), ("retained-checked.json", retainedPhysical) })
                await File.WriteAllTextAsync(Path.Combine(output, item.Item1), JsonSerializer.Serialize(item.Item2, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }) + Environment.NewLine);
        }
    }
}
