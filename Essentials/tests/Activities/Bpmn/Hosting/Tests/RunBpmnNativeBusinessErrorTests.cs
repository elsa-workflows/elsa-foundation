using System.Text.Json;
using System.Text.Json.Nodes;
using Bpmn.Model;
using Elsa.Activities.Bpmn.Core;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Activities.Bpmn.Hosting.Tests;

/// <summary>
/// G2.4 row A2: a native Root RunBpmn parent waits on a direct BPMN model whose serviceTask
/// calls a native worker that Completes with outcome BusinessError. The driver maps that
/// completed outcome to interpreter OnWorkFaulted with ErrorCode = binding.BusinessErrorCode
/// (BpmnDirectExecutionDriver, Completed + OutcomeKey BusinessError — not a Faulted worker).
/// Matching coded error boundary (native-work profile admits service + coded error-end, not
/// a recover userTask): model Completed, parent Done, no incident. No matching boundary:
/// unmatched BPMN error follows spec 1017 FR-013 / US4 (matching/unmatched business error
/// must not become success) → model Faulted / parent bpmn.model-faulted. The unmatched
/// Fact is the discriminator that BusinessError is not OnWorkCompleted.
/// SQLite Owned Workbench, one in-process host, Root composition. Does not use
/// ELSA_BPMN_HOLD_NATIVE_PARENT_RESUME. Not ETL. The Workbench child CLR source scans only its
/// own binary folder, so a test-only CLR activity would need a production change or a feed
/// package. The hosted emitter is the first-party Finish intrinsic (Elsa.Finish /
/// elsa.intrinsic.finish@1) Completing with outcome BusinessError — the same driver branch
/// RunEtl hits. Finish is not in /design/activities/definitions (persisted CLR rows only);
/// it is taken from /design/activities/catalog and submitted as an AuthoredWorkflowIntrinsic.
/// </summary>
public sealed class RunBpmnNativeBusinessErrorTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private const string BusinessErrorCode = "native.business-error";
    private const string BusinessErrorOutcome = "BusinessError";

    [Fact]
    public async Task A_native_worker_Completed_BusinessError_under_RunBpmn_takes_the_matching_coded_boundary()
    {
        await using var host = await OwnedWorkbench.StartAsync(directBpmn: true, recoverySweepSeconds: 0.2);
        await host.LoginAsync();
        var pin = await PublishWorkerAsync(host);
        var (execution, nativePath, modelPath) = await StartRunBpmnAsync(host, pin, withMatchingBoundary: true);
        var (nativeFinal, modelFinal) = await WaitNativeAsync(host, nativePath, modelPath, "Completed", "Completed",
            "The matching coded boundary did not complete the model and resume the RunBpmn parent with Done.");
        Assert.Empty(nativeFinal["incidents"]!.AsArray());
        var completedRun = Assert.Single(nativeFinal["activities"]!.AsArray(), item => item!["authoredActivityId"]!.GetValue<string>() == "run-bpmn")!;
        Assert.Equal("Completed", completedRun["status"]!.GetValue<string>());
        Assert.Equal("Done", Assert.Single(completedRun["outcomeNames"]!.AsArray())!.GetValue<string>());
        Assert.Equal("TerminalSettled", modelFinal["status"]!.GetValue<string>());
        Assert.Equal("Completed", modelFinal["result"]!["status"]!.GetValue<string>());
        Assert.Empty((await host.GetAsync(modelPath + "/incidents"))["items"]!.AsArray());
        await AssertBusinessErrorWorkerAsync(host, nativeFinal, Assert.Single((await host.GetAsync(modelPath + "/inspection"))["attempts"]!.AsArray())!);
        Assert.Equal(execution, nativeFinal["instance"]!["workflowExecutionId"]!.GetValue<string>());
        Assert.Empty((await host.GetAsync(modelPath + "/work"))["items"]!.AsArray());
    }

    [Fact]
    public async Task A_native_worker_Completed_BusinessError_under_RunBpmn_without_a_matching_boundary_faults_the_model()
    {
        await using var host = await OwnedWorkbench.StartAsync(directBpmn: true, recoverySweepSeconds: 0.2);
        await host.LoginAsync();
        var pin = await PublishWorkerAsync(host);
        var (_, nativePath, modelPath) = await StartRunBpmnAsync(host, pin, withMatchingBoundary: false);
        var (nativeFinal, modelFinal) = await WaitNativeAsync(host, nativePath, modelPath, "Faulted", "Faulted",
            "The unmatched BusinessError did not fault the model. Spec 1017 US4 scenario 2: unmatched business errors follow the declared profile rather than silently succeeding.");
        AssertParentModelFaultedOnce(nativeFinal, modelFinal);
        var attempt = Assert.Single((await host.GetAsync(modelPath + "/inspection"))["attempts"]!.AsArray())!;
        await AssertBusinessErrorWorkerAsync(host, nativeFinal, attempt);
        Assert.Empty((await host.GetAsync(modelPath + "/incidents"))["items"]!.AsArray());
        Assert.Equal("Faulted", (await host.GetAsync(nativePath))["instance"]!["status"]!.GetValue<string>());
        Assert.Single((await host.GetAsync(nativePath))["activities"]!.AsArray(), item => item!["authoredActivityId"]!.GetValue<string>() == "run-bpmn");
    }

    /// <summary>
    /// The native worker Completed with BusinessError. That is not A3 technical failure (Faulted worker,
    /// HoldForRetry / FinalFault, ErrorCode null). Inspection status stays Completed; faultCode stays absent.
    /// </summary>
    private static async Task AssertBusinessErrorWorkerAsync(OwnedWorkbench host, JsonNode native, JsonNode attempt)
    {
        Assert.Equal("Completed", attempt["status"]!.GetValue<string>());
        Assert.Equal(BusinessErrorOutcome, attempt["outcomeKey"]!.GetValue<string>());
        if (attempt["faultCode"] is JsonValue faultCode && faultCode.TryGetValue<string>(out var faultText))
            Assert.True(string.IsNullOrEmpty(faultText),
                "A Completed BusinessError worker must not carry a technical faultCode. Attempt=" + attempt.ToJsonString());
        var workerId = attempt["workerExecutionId"]!.GetValue<string>();
        Assert.False(string.IsNullOrWhiteSpace(workerId));
        Assert.NotEqual(native["instance"]!["workflowExecutionId"]!.GetValue<string>(), workerId);
        var worker = await host.GetAsync("/runtime/workflows/instances/" + workerId);
        Assert.Equal("Completed", worker["instance"]!["status"]!.GetValue<string>());
        Assert.Empty(worker["incidents"]!.AsArray());
        var root = Assert.Single(worker["activities"]!.AsArray())!;
        Assert.Equal("Completed", root["status"]!.GetValue<string>());
        Assert.Equal(BusinessErrorOutcome, Assert.Single(root["outcomeNames"]!.AsArray())!.GetValue<string>());
    }

    private static void AssertParentModelFaultedOnce(JsonNode nativeFinal, JsonNode modelFinal)
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
        else
            Assert.Fail("Unmatched BusinessError did not leave bpmn.model-faulted / ActivityReturnedFault on RunBpmn. Native=" +
                nativeFinal.ToJsonString() + " Model=" + modelFinal.ToJsonString());
    }

    private static async Task<WorkflowExecutableIdentity> PublishWorkerAsync(OwnedWorkbench host)
    {
        var catalog = await host.GetAsync("/design/activities/catalog");
        var finish = Assert.Single(catalog["activities"]!.AsArray(), item =>
            item!["activityTypeKey"]!.GetValue<string>() == "Elsa.Finish")!;
        Assert.Equal("elsa.intrinsic.finish@1", finish["activityVersionId"]!.GetValue<string>());
        Assert.Equal("Finish", finish["intrinsic"]!["kind"]!.GetValue<string>());
        var definition = await host.PostAsync("/design/workflows/definitions/submit", new
        {
            name = "RunBpmn native BusinessError worker",
            state = new
            {
                variables = Array.Empty<object>(),
                inputs = Array.Empty<object>(),
                outputs = Array.Empty<object>(),
                rootActivity = new
                {
                    nodeId = "service-root",
                    activityVersionId = finish["activityVersionId"]!.GetValue<string>(),
                    outputs = Array.Empty<object>(),
                    inputs = new[]
                    {
                        new { referenceKey = "outcome", value = new { value = BusinessErrorOutcome, expressionType = "Literal" } }
                    },
                    intrinsic = new { kind = "finish" }
                }
            }
        });
        var publication = await host.PostAsync($"/publishing/workflows/{definition["version"]!["id"]!.GetValue<string>()}/publish", new { });
        return new WorkflowExecutableIdentity(publication["artifactId"]!.GetValue<string>(), publication["definitionId"]!.GetValue<string>(),
            publication["definitionVersionId"]!.GetValue<string>(), publication["artifactVersion"]!.GetValue<string>(), publication["artifactHash"]!.GetValue<string>());
    }

    private static async Task<(string Execution, string NativePath, string ModelPath)> StartRunBpmnAsync(
        OwnedWorkbench host, WorkflowExecutableIdentity pin, bool withMatchingBoundary)
    {
        var properties = new Dictionary<string, string> { [BpmnEventDefinitionProperties.Code] = BusinessErrorCode };
        var process = withMatchingBoundary
            ? new BpmnProcessDefinition("runbpmn-business-error", Elements:
                [
                    new("start", BpmnElementTypes.StartEvent),
                    new("service", BpmnElementTypes.ServiceTask, bindingRef: "native-work"),
                    new("end", BpmnElementTypes.EndEvent),
                    new("error", BpmnElementTypes.BoundaryEvent, attachedToRef: "service",
                        eventDefinitions: [new(BpmnEventDefinitionTypes.Error, properties)]),
                    new("caught", BpmnElementTypes.EndEvent)
                ],
                SequenceFlows:
                [
                    new("to-service", "start", "service"),
                    new("to-end", "service", "end"),
                    new("error-caught", "error", "caught")
                ])
            : new BpmnProcessDefinition("runbpmn-business-error", Elements:
                [
                    new("start", BpmnElementTypes.StartEvent),
                    new("service", BpmnElementTypes.ServiceTask, bindingRef: "native-work"),
                    new("end", BpmnElementTypes.EndEvent)
                ],
                SequenceFlows: [new("to-service", "start", "service"), new("to-end", "service", "end")]);
        var definition = new BpmnModelDefinition(2, JsonSerializer.SerializeToElement(process, Web), [],
            [new BpmnNativeServiceBinding("native-work", pin, BusinessErrorCode)]);
        var published = await host.PostAsync("/bpmn/artifacts", new { requestId = "publish-runbpmn-business-error", definition });
        var catalog = await host.GetAsync("/design/activities/definitions?search=Elsa.Activities.Bpmn.Activities.RunBpmn");
        var activity = Assert.Single(catalog["items"]!.AsArray(), item => item!["definition"]!["activityTypeKey"]!.GetValue<string>() == "Elsa.Activities.Bpmn.Activities.RunBpmn");
        var native = await host.PostAsync("/design/workflows/definitions/submit", new
        {
            name = "Actual RunBpmn native BusinessError parent",
            state = new
            {
                variables = Array.Empty<object>(),
                inputs = Array.Empty<object>(),
                outputs = Array.Empty<object>(),
                rootActivity = new
                {
                    nodeId = "run-bpmn",
                    activityVersionId = activity!["definition"]!["headVersionId"]!.GetValue<string>(),
                    outputs = Array.Empty<object>(),
                    inputs = new[]
                    {
                        new { referenceKey = "Model", value = new { value = published["sourceReferenceId"]!.GetValue<string>(), expressionType = "Literal" } }
                    }
                }
            }
        });
        var publication = await host.PostAsync($"/publishing/workflows/{native["version"]!["id"]!.GetValue<string>()}/publish", new { });
        var started = await host.PostAsync($"/runtime/workflows/executables/{publication["artifactId"]!.GetValue<string>()}/execute",
            new { sourceReferenceId = publication["sourceReferenceId"]!.GetValue<string>() });
        var execution = started["workflowExecutionId"]!.GetValue<string>();
        var nativePath = "/runtime/workflows/instances/" + execution;
        string? modelId = null;
        var deadline = DateTimeOffset.UtcNow.AddSeconds(60);
        while (modelId is null)
        {
            Assert.True(DateTimeOffset.UtcNow < deadline, "The actual RunBpmn native wait did not publish its model invocation.");
            var view = await host.GetAsync(nativePath);
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
}
