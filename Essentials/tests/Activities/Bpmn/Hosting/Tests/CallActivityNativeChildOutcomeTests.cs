using System.Text.Json.Nodes;
using Xunit;

namespace Elsa.Activities.Bpmn.Hosting.Tests;

/// <summary>
/// G2.4 composite-plane child-outcome mapping: a native WorkflowExecution whose root is BpmnProcess
/// runs a callActivity that dispatches a native-root child. Operator CancelWorkflow of that child
/// is a child-initiated terminal. DispatchWorkflow completes with outcome Cancelled; BpmnProcess
/// then routes that outcome through the BPMN error-handling ladder (error boundary, else composite
/// fault bpmn.call-activity.cancelled). That is not RunBpmn ActivityTransition.Cancel and is not a
/// parent CancelWorkflow. Direct BPMN publish rejects CallActivity.
/// SQLite Owned Workbench, one in-process host, Root composition. Does not use
/// ELSA_BPMN_HOLD_NATIVE_PARENT_RESUME.
/// </summary>
public sealed class CallActivityNativeChildOutcomeTests
{
    [Fact]
    public async Task A_cancelled_native_child_under_callActivity_routes_the_error_boundary_and_does_not_look_like_success()
    {
        await using var host = await OwnedWorkbench.StartAsync(recoverySweepSeconds: 0.2);
        await host.LoginAsync();
        var eventName = "call-child-cancel-boundary-" + Guid.NewGuid().ToString("N");
        var child = await Publish(host, "CallActivity native cancel-boundary child",
            await Node(host, "child-wait", "Elsa.Activities.Primitives.Activities.Event",
                [Literal("EventName", eventName), Literal("CanStartWorkflow", false)]));
        var parent = await PublishBoundaryParent(host, "CallActivity native child cancel with error boundary",
            child["definitionId"]!.GetValue<string>(),
            "PARENT error boundary routed the cancelled native child.");
        var parentId = await Execute(host, parent);
        var waiting = await WaitWaitingAsync(host, parentId, child["artifactId"]!.GetValue<string>());
        Assert.Empty(waiting.Parent["incidents"]!.AsArray());
        Assert.Empty(waiting.Child["incidents"]!.AsArray());
        Assert.DoesNotContain(waiting.Parent["activities"]!.AsArray(), item => IsNode(item, "node-recover"));
        var childId = waiting.Child["instance"]!["workflowExecutionId"]!.GetValue<string>();
        await host.PostAsync("/runtime/workflows/alteration-plans", new
        {
            target = new { executionIds = new[] { childId } },
            alterations = new[] { new { kind = "CancelWorkflow", schemaVersion = 1, payload = new { } } }
        }, idempotencyKey: "cancel-native-call-child-boundary");
        var settled = await WaitUntilAsync(host, parentId, child["artifactId"]!.GetValue<string>(), observed =>
            observed.Parent["instance"]!["status"]!.GetValue<string>() == "Completed" &&
            observed.Child["instance"]!["status"]!.GetValue<string>() == "Cancelled" &&
            Status(observed.Dispatch) == "Cancelled" &&
            TryNode(observed.Parent, "node-call")?["status"]?.GetValue<string>() == "Completed" &&
            TryNode(observed.Parent, "node-recover")?["status"]?.GetValue<string>() == "Completed",
            "The composite callActivity did not map a cancelled native child through its error boundary.");
        AssertBoundaryCancel(settled.Parent, settled.Child, settled.Dispatch);
        Assert.Single(await DispatchesAsync(host, parentId));
    }

    [Fact]
    public async Task A_cancelled_native_child_under_callActivity_without_a_catcher_faults_the_composite_with_the_cancelled_code()
    {
        await using var host = await OwnedWorkbench.StartAsync(recoverySweepSeconds: 0.2);
        await host.LoginAsync();
        var eventName = "call-child-cancel-nocatcher-" + Guid.NewGuid().ToString("N");
        var child = await Publish(host, "CallActivity native cancel-nocatcher child",
            await Node(host, "child-wait", "Elsa.Activities.Primitives.Activities.Event",
                [Literal("EventName", eventName), Literal("CanStartWorkflow", false)]));
        var parent = await PublishSuccessParent(host, "CallActivity native child cancel without catcher",
            child["definitionId"]!.GetValue<string>(),
            "PARENT must not continue after an uncaught cancelled native child.");
        var parentId = await Execute(host, parent);
        var waiting = await WaitWaitingAsync(host, parentId, child["artifactId"]!.GetValue<string>());
        Assert.Empty(waiting.Parent["incidents"]!.AsArray());
        Assert.Empty(waiting.Child["incidents"]!.AsArray());
        Assert.DoesNotContain(waiting.Parent["activities"]!.AsArray(), item => IsNode(item, "node-after"));
        var childId = waiting.Child["instance"]!["workflowExecutionId"]!.GetValue<string>();
        await host.PostAsync("/runtime/workflows/alteration-plans", new
        {
            target = new { executionIds = new[] { childId } },
            alterations = new[] { new { kind = "CancelWorkflow", schemaVersion = 1, payload = new { } } }
        }, idempotencyKey: "cancel-native-call-child-nocatcher");
        var settled = await WaitUntilAsync(host, parentId, child["artifactId"]!.GetValue<string>(), observed =>
            observed.Parent["instance"]!["status"]!.GetValue<string>() == "Faulted" &&
            observed.Child["instance"]!["status"]!.GetValue<string>() == "Cancelled" &&
            Status(observed.Dispatch) == "Cancelled" &&
            TryNode(observed.Parent, "node-call")?["status"]?.GetValue<string>() == "Completed",
            "The composite callActivity did not fault after an uncaught cancelled native child.");
        AssertUncaughtCancel(settled.Parent, settled.Child, settled.Dispatch);
        Assert.Single(await DispatchesAsync(host, parentId));
    }

    private static void AssertBoundaryCancel(JsonNode parent, JsonNode child, JsonNode dispatch)
    {
        Assert.Equal("Completed", parent["instance"]!["status"]!.GetValue<string>());
        Assert.Equal("Cancelled", child["instance"]!["status"]!.GetValue<string>());
        Assert.Equal("Cancelled", Status(dispatch));
        Assert.Empty(parent["incidents"]!.AsArray());
        var call = Assert.Single(parent["activities"]!.AsArray(), item => IsNode(item, "node-call"))!;
        Assert.Equal("Completed", call["status"]!.GetValue<string>());
        Assert.Equal("Cancelled", Assert.Single(call["outcomeNames"]!.AsArray())!.GetValue<string>());
        var recover = Assert.Single(parent["activities"]!.AsArray(), item => IsNode(item, "node-recover"))!;
        Assert.Equal("Completed", recover["status"]!.GetValue<string>());
        Assert.DoesNotContain(parent["activities"]!.AsArray(), item => IsNode(item, "node-after"));
        Assert.Equal(parent["instance"]!["workflowExecutionId"]!.GetValue<string>(), dispatch["parentWorkflowExecutionId"]!.GetValue<string>());
        Assert.Equal(child["instance"]!["workflowExecutionId"]!.GetValue<string>(), dispatch["childWorkflowExecutionId"]!.GetValue<string>());
    }

    private static void AssertUncaughtCancel(JsonNode parent, JsonNode child, JsonNode dispatch)
    {
        Assert.Equal("Faulted", parent["instance"]!["status"]!.GetValue<string>());
        Assert.Equal("Cancelled", child["instance"]!["status"]!.GetValue<string>());
        Assert.Equal("Cancelled", Status(dispatch));
        var call = Assert.Single(parent["activities"]!.AsArray(), item => IsNode(item, "node-call"))!;
        Assert.Equal("Completed", call["status"]!.GetValue<string>());
        Assert.Equal("Cancelled", Assert.Single(call["outcomeNames"]!.AsArray())!.GetValue<string>());
        Assert.Null(call["fault"]);
        Assert.DoesNotContain(parent["activities"]!.AsArray(), item => IsNode(item, "node-after"));
        var process = Assert.Single(parent["activities"]!.AsArray(), item => IsNode(item, "bpmn-root"))!;
        Assert.Equal("Faulted", process["status"]!.GetValue<string>());
        AssertUncaughtCancelIncident(process, parent);
        Assert.Equal(parent["instance"]!["workflowExecutionId"]!.GetValue<string>(), dispatch["parentWorkflowExecutionId"]!.GetValue<string>());
        Assert.Equal(child["instance"]!["workflowExecutionId"]!.GetValue<string>(), dispatch["childWorkflowExecutionId"]!.GetValue<string>());
    }

    /// <summary>
    /// Hosted instance REST redacts incident metadata without inspect-values (same as
    /// RunBpmnChildFaultHeldRetryTests.AssertParentFaultedOnce). Probes pin
    /// bpmn.call-activity.cancelled on in-memory BpmnProcess.Fault; when REST exposes a code it must
    /// be that value, otherwise the wrapping ActivityReturnedFault is the hosted surface.
    /// </summary>
    private static void AssertUncaughtCancelIncident(JsonNode process, JsonNode parent)
    {
        var incident = Assert.Single(parent["incidents"]!.AsArray())!;
        Assert.Equal("ActivityReturnedFault", incident["failureType"]!.GetValue<string>());
        Assert.True(incident["isBlocking"]!.GetValue<bool>());
        Assert.Equal(process["activityExecutionId"]!.GetValue<string>(), incident["activityExecutionId"]!.GetValue<string>());
        if (TryCode(incident["metadata"]) is { } code)
            Assert.Equal("bpmn.call-activity.cancelled", code);
        else if (process["subStatus"] is JsonValue subStatus)
            Assert.Equal("ActivityReturnedFault", subStatus.GetValue<string>());
        else
            Assert.Fail("Uncaught cancelled native child did not leave ActivityReturnedFault on the composite. Parent=" + parent.ToJsonString());
    }

    private static string? TryCode(JsonNode? node)
    {
        if (node is not JsonObject obj) return null;
        foreach (var key in new[] { "code", "faultCode", "Code" })
        {
            if (obj[key] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text))
                return text;
        }

        return null;
    }

    private static async Task<(JsonNode Parent, JsonNode Child, JsonNode Dispatch)> WaitWaitingAsync(
        OwnedWorkbench host, string parentId, string childArtifactId) =>
        await WaitUntilAsync(host, parentId, childArtifactId, observed =>
                observed.Parent["instance"]!["status"]!.GetValue<string>() == "Running" &&
                observed.Child["instance"]!["status"]!.GetValue<string>() == "Running" &&
                Status(observed.Dispatch) == "Started" &&
                TryNode(observed.Parent, "node-call")?["status"]?.GetValue<string>() == "Suspended" &&
                TryNode(observed.Child, "child-wait")?["status"]?.GetValue<string>() == "Suspended",
            "The native child did not reach its wait under the composite callActivity.");

    private static async Task<(JsonNode Parent, JsonNode Child, JsonNode Dispatch)> WaitUntilAsync(
        OwnedWorkbench host, string parentId, string childArtifactId,
        Func<(JsonNode Parent, JsonNode Child, JsonNode Dispatch), bool> ready, string failure)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (true)
        {
            var observed = await ObserveAsync(host, parentId, childArtifactId);
            if (ready(observed)) return observed;
            Assert.True(DateTimeOffset.UtcNow < deadline,
                failure + " Parent=" + observed.Parent.ToJsonString() +
                " Child=" + observed.Child.ToJsonString() + " Dispatch=" + observed.Dispatch.ToJsonString());
            await Task.Delay(100);
        }
    }

    private static async Task<JsonNode> PublishSuccessParent(OwnedWorkbench host, string name, string childDefinitionId, string afterText)
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

    private static async Task<JsonNode> PublishBoundaryParent(OwnedWorkbench host, string name, string childDefinitionId, string recoverText)
    {
        var call = await Node(host, "node-call", "Elsa.Activities.DispatchWorkflow.Runtime.Activities.DispatchWorkflow",
            [Literal("WorkflowDefinitionId", childDefinitionId), Literal("WaitForCompletion", true)]);
        var recover = await Node(host, "node-recover", "Elsa.Activities.Primitives.Activities.WriteLine", [Literal("text", recoverText)]);
        return await Publish(host, name, await Node(host, "bpmn-root", "Elsa.Activities.Bpmn.Activities.BpmnProcess", [],
            new
            {
                kind = "elsa.bpmn.structure", schemaVersion = "1.0.0",
                payload = new
                {
                    activities = new[] { call, recover },
                    elements = new object[]
                    {
                        new { elementId = "start", elementType = "startEvent" },
                        new { elementId = "call", elementType = "callActivity", childNodeId = "node-call" },
                        new { elementId = "b-error", elementType = "boundaryEvent", attachedToRef = "call",
                            eventDefinitions = new[] { new { type = "error" } } },
                        new { elementId = "recover", elementType = "task", childNodeId = "node-recover" },
                        new { elementId = "end-ok", elementType = "endEvent" },
                        new { elementId = "end-error", elementType = "endEvent" }
                    },
                    sequenceFlows = new[]
                    {
                        new { flowId = "flow-1", sourceRef = "start", targetRef = "call" },
                        new { flowId = "flow-2", sourceRef = "call", targetRef = "end-ok" },
                        new { flowId = "flow-3", sourceRef = "b-error", targetRef = "recover" },
                        new { flowId = "flow-4", sourceRef = "recover", targetRef = "end-error" }
                    }
                }
            }));
    }

    private static async Task<(JsonNode Parent, JsonNode Child, JsonNode Dispatch)> ObserveAsync(OwnedWorkbench host, string parentId, string childArtifactId)
    {
        var parent = await host.GetAsync("/runtime/workflows/instances/" + parentId);
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
}
