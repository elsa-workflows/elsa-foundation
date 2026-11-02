using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bpmn.Model;
using Elsa.Activities.Bpmn.Core;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Activities.Bpmn.Hosting.Tests;

/// <summary>
/// A real held native service failure in the production Workbench, with retry and cancellation arriving in both orders.
/// </summary>
public sealed class BpmnServiceRetryCancellationTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Retry_and_cancellation_of_a_held_service_failure_keep_one_truthful_winner(bool cancelFirst)
    {
        await using var host = await OwnedWorkbench.StartAsync(directBpmn: true, recoverySweepSeconds: 0.2);
        await host.LoginAsync();
        var catalog = await host.GetAsync("/design/activities/definitions?search=Elsa.Activities.Primitives.Activities.Fault");
        var fault = Assert.Single(catalog["items"]!.AsArray(), item =>
            item!["definition"]!["activityTypeKey"]!.GetValue<string>() == "Elsa.Activities.Primitives.Activities.Fault")!;
        var worker = await host.PostAsync("/design/workflows/definitions/submit", new
        {
            name = "Always failing BPMN service worker",
            state = new { variables = Array.Empty<object>(), inputs = Array.Empty<object>(), outputs = Array.Empty<object>(),
                rootActivity = new { nodeId = "service-root", activityVersionId = fault["definition"]!["headVersionId"]!.GetValue<string>(), outputs = Array.Empty<object>(),
                    inputs = new[] { new { referenceKey = "code", value = new { value = "service.unavailable", expressionType = "Literal" } } } } }
        });
        var publication = await host.PostAsync($"/publishing/workflows/{worker["version"]!["id"]!.GetValue<string>()}/publish", new { });
        var pin = new WorkflowExecutableIdentity(publication["artifactId"]!.GetValue<string>(), publication["definitionId"]!.GetValue<string>(),
            publication["definitionVersionId"]!.GetValue<string>(), publication["artifactVersion"]!.GetValue<string>(), publication["artifactHash"]!.GetValue<string>());
        var process = new BpmnProcessDefinition("held-service", Elements:
            [new("start", BpmnElementTypes.StartEvent), new("service", BpmnElementTypes.ServiceTask, bindingRef: "native-work"), new("end", BpmnElementTypes.EndEvent)],
            SequenceFlows: [new("to-service", "start", "service"), new("to-end", "service", "end")]);
        var definition = new BpmnModelDefinition(3, JsonSerializer.SerializeToElement(process, Web), [],
            [new BpmnNativeServiceBinding("native-work", pin, "service.business-error", new BpmnServiceFaultPolicy(1, BpmnServiceFaultDisposition.HoldForRetry, 2))]);
        var published = await host.PostAsync("/bpmn/artifacts", new { requestId = "publish-held", definition });
        var started = await host.PostAsync("/bpmn/executions", new { sourceReferenceId = published["sourceReferenceId"]!.GetValue<string>(), requestId = "start-held" });
        var path = "/bpmn/executions/" + Uri.EscapeDataString(started["executionId"]!.GetValue<string>());
        await WaitAsync(host, path, status => status == "WaitingIncident", "The actual native fault was not held.");
        var incident = Assert.Single((await host.GetAsync(path + "/incidents"))["items"]!.AsArray())!;
        Assert.Equal("Open", incident["status"]!.GetValue<string>());
        var retry = new { requestId = "retry", incidentId = incident["incidentId"]!.GetValue<string>(),
            invocationId = incident["invocationId"]!.GetValue<string>(), failedAttemptId = incident["failedAttemptId"]!.GetValue<string>() };
        var cancel = new { requestId = "cancel", reason = "Withdrawn while held" };

        if (cancelFirst)
        {
            var cancelled = await host.PostAsync(path + "/cancel", cancel);
            Assert.Equal("Accepted", cancelled["result"]!["status"]!.GetValue<string>());
            var settled = await WaitAsync(host, path, status => status == "TerminalSettled", "The held cancellation did not settle.");
            Assert.Equal("Cancelled", settled["result"]!["status"]!.GetValue<string>());
            var late = await host.PostAsync(path + "/retry-service", retry, HttpStatusCode.Conflict);
            Assert.Equal("execution-terminal", late["result"]!["code"]!.GetValue<string>());
            // The cancellation won: the original failure is retained and no replacement worker exists.
            Assert.Single((await host.GetAsync("/runtime/workflows/instances")).AsArray());
            var retained = Assert.Single((await host.GetAsync(path + "/incidents"))["items"]!.AsArray())!;
            Assert.Equal(incident["failedAttemptId"]!.GetValue<string>(), retained["failedAttemptId"]!.GetValue<string>());
            Assert.Equal("Retired", retained["status"]!.GetValue<string>());
            return;
        }

        var accepted = await host.PostAsync(path + "/retry-service", retry);
        Assert.Equal("Accepted", accepted["result"]!["status"]!.GetValue<string>());
        // The replacement may fault before the cancellation arrives; either winner must be truthful and final.
        var (cancelStatus, cancelOutcome) = await host.PostOutcomeAsync(path + "/cancel", cancel);
        var terminal = await WaitAsync(host, path, status => status == "TerminalSettled", "The retried execution did not settle.");
        var status = terminal["result"]!["status"]!.GetValue<string>();
        Assert.Contains(status, new[] { "Cancelled", "Faulted" });
        Assert.Equal(status == "Cancelled" ? HttpStatusCode.OK : HttpStatusCode.Conflict, cancelStatus);
        Assert.Equal(status == "Cancelled" ? "Accepted" : "Rejected", cancelOutcome["result"]!["status"]!.GetValue<string>());
        // Exactly one replacement attempt, whatever won. When cancellation retires its start before admission,
        // no physical replacement worker exists; otherwise exactly one does.
        var attempts = await AttemptsAsync(host, path);
        Assert.Equal(2, attempts.Count);
        var replacement = Assert.Single(attempts, attempt => attempt!["attemptId"]!.GetValue<string>() != retry.failedAttemptId)!;
        var replacementStatus = replacement["status"]!.GetValue<string>();
        // A cancellation that wins semantically still records an already running worker's actual terminal for settlement.
        Assert.Contains(replacementStatus, status == "Cancelled" ? new[] { "NotAdmitted", "Cancelled", "Faulted" } : new[] { "Faulted" });
        if (status == "Cancelled" && replacementStatus != "NotAdmitted")
            Assert.Contains((await host.GetAsync(path + "/inspection"))["settlements"]!.AsArray(), settlement =>
                settlement!["kind"]!.GetValue<string>() == "NativeCancellation" && settlement["settled"]!.GetValue<bool>() &&
                settlement["attemptId"]!.GetValue<string>() == replacement["attemptId"]!.GetValue<string>());
        var admitted = attempts.Count(attempt => attempt!["status"]!.GetValue<string>() != "NotAdmitted");
        Assert.Equal(admitted, (await host.GetAsync("/runtime/workflows/instances")).AsArray().Count);
        var replayed = await host.PostAsync(path + "/retry-service", retry);
        Assert.True(replayed["replay"]!.GetValue<bool>());
        Assert.True(JsonNode.DeepEquals(accepted["result"], replayed["result"]));
        var fresh = await host.PostAsync(path + "/retry-service", new { requestId = "retry-again", retry.incidentId, retry.invocationId, retry.failedAttemptId }, HttpStatusCode.Conflict);
        Assert.Equal("execution-terminal", fresh["result"]!["code"]!.GetValue<string>());
        Assert.Equal(attempts.Count, (await AttemptsAsync(host, path)).Count);
        Assert.Equal(admitted, (await host.GetAsync("/runtime/workflows/instances")).AsArray().Count);
        var original = Assert.Single((await host.GetAsync(path + "/incidents"))["items"]!.AsArray(),
            item => item!["failedAttemptId"]!.GetValue<string>() == retry.failedAttemptId)!;
        Assert.Equal("Retried", original["status"]!.GetValue<string>());
    }

    private static async Task<JsonArray> AttemptsAsync(OwnedWorkbench host, string path) =>
        (await host.GetAsync(path + "/inspection"))["attempts"]!.AsArray();

    private static async Task<JsonNode> WaitAsync(OwnedWorkbench host, string path, Func<string, bool> done, string failure)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(60);
        while (true)
        {
            var view = await host.GetAsync(path);
            if (done(view["status"]!.GetValue<string>())) return view;
            Assert.True(DateTimeOffset.UtcNow < deadline, failure);
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }
    }
}
