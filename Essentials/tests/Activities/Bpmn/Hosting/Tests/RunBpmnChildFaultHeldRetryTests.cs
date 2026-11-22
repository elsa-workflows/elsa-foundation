using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bpmn.Model;
using Elsa.Activities.Bpmn.Core;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Activities.Bpmn.Hosting.Tests;

/// <summary>
/// Cross-model G2.2 evidence: a native Root RunBpmn parent waits on a direct BPMN model whose serviceTask
/// calls a real native worker through BpmnNativeWorkBoundary. When that worker faults, HoldForRetry keeps
/// the child from terminating; a later RetryServiceAsync that succeeds lets the parent continue exactly once
/// with Done; FinalFault or an exhausted budget surfaces bpmn.model-faulted exactly once.
/// SQLite Owned Workbench, one in-process host only. Sequence/Flowchart parents, other providers, child-to-parent
/// cancel, G3 overhead, and interruption cuts are not claimed.
/// </summary>
public sealed class RunBpmnChildFaultHeldRetryTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task A_held_native_service_fault_under_RunBpmn_retries_once_and_the_parent_continues_with_Done()
    {
        await using var responder = await ControllableHttpResponder.StartAsync();
        await using var host = await OwnedWorkbench.StartAsync(directBpmn: true, recoverySweepSeconds: 0.2);
        await host.LoginAsync();
        var pin = await PublishWorkerAsync(host, FailThenSucceedWorker(host, responder.Url));
        var (execution, nativePath, modelPath) = await StartRunBpmnAsync(host, pin, new BpmnServiceFaultPolicy(1, BpmnServiceFaultDisposition.HoldForRetry, 2));
        var (heldNative, heldModel, incident) = await WaitHeldAsync(host, nativePath, modelPath);
        Assert.Equal("Running", heldNative["instance"]!["status"]!.GetValue<string>());
        Assert.Empty(heldNative["incidents"]!.AsArray());
        var heldRun = Assert.Single(heldNative["activities"]!.AsArray(), item => item!["authoredActivityId"]!.GetValue<string>() == "run-bpmn")!;
        Assert.Equal("Suspended", heldRun["status"]!.GetValue<string>());
        var heldDetail = await host.GetAsync(nativePath + "/activity-executions/" + heldRun["activityExecutionId"]!.GetValue<string>());
        Assert.Single(heldDetail["bookmarks"]!.AsArray());
        Assert.Equal("WaitingIncident", heldModel["status"]!.GetValue<string>());
        Assert.Null(heldModel["result"]);
        responder.Succeed();
        var retry = new { requestId = "retry-held-runbpmn", incidentId = incident["incidentId"]!.GetValue<string>(),
            invocationId = incident["invocationId"]!.GetValue<string>(), failedAttemptId = incident["failedAttemptId"]!.GetValue<string>() };
        var accepted = await host.PostAsync(modelPath + "/retry-service", retry);
        Assert.Equal("Accepted", accepted["result"]!["status"]!.GetValue<string>());
        var (nativeFinal, modelFinal) = await WaitNativeAsync(host, nativePath, modelPath, "Completed", "Completed",
            "The retried native service did not complete the model and resume the RunBpmn parent once.");
        Assert.Empty(nativeFinal["incidents"]!.AsArray());
        var completedRun = Assert.Single(nativeFinal["activities"]!.AsArray(), item => item!["authoredActivityId"]!.GetValue<string>() == "run-bpmn")!;
        Assert.Equal("Completed", completedRun["status"]!.GetValue<string>());
        Assert.Equal("Done", Assert.Single(completedRun["outcomeNames"]!.AsArray())!.GetValue<string>());
        Assert.Equal("TerminalSettled", modelFinal["status"]!.GetValue<string>());
        Assert.Equal("Completed", modelFinal["result"]!["status"]!.GetValue<string>());
        var replayed = await host.PostAsync(modelPath + "/retry-service", retry);
        Assert.True(replayed["replay"]!.GetValue<bool>());
        Assert.True(JsonNode.DeepEquals(accepted["result"], replayed["result"]));
        var fresh = await host.PostAsync(modelPath + "/retry-service",
            new { requestId = "retry-held-runbpmn-again", retry.incidentId, retry.invocationId, retry.failedAttemptId }, HttpStatusCode.Conflict);
        Assert.Equal("execution-terminal", fresh["result"]!["code"]!.GetValue<string>());
        Assert.Equal("Completed", (await host.GetAsync(nativePath))["instance"]!["status"]!.GetValue<string>());
        Assert.Single((await host.GetAsync(nativePath))["activities"]!.AsArray(), item => item!["authoredActivityId"]!.GetValue<string>() == "run-bpmn");
        var attempts = (await host.GetAsync(modelPath + "/inspection"))["attempts"]!.AsArray();
        Assert.Equal(2, attempts.Count);
        Assert.Contains(attempts, attempt => attempt!["status"]!.GetValue<string>() == "Faulted");
        Assert.Contains(attempts, attempt => attempt!["status"]!.GetValue<string>() == "Completed");
        Assert.Equal(execution, nativeFinal["instance"]!["workflowExecutionId"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_final_native_service_fault_under_RunBpmn_surfaces_bpmn_model_faulted_to_the_parent_once()
    {
        await using var host = await OwnedWorkbench.StartAsync(directBpmn: true, recoverySweepSeconds: 0.2);
        await host.LoginAsync();
        var pin = await PublishWorkerAsync(host, AlwaysFaultWorker(host));
        var (_, nativePath, modelPath) = await StartRunBpmnAsync(host, pin, new BpmnServiceFaultPolicy(1, BpmnServiceFaultDisposition.FinalFault, 1));
        var (nativeFinal, modelFinal) = await WaitNativeAsync(host, nativePath, modelPath, "Faulted", "Faulted",
            "The FinalFault native service did not surface bpmn.model-faulted to the RunBpmn parent.");
        AssertParentFaultedOnce(nativeFinal, modelFinal);
        var late = await host.PostAsync(modelPath + "/retry-service",
            new { requestId = "retry-final-fault", incidentId = "unused", invocationId = "unused", failedAttemptId = "unused" }, HttpStatusCode.Conflict);
        Assert.Equal("execution-terminal", late["result"]!["code"]!.GetValue<string>());
        Assert.Equal("Faulted", (await host.GetAsync(nativePath))["instance"]!["status"]!.GetValue<string>());
        Assert.Single((await host.GetAsync(nativePath))["activities"]!.AsArray(), item => item!["authoredActivityId"]!.GetValue<string>() == "run-bpmn");
    }

    [Fact]
    public async Task An_exhausted_held_native_service_fault_under_RunBpmn_surfaces_bpmn_model_faulted_to_the_parent_once()
    {
        await using var host = await OwnedWorkbench.StartAsync(directBpmn: true, recoverySweepSeconds: 0.2);
        await host.LoginAsync();
        var pin = await PublishWorkerAsync(host, AlwaysFaultWorker(host));
        var (_, nativePath, modelPath) = await StartRunBpmnAsync(host, pin, new BpmnServiceFaultPolicy(1, BpmnServiceFaultDisposition.HoldForRetry, 2));
        var (heldNative, _, incident) = await WaitHeldAsync(host, nativePath, modelPath);
        Assert.Equal("Running", heldNative["instance"]!["status"]!.GetValue<string>());
        Assert.Empty(heldNative["incidents"]!.AsArray());
        var retry = new { requestId = "retry-exhaust-runbpmn", incidentId = incident["incidentId"]!.GetValue<string>(),
            invocationId = incident["invocationId"]!.GetValue<string>(), failedAttemptId = incident["failedAttemptId"]!.GetValue<string>() };
        var accepted = await host.PostAsync(modelPath + "/retry-service", retry);
        Assert.Equal("Accepted", accepted["result"]!["status"]!.GetValue<string>());
        var (nativeFinal, modelFinal) = await WaitNativeAsync(host, nativePath, modelPath, "Faulted", "Faulted",
            "The exhausted held native service did not surface bpmn.model-faulted to the RunBpmn parent.");
        AssertParentFaultedOnce(nativeFinal, modelFinal);
        var replayed = await host.PostAsync(modelPath + "/retry-service", retry);
        Assert.True(replayed["replay"]!.GetValue<bool>());
        var fresh = await host.PostAsync(modelPath + "/retry-service",
            new { requestId = "retry-exhaust-runbpmn-again", retry.incidentId, retry.invocationId, retry.failedAttemptId }, HttpStatusCode.Conflict);
        Assert.Equal("execution-terminal", fresh["result"]!["code"]!.GetValue<string>());
        Assert.Equal("Faulted", (await host.GetAsync(nativePath))["instance"]!["status"]!.GetValue<string>());
        Assert.Single((await host.GetAsync(nativePath))["activities"]!.AsArray(), item => item!["authoredActivityId"]!.GetValue<string>() == "run-bpmn");
        var attempts = (await host.GetAsync(modelPath + "/inspection"))["attempts"]!.AsArray();
        Assert.Equal(2, attempts.Count);
        Assert.All(attempts, attempt => Assert.Equal("Faulted", attempt!["status"]!.GetValue<string>()));
    }

    private static async Task<WorkflowExecutableIdentity> PublishWorkerAsync(OwnedWorkbench host, Task<JsonNode> worker)
    {
        var definition = await worker;
        var publication = await host.PostAsync($"/publishing/workflows/{definition["version"]!["id"]!.GetValue<string>()}/publish", new { });
        return new WorkflowExecutableIdentity(publication["artifactId"]!.GetValue<string>(), publication["definitionId"]!.GetValue<string>(),
            publication["definitionVersionId"]!.GetValue<string>(), publication["artifactVersion"]!.GetValue<string>(), publication["artifactHash"]!.GetValue<string>());
    }

    private static async Task<JsonNode> AlwaysFaultWorker(OwnedWorkbench host)
    {
        var catalog = await host.GetAsync("/design/activities/definitions?search=Elsa.Activities.Primitives.Activities.Fault");
        var fault = Assert.Single(catalog["items"]!.AsArray(), item =>
            item!["definition"]!["activityTypeKey"]!.GetValue<string>() == "Elsa.Activities.Primitives.Activities.Fault")!;
        return await host.PostAsync("/design/workflows/definitions/submit", new
        {
            name = "Always failing RunBpmn service worker",
            state = new { variables = Array.Empty<object>(), inputs = Array.Empty<object>(), outputs = Array.Empty<object>(),
                rootActivity = new { nodeId = "service-root", activityVersionId = fault["definition"]!["headVersionId"]!.GetValue<string>(), outputs = Array.Empty<object>(),
                    inputs = new[] { new { referenceKey = "code", value = new { value = "service.unavailable", expressionType = "Literal" } } } } }
        });
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
            name = "Fail then succeed RunBpmn service worker",
            state = new { variables = Array.Empty<object>(), inputs = Array.Empty<object>(), outputs = Array.Empty<object>(),
                rootActivity = new { nodeId = "service-root", activityVersionId = await Version("Elsa.Activities.Flowchart.Activities.Flowchart"),
                    inputs = Array.Empty<object>(), outputs = Array.Empty<object>(),
                    structure = new { kind = "elsa.flowchart.structure", schemaVersion = "1.0.0",
                        payload = new { startNodeId = "send", activities = new object[] { send, fault },
                            connections = new[] { new { id = "send-fault", source = new { nodeId = "send", port = "Failed" }, target = new { nodeId = "fault" } } } } } } }
        });
    }

    private static async Task<(string Execution, string NativePath, string ModelPath)> StartRunBpmnAsync(
        OwnedWorkbench host, WorkflowExecutableIdentity pin, BpmnServiceFaultPolicy policy)
    {
        var process = new BpmnProcessDefinition("runbpmn-service", Elements:
            [new("start", BpmnElementTypes.StartEvent), new("service", BpmnElementTypes.ServiceTask, bindingRef: "native-work"), new("end", BpmnElementTypes.EndEvent)],
            SequenceFlows: [new("to-service", "start", "service"), new("to-end", "service", "end")]);
        var definition = new BpmnModelDefinition(3, JsonSerializer.SerializeToElement(process, Web), [],
            [new BpmnNativeServiceBinding("native-work", pin, "service.business-error", policy)]);
        var published = await host.PostAsync("/bpmn/artifacts", new { requestId = "publish-runbpmn-service", definition });
        var catalog = await host.GetAsync("/design/activities/definitions?search=Elsa.Activities.Bpmn.Activities.RunBpmn");
        var activity = Assert.Single(catalog["items"]!.AsArray(), item => item!["definition"]!["activityTypeKey"]!.GetValue<string>() == "Elsa.Activities.Bpmn.Activities.RunBpmn");
        var native = await host.PostAsync("/design/workflows/definitions/submit", new
        {
            name = "Actual RunBpmn native service parent",
            state = new { variables = Array.Empty<object>(), inputs = Array.Empty<object>(), outputs = Array.Empty<object>(),
                rootActivity = new { nodeId = "run-bpmn", activityVersionId = activity!["definition"]!["headVersionId"]!.GetValue<string>(), outputs = Array.Empty<object>(),
                    inputs = new[] { new { referenceKey = "Model", value = new { value = published["sourceReferenceId"]!.GetValue<string>(), expressionType = "Literal" } } } } }
        });
        var publication = await host.PostAsync($"/publishing/workflows/{native["version"]!["id"]!.GetValue<string>()}/publish", new { });
        var started = await host.PostAsync($"/runtime/workflows/executables/{publication["artifactId"]!.GetValue<string>()}/execute",
            new { sourceReferenceId = publication["sourceReferenceId"]!.GetValue<string>() });
        var execution = started["workflowExecutionId"]!.GetValue<string>();
        var nativePath = "/runtime/workflows/instances/" + execution;
        string? modelId = null;
        var deadline = DateTimeOffset.UtcNow.AddSeconds(60);
        JsonNode view = null!;
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

        return (execution, nativePath, modelPath);
    }

    private static async Task<(JsonNode Native, JsonNode Model, JsonNode Incident)> WaitHeldAsync(OwnedWorkbench host, string nativePath, string modelPath)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(60);
        while (true)
        {
            var model = await host.GetAsync(modelPath);
            var native = await host.GetAsync(nativePath);
            if (model["status"]!.GetValue<string>() == "WaitingIncident")
            {
                Assert.Equal("Running", native["instance"]!["status"]!.GetValue<string>());
                var incident = Assert.Single((await host.GetAsync(modelPath + "/incidents"))["items"]!.AsArray())!;
                Assert.Equal("Open", incident["status"]!.GetValue<string>());
                return (native, model, incident);
            }

            Assert.True(DateTimeOffset.UtcNow < deadline,
                $"The actual native fault was not held. Native: {native.ToJsonString()}. Model: {model.ToJsonString()}.");
            await Task.Delay(100);
        }
    }

    private static async Task<(JsonNode Native, JsonNode Model)> WaitNativeAsync(
        OwnedWorkbench host, string nativePath, string modelPath, string nativeStatus, string modelResult, string failure)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(60);
        while (true)
        {
            var native = await host.GetAsync(nativePath);
            var model = await host.GetAsync(modelPath);
            if (native["instance"]!["status"]!.GetValue<string>() == nativeStatus &&
                model["status"]!.GetValue<string>() == "TerminalSettled" &&
                model["result"]!["status"]!.GetValue<string>() == modelResult)
                return (native, model);
            Assert.True(DateTimeOffset.UtcNow < deadline, $"{failure} Native: {native.ToJsonString()}. Model: {model.ToJsonString()}.");
            await Task.Delay(100);
        }
    }

    private static void AssertParentFaultedOnce(JsonNode nativeFinal, JsonNode modelFinal)
    {
        Assert.Equal("Faulted", nativeFinal["instance"]!["status"]!.GetValue<string>());
        var run = Assert.Single(nativeFinal["activities"]!.AsArray(), item => item!["authoredActivityId"]!.GetValue<string>() == "run-bpmn")!;
        Assert.Equal("Faulted", run["status"]!.GetValue<string>());
        var incident = Assert.Single(nativeFinal["incidents"]!.AsArray())!;
        Assert.Equal("ActivityReturnedFault", incident["failureType"]!.GetValue<string>());
        Assert.True(incident["isBlocking"]!.GetValue<bool>());
        Assert.Equal(run["activityExecutionId"]!.GetValue<string>(), incident["activityExecutionId"]!.GetValue<string>());
        Assert.Equal("Faulted", modelFinal["result"]!["status"]!.GetValue<string>());
        Assert.Equal("TerminalSettled", modelFinal["status"]!.GetValue<string>());
        var metadata = incident["metadata"]!.AsObject();
        if (metadata.TryGetPropertyValue("code", out var code) && code is JsonValue)
            Assert.Equal("bpmn.model-faulted", code.GetValue<string>());
        else if (metadata.TryGetPropertyValue("faultCode", out var faultCode) && faultCode is JsonValue)
            Assert.Equal("bpmn.model-faulted", faultCode.GetValue<string>());
        else if (run["subStatus"] is JsonValue subStatus)
            Assert.Equal("ActivityReturnedFault", subStatus.GetValue<string>());
    }

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
