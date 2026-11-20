using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bpmn.Model;
using Elsa.Activities.Bpmn.Core;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;
using Xunit.Abstractions;

namespace Elsa.Activities.Bpmn.Hosting.Tests;

/// <summary>
/// G3 measurement of the three Root/SQLite G2 journeys #4039 did not count. Test-only; reuses
/// <c>capturePersistence</c> and the existing <c>ELSA_BPMN_HOLD_NATIVE_PARENT_RESUME</c> hold sites.
/// No budget, throughput or CI-threshold gate. SQLite, one in-process host only.
/// </summary>
public sealed class G3RemainingJourneyOverheadTests(ITestOutputHelper output)
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task RunBpmn_pending_parent_result_crash_records_overhead()
    {
        await using var host = await OwnedWorkbench.StartAsync(directBpmn: true, recoverySweepSeconds: 0.2, capturePersistence: true);
        await host.LoginAsync();
        var measure = new G3Measurement(host);
        var process = new BpmnProcessDefinition("g3-pending-parent-result", Elements:
            [new("start", BpmnElementTypes.StartEvent), new("human", BpmnElementTypes.UserTask, bindingRef: "work"), new("end", BpmnElementTypes.EndEvent)],
            SequenceFlows: [new("to-work", "start", "human"), new("to-end", "human", "end")]);
        var definition = new BpmnModelDefinition(1, JsonSerializer.SerializeToElement(process, Web), ["work"]);
        var published = await measure.StepAsync("publish-bpmn-model", () =>
            host.PostAsync("/bpmn/artifacts", new { requestId = "g3-publish-pending-parent", definition }));
        var catalog = await host.GetAsync("/design/activities/definitions?search=Elsa.Activities.Bpmn.Activities.RunBpmn");
        var activity = Assert.Single(catalog["items"]!.AsArray(), item => item!["definition"]!["activityTypeKey"]!.GetValue<string>() == "Elsa.Activities.Bpmn.Activities.RunBpmn");
        var native = await measure.StepAsync("submit-native-parent", () => host.PostAsync("/design/workflows/definitions/submit", new
        {
            name = "G3 RunBpmn pending parent result crash overhead",
            state = new
            {
                variables = Array.Empty<object>(), inputs = Array.Empty<object>(), outputs = Array.Empty<object>(),
                rootActivity = new
                {
                    nodeId = "run-bpmn", activityVersionId = activity!["definition"]!["headVersionId"]!.GetValue<string>(), outputs = Array.Empty<object>(),
                    inputs = new[] { new { referenceKey = "Model", value = new { value = published["sourceReferenceId"]!.GetValue<string>(), expressionType = "Literal" } } }
                }
            }
        }));
        var publication = await measure.StepAsync("publish-native-parent", () =>
            host.PostAsync($"/publishing/workflows/{native["version"]!["id"]!.GetValue<string>()}/publish", new { }));
        var started = await measure.StepAsync("execute-parent-admit-child", () =>
            host.PostAsync($"/runtime/workflows/executables/{publication["artifactId"]!.GetValue<string>()}/execute",
                new { sourceReferenceId = publication["sourceReferenceId"]!.GetValue<string>() }));
        var execution = started["workflowExecutionId"]!.GetValue<string>();
        var nativePath = "/runtime/workflows/instances/" + execution;
        string? modelId = null;
        await measure.StepVoidAsync("wait-cross-model-human", async () =>
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
            while (modelId is null)
            {
                Assert.True(DateTimeOffset.UtcNow < deadline, "The actual RunBpmn native wait did not publish its model invocation.");
                var view = await host.GetAsync(nativePath);
                Assert.Empty(view["incidents"]!.AsArray());
                if (view["activities"]!.AsArray().SingleOrDefault(item => item!["activityType"]!.GetValue<string>() == "Elsa.Activities.Bpmn.Activities.RunBpmn") is { } root)
                {
                    var detail = await host.GetAsync(nativePath + "/activity-executions/" + root["activityExecutionId"]!.GetValue<string>());
                    if (detail["bookmarks"] is JsonArray waits && waits.Count == 1) modelId = waits[0]!["stimulusHash"]!.GetValue<string>();
                }
                if (modelId is null) await Task.Delay(100);
            }
            while (await host.FindAsync("/bpmn/executions/" + modelId) is null)
            {
                Assert.True(DateTimeOffset.UtcNow < deadline, "The real native source did not admit its model child.");
                await Task.Delay(100);
            }
        });
        var modelPath = "/bpmn/executions/" + modelId;
        var waiting = Assert.Single((await host.GetAsync(modelPath + "/work"))["items"]!.AsArray())!;
        host.HoldClaimedBpmnParentResume();
        var completing = host.PostAsync(modelPath + "/work/" + waiting["invocationId"]!.GetValue<string>() + "/complete",
            new { requestId = "g3-complete-embedded", attemptId = waiting["attemptId"]!.GetValue<string>() });
        DateTimeOffset visibleAfter = default;
        await measure.StepVoidAsync("complete-child-before-parent-consume", async () =>
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
            while (true)
            {
                var pendingModel = await host.GetAsync(modelPath);
                var pendingNative = await host.GetAsync(nativePath);
                if (pendingModel["status"]!.GetValue<string>() == "TerminalPendingSettlement" &&
                    pendingModel["result"]!["status"]!.GetValue<string>() == "Completed" &&
                    pendingNative["instance"]!["status"]!.GetValue<string>() == "Running")
                {
                    var physical = await host.ReadEmbeddingAsync(modelId!, execution);
                    var candidate = physical["sources"]!.AsArray().SingleOrDefault(item =>
                        item!["intent"]!["kind"]!.GetValue<string>() == "Elsa.Bpmn.ResumeNativeParent.v1");
                    if (candidate?["status"]?.GetValue<int>() == (int)RuntimePostCommitOutboxStatus.Delivering)
                    {
                        visibleAfter = candidate["deliveryVisibleAfter"]!.GetValue<DateTimeOffset>();
                        break;
                    }
                }
                Assert.True(DateTimeOffset.UtcNow < deadline, "The model did not reach its terminal result while the native parent was still waiting.");
                await Task.Delay(100);
            }
        });
        await measure.StepVoidAsync("kill-before-parent-consume", async () =>
        {
            await host.StopAsync();
            try { await completing; }
            catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException or TaskCanceledException or IOException) { }
            var pendingPhysical = await host.ReadEmbeddingAsync(modelId!, execution);
            Assert.False(pendingPhysical["nativeParent"]!["status"]!.GetValue<int>() is 3 or 4 or 5,
                "The native parent must still be waiting when the host is killed.");
        });
        host.ReleaseNativeParentResumeHold();
        await measure.StepVoidAsync("restart-host", async () =>
        {
            await host.RestartAsync();
            await host.LoginAsync();
        });
        await measure.StepVoidAsync("parent-consumes-Completed", async () =>
        {
            var restartedAt = DateTimeOffset.UtcNow;
            var recoveryEligibleAt = visibleAfter > restartedAt ? visibleAfter : restartedAt;
            var deadline = recoveryEligibleAt.AddSeconds(30);
            while (true)
            {
                var settled = await host.ReadEmbeddingAsync(modelId!, execution);
                var nativeFinal = await host.GetAsync(nativePath);
                var final = await host.GetAsync(modelPath);
                Assert.True(DateTimeOffset.UtcNow < deadline, "The restarted host did not deliver exactly one native parent continuation.");
                if (final["status"]!.GetValue<string>() == "TerminalSettled" && nativeFinal["instance"]!["status"]!.GetValue<string>() == "Completed" &&
                    settled["sources"]!.AsArray().Count >= 2 && settled["sources"]!.AsArray().All(item => item!["status"]!.GetValue<int>() == 2))
                {
                    var completedRun = Assert.Single(nativeFinal["activities"]!.AsArray(), item => item!["authoredActivityId"]!.GetValue<string>() == "run-bpmn")!;
                    Assert.Equal("Completed", completedRun["status"]!.GetValue<string>());
                    Assert.Equal("Done", Assert.Single(completedRun["outcomeNames"]!.AsArray())!.GetValue<string>());
                    break;
                }
                await Task.Delay(100);
            }
        });
        await host.StopAsync();
        await measure.WriteAsync(output, "g3-pending-parent-result-crash.json",
            measure.Receipt("RunBpmnPendingParentResultCrashTests / #3992 kill after child terminal before parent consume"));
    }

    [Fact]
    public async Task RunBpmn_held_native_service_retry_records_overhead()
    {
        await using var responder = await ControllableHttpResponder.StartAsync();
        await using var host = await OwnedWorkbench.StartAsync(directBpmn: true, recoverySweepSeconds: 0.2, capturePersistence: true);
        await host.LoginAsync();
        var measure = new G3Measurement(host);
        var worker = await measure.StepAsync("publish-native-worker", () => FailThenSucceedWorker(host, responder.Url));
        var pin = await measure.StepAsync("publish-worker-artifact", () =>
            host.PostAsync($"/publishing/workflows/{worker["version"]!["id"]!.GetValue<string>()}/publish", new { }));
        var identity = new WorkflowExecutableIdentity(pin["artifactId"]!.GetValue<string>(), pin["definitionId"]!.GetValue<string>(),
            pin["definitionVersionId"]!.GetValue<string>(), pin["artifactVersion"]!.GetValue<string>(), pin["artifactHash"]!.GetValue<string>());
        var process = new BpmnProcessDefinition("g3-runbpmn-service", Elements:
            [new("start", BpmnElementTypes.StartEvent), new("service", BpmnElementTypes.ServiceTask, bindingRef: "native-work"), new("end", BpmnElementTypes.EndEvent)],
            SequenceFlows: [new("to-service", "start", "service"), new("to-end", "service", "end")]);
        var definition = new BpmnModelDefinition(3, JsonSerializer.SerializeToElement(process, Web), [],
            [new BpmnNativeServiceBinding("native-work", identity, "service.business-error", new BpmnServiceFaultPolicy(1, BpmnServiceFaultDisposition.HoldForRetry, 2))]);
        var published = await measure.StepAsync("publish-bpmn-model", () =>
            host.PostAsync("/bpmn/artifacts", new { requestId = "g3-publish-runbpmn-service", definition }));
        var catalog = await host.GetAsync("/design/activities/definitions?search=Elsa.Activities.Bpmn.Activities.RunBpmn");
        var activity = Assert.Single(catalog["items"]!.AsArray(), item => item!["definition"]!["activityTypeKey"]!.GetValue<string>() == "Elsa.Activities.Bpmn.Activities.RunBpmn");
        var native = await measure.StepAsync("submit-native-parent", () => host.PostAsync("/design/workflows/definitions/submit", new
        {
            name = "G3 RunBpmn held native service retry overhead",
            state = new
            {
                variables = Array.Empty<object>(), inputs = Array.Empty<object>(), outputs = Array.Empty<object>(),
                rootActivity = new
                {
                    nodeId = "run-bpmn", activityVersionId = activity!["definition"]!["headVersionId"]!.GetValue<string>(), outputs = Array.Empty<object>(),
                    inputs = new[] { new { referenceKey = "Model", value = new { value = published["sourceReferenceId"]!.GetValue<string>(), expressionType = "Literal" } } }
                }
            }
        }));
        var publication = await measure.StepAsync("publish-native-parent", () =>
            host.PostAsync($"/publishing/workflows/{native["version"]!["id"]!.GetValue<string>()}/publish", new { }));
        var started = await measure.StepAsync("execute-parent-admit-child", () =>
            host.PostAsync($"/runtime/workflows/executables/{publication["artifactId"]!.GetValue<string>()}/execute",
                new { sourceReferenceId = publication["sourceReferenceId"]!.GetValue<string>() }));
        var execution = started["workflowExecutionId"]!.GetValue<string>();
        var nativePath = "/runtime/workflows/instances/" + execution;
        string? modelId = null;
        await measure.StepVoidAsync("wait-cross-model-service", async () =>
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(60);
            while (modelId is null)
            {
                Assert.True(DateTimeOffset.UtcNow < deadline, "The actual RunBpmn native wait did not publish its model invocation.");
                var view = await host.GetAsync(nativePath);
                Assert.Empty(view["incidents"]!.AsArray());
                if (view["activities"]!.AsArray().SingleOrDefault(item => item!["activityType"]!.GetValue<string>() == "Elsa.Activities.Bpmn.Activities.RunBpmn") is { } root)
                {
                    var detail = await host.GetAsync(nativePath + "/activity-executions/" + root["activityExecutionId"]!.GetValue<string>());
                    if (detail["bookmarks"] is JsonArray waits && waits.Count == 1) modelId = waits[0]!["stimulusHash"]!.GetValue<string>();
                }
                if (modelId is null) await Task.Delay(100);
            }
            while (await host.FindAsync("/bpmn/executions/" + modelId) is null)
            {
                Assert.True(DateTimeOffset.UtcNow < deadline, "The real native source did not admit its model child.");
                await Task.Delay(100);
            }
        });
        var modelPath = "/bpmn/executions/" + modelId;
        JsonNode incident = null!;
        await measure.StepVoidAsync("wait-held-incident", async () =>
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(60);
            while (true)
            {
                var model = await host.GetAsync(modelPath);
                var nativeView = await host.GetAsync(nativePath);
                if (model["status"]!.GetValue<string>() == "WaitingIncident")
                {
                    Assert.Equal("Running", nativeView["instance"]!["status"]!.GetValue<string>());
                    incident = Assert.Single((await host.GetAsync(modelPath + "/incidents"))["items"]!.AsArray())!;
                    break;
                }
                Assert.True(DateTimeOffset.UtcNow < deadline, "The actual native fault was not held.");
                await Task.Delay(100);
            }
        });
        responder.Succeed();
        var retry = new { requestId = "g3-retry-held-runbpmn", incidentId = incident["incidentId"]!.GetValue<string>(),
            invocationId = incident["invocationId"]!.GetValue<string>(), failedAttemptId = incident["failedAttemptId"]!.GetValue<string>() };
        await measure.StepAsync("retry-held-service", () => host.PostAsync(modelPath + "/retry-service", retry));
        await measure.StepVoidAsync("parent-consumes-Done", async () =>
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(60);
            while (true)
            {
                var nativeFinal = await host.GetAsync(nativePath);
                var modelFinal = await host.GetAsync(modelPath);
                if (nativeFinal["instance"]!["status"]!.GetValue<string>() == "Completed" &&
                    modelFinal["status"]!.GetValue<string>() == "TerminalSettled" &&
                    modelFinal["result"]!["status"]!.GetValue<string>() == "Completed")
                {
                    var completedRun = Assert.Single(nativeFinal["activities"]!.AsArray(), item => item!["authoredActivityId"]!.GetValue<string>() == "run-bpmn")!;
                    Assert.Equal("Done", Assert.Single(completedRun["outcomeNames"]!.AsArray())!.GetValue<string>());
                    return;
                }
                Assert.True(DateTimeOffset.UtcNow < deadline, "The retried native service did not complete the model and resume the RunBpmn parent once.");
                await Task.Delay(100);
            }
        });
        await host.StopAsync();
        await measure.WriteAsync(output, "g3-held-native-service-retry.json",
            measure.Receipt("RunBpmnChildFaultHeldRetryTests / #3993 held native-worker fault then retry to Done"));
    }

    [Fact]
    public async Task CallActivity_pending_parent_resume_crash_records_overhead()
    {
        await using var host = await OwnedWorkbench.StartAsync(recoverySweepSeconds: 0.2, capturePersistence: true);
        await host.LoginAsync();
        var measure = new G3Measurement(host);
        var childDraft = await measure.StepAsync("submit-native-child", async () =>
        {
            var node = await Node(host, "child-write", "Elsa.Activities.Primitives.Activities.WriteLine",
                [Literal("text", "G3 CHILD (callActivity kill) completed.")]);
            return await host.PostAsync("/design/workflows/definitions/submit", new
            {
                name = "G3 CallActivity native complete child",
                state = new { variables = Array.Empty<object>(), inputs = Array.Empty<object>(), outputs = Array.Empty<object>(), rootActivity = node }
            });
        });
        var child = await measure.StepAsync("publish-native-child", () =>
            host.PostAsync("/publishing/workflows/" + childDraft["version"]!["id"]!.GetValue<string>() + "/publish", new { }));
        var parentDraft = await measure.StepAsync("submit-callActivity-parent", async () =>
        {
            var call = await Node(host, "node-call", "Elsa.Activities.DispatchWorkflow.Runtime.Activities.DispatchWorkflow",
                [Literal("WorkflowDefinitionId", child["definitionId"]!.GetValue<string>()), Literal("WaitForCompletion", true)]);
            var after = await Node(host, "node-after", "Elsa.Activities.Primitives.Activities.WriteLine",
                [Literal("text", "G3 PARENT continued after the completed native child was consumed once.")]);
            var root = await Node(host, "bpmn-root", "Elsa.Activities.Bpmn.Activities.BpmnProcess", [],
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
                });
            return await host.PostAsync("/design/workflows/definitions/submit", new
            {
                name = "G3 CallActivity pending-parent-resume crash overhead",
                state = new { variables = Array.Empty<object>(), inputs = Array.Empty<object>(), outputs = Array.Empty<object>(), rootActivity = root }
            });
        });
        var parent = await measure.StepAsync("publish-callActivity-parent", () =>
            host.PostAsync("/publishing/workflows/" + parentDraft["version"]!["id"]!.GetValue<string>() + "/publish", new { }));
        host.HoldNativeParentResume();
        var parentId = (await measure.StepAsync("execute-parent-admit-child", () =>
            host.PostAsync("/runtime/workflows/executables/" + parent["artifactId"]!.GetValue<string>() + "/execute",
                new { sourceReferenceId = parent["sourceReferenceId"]!.GetValue<string>() })))["workflowExecutionId"]!.GetValue<string>();
        await measure.StepVoidAsync("wait-child-complete-parent-waiting", async () =>
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
            while (true)
            {
                var observed = await ObserveAsync(host, parentId, child["artifactId"]!.GetValue<string>());
                if (observed.Parent["instance"]!["status"]!.GetValue<string>() == "Running" &&
                    observed.Child["instance"]!["status"]!.GetValue<string>() == "Completed" &&
                    CallActivityStatus(observed.Dispatch) == "Completed" &&
                    TryNode(observed.Parent, "node-call")?["status"]?.GetValue<string>() == "Suspended")
                    break;
                Assert.True(DateTimeOffset.UtcNow < deadline, "The native child did not complete while the composite parent was still waiting to consume it.");
                await Task.Delay(100);
            }
        });
        await measure.StepVoidAsync("kill-before-parent-consume", async () => await host.StopAsync());
        host.ReleaseNativeParentResumeHold();
        await measure.StepVoidAsync("restart-host", async () =>
        {
            await host.RestartAsync();
            await host.LoginAsync();
        });
        await measure.StepVoidAsync("parent-consumes-Completed", async () =>
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
            while (true)
            {
                var observed = await ObserveAsync(host, parentId, child["artifactId"]!.GetValue<string>());
                if (observed.Parent["instance"]!["status"]!.GetValue<string>() == "Completed" &&
                    observed.Child["instance"]!["status"]!.GetValue<string>() == "Completed" &&
                    CallActivityStatus(observed.Dispatch) == "Completed" &&
                    TryNode(observed.Parent, "node-call")?["status"]?.GetValue<string>() == "Completed" &&
                    TryNode(observed.Parent, "node-after")?["status"]?.GetValue<string>() == "Completed")
                {
                    Assert.Empty(observed.Parent["incidents"]!.AsArray());
                    return;
                }
                Assert.True(DateTimeOffset.UtcNow < deadline, "The restarted host did not deliver exactly one callActivity continuation.");
                await Task.Delay(100);
            }
        });
        await host.StopAsync();
        await measure.WriteAsync(output, "g3-callactivity-pending-parent-resume.json",
            measure.Receipt("CallActivityNativeChildCrashTests / #3995 callActivity host kill after native child complete"));
    }

    private static async Task<JsonNode> FailThenSucceedWorker(OwnedWorkbench host, Uri url)
    {
        async Task<string> Version(string type)
        {
            var catalog = await host.GetAsync("/design/activities/definitions?search=" + type);
            return Assert.Single(catalog["items"]!.AsArray(), item => item!["definition"]!["activityTypeKey"]!.GetValue<string>() == type)!
                ["definition"]!["headVersionId"]!.GetValue<string>();
        }

        var send = new { nodeId = "send", activityVersionId = await Version("Elsa.Activities.Http.Activities.SendHttpRequest"), outputs = Array.Empty<object>(),
            inputs = new[] { new { referenceKey = "Url", value = new { value = url.ToString(), expressionType = "Literal" } },
                new { referenceKey = "Method", value = new { value = "GET", expressionType = "Literal" } } } };
        var fault = new { nodeId = "fault", activityVersionId = await Version("Elsa.Activities.Primitives.Activities.Fault"), outputs = Array.Empty<object>(),
            inputs = new[] { new { referenceKey = "code", value = new { value = "service.unavailable", expressionType = "Literal" } } } };
        return await host.PostAsync("/design/workflows/definitions/submit", new
        {
            name = "G3 Fail then succeed RunBpmn service worker",
            state = new { variables = Array.Empty<object>(), inputs = Array.Empty<object>(), outputs = Array.Empty<object>(),
                rootActivity = new { nodeId = "service-root", activityVersionId = await Version("Elsa.Activities.Flowchart.Activities.Flowchart"),
                    inputs = Array.Empty<object>(), outputs = Array.Empty<object>(),
                    structure = new { kind = "elsa.flowchart.structure", schemaVersion = "1.0.0",
                        payload = new { startNodeId = "send", activities = new object[] { send, fault },
                            connections = new[] { new { id = "send-fault", source = new { nodeId = "send", port = "Failed" }, target = new { nodeId = "fault" } } } } } } }
        });
    }

    private static async Task<object> Node(OwnedWorkbench host, string id, string type, object[] inputs, object? structure = null)
    {
        var catalog = await host.GetAsync("/design/activities/definitions?search=" + Uri.EscapeDataString(type));
        var activity = Assert.Single(catalog["items"]!.AsArray(), item => item!["definition"]!["activityTypeKey"]!.GetValue<string>() == type)!;
        return new { nodeId = id, activityVersionId = activity["definition"]!["headVersionId"]!.GetValue<string>(), inputs, outputs = Array.Empty<object>(), structure };
    }

    private static object Literal(string key, object value) => new { referenceKey = key, value = new { value, expressionType = "Literal" } };

    private static async Task<(JsonNode Parent, JsonNode Child, JsonNode Dispatch)> ObserveAsync(OwnedWorkbench host, string parentId, string childArtifactId)
    {
        var parent = await host.GetAsync("/runtime/workflows/instances/" + parentId);
        Assert.Empty(parent["incidents"]!.AsArray());
        var listed = await host.GetAsync("/runtime/workflows/dispatches?parentWorkflowExecutionId=" + Uri.EscapeDataString(parentId));
        var dispatches = listed as JsonArray ?? listed["items"]!.AsArray();
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
        return (parent, child, dispatch);
    }

    private static string CallActivityStatus(JsonNode node)
    {
        if (node["status"] is JsonValue value)
        {
            if (value.TryGetValue<string>(out var text)) return text;
            if (value.TryGetValue<int>(out var number)) return number switch
            {
                0 => "Pending", 1 => "Started", 2 => "Completed", 3 => "Faulted", 4 => "Cancelled", 5 => "DispatchFailed", _ => number.ToString()
            };
        }
        return node["status"]?.ToString() ?? "";
    }

    private static JsonNode? TryNode(JsonNode view, string id) =>
        view["activities"]?.AsArray().SingleOrDefault(item =>
            item?["authoredActivityId"]?.GetValue<string>() == id || item?["executableNodeId"]?.GetValue<string>() == id);

    /// <summary>Test-owned loopback HTTP worker target. Starts 500 so SendHttpRequest takes Failed → Fault; Succeed() flips to 200/Done.</summary>
    private sealed class ControllableHttpResponder : IAsyncDisposable
    {
        private readonly HttpListener listener = new();
        private readonly CancellationTokenSource cts = new();
        private Task? pump;
        private volatile int status = (int)HttpStatusCode.InternalServerError;

        private ControllableHttpResponder(Uri url) => Url = url;

        public Uri Url { get; }

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
                    context.Response.StatusCode = status;
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
