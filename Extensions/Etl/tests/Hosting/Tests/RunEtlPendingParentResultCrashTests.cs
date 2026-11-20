using System.Text.Json.Nodes;
using Xunit;

namespace Elsa.Etl.Hosting.Tests;

/// <summary>
/// A real native parent waiting on <c>RunEtl</c> in the production Workbench survives a process kill at the point where
/// the ETL result exists and its delivery lease is claimed but the parent has not been resumed, then continues exactly
/// once in the restarted process over the same SQLite files. This is the process-kill counterpart of the in-process
/// witnesses in <c>Elsa.Etl.Tests</c> and the ETL counterpart of the #3998 BPMN witness.
/// </summary>
public sealed class RunEtlPendingParentResultCrashTests
{
    private const string RunEtlType = "Elsa.Etl.Workflows.Runtime.Activities.RunEtl";
    private const string WriteLineType = "Elsa.Activities.Primitives.Activities.WriteLine";
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(90);

    [Theory]
    [InlineData("Root")]
    [InlineData("Sequence")]
    [InlineData("Flowchart")]
    public async Task A_real_native_RunEtl_parent_recovers_once_when_the_host_is_killed_after_the_result_before_consumption(string parent)
    {
        await using var fixture = await EtlWorkbenchFixture.StartAsync();
        var selection = await fixture.PublishPipelineAsync();
        // Armed before the parent starts: the hold is read only inside the result delivery, after the run is terminal
        // and its lease claimed, so arming early closes the window in which the worker could deliver first.
        await fixture.HoldClaimedResultDeliveryAsync();
        var execution = await StartNativeParentAsync(fixture, parent, selection);
        var nativePath = "/runtime/workflows/instances/" + execution;

        // The parent reaches its durable RunEtl wait; the Workbench's own ETL worker then admits and runs the pipeline.
        await WaitForAsync(async () => ActivityStatus(await fixture.GetAsync(nativePath), RunEtlType) == "Suspended",
            "The native parent did not reach its RunEtl wait.");
        // ETL REST serializes enums in camelCase, so a terminal run reads "succeeded".
        var runId = await WaitForAsync(async () =>
        {
            var runs = (await fixture.GetAsync("/etl/runs?limit=100"))["items"]!.AsArray();
            Assert.True(runs.Count <= 1, "The pipeline was admitted more than once before the kill: " + runs.ToJsonString());
            var run = runs.SingleOrDefault();
            return run?["status"]?.GetValue<string>() == "succeeded" ? run["runId"]!.GetValue<string>() : null;
        }, "The real RunEtl run did not reach its terminal result.");

        // Cut: the result is durable and its delivery lease is claimed by this process, which is now frozen before dispatch.
        await WaitForAsync(() => Task.FromResult(File.Exists(fixture.EngagedMarkerPath)), "The result delivery did not engage the test hold.");
        var beforeKill = await fixture.GetAsync(nativePath);
        Assert.Equal("Running", WorkflowStatus(beforeKill));
        Assert.Equal("Suspended", ActivityStatus(beforeKill, RunEtlType));
        Assert.Empty(beforeKill["incidents"]!.AsArray());

        var killedProcessId = fixture.Host.ProcessId;
        await fixture.CrashAndRestartAsync();
        var (killedId, exitCode) = Assert.Single(fixture.KilledHosts);
        Assert.Equal(killedProcessId, killedId);
        Assert.NotEqual(0, exitCode);
        Assert.NotEqual(killedProcessId, fixture.Host.ProcessId);

        // The dead owner's lease has lapsed by the time the restarted worker first polls; it reclaims the delivery and
        // resumes the parent exactly once.
        await WaitForAsync(async () => WorkflowStatus(await fixture.GetAsync(nativePath)) == "Completed",
            "The restarted host did not complete the native parent.");
        var completed = await fixture.GetAsync(nativePath);
        Assert.Empty(completed["incidents"]!.AsArray());
        var activities = completed["activities"]!.AsArray();
        var runEtl = Assert.Single(activities, item => item!["activityType"]!.GetValue<string>() == RunEtlType)!;
        Assert.Equal("Completed", runEtl["status"]!.GetValue<string>());
        if (parent != "Root")
            Assert.Equal("Completed", Assert.Single(activities, item => item!["activityType"]!.GetValue<string>() == WriteLineType)!["status"]!.GetValue<string>());
        Assert.Equal("succeeded", (await fixture.GetAsync("/etl/runs/" + runId))["status"]!.GetValue<string>());
        Assert.Single((await fixture.GetAsync("/etl/runs?limit=100"))["items"]!.AsArray());
    }

    private static async Task<string> StartNativeParentAsync(EtlWorkbenchFixture fixture, string parent, string selection)
    {
        var etl = new JsonObject
        {
            ["nodeId"] = "etl", ["activityVersionId"] = await fixture.ActivityVersionIdAsync(RunEtlType), ["outputs"] = new JsonArray(),
            ["inputs"] = new JsonArray(Literal("Pipeline", selection))
        };
        var after = new JsonObject
        {
            ["nodeId"] = "after", ["activityVersionId"] = await fixture.ActivityVersionIdAsync(WriteLineType), ["outputs"] = new JsonArray(),
            ["inputs"] = new JsonArray(Literal("text", "Recovered real ETL " + parent))
        };
        JsonObject root = parent switch
        {
            "Root" => etl,
            "Sequence" => new JsonObject
            {
                ["nodeId"] = "root", ["activityVersionId"] = await fixture.ActivityVersionIdAsync("Elsa.Activities.Sequence.Activities.Sequence"),
                ["inputs"] = new JsonArray(), ["outputs"] = new JsonArray(),
                ["structure"] = new JsonObject
                {
                    ["kind"] = "elsa.sequence.structure", ["schemaVersion"] = "1.0.0",
                    ["payload"] = new JsonObject { ["activities"] = new JsonArray(etl, after) }
                }
            },
            _ => new JsonObject
            {
                ["nodeId"] = "root", ["activityVersionId"] = await fixture.ActivityVersionIdAsync("Elsa.Activities.Flowchart.Activities.Flowchart"),
                ["inputs"] = new JsonArray(), ["outputs"] = new JsonArray(),
                ["structure"] = new JsonObject
                {
                    ["kind"] = "elsa.flowchart.structure", ["schemaVersion"] = "1.0.0",
                    ["payload"] = new JsonObject
                    {
                        ["startNodeId"] = "etl", ["activities"] = new JsonArray(etl, after),
                        ["connections"] = new JsonArray(new JsonObject
                        {
                            ["source"] = new JsonObject { ["nodeId"] = "etl", ["port"] = "Done" },
                            ["target"] = new JsonObject { ["nodeId"] = "after", ["port"] = "Done" }
                        })
                    }
                }
            }
        };
        var native = await fixture.PostAsync("/design/workflows/definitions/submit", new JsonObject
        {
            ["name"] = "Real ETL pending parent result crash " + parent,
            ["state"] = new JsonObject { ["variables"] = new JsonArray(), ["inputs"] = new JsonArray(), ["outputs"] = new JsonArray(), ["rootActivity"] = root }
        });
        var publication = await fixture.PostAsync($"/publishing/workflows/{native["version"]!["id"]!.GetValue<string>()}/publish", new JsonObject());
        var started = await fixture.PostAsync($"/runtime/workflows/executables/{publication["artifactId"]!.GetValue<string>()}/execute",
            new JsonObject { ["sourceReferenceId"] = publication["sourceReferenceId"]!.GetValue<string>() });
        return started["workflowExecutionId"]!.GetValue<string>();
    }

    private static JsonObject Literal(string referenceKey, string value) => new()
    {
        ["referenceKey"] = referenceKey, ["value"] = new JsonObject { ["value"] = value, ["expressionType"] = "Literal" }
    };

    private static string WorkflowStatus(JsonNode view) => view["instance"]!["status"]!.GetValue<string>();

    private static string? ActivityStatus(JsonNode view, string activityType) =>
        view["activities"]!.AsArray().SingleOrDefault(item => item!["activityType"]!.GetValue<string>() == activityType)?["status"]?.GetValue<string>();

    private static async Task WaitForAsync(Func<Task<bool>> condition, string message) =>
        await WaitForAsync(async () => await condition() ? "done" : null, message);

    private static async Task<string> WaitForAsync(Func<Task<string?>> probe, string message)
    {
        var deadline = DateTimeOffset.UtcNow + Deadline;
        while (true)
        {
            if (await probe() is { } value)
                return value;
            Assert.True(DateTimeOffset.UtcNow < deadline, message);
            await Task.Delay(200);
        }
    }
}
