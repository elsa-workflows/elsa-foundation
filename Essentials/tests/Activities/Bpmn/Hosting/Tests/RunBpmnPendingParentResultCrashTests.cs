using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bpmn.Model;
using Elsa.Activities.Bpmn.Core;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Activities.Bpmn.Hosting.Tests;

public sealed class RunBpmnPendingParentResultCrashTests
{
    [Fact]
    public async Task A_real_native_RunBpmn_recovers_once_when_the_host_crashes_after_the_model_result_before_parent_consumption()
    {
        await using var host = await OwnedWorkbench.StartAsync(directBpmn: true, recoverySweepSeconds: 0.2);
        await host.LoginAsync();
        var process = new BpmnProcessDefinition("pending-parent-result", Elements:
            [new("start", BpmnElementTypes.StartEvent), new("human", BpmnElementTypes.UserTask, bindingRef: "work"), new("end", BpmnElementTypes.EndEvent)],
            SequenceFlows: [new("to-work", "start", "human"), new("to-end", "human", "end")]);
        var definition = new BpmnModelDefinition(1, JsonSerializer.SerializeToElement(process, new JsonSerializerOptions(JsonSerializerDefaults.Web)), ["work"]);
        var published = await host.PostAsync("/bpmn/artifacts", new { requestId = "publish-pending-parent", definition });
        var catalog = await host.GetAsync("/design/activities/definitions?search=Elsa.Activities.Bpmn.Activities.RunBpmn");
        var activity = Assert.Single(catalog["items"]!.AsArray(), item => item!["definition"]!["activityTypeKey"]!.GetValue<string>() == "Elsa.Activities.Bpmn.Activities.RunBpmn");
        var native = await host.PostAsync("/design/workflows/definitions/submit", new
        {
            name = "Actual RunBpmn pending parent result crash",
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
        const string caseName = "root-pending-parent-result-crash";
        var evidenceDirectory = Environment.GetEnvironmentVariable("ELSA_BPMN_HOST_EVIDENCE_DIRECTORY");
        if (evidenceDirectory is not null) Directory.CreateDirectory(Path.Combine(evidenceDirectory, caseName));
        host.HoldClaimedBpmnParentResume();
        var completing = host.PostAsync(modelPath + "/work/" + waiting["invocationId"]!.GetValue<string>() + "/complete",
            new { requestId = "complete-embedded", attemptId = waiting["attemptId"]!.GetValue<string>() });
        JsonNode pendingModel = null!; JsonNode pendingNative = null!; JsonNode claimedSource = null!;
        deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (true)
        {
            pendingModel = await host.GetAsync(modelPath);
            pendingNative = await host.GetAsync(nativePath);
            if (pendingModel["status"]!.GetValue<string>() == "TerminalPendingSettlement" &&
                pendingModel["result"]!["status"]!.GetValue<string>() == "Completed" &&
                pendingNative["instance"]!["status"]!.GetValue<string>() == "Running")
            {
                var physical = await host.ReadEmbeddingAsync(modelId, execution);
                var candidate = physical["sources"]!.AsArray().SingleOrDefault(item =>
                    item!["intent"]!["kind"]!.GetValue<string>() == "Elsa.Bpmn.ResumeNativeParent.v1");
                if (candidate?["status"]?.GetValue<int>() == (int)RuntimePostCommitOutboxStatus.Delivering)
                {
                    claimedSource = candidate;
                    break;
                }
            }
            Assert.True(DateTimeOffset.UtcNow < deadline, "The model did not reach its terminal result while the native parent was still waiting.");
            await Task.Delay(100);
        }
        Assert.Empty(pendingNative["incidents"]!.AsArray());
        var pendingActivity = Assert.Single(pendingNative["activities"]!.AsArray(), item => item!["authoredActivityId"]!.GetValue<string>() == "run-bpmn")!;
        Assert.Equal("Suspended", pendingActivity["status"]!.GetValue<string>());
        var pendingDetail = await host.GetAsync(nativePath + "/activity-executions/" + pendingActivity["activityExecutionId"]!.GetValue<string>());
        Assert.Single(pendingDetail["bookmarks"]!.AsArray());
        var firstProcess = host.ProcessId;
        await host.StopAsync();
        var predecessorExit = host.LastStoppedExitCode;
        Assert.NotNull(predecessorExit); Assert.NotEqual(0, predecessorExit.Value);
        try { await completing; }
        catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException or TaskCanceledException or IOException) { }
        var pendingPhysical = await host.ReadEmbeddingAsync(modelId, execution);
        Assert.Equal(2, pendingPhysical["model"]!["lifecycle"]!.GetValue<int>());
        Assert.Equal("Completed", pendingPhysical["model"]!["terminalResult"]!["status"]!.GetValue<string>());
        Assert.False(pendingPhysical["nativeParent"]!["status"]!.GetValue<int>() is 3 or 4 or 5,
            "The native parent must still be waiting when the host is killed.");
        Assert.Single(pendingPhysical["bookmarks"]!.AsArray());
        var pendingResult = Assert.Single(pendingPhysical["sources"]!.AsArray(),
            item => item!["intent"]!["kind"]!.GetValue<string>() == "Elsa.Bpmn.ResumeNativeParent.v1")!;
        Assert.Equal((int)RuntimePostCommitOutboxStatus.Delivering, pendingResult["status"]!.GetValue<int>());
        Assert.True(pendingResult["deliveryFencingToken"]!.GetValue<long>() > 0);
        Assert.Equal(claimedSource["deliveryFencingToken"]!.GetValue<long>(), pendingResult["deliveryFencingToken"]!.GetValue<long>());
        var visibleAfter = pendingResult["deliveryVisibleAfter"]!.GetValue<DateTimeOffset>();
        Assert.Equal(claimedSource["deliveryVisibleAfter"]!.GetValue<DateTimeOffset>(), visibleAfter);
        Assert.True(visibleAfter > DateTimeOffset.UtcNow, "The crash must retain an unexpired actual delivery claim.");
        var pendingRuntime = await host.SnapshotAsync("pending-runtime.sqlite.gz", caseName);
        var pendingDesign = await host.SnapshotAsync("pending-design.sqlite.gz", caseName, design: true);
        host.ReleaseNativeParentResumeHold();
        await host.RestartAsync(); await host.LoginAsync();
        var secondProcess = host.ProcessId; Assert.NotEqual(firstProcess, secondProcess);
        JsonNode settled = null!; JsonNode nativeFinal = null!; JsonNode final = null!;
        var restartedAt = DateTimeOffset.UtcNow;
        Assert.True(visibleAfter <= restartedAt + RuntimePostCommitOutboxProcessing.ClaimVisibilityTimeout);
        var recoveryEligibleAt = visibleAfter > restartedAt ? visibleAfter : restartedAt;
        // A dead deliverer's real claim is fenced for one minute. Keep the original thirty-
        // second recovery bound, measured from actual eligibility rather than process startup.
        deadline = recoveryEligibleAt.AddSeconds(30);
        while (true)
        {
            settled = await host.ReadEmbeddingAsync(modelId, execution);
            nativeFinal = await host.GetAsync(nativePath);
            final = await host.GetAsync(modelPath);
            if (DateTimeOffset.UtcNow < visibleAfter)
                Assert.Equal("Running", nativeFinal["instance"]!["status"]!.GetValue<string>());
            if (final["status"]!.GetValue<string>() == "TerminalSettled" && nativeFinal["instance"]!["status"]!.GetValue<string>() == "Completed" &&
                settled["sources"]!.AsArray().Count >= 2 && settled["sources"]!.AsArray().All(item => item!["status"]!.GetValue<int>() == 2))
                break;
            Assert.True(DateTimeOffset.UtcNow < deadline, $"The restarted host did not deliver exactly one native parent continuation after claim eligibility {recoveryEligibleAt:o}. Physical: {settled.ToJsonString()}. Native: {nativeFinal.ToJsonString()}. Model: {final.ToJsonString()}.");
            await Task.Delay(100);
        }
        Assert.Empty(nativeFinal["incidents"]!.AsArray());
        var completedActivities = nativeFinal["activities"]!.AsArray()
            .Where(item => item!["authoredActivityId"]!.GetValue<string>() == "run-bpmn").ToArray();
        var completedRun = Assert.Single(completedActivities)!;
        Assert.Equal("Completed", completedRun["status"]!.GetValue<string>());
        Assert.Equal("Done", Assert.Single(completedRun["outcomeNames"]!.AsArray())!.GetValue<string>());
        Assert.Equal("Completed", final["result"]!["status"]!.GetValue<string>());
        Assert.Single((await host.GetAsync("/runtime/workflows/instances")).AsArray());
        await host.PostAsync(modelPath + "/work/" + waiting["invocationId"]!.GetValue<string>() + "/complete",
            new { requestId = "late-embedded", attemptId = waiting["attemptId"]!.GetValue<string>() }, HttpStatusCode.Conflict);
        Assert.True(JsonNode.DeepEquals(final, await host.GetAsync(modelPath)));
        Assert.Equal("Completed", (await host.GetAsync(nativePath))["instance"]!["status"]!.GetValue<string>());
        Assert.Single((await host.GetAsync(nativePath))["activities"]!.AsArray(), item => item!["authoredActivityId"]!.GetValue<string>() == "run-bpmn");
        await host.StopAsync();
        var finishedPhysical = await host.ReadEmbeddingAsync(modelId, execution);
        var completedRuntime = await host.SnapshotAsync("completed-runtime.sqlite.gz", caseName);
        var completedDesign = await host.SnapshotAsync("completed-design.sqlite.gz", caseName, design: true);
        await host.RestartAsync(); await host.LoginAsync();
        var thirdProcess = host.ProcessId; Assert.NotEqual(secondProcess, thirdProcess);
        Assert.True(JsonNode.DeepEquals(final, await host.GetAsync(modelPath)));
        var retainedNative = await host.GetAsync(nativePath);
        Assert.Equal("Completed", retainedNative["instance"]!["status"]!.GetValue<string>());
        Assert.Single(retainedNative["activities"]!.AsArray(), item => item!["authoredActivityId"]!.GetValue<string>() == "run-bpmn");
        Assert.Equal("Done", Assert.Single(Assert.Single(retainedNative["activities"]!.AsArray(),
            item => item!["authoredActivityId"]!.GetValue<string>() == "run-bpmn")!["outcomeNames"]!.AsArray())!.GetValue<string>());
        var retainedPhysical = await host.ReadEmbeddingAsync(modelId, execution);
        Assert.True(JsonNode.DeepEquals(finishedPhysical, retainedPhysical));
        if (evidenceDirectory is not null)
        {
            var sourceRevision = Environment.GetEnvironmentVariable("ELSA_BPMN_HOST_SOURCE_REVISION") ?? throw new InvalidOperationException("Embedding evidence requires its exact executing source.");
            var receipt = new { formatVersion = 1, kind = "actual-native-runbpmn-pending-parent-result-crash", sourceRevision, parent = "Root",
                nativeExecutionId = execution, modelExecutionId = modelId, published, publication,
                firstProcess, secondProcess, thirdProcess, predecessorExit, visibleAfter, restartedAt, recoveryEligibleAt, termination = ".NET Process.Kill(entireProcessTree: true)",
                binaries = host.DescribeBinaries(sourceRevision, includePublishing: true), pendingModel, pendingNative, final, nativeFinal,
                actualHumanWait = waiting, snapshots = new[] { pendingRuntime, pendingDesign, completedRuntime, completedDesign },
                limits = new[] { "SQLite Owned Workbench only; no PostgreSQL/SQL Server/MySQL cell.",
                    "G2.1 phase 3: crash after model terminal, before native parent consumption. Wait-phase kill, instant settled-before-start, and admission cancellation are separate tests.",
                    "Root native parent only; Sequence/Flowchart not claimed.",
                    "Human-work profile only; no native-service/ETL embedding, Studio, R3, or generic-kernel extraction." } };
            var output = Path.Combine(evidenceDirectory, caseName);
            foreach (var item in new[] { ("case.json", (object)receipt), ("pending-checked.json", pendingPhysical),
                         ("completed-checked.json", finishedPhysical), ("retained-checked.json", retainedPhysical) })
                await File.WriteAllTextAsync(Path.Combine(output, item.Item1), JsonSerializer.Serialize(item.Item2, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }) + Environment.NewLine);
        }
    }
}
