using System.Text.Json;
using System.Text.Json.Nodes;
using Bpmn.Model;
using Elsa.Activities.Bpmn.Core;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Elsa.Activities.Bpmn.Hosting.Tests;

public sealed class BpmnUserTaskWorkerTests
{
    [Fact]
    public async Task Published_native_user_task_survives_restart_and_returns_its_action_and_data_to_Bpmn_once()
    {
        await using var host = await OwnedWorkbench.StartAsync(directBpmn: true, recoverySweepSeconds: 0.2);
        await host.LoginAsync();
        var actor = await host.GetAsync("/runtime/user-task-identity");
        await host.PostAsync("/runtime/user-task-forms", new { id = "bpmn-response", version = "1", name = "BPMN response", provider = "elsa.basic-forms/1",
            fields = new[] { new { key = "PurchaseCode", label = "Purchase", kind = "text", required = true } } });
        var catalog = await host.GetAsync("/design/activities/definitions?search=Elsa.Activities.Primitives.Activities.CreateUserTask");
        var activity = Assert.Single(catalog["items"]!.AsArray(), item => item!["definition"]!["activityTypeKey"]!.GetValue<string>() == "Elsa.Activities.Primitives.Activities.CreateUserTask");
        object Input(string key, object value) => new { referenceKey = key, value = new { value, expressionType = "Literal" } };
        var submitted = await host.PostAsync("/design/workflows/definitions/submit", new { name = "BPMN human worker", state = new {
            variables = Array.Empty<object>(), inputs = Array.Empty<object>(), outputs = Array.Empty<object>(), rootActivity = new {
                nodeId = "human-worker", activityVersionId = activity!["definition"]!["headVersionId"]!.GetValue<string>(), outputs = Array.Empty<object>(),
                inputs = new[] { Input("Title", "Approve BPMN purchase"), Input("AssigneeId", actor["userId"]!.GetValue<string>()),
                    Input("Actions", new[] { "Approve", "Reject" }), Input("OptionalFormActions", Array.Empty<string>()), Input("FormId", "bpmn-response"), Input("FormVersion", "1"), Input("Prefill", new { }) }
            } } });
        var native = await host.PostAsync($"/publishing/workflows/{submitted["version"]!["id"]!.GetValue<string>()}/publish", new { });
        var pin = new WorkflowExecutableIdentity(native["artifactId"]!.GetValue<string>(), native["definitionId"]!.GetValue<string>(), native["definitionVersionId"]!.GetValue<string>(), native["artifactVersion"]!.GetValue<string>(), native["artifactHash"]!.GetValue<string>());
        var process = new BpmnProcessDefinition("human-service", Elements: [new("start", BpmnElementTypes.StartEvent), new("approval", BpmnElementTypes.ServiceTask, bindingRef: "approval-worker"), new("end", BpmnElementTypes.EndEvent)],
            SequenceFlows: [new("start-approval", "start", "approval"), new("approval-end", "approval", "end")]);
        var published = await host.PostAsync("/bpmn/artifacts", new { requestId = "publish-human-service", definition = new BpmnModelDefinition(2, JsonSerializer.SerializeToElement(process, new JsonSerializerOptions(JsonSerializerDefaults.Web)), [], [new BpmnNativeServiceBinding("approval-worker", pin, "human.business-error")]) });
        var started = await host.PostAsync("/bpmn/executions", new { requestId = "start-human-service", sourceReferenceId = published["sourceReferenceId"]!.GetValue<string>() });
        var executionPath = "/bpmn/executions/" + started["executionId"]!.GetValue<string>();
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30); JsonNode? task = null;
        while (task is null)
        {
            task = (await host.GetAsync("/runtime/user-tasks?view=mine"))["items"]!.AsArray().SingleOrDefault();
            Assert.True(DateTimeOffset.UtcNow < deadline, "The native worker never exposed its human task.");
            if (task is null) await Task.Delay(100);
        }
        Assert.Equal("Open", task["status"]!.GetValue<string>());
        Assert.Equal("Running", (await host.GetAsync(executionPath))["status"]!.GetValue<string>());
        var taskId = task["id"]!.GetValue<string>(); var revision = task["revision"]!.GetValue<long>();
        await host.RestartAsync(); await host.LoginAsync();
        var completion = new { revision, requestId = "bpmn-human-response", action = "Approve", data = JsonSerializer.SerializeToElement(new { PurchaseCode = "PO-107" }) };
        var accepted = await host.PostAsync($"/runtime/user-tasks/{taskId}/complete", completion);
        var replay = await host.PostAsync($"/runtime/user-tasks/{taskId}/complete", completion);
        Assert.Equal(accepted["decision"]!["response"]!["completionId"]!.GetValue<string>(), replay["decision"]!["response"]!["completionId"]!.GetValue<string>());
        deadline = DateTimeOffset.UtcNow.AddSeconds(30); var settled = await host.GetAsync(executionPath);
        while (settled["status"]!.GetValue<string>() != "TerminalSettled")
        {
            Assert.True(DateTimeOffset.UtcNow < deadline, "The native User Task result never settled BPMN.");
            await Task.Delay(100); settled = await host.GetAsync(executionPath);
        }
        Assert.Equal("Completed", settled["result"]!["status"]!.GetValue<string>());
        var terminal = await host.GetAsync("/runtime/user-tasks/" + taskId);
        Assert.Equal("Completed", terminal["status"]!.GetValue<string>());
        Assert.Single(terminal["history"]!.AsArray(), entry => entry!["operation"]!.GetValue<string>() == "completed");
        await host.StopAsync();
        await using var database = new SqliteConnection($"Data Source={host.RuntimeDatabase};Mode=ReadOnly;Pooling=False"); await database.OpenAsync();
        await using var command = database.CreateCommand(); command.CommandText = "SELECT ContentJson FROM elsa_runtime_model_execution";
        var state = JsonNode.Parse((string)(await command.ExecuteScalarAsync())!)!;
        var result = Assert.Single(Objects(state), item => item["completion"] is JsonObject complete && complete["result"] is JsonObject response && response["taskId"]?.GetValue<string>() == taskId);
        Assert.Equal("Approve", result["completion"]!["outcomeKey"]!.GetValue<string>());
        Assert.Equal("PO-107", result["completion"]!["result"]!["data"]!["PurchaseCode"]!.GetValue<string>());
    }
    private static IEnumerable<JsonObject> Objects(JsonNode node)
    {
        if (node is JsonObject value) { yield return value; foreach (var child in value.Select(x => x.Value).Where(x => x is not null)) foreach (var nested in Objects(child!)) yield return nested; }
        else if (node is JsonArray array) foreach (var child in array.Where(x => x is not null)) foreach (var nested in Objects(child!)) yield return nested;
    }
}
