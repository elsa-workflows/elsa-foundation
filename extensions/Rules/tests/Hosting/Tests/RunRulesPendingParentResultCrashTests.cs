using System.Text.Json.Nodes;
using Xunit;

namespace Elsa.Rules.Hosting.Tests;

/// <summary>
/// A real native parent waiting on <c>RunRules</c> in the production Workbench survives a process kill at the point where
/// the Rules terminal result exists and its delivery lease is claimed but the parent has not been resumed, then continues
/// exactly once in the restarted process over the same SQLite files. This is the process-kill counterpart of the
/// in-process witnesses in <c>Elsa.Rules.Tests</c> and the Rules counterpart of the #3998 BPMN witness; the Workbench runs
/// under the opt-in source-build Rules identity because a source build has no packaged deployment receipt.
/// </summary>
public sealed class RunRulesPendingParentResultCrashTests
{
    private const string RunRulesType = "Elsa.Rules.Workflows.Runtime.Activities.RunRules";
    private const string WriteLineType = "Elsa.Activities.Primitives.Activities.WriteLine";
    private const string Program =
        """{"formatVersion":1,"languageId":"elsa.rules.finite-positive","languageVersion":1,"vocabulary":["derived","seed"],"rules":[{"id":"derive","premises":["seed"],"conclusion":"derived"}]}""";
    private const int SucceededStatus = 1;
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(90);

    [Theory]
    [InlineData("Root")]
    [InlineData("Sequence")]
    [InlineData("Flowchart")]
    public async Task A_real_native_RunRules_parent_recovers_once_when_the_host_is_killed_after_the_result_before_consumption(string parent)
    {
        await using var fixture = await RulesWorkbenchFixture.StartAsync();
        // Armed before the parent starts: the hold is read only inside the terminal delivery, after the run is terminal
        // and its lease claimed, so arming early closes the window in which the worker could deliver first.
        await fixture.HoldClaimedDeliveryAsync();
        var execution = await StartNativeParentAsync(fixture, parent);
        var nativePath = "/runtime/workflows/instances/" + execution;

        // The parent reaches its durable RunRules wait; the Workbench's own Rules worker then runs the rounds to a terminal.
        await WaitForAsync(async () => ActivityStatus(await fixture.GetAsync(nativePath), RunRulesType) == "Suspended",
            "The native parent did not reach its RunRules wait.");

        // Cut: the terminal result is durable and its delivery lease is claimed by this process, which is now frozen before dispatch.
        await WaitForAsync(() => Task.FromResult(File.Exists(fixture.EngagedMarkerPath)), "The terminal delivery did not engage the test hold.");
        var run = Assert.Single(fixture.ReadRuns());
        Assert.Equal(SucceededStatus, run.Status);
        Assert.True(run.HasTerminal);
        Assert.NotNull(run.DeliveryOwner);
        Assert.False(run.DeliveryAcknowledged);
        var beforeKill = await fixture.GetAsync(nativePath);
        Assert.Equal("Running", WorkflowStatus(beforeKill));
        Assert.Equal("Suspended", ActivityStatus(beforeKill, RunRulesType));
        Assert.Empty(beforeKill["incidents"]!.AsArray());

        var killedProcessId = fixture.Host.ProcessId;
        await fixture.CrashAndRestartAsync();
        var (killedId, exitCode) = Assert.Single(fixture.KilledHosts);
        Assert.Equal(killedProcessId, killedId);
        Assert.NotEqual(0, exitCode);
        Assert.NotEqual(killedProcessId, fixture.Host.ProcessId);

        // The dead owner's lease (database time) lapses within the restart; the restarted worker reclaims the delivery and
        // resumes the parent exactly once.
        await WaitForAsync(async () => WorkflowStatus(await fixture.GetAsync(nativePath)) == "Completed",
            "The restarted host did not complete the native parent.");
        var completed = await fixture.GetAsync(nativePath);
        Assert.Empty(completed["incidents"]!.AsArray());
        var activities = completed["activities"]!.AsArray();
        var runRules = Assert.Single(activities, item => item!["activityType"]!.GetValue<string>() == RunRulesType)!;
        Assert.Equal("Completed", runRules["status"]!.GetValue<string>());
        if (parent != "Root")
        {
            Assert.Equal("Completed", Assert.Single(activities, item => item!["activityType"]!.GetValue<string>() == WriteLineType)!["status"]!.GetValue<string>());
            // The line after the wait ran once, and only in the restarted process.
            var line = "Recovered real Rules " + parent;
            // The console pipe is read asynchronously, so the line may trail the completion by a moment.
            await WaitForAsync(() => Task.FromResult(CountOf(fixture.Host.Output, line) == 1), "The WriteLine text did not appear exactly once in the restarted process's output.");
            Assert.Equal(0, CountOf(Assert.Single(fixture.KilledHostOutputs), line));
        }
        await WaitForAsync(() => Task.FromResult(fixture.ReadRuns().Single().DeliveryAcknowledged), "The delivery was not acknowledged after the resume.");
        var settled = Assert.Single(fixture.ReadRuns());
        Assert.Equal(SucceededStatus, settled.Status);
        Assert.True(settled.DeliveryAcknowledged);
    }

    private static async Task<string> StartNativeParentAsync(RulesWorkbenchFixture fixture, string parent)
    {
        var rules = new JsonObject
        {
            ["nodeId"] = "rules", ["activityVersionId"] = await fixture.ActivityVersionIdAsync(RunRulesType), ["outputs"] = new JsonArray(),
            ["inputs"] = new JsonArray(Literal("Program", Program), Literal("InitialFacts", new JsonArray("seed")), Literal("MaxRounds", 4))
        };
        var after = new JsonObject
        {
            ["nodeId"] = "after", ["activityVersionId"] = await fixture.ActivityVersionIdAsync(WriteLineType), ["outputs"] = new JsonArray(),
            ["inputs"] = new JsonArray(Literal("text", "Recovered real Rules " + parent))
        };
        JsonObject root = parent switch
        {
            "Root" => rules,
            "Sequence" => new JsonObject
            {
                ["nodeId"] = "root", ["activityVersionId"] = await fixture.ActivityVersionIdAsync("Elsa.Activities.Sequence.Activities.Sequence"),
                ["inputs"] = new JsonArray(), ["outputs"] = new JsonArray(),
                ["structure"] = new JsonObject
                {
                    ["kind"] = "elsa.sequence.structure", ["schemaVersion"] = "1.0.0",
                    ["payload"] = new JsonObject { ["activities"] = new JsonArray(rules, after) }
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
                        ["startNodeId"] = "rules", ["activities"] = new JsonArray(rules, after),
                        ["connections"] = new JsonArray(new JsonObject
                        {
                            ["source"] = new JsonObject { ["nodeId"] = "rules", ["port"] = "Done" },
                            ["target"] = new JsonObject { ["nodeId"] = "after", ["port"] = "Done" }
                        })
                    }
                }
            }
        };
        var native = await fixture.PostAsync("/design/workflows/definitions/submit", new JsonObject
        {
            ["name"] = "Real Rules pending parent result crash " + parent,
            ["state"] = new JsonObject { ["variables"] = new JsonArray(), ["inputs"] = new JsonArray(), ["outputs"] = new JsonArray(), ["rootActivity"] = root }
        });
        var publication = await fixture.PostAsync($"/publishing/workflows/{native["version"]!["id"]!.GetValue<string>()}/publish", new JsonObject());
        var started = await fixture.PostAsync($"/runtime/workflows/executables/{publication["artifactId"]!.GetValue<string>()}/execute",
            new JsonObject { ["sourceReferenceId"] = publication["sourceReferenceId"]!.GetValue<string>() });
        return started["workflowExecutionId"]!.GetValue<string>();
    }

    private static JsonObject Literal(string referenceKey, JsonNode? value) => new()
    {
        ["referenceKey"] = referenceKey, ["value"] = new JsonObject { ["value"] = value, ["expressionType"] = "Literal" }
    };

    private static int CountOf(string text, string value)
    {
        var count = 0;
        for (var index = text.IndexOf(value, StringComparison.Ordinal); index >= 0; index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
            count++;
        return count;
    }

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
