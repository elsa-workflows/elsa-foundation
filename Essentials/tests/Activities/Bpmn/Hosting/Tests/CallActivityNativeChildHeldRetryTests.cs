using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using Xunit;

namespace Elsa.Activities.Bpmn.Hosting.Tests;

/// <summary>
/// G-b (inverse of #3993): a native-root child under a BPMN composite callActivity is retried
/// and its terminal outcome maps back through DispatchWorkflow to the BPMN parent.
/// #3993 proved held retry for a NATIVE parent with a BPMN child (RunBpmn → HoldForRetry →
/// RetryServiceAsync). That is not this plane, and it is not owner decision D3 (narrowly
/// validated alteration).
/// Native retry admission is CompensationScope bounded handler retry (spec 261), not BPMN
/// RetryServiceAsync / HoldForRetry. First-party Fault always faults; SendHttpRequest never
/// faults on HTTP 500 (completes Failed). Fail-then-succeed therefore uses a Flowchart
/// SendHttpRequest Failed→Fault handler, which CompensationScope re-schedules from the start
/// on the next attempt (a new handler invocation, like #3993's new worker, not
/// RetryFaultedActivity on the inner Fault).
/// SQLite Owned Workbench, one in-process host, Root composition. Not dual-model retry
/// admission. Direct-plane CallActivity is refused at publish. Does not use
/// ELSA_BPMN_HOLD_NATIVE_PARENT_RESUME.
/// </summary>
public sealed class CallActivityNativeChildHeldRetryTests
{
    [Fact]
    public async Task A_retried_native_CompensationScope_handler_under_callActivity_maps_Completed_to_the_BPMN_parent()
    {
        await using var responder = await ControllableHttpResponder.StartAsync();
        await using var host = await OwnedWorkbench.StartAsync(recoverySweepSeconds: 0.2);
        await host.LoginAsync();
        var child = await PublishNativeChild(host, responder.Url);
        var parent = await PublishSuccessParent(host, "CallActivity native child held-retry parent",
            child["definitionId"]!.GetValue<string>(),
            "PARENT continued after the retried native child completed.");
        var parentId = await Execute(host, parent);

        var first = await WaitUntilAsync(host, parentId, child["artifactId"]!.GetValue<string>(),
            observed => responder.RequestCount >= 1 &&
                        observed.Parent["instance"]!["status"]!.GetValue<string>() == "Running" &&
                        observed.Child["instance"]!["status"]!.GetValue<string>() == "Running" &&
                        Status(observed.Dispatch) == "Started" &&
                        TryNode(observed.Parent, "node-call")?["status"]?.GetValue<string>() == "Suspended",
            "The native CompensationScope child did not survive its first handler fault under the composite callActivity.");
        Assert.DoesNotContain(first.Parent["activities"]!.AsArray(), item => IsNode(item, "node-after"));
        Assert.Equal(1, responder.RequestCount);
        Assert.Equal([(int)HttpStatusCode.InternalServerError], responder.Statuses);
        Assert.Empty(first.Parent["incidents"]!.AsArray());

        responder.Succeed();
        var settled = await WaitUntilAsync(host, parentId, child["artifactId"]!.GetValue<string>(), observed =>
                observed.Parent["instance"]!["status"]!.GetValue<string>() == "Completed" &&
                observed.Child["instance"]!["status"]!.GetValue<string>() == "Completed" &&
                Status(observed.Dispatch) == "Completed" &&
                TryNode(observed.Parent, "node-call")?["status"]?.GetValue<string>() == "Completed" &&
                TryNode(observed.Parent, "node-after")?["status"]?.GetValue<string>() == "Completed",
            "The retried native child did not map Completed through the composite callActivity.");
        AssertSuccessMapping(settled.Parent, settled.Child, settled.Dispatch);
        Assert.Equal(2, responder.RequestCount);
        Assert.Equal([(int)HttpStatusCode.InternalServerError, (int)HttpStatusCode.OK], responder.Statuses);
        Assert.Single(await DispatchesAsync(host, parentId));
    }

    private static void AssertSuccessMapping(JsonNode parent, JsonNode child, JsonNode dispatch)
    {
        Assert.Equal("Completed", parent["instance"]!["status"]!.GetValue<string>());
        Assert.Equal("Completed", child["instance"]!["status"]!.GetValue<string>());
        Assert.Equal("Completed", Status(dispatch));
        Assert.Empty(parent["incidents"]!.AsArray());
        var call = Assert.Single(parent["activities"]!.AsArray(), item => IsNode(item, "node-call"))!;
        Assert.Equal("Completed", call["status"]!.GetValue<string>());
        Assert.Equal("Completed", Assert.Single(call["outcomeNames"]!.AsArray())!.GetValue<string>());
        var after = Assert.Single(parent["activities"]!.AsArray(), item => IsNode(item, "node-after"))!;
        Assert.Equal("Completed", after["status"]!.GetValue<string>());
        var scope = Assert.Single(child["activities"]!.AsArray(), item => IsNode(item, "child-scope"))!;
        Assert.Equal("Completed", scope["status"]!.GetValue<string>());
        Assert.Contains("Compensated", scope["outcomeNames"]!.AsArray().Select(name => name!.GetValue<string>()));
        Assert.Equal(parent["instance"]!["workflowExecutionId"]!.GetValue<string>(), dispatch["parentWorkflowExecutionId"]!.GetValue<string>());
        Assert.Equal(child["instance"]!["workflowExecutionId"]!.GetValue<string>(), dispatch["childWorkflowExecutionId"]!.GetValue<string>());
    }

    private static async Task<JsonNode> PublishNativeChild(OwnedWorkbench host, Uri url)
    {
        var prepare = await Node(host, "prepare", "Elsa.Activities.Primitives.Activities.WriteLine",
            [Literal("text", "NATIVE child reserved work before the trigger fault.")]);
        var send = await Node(host, "send", "Elsa.Activities.Http.Activities.SendHttpRequest",
            [Literal("Url", url.ToString()), Literal("Method", "GET")]);
        var handlerFault = await Node(host, "handler-fault", "Elsa.Activities.Primitives.Activities.Fault",
            [Literal("code", "service.unavailable")]);
        var handler = await Node(host, "prepare-handler", "Elsa.Activities.Flowchart.Activities.Flowchart", [],
            new
            {
                kind = "elsa.flowchart.structure", schemaVersion = "1.0.0",
                payload = new
                {
                    startNodeId = "send",
                    activities = new object[] { send, handlerFault },
                    connections = new[]
                    {
                        new { id = "send-fault", source = new { nodeId = "send", port = "Failed" }, target = new { nodeId = "handler-fault" } }
                    }
                }
            });
        var trigger = await Node(host, "trigger", "Elsa.Activities.Primitives.Activities.Fault",
            [Literal("code", "trigger.fault")]);
        return await Publish(host, "CallActivity native CompensationScope retry child",
            await Node(host, "child-scope", "Elsa.Activities.Compensation.Activities.CompensationScope",
                [
                    Literal("handlerMaxAttempts", 2),
                    Literal("handlerRetryDelay", "00:00:02"),
                    Literal("handlerRetryBackoffMultiplier", 1),
                    Literal("handlerRetryMaxDelay", "00:00:05"),
                    Literal("faultAfterCompensation", false)
                ],
                new
                {
                    kind = "elsa.compensation-scope.structure", schemaVersion = "1.0.0",
                    payload = new
                    {
                        steps = new object[]
                        {
                            new { name = "prepare", activity = prepare, compensation = handler },
                            new { name = "trigger", activity = trigger }
                        }
                    }
                }));
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

    private static async Task<(JsonNode Parent, JsonNode Child, JsonNode Dispatch)> WaitUntilAsync(
        OwnedWorkbench host, string parentId, string childArtifactId,
        Func<(JsonNode Parent, JsonNode Child, JsonNode Dispatch), bool> ready, string failure)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(60);
        (JsonNode Parent, JsonNode Child, JsonNode Dispatch) observed = default!;
        while (true)
        {
            observed = await ObserveAsync(host, parentId, childArtifactId);
            if (ready(observed)) return observed;
            Assert.True(DateTimeOffset.UtcNow < deadline,
                failure + " Parent=" + observed.Parent.ToJsonString() +
                " Child=" + observed.Child.ToJsonString() + " Dispatch=" + observed.Dispatch.ToJsonString());
            await Task.Delay(100);
        }
    }

    private static async Task<(JsonNode Parent, JsonNode Child, JsonNode Dispatch)> ObserveAsync(
        OwnedWorkbench host, string parentId, string childArtifactId)
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

    /// <summary>Test-owned loopback HTTP worker target. Starts 500 so SendHttpRequest takes Failed → Fault; Succeed() flips to 200/Done.</summary>
    private sealed class ControllableHttpResponder : IAsyncDisposable
    {
        private readonly HttpListener listener = new();
        private readonly CancellationTokenSource cts = new();
        private readonly List<int> statuses = [];
        private readonly object gate = new();
        private Task? pump;
        private volatile int status = (int)HttpStatusCode.InternalServerError;

        private ControllableHttpResponder(Uri url) => Url = url;

        public Uri Url { get; }
        public int RequestCount { get { lock (gate) return statuses.Count; } }
        public int[] Statuses { get { lock (gate) return [.. statuses]; } }

        public static async Task<ControllableHttpResponder> StartAsync()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            var responder = new ControllableHttpResponder(new Uri($"http://127.0.0.1:{port}/worker"));
            responder.listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            responder.listener.Start();
            responder.pump = responder.PumpAsync();
            await Task.Yield();
            return responder;
        }

        public void Succeed() => status = (int)HttpStatusCode.OK;

        private async Task PumpAsync()
        {
            while (!cts.IsCancellationRequested)
            {
                HttpListenerContext context;
                try { context = await listener.GetContextAsync().WaitAsync(cts.Token); }
                catch (Exception) when (cts.IsCancellationRequested) { return; }
                catch (HttpListenerException) { return; }
                catch (ObjectDisposedException) { return; }

                try
                {
                    var code = status;
                    lock (gate) statuses.Add(code);
                    context.Response.StatusCode = code;
                    context.Response.Close();
                }
                catch (HttpListenerException) { }
            }
        }

        public async ValueTask DisposeAsync()
        {
            cts.Cancel();
            try { listener.Stop(); }
            catch (ObjectDisposedException) { }
            listener.Close();
            if (pump is not null)
            {
                try { await pump.WaitAsync(TimeSpan.FromSeconds(2)); }
                catch (TimeoutException) { }
            }

            cts.Dispose();
        }
    }
}
