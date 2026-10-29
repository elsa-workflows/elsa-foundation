using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bpmn.Model;
using Elsa.Activities.Bpmn.Core;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Elsa.Activities.Bpmn.Hosting.Tests;

public sealed class DirectBpmnWorkbenchTests
{
    [Fact]
    public async Task Direct_service_work_uses_the_real_coalescing_host_and_returns_its_native_completion()
    {
        await using var host = await OwnedWorkbench.StartAsync(directBpmn: true, recoverySweepSeconds: 0.2);
        await host.LoginAsync();
        var catalog = await host.GetAsync("/design/activities/definitions?search=Elsa.Activities.Primitives.Activities.WriteLine");
        var activity = Assert.Single(catalog["items"]!.AsArray(), item =>
            item!["definition"]!["activityTypeKey"]!.GetValue<string>() == "Elsa.Activities.Primitives.Activities.WriteLine");
        var nativeDefinition = await host.PostAsync("/design/workflows/definitions/submit", new
        {
            name = "Direct BPMN native service worker",
            state = new { variables = Array.Empty<object>(), inputs = Array.Empty<object>(), outputs = Array.Empty<object>(),
                rootActivity = new { nodeId = "service-root", activityVersionId = activity!["definition"]!["headVersionId"]!.GetValue<string>(), outputs = Array.Empty<object>(),
                    inputs = new[] { new { referenceKey = "text", value = new { value = "BPMN service completed.", expressionType = "Literal" } } } } }
        });
        var nativePublication = await host.PostAsync($"/publishing/workflows/{nativeDefinition["version"]!["id"]!.GetValue<string>()}/publish", new { });
        var pin = new WorkflowExecutableIdentity(
            ArtifactId: nativePublication["artifactId"]!.GetValue<string>(),
            DefinitionId: nativePublication["definitionId"]!.GetValue<string>(),
            DefinitionVersionId: nativePublication["definitionVersionId"]!.GetValue<string>(),
            ArtifactVersion: nativePublication["artifactVersion"]!.GetValue<string>(),
            ArtifactHash: nativePublication["artifactHash"]!.GetValue<string>());
        var process = new BpmnProcessDefinition("direct-service", Elements:
            [new("start", BpmnElementTypes.StartEvent), new("service", BpmnElementTypes.ServiceTask, bindingRef: "native-work"), new("end", BpmnElementTypes.EndEvent)],
            SequenceFlows: [new("to-service", "start", "service"), new("to-end", "service", "end")]);
        var definition = new BpmnModelDefinition(2, JsonSerializer.SerializeToElement(process, new JsonSerializerOptions(JsonSerializerDefaults.Web)), [],
            [new BpmnNativeServiceBinding("native-work", pin, "service.business-error")]);
        var published = await host.PostAsync("/bpmn/artifacts", new { requestId = "publish-service", definition });
        var started = await host.PostAsync("/bpmn/executions", new { sourceReferenceId = published["sourceReferenceId"]!.GetValue<string>(), requestId = "start-service" });
        var executionPath = "/bpmn/executions/" + started["executionId"]!.GetValue<string>();
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        var completed = await host.GetAsync(executionPath);
        while (completed["status"]!.GetValue<string>() != "TerminalSettled")
        {
            Assert.True(DateTimeOffset.UtcNow < deadline, "The actual model/native service return did not settle.");
            await Task.Delay(TimeSpan.FromMilliseconds(100));
            completed = await host.GetAsync(executionPath);
        }
        Assert.Equal("Completed", completed["result"]!["status"]!.GetValue<string>());
        var workers = (await host.GetAsync("/runtime/workflows/instances")).AsArray();
        var workerId = Assert.Single(workers)!["workflowExecutionId"]!.GetValue<string>();
        var worker = await host.GetAsync("/runtime/workflows/instances/" + workerId);
        Assert.Equal("Completed", worker["instance"]!["status"]!.GetValue<string>());
        Assert.Null(worker["instance"]!["parentWorkflowExecutionId"]);
        foreach (var field in new[] { "artifactId", "artifactHash", "artifactVersion", "definitionId", "definitionVersionId" })
            Assert.Equal(JsonSerializer.SerializeToNode(pin, new JsonSerializerOptions(JsonSerializerDefaults.Web))![field]!.GetValue<string>(),
                worker["instance"]![field]!.GetValue<string>());
        Assert.Equal("Completed", Assert.Single(worker["activities"]!.AsArray())!["status"]!.GetValue<string>());
        var previousProcess = host.ProcessId;
        await host.RestartAsync();
        Assert.NotEqual(previousProcess, host.ProcessId);
        await host.LoginAsync();
        Assert.True(JsonNode.DeepEquals(completed, await host.GetAsync(executionPath)));
        Assert.Single((await host.GetAsync("/runtime/workflows/instances")).AsArray());
    }

    [Fact]
    public async Task Authenticated_direct_execution_waits_restarts_resumes_and_completes_in_the_real_host()
    {
        await using var host = await OwnedWorkbench.StartAsync(directBpmn: true);
        await host.GetAsync("/bpmn/executions/unknown", HttpStatusCode.Unauthorized);
        await host.LoginAsync();
        await host.GetAsync("/bpmn/executions/unknown", HttpStatusCode.NotFound);
        var process = new BpmnProcessDefinition("direct-process", Elements:
            [new("start", BpmnElementTypes.StartEvent), new("human", BpmnElementTypes.UserTask, bindingRef: "human-work"), new("end", BpmnElementTypes.EndEvent)],
            SequenceFlows: [new("to-human", "start", "human"), new("to-end", "human", "end")]);
        var definition = new BpmnModelDefinition(1, JsonSerializer.SerializeToElement(process, new JsonSerializerOptions(JsonSerializerDefaults.Web)), ["human-work"]);
        var published = await host.PostAsync("/bpmn/artifacts", new { requestId = "publish", definition });
        var sourceReferenceId = published["sourceReferenceId"]!.GetValue<string>();
        var started = await host.PostAsync("/bpmn/executions", new { sourceReferenceId, requestId = "start" });
        Assert.False(started["pending"]!.GetValue<bool>());
        Assert.False(started["replay"]!.GetValue<bool>());
        var executionId = started["executionId"]!.GetValue<string>();
        var executionPath = "/bpmn/executions/" + executionId;
        var waiting = await host.GetAsync(executionPath);
        Assert.Equal("Running", waiting["status"]!.GetValue<string>());
        var work = Assert.Single((await host.GetAsync(executionPath + "/work"))["items"]!.AsArray())!;
        Assert.Equal("human", work["elementId"]!.GetValue<string>());
        Assert.True(JsonNode.DeepEquals(published["executable"], waiting["executable"]));
        Assert.Equal(published["incarnationId"]!.GetValue<string>(), waiting["materialIncarnationId"]!.GetValue<string>());
        var previousProcess = host.ProcessId;
        await host.StopAsync();
        var waitingSnapshot = await host.SnapshotAsync("waiting.sqlite.gz");
        await host.RestartAsync();
        Assert.NotEqual(previousProcess, host.ProcessId);
        var restartedProcess = host.ProcessId;
        await host.LoginAsync();
        var recovered = await host.GetAsync(executionPath);
        foreach (var field in new[] { "executable", "protocol", "materialIncarnationId", "revision", "status" })
            Assert.True(JsonNode.DeepEquals(waiting[field], recovered[field]), $"Restart changed the execution's {field}.");
        Assert.True(JsonNode.DeepEquals(work, Assert.Single((await host.GetAsync(executionPath + "/work"))["items"]!.AsArray())));
        var completionPath = executionPath + "/work/" + work["invocationId"]!.GetValue<string>() + "/complete";
        var obsolete = await host.PostAsync(completionPath, new { requestId = "obsolete", attemptId = "obsolete-attempt" }, HttpStatusCode.Conflict);
        Assert.Equal("obsolete-human-work", obsolete["result"]!["code"]!.GetValue<string>());
        Assert.True(JsonNode.DeepEquals(recovered, await host.GetAsync(executionPath)));
        Assert.True(JsonNode.DeepEquals(work, Assert.Single((await host.GetAsync(executionPath + "/work"))["items"]!.AsArray())));
        var rejectedRevision = (await host.GetAsync(executionPath))["revision"]!.GetValue<long>();
        await host.PostAsync(completionPath, new { requestId = "obsolete", attemptId = work["attemptId"]!.GetValue<string>() }, HttpStatusCode.Conflict);
        Assert.Equal(rejectedRevision, (await host.GetAsync(executionPath))["revision"]!.GetValue<long>());
        var completion = new { requestId = "complete", attemptId = work["attemptId"]!.GetValue<string>(), id = "body-cannot-select-an-execution", invocation = "body-cannot-select-work" };
        var accepted = await host.PostAsync(completionPath, completion);
        Assert.False(accepted["pending"]!.GetValue<bool>());
        Assert.Equal("Accepted", accepted["result"]!["status"]!.GetValue<string>());
        var completed = await host.GetAsync(executionPath);
        Assert.Equal("TerminalSettled", completed["status"]!.GetValue<string>());
        Assert.Equal("Completed", completed["result"]!["status"]!.GetValue<string>());
        Assert.Empty((await host.GetAsync(executionPath + "/work"))["items"]!.AsArray());
        Assert.True((await host.PostAsync(completionPath, completion))["replay"]!.GetValue<bool>());
        Assert.Equal(completed["revision"]!.GetValue<long>(), (await host.GetAsync(executionPath))["revision"]!.GetValue<long>());
        Assert.Equal("Accepted", (await host.GetAsync(executionPath + "/commands/complete"))["result"]!["status"]!.GetValue<string>());
        var late = await host.PostAsync(completionPath, new { requestId = "late", attemptId = work["attemptId"]!.GetValue<string>() }, HttpStatusCode.Conflict);
        Assert.False(late["pending"]!.GetValue<bool>());
        Assert.Equal("execution-terminal", late["result"]!["code"]!.GetValue<string>());
        Assert.True(JsonNode.DeepEquals(completed, await host.GetAsync(executionPath)));
        Assert.Equal("execution-terminal", (await host.GetAsync(executionPath + "/commands/late"))["result"]!["code"]!.GetValue<string>());
        await host.StopAsync();
        await using var database = new SqliteConnection($"Data Source={host.RuntimeDatabase};Mode=ReadOnly;Pooling=False");
        await database.OpenAsync();
        foreach (var table in new[] { "elsa_runtime_workflow_execution_state", "elsa_runtime_activity_execution_state", "elsa_runtime_bookmark_state" })
        {
            await using var command = database.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM \"{table}\"";
            Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
        }
        var completedSnapshot = await host.SnapshotAsync("completed.sqlite.gz");
        var evidenceDirectory = Environment.GetEnvironmentVariable("ELSA_BPMN_HOST_EVIDENCE_DIRECTORY");
        if (evidenceDirectory is not null)
        {
            var sourceRevision = Environment.GetEnvironmentVariable("ELSA_BPMN_HOST_SOURCE_REVISION");
            var binaries = host.DescribeBinaries(sourceRevision);
            var evidence = new { formatVersion = 1, sourceRevision, capturedAt = DateTimeOffset.UtcNow,
                profile = "foundation-direct-v1 / built-in single-node mailbox / EF SQLite / production Workbench authenticated HTTP",
                originalProcessId = previousProcess, restartedProcessId = restartedProcess, published, started, waiting, recovered, work, obsolete, accepted, completed, late,
                nativeWorkflowRows = 0, nativeActivityRows = 0, nativeBookmarkRows = 0, exactCompletionReplayPreservedRevision = true,
                binaries, snapshots = new[] { waitingSnapshot, completedSnapshot },
                limits = new[] { "Same-source process restart; no changed-binary or legacy-upgrade acceptance.", "Combined host has Design storage configured; runtime-only proof remains open.",
                    "Clustered model placement, service/timer/native bindings, Studio and full scenario matrix remain open.", "No concurrent source-retirement/admission proof." } };
            await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, "direct-process.json"), JsonSerializer.Serialize(evidence, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }) + Environment.NewLine);
        }
    }

}
