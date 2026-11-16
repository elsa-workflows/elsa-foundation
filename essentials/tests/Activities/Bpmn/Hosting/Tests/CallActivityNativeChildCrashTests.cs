using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Elsa.Activities.Bpmn.Hosting.Tests;

/// <summary>
/// G2.3 composite-plane process-kill: a native WorkflowExecution whose root is BpmnProcess runs a
/// callActivity that dispatches a native-root child. Direct BPMN publish rejects CallActivity.
/// SQLite Owned Workbench, one host.
/// </summary>
public sealed class CallActivityNativeChildCrashTests
{
    [Fact]
    public async Task A_composite_callActivity_recovers_once_when_the_host_crashes_while_the_native_child_is_waiting()
    {
        await using var host = await OwnedWorkbench.StartAsync(recoverySweepSeconds: 0.2);
        await host.LoginAsync();
        var eventName = "call-child-release-" + Guid.NewGuid().ToString("N");
        var child = await Publish(host, "CallActivity native wait child",
            await Node(host, "child-wait", "Elsa.Activities.Primitives.Activities.Event",
                [Literal("EventName", eventName), Literal("CanStartWorkflow", false)]));
        var parent = await PublishCallParent(host, "CallActivity wait-phase crash parent", child["definitionId"]!.GetValue<string>(),
            "PARENT continued after the waiting native child completed.");
        var parentId = await Execute(host, parent);
        JsonNode parentView = null!; JsonNode childView = null!; JsonNode dispatch = null!;
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (true)
        {
            (parentView, childView, dispatch) = await ObserveAsync(host, parentId, child["artifactId"]!.GetValue<string>());
            if (parentView["instance"]!["status"]!.GetValue<string>() == "Running" &&
                childView["instance"]!["status"]!.GetValue<string>() == "Running" &&
                Status(dispatch) == "Started" &&
                TryNode(parentView, "node-call")?["status"]?.GetValue<string>() == "Suspended" &&
                TryNode(childView, "child-wait")?["status"]?.GetValue<string>() == "Suspended")
                break;
            Assert.True(DateTimeOffset.UtcNow < deadline,
                "The native child did not reach its wait under the composite callActivity. Parent=" + parentView.ToJsonString() +
                " Child=" + childView.ToJsonString() + " Dispatch=" + dispatch.ToJsonString());
            await Task.Delay(100);
        }
        Assert.Empty(parentView["incidents"]!.AsArray());
        Assert.Empty(childView["incidents"]!.AsArray());
        Assert.DoesNotContain(parentView["activities"]!.AsArray(), item => IsNode(item, "node-after"));
        var firstProcess = host.ProcessId;
        await host.StopAsync();
        var predecessorExit = host.LastStoppedExitCode;
        Assert.NotNull(predecessorExit); Assert.NotEqual(0, predecessorExit.Value);
        await host.RestartAsync(); await host.LoginAsync();
        var secondProcess = host.ProcessId; Assert.NotEqual(firstProcess, secondProcess);
        (parentView, childView, dispatch) = await ObserveAsync(host, parentId, child["artifactId"]!.GetValue<string>());
        Assert.Equal("Running", parentView["instance"]!["status"]!.GetValue<string>());
        Assert.Equal("Running", childView["instance"]!["status"]!.GetValue<string>());
        Assert.Equal("Started", Status(dispatch));
        Assert.Equal(childView["instance"]!["workflowExecutionId"]!.GetValue<string>(), dispatch["childWorkflowExecutionId"]!.GetValue<string>());
        Assert.Single(await DispatchesAsync(host, parentId));
        var publisher = await Publish(host, "CallActivity wait-phase crash publisher",
            await Node(host, "send", "Elsa.Activities.Primitives.Activities.PublishEvent",
                [Literal("EventName", eventName), Literal("IsLocalEvent", false)]));
        await Execute(host, publisher);
        var settled = await WaitSettledAsync(host, parentId, child["artifactId"]!.GetValue<string>());
        AssertSettledOnce(settled.Parent, settled.Child, settled.Dispatch);
        await host.StopAsync();
        await host.RestartAsync(); await host.LoginAsync();
        var thirdProcess = host.ProcessId; Assert.NotEqual(secondProcess, thirdProcess);
        var retained = await ObserveAsync(host, parentId, child["artifactId"]!.GetValue<string>());
        AssertSettledOnce(retained.Parent, retained.Child, retained.Dispatch);
        WriteReceipt(host, "wait-phase", parentId, firstProcess, secondProcess, thirdProcess, predecessorExit,
            "G2.3 wait-phase: Process.Kill while the native Event child is suspended, before it completes.",
            new[] { "SQLite Owned Workbench only; no PostgreSQL/SQL Server/MySQL cell.",
                "Composite BpmnProcess callActivity -> DispatchWorkflow -> native Event root. Direct-plane CallActivity is refused at publish.",
                "Root composition only; Sequence/Flowchart not claimed.",
                "No child-to-parent cancel, G3 overhead, Studio, ETL, R3, or generic-kernel extraction." });
    }

    [Fact]
    public async Task A_composite_callActivity_recovers_once_when_the_host_crashes_after_the_native_child_completes_before_parent_consumption()
    {
        await using var host = await OwnedWorkbench.StartAsync(recoverySweepSeconds: 0.2);
        await host.LoginAsync();
        var child = await Publish(host, "CallActivity native complete child",
            await Node(host, "child-write", "Elsa.Activities.Primitives.Activities.WriteLine",
                [Literal("text", "CHILD (callActivity kill) completed.")]));
        var parent = await PublishCallParent(host, "CallActivity pending-parent-resume crash parent", child["definitionId"]!.GetValue<string>(),
            "PARENT continued after the completed native child was consumed once.");
        host.HoldNativeParentResume();
        var parentId = await Execute(host, parent);
        JsonNode parentView = null!; JsonNode childView = null!; JsonNode dispatch = null!;
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (true)
        {
            (parentView, childView, dispatch) = await ObserveAsync(host, parentId, child["artifactId"]!.GetValue<string>());
            if (parentView["instance"]!["status"]!.GetValue<string>() == "Running" &&
                childView["instance"]!["status"]!.GetValue<string>() == "Completed" &&
                Status(dispatch) == "Completed" &&
                TryNode(parentView, "node-call")?["status"]?.GetValue<string>() == "Suspended")
                break;
            Assert.True(DateTimeOffset.UtcNow < deadline,
                "The native child did not complete while the composite parent was still waiting to consume it. Parent=" + parentView.ToJsonString() +
                " Child=" + childView.ToJsonString() + " Dispatch=" + dispatch.ToJsonString());
            await Task.Delay(100);
        }
        Assert.Empty(parentView["incidents"]!.AsArray());
        Assert.Empty(childView["incidents"]!.AsArray());
        Assert.DoesNotContain(parentView["activities"]!.AsArray(), item => IsNode(item, "node-after"));
        Assert.Single(await DispatchesAsync(host, parentId));
        var firstProcess = host.ProcessId;
        await host.StopAsync();
        var predecessorExit = host.LastStoppedExitCode;
        Assert.NotNull(predecessorExit); Assert.NotEqual(0, predecessorExit.Value);
        host.ReleaseNativeParentResumeHold();
        await host.RestartAsync(); await host.LoginAsync();
        var secondProcess = host.ProcessId; Assert.NotEqual(firstProcess, secondProcess);
        var settled = await WaitSettledAsync(host, parentId, child["artifactId"]!.GetValue<string>());
        AssertSettledOnce(settled.Parent, settled.Child, settled.Dispatch);
        await host.StopAsync();
        await host.RestartAsync(); await host.LoginAsync();
        var thirdProcess = host.ProcessId; Assert.NotEqual(secondProcess, thirdProcess);
        var retained = await ObserveAsync(host, parentId, child["artifactId"]!.GetValue<string>());
        AssertSettledOnce(retained.Parent, retained.Child, retained.Dispatch);
        WriteReceipt(host, "pending-parent-resume", parentId, firstProcess, secondProcess, thirdProcess, predecessorExit,
            "G2.3 pending-parent-resume: Process.Kill after the native child completed and before BpmnProcess consumed the dispatch outcome.",
            new[] { "SQLite Owned Workbench only; no PostgreSQL/SQL Server/MySQL cell.",
                "Composite BpmnProcess callActivity -> DispatchWorkflow -> native WriteLine root. Direct-plane CallActivity is refused at publish.",
                "Reuses the #3992 OwnedWorkbench file-hold (ELSA_BPMN_HOLD_NATIVE_PARENT_RESUME) at ParentResumeExecutor, before bookmark resume.",
                "Root composition only; Sequence/Flowchart not claimed.",
                "No child-to-parent cancel, G3 overhead, Studio, ETL, R3, or generic-kernel extraction." });
    }

    private static async Task<JsonNode> PublishCallParent(OwnedWorkbench host, string name, string childDefinitionId, string afterText)
    {
        var call = await Node(host, "node-call", "Elsa.Activities.DispatchWorkflow.Runtime.Activities.DispatchWorkflow",
            [Literal("WorkflowDefinitionId", childDefinitionId), Literal("WaitForCompletion", true)]);
        var after = await Node(host, "node-after", "Elsa.Activities.Primitives.Activities.WriteLine", [Literal("text", afterText)]);
        return await Publish(host, name, await Node(host, "bpmn-root", "Elsa.Activities.Bpmn.Activities.BpmnProcess", [],
            new
            {
                kind = "elsa.bpmn.structure", schemaVersion = "1.0.0",
                payload = new
                {
                    activities = new[] { call, after },
                    elements = new object[]
                    {
                        new { elementId = "start", elementType = "startEvent" },
                        new { elementId = "call", elementType = "callActivity", childNodeId = "node-call" },
                        new { elementId = "after", elementType = "task", childNodeId = "node-after" },
                        new { elementId = "end", elementType = "endEvent" }
                    },
                    sequenceFlows = new[]
                    {
                        new { flowId = "flow-1", sourceRef = "start", targetRef = "call" },
                        new { flowId = "flow-2", sourceRef = "call", targetRef = "after" },
                        new { flowId = "flow-3", sourceRef = "after", targetRef = "end" }
                    }
                }
            }));
    }

    private static async Task<(JsonNode Parent, JsonNode Child, JsonNode Dispatch)> ObserveAsync(OwnedWorkbench host, string parentId, string childArtifactId)
    {
        var parent = await host.GetAsync("/runtime/workflows/instances/" + parentId);
        Assert.Empty(parent["incidents"]!.AsArray());
        var dispatches = await DispatchesAsync(host, parentId);
        Assert.True(dispatches.Count <= 1, "The composite callActivity dispatched more than one child.");
        if (dispatches.Count == 0)
            return (parent, new JsonObject { ["instance"] = new JsonObject { ["status"] = "Missing" } }, new JsonObject { ["status"] = "Missing" });
        var dispatch = dispatches[0]!;
        var childId = dispatch["childWorkflowExecutionId"]?.GetValue<string>();
        if (string.IsNullOrEmpty(childId))
            return (parent, new JsonObject { ["instance"] = new JsonObject { ["status"] = "Missing" } }, dispatch);
        var child = await host.FindAsync("/runtime/workflows/instances/" + childId);
        if (child is null)
            return (parent, new JsonObject { ["instance"] = new JsonObject { ["status"] = "Missing" } }, dispatch);
        Assert.Equal(childArtifactId, child["instance"]!["artifactId"]!.GetValue<string>());
        Assert.Equal(parentId, child["instance"]!["parentWorkflowExecutionId"]!.GetValue<string>());
        return (parent, child, dispatch);
    }

    private static async Task<(JsonNode Parent, JsonNode Child, JsonNode Dispatch)> WaitSettledAsync(OwnedWorkbench host, string parentId, string childArtifactId)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (true)
        {
            var observed = await ObserveAsync(host, parentId, childArtifactId);
            if (observed.Parent["instance"]!["status"]!.GetValue<string>() == "Completed" &&
                observed.Child["instance"]!["status"]!.GetValue<string>() == "Completed" &&
                Status(observed.Dispatch) == "Completed" &&
                TryNode(observed.Parent, "node-call")?["status"]?.GetValue<string>() == "Completed" &&
                TryNode(observed.Parent, "node-after")?["status"]?.GetValue<string>() == "Completed")
                return observed;
            Assert.True(DateTimeOffset.UtcNow < deadline, "The restarted host did not deliver exactly one callActivity continuation.");
            await Task.Delay(100);
        }
    }

    private static void AssertSettledOnce(JsonNode parent, JsonNode child, JsonNode dispatch)
    {
        Assert.Equal("Completed", parent["instance"]!["status"]!.GetValue<string>());
        Assert.Equal("Completed", child["instance"]!["status"]!.GetValue<string>());
        Assert.Equal("Completed", Status(dispatch));
        Assert.Empty(parent["incidents"]!.AsArray());
        Assert.Empty(child["incidents"]!.AsArray());
        var call = Assert.Single(parent["activities"]!.AsArray(), item => IsNode(item, "node-call"))!;
        Assert.Equal("Completed", call["status"]!.GetValue<string>());
        Assert.Equal("Completed", Assert.Single(call["outcomeNames"]!.AsArray())!.GetValue<string>());
        var after = Assert.Single(parent["activities"]!.AsArray(), item => IsNode(item, "node-after"))!;
        Assert.Equal("Completed", after["status"]!.GetValue<string>());
        Assert.Equal(parent["instance"]!["workflowExecutionId"]!.GetValue<string>(), dispatch["parentWorkflowExecutionId"]!.GetValue<string>());
        Assert.Equal(child["instance"]!["workflowExecutionId"]!.GetValue<string>(), dispatch["childWorkflowExecutionId"]!.GetValue<string>());
    }

    private static async Task<JsonArray> DispatchesAsync(OwnedWorkbench host, string parentId)
    {
        var listed = await host.GetAsync("/runtime/workflows/dispatches?parentWorkflowExecutionId=" + Uri.EscapeDataString(parentId));
        return listed as JsonArray ?? listed["items"]!.AsArray();
    }

    private static async Task<object> Node(OwnedWorkbench host, string id, string type, object[] inputs, object? structure = null)
    {
        var catalog = await host.GetAsync("/design/activities/definitions?search=" + Uri.EscapeDataString(type));
        var activity = Assert.Single(catalog["items"]!.AsArray(), item => item!["definition"]!["activityTypeKey"]!.GetValue<string>() == type)!;
        return new { nodeId = id, activityVersionId = activity["definition"]!["headVersionId"]!.GetValue<string>(), inputs, outputs = Array.Empty<object>(), structure };
    }

    private static async Task<JsonNode> Publish(OwnedWorkbench host, string name, object root)
    {
        var draft = await host.PostAsync("/design/workflows/definitions/submit", new
        {
            name,
            state = new { variables = Array.Empty<object>(), inputs = Array.Empty<object>(), outputs = Array.Empty<object>(), rootActivity = root }
        });
        return await host.PostAsync("/publishing/workflows/" + draft["version"]!["id"]!.GetValue<string>() + "/publish", new { });
    }

    private static async Task<string> Execute(OwnedWorkbench host, JsonNode publication) =>
        (await host.PostAsync("/runtime/workflows/executables/" + publication["artifactId"]!.GetValue<string>() + "/execute",
            new { sourceReferenceId = publication["sourceReferenceId"]!.GetValue<string>() }))["workflowExecutionId"]!.GetValue<string>();

    private static object Literal(string key, object value) => new { referenceKey = key, value = new { value, expressionType = "Literal" } };

    private static string Status(JsonNode node)
    {
        if (node["status"] is JsonValue value)
        {
            if (value.TryGetValue<string>(out var text)) return text;
            if (value.TryGetValue<int>(out var number)) return number switch
            {
                0 => "Pending",
                1 => "Started",
                2 => "Completed",
                3 => "Faulted",
                4 => "Cancelled",
                5 => "DispatchFailed",
                _ => number.ToString()
            };
        }
        return node["status"]?.ToString() ?? "";
    }

    private static JsonNode? TryNode(JsonNode view, string id) =>
        view["activities"]?.AsArray().SingleOrDefault(item => IsNode(item, id));

    private static bool IsNode(JsonNode? item, string id) =>
        item?["authoredActivityId"]?.GetValue<string>() == id || item?["executableNodeId"]?.GetValue<string>() == id;

    private static void WriteReceipt(OwnedWorkbench host, string caseName, string parentId, int firstProcess, int secondProcess, int thirdProcess,
        int? predecessorExit, string kind, string[] limits)
    {
        var evidenceDirectory = Environment.GetEnvironmentVariable("ELSA_BPMN_HOST_EVIDENCE_DIRECTORY");
        if (evidenceDirectory is null) return;
        var sourceRevision = Environment.GetEnvironmentVariable("ELSA_BPMN_HOST_SOURCE_REVISION") ?? throw new InvalidOperationException("CallActivity crash evidence requires its exact executing source.");
        Directory.CreateDirectory(Path.Combine(evidenceDirectory, caseName));
        var receipt = new
        {
            formatVersion = 1, kind, sourceRevision, parent = "BpmnProcess-root",
            parentWorkflowExecutionId = parentId, firstProcess, secondProcess, thirdProcess, predecessorExit,
            termination = ".NET Process.Kill(entireProcessTree: true)",
            binaries = host.DescribeBinaries(sourceRevision, includePublishing: true), limits
        };
        File.WriteAllText(Path.Combine(evidenceDirectory, caseName, "case.json"),
            JsonSerializer.Serialize(receipt, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }) + Environment.NewLine);
    }
}
