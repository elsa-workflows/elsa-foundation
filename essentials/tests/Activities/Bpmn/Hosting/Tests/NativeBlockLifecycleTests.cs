using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Xunit;

namespace Elsa.Activities.Bpmn.Hosting.Tests;

/// <summary>
/// Waits, restart, cancellation and loop control inside a named block on the owned Workbench. A named block is a
/// native Sequence node with its own presentation; extraction of steps that share no results produces exactly this
/// node, so the documents use that shape directly. Everything goes through the normal authoring, publication,
/// runtime, stimulus and alteration APIs.
/// </summary>
public sealed class NativeBlockLifecycleTests
{
    [Fact]
    public async Task A_named_block_keeps_its_wait_across_restart_resumes_once_cancels_cleanly_and_breaks_its_nearest_loop()
    {
        await using var host = await OwnedWorkbench.StartAsync();
        await host.LoginAsync();
        var versions = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, type) in new[]
                 {
                     ("sequence", "Elsa.Activities.Sequence.Activities.Sequence"), ("event", "Elsa.Activities.Primitives.Activities.Event"),
                     ("while", "Elsa.Activities.While.Activities.While"), ("break", "Elsa.Activities.Primitives.Activities.Break")
                 })
        {
            var page = await host.GetAsync("/design/activities/definitions?search=" + Uri.EscapeDataString(type));
            versions[key] = Assert.Single(page["items"]!.AsArray(), item => item!["definition"]!["activityTypeKey"]!.GetValue<string>() == type)!
                ["definition"]!["headVersionId"]!.GetValue<string>();
        }

        // 1. A wait inside the block survives a process restart in both persistence modes and resumes exactly once.
        var held = new List<(string Mode, string Event, string Execution, JsonNode View)>();
        foreach (var mode in new[] { "Balanced", "Durable" })
        {
            var eventName = "named-block-wait-" + Guid.NewGuid().ToString("N");
            var root = Sequence("root",
                Set("before", "message", "retained"),
                Sequence("block", Wait("wait", eventName), Set("resumed", "after", "resumed")),
                Output("expose-message", "message", Variable("message")),
                Output("expose-after", "after", Variable("after")));
            var execution = await Start(root, mode);
            held.Add((mode, eventName, execution, await AwaitView(execution, view => Status(view, "wait") == "Suspended")));
        }
        await host.StopAsync();
        await host.RestartAsync();
        await host.LoginAsync();
        foreach (var (mode, eventName, execution, before) in held)
        {
            var after = await host.GetAsync("/runtime/workflows/instances/" + execution);
            Assert.Equal(Occurrences(before), Occurrences(after));
            var delivery = await Deliver(eventName, execution);
            Assert.Equal((0, 1), (delivery["startedCount"]!.GetValue<int>(), delivery["resumedCount"]!.GetValue<int>()));
            var completed = await AwaitView(execution, view => view["instance"]!["status"]!.GetValue<string>() == "Completed");
            Assert.Empty(completed["incidents"]!.AsArray());
            Assert.Equal("retained", Preview(completed, "message"));
            Assert.Equal("resumed", Preview(completed, "after"));
            foreach (var node in new[] { "before", "block", "wait", "resumed" })
                Assert.True(Count(completed, node) == 1, $"{mode}: {node} ran {Count(completed, node)} times");
        }

        // 2. Cancelling a run held inside the block cancels the exact held occurrences; a late delivery cannot continue it.
        var cancelEvent = "named-block-cancel-" + Guid.NewGuid().ToString("N");
        var cancelled = await Start(Sequence("root", Sequence("block", Wait("wait", cancelEvent)), Output("after", "after", Literal("must not run"))), "Balanced");
        var waiting = await AwaitView(cancelled, view => Status(view, "wait") == "Suspended");
        var plan = await host.PostAsync("/runtime/workflows/alteration-plans", new
        {
            target = new { executionIds = new[] { cancelled } },
            alterations = new[] { new { kind = "CancelWorkflow", schemaVersion = 1, payload = new { } } }
        }, idempotencyKey: "named-block-cancel-" + Guid.NewGuid().ToString("N"));
        var planId = plan["planId"]!.GetValue<string>();
        var terminal = await Poll(() => host.GetAsync("/runtime/workflows/alteration-plans/" + planId),
            view => view["status"]!.GetValue<string>() is "Completed" or "CompletedWithFailures" or "Failed" or "Cancelled");
        Assert.Equal("Completed", terminal["status"]!.GetValue<string>());
        var afterCancel = await host.GetAsync("/runtime/workflows/instances/" + cancelled);
        Assert.Equal("Cancelled", afterCancel["instance"]!["status"]!.GetValue<string>());
        Assert.Equal(Occurrences(waiting).Select(item => item.Id), Occurrences(afterCancel).Select(item => item.Id));
        Assert.All(afterCancel["activities"]!.AsArray(), node => Assert.Equal("Cancelled", node!["status"]!.GetValue<string>()));
        var late = await host.PostAsync("/runtime/workflows/stimuli", Stimulus(cancelEvent, cancelled));
        Assert.Equal((0, 0), (late["startedCount"]!.GetValue<int>(), late["resumedCount"]!.GetValue<int>()));
        Assert.Equal(0, Count(await host.GetAsync("/runtime/workflows/instances/" + cancelled), "after"));

        // 3. Break inside a block ends only its nearest loop. Both loop conditions are always true: the inner Break
        // must end the inner loop and let the outer body continue to its own Break; if it ended the outer loop
        // instead, "after-inner" would never run.
        JsonObject WhileTrue(string id, JsonNode body) => new()
        {
            ["nodeId"] = id, ["activityVersionId"] = versions["while"], ["outputs"] = new JsonArray(),
            ["inputs"] = new JsonArray(new JsonObject { ["referenceKey"] = "Condition", ["value"] = Literal(true) }),
            ["structure"] = new JsonObject { ["kind"] = "elsa.while.structure", ["schemaVersion"] = "1.0.0", ["payload"] = new JsonObject { ["body"] = body } }
        };
        var inner = WhileTrue("inner", Sequence("inner-body", Sequence("block", Set("count", "counter", 1), Node("break", versions["break"]))));
        var outer = WhileTrue("outer", Sequence("outer-body", inner, Set("after-inner", "after", "inner loop ended"), Node("outer-break", versions["break"])));
        var broken = await Start(Sequence("root", outer, Output("expose-counter", "counter", Variable("counter")), Output("expose-after", "after", Variable("after"))),
            "Balanced", ("counter", "Int32", 0));
        var ended = await AwaitView(broken, view => view["instance"]!["status"]!.GetValue<string>() == "Completed");
        Assert.Empty(ended["incidents"]!.AsArray());
        foreach (var node in new[] { "count", "break", "after-inner", "outer-break", "expose-counter" })
            Assert.True(Count(ended, node) == 1, $"{node} ran {Count(ended, node)} times");
        Assert.Equal(1, ended["outputs"]!["counter"]!["value"]!["value"]!.GetValue<int>());
        Assert.Equal("inner loop ended", Preview(ended, "after"));

        async Task<string> Start(JsonNode root, string mode, params (string Key, string Alias, object Default)[] extra)
        {
            var variables = new JsonArray(VariableDeclaration("message", "String", ""), VariableDeclaration("after", "String", ""));
            foreach (var (key, alias, value) in extra) variables.Add(VariableDeclaration(key, alias, value));
            var source = await host.PostAsync("/design/workflows/definitions/submit", new
            {
                name = "Named block lifecycle " + Guid.NewGuid().ToString("N"),
                state = new { rootActivity = root, inputs = Array.Empty<object>(), outputs = Array.Empty<object>(), variables, strategyOptions = new { persistenceMode = mode } }
            });
            var published = await host.PostAsync($"/publishing/workflows/{source["version"]!["id"]!.GetValue<string>()}/publish", new { });
            var started = await host.PostAsync($"/runtime/workflows/executables/{published["artifactId"]!.GetValue<string>()}/execute",
                new { sourceReferenceId = published["sourceReferenceId"]!.GetValue<string>() });
            return started["workflowExecutionId"]!.GetValue<string>();
        }
        async Task<JsonNode> AwaitView(string execution, Func<JsonNode, bool> done) =>
            await Poll(() => host.GetAsync("/runtime/workflows/instances/" + execution), view =>
            {
                var status = view["instance"]!["status"]!.GetValue<string>();
                Assert.False(status is "Faulted" || (status == "Cancelled" && !done(view)), view.ToJsonString());
                return done(view);
            });
        async Task<JsonNode> Deliver(string eventName, string execution) => await host.PostAsync("/runtime/workflows/stimuli", Stimulus(eventName, execution));
        JsonNode Sequence(string id, params JsonNode[] children) => new JsonObject
        {
            ["nodeId"] = id, ["activityVersionId"] = versions["sequence"], ["inputs"] = new JsonArray(), ["outputs"] = new JsonArray(),
            ["structure"] = new JsonObject { ["kind"] = "elsa.sequence.structure", ["schemaVersion"] = "1.0.0", ["payload"] = new JsonObject { ["activities"] = new JsonArray(children) } }
        };
        JsonNode Wait(string id, string eventName) => Node(id, versions["event"],
            Input("EventName", Literal(eventName)), Input("CanStartWorkflow", Literal(false)),
            Input("CorrelationId", new JsonObject
            {
                ["expressionType"] = "JavaScript",
                ["value"] = new JsonObject
                {
                    ["language"] = "JavaScript", ["source"] = "args.instance", ["capabilityProfile"] = "binding-pure-v1", ["options"] = new JsonObject(),
                    ["resultType"] = new JsonObject { ["alias"] = "String", ["collectionKind"] = "single" },
                    ["parameters"] = new JsonObject { ["instance"] = new JsonObject { ["kind"] = "WorkflowInstance", ["member"] = "instanceId" } }
                }
            }));
    }

    private static JsonObject Node(string id, string version, params JsonNode[] inputs) =>
        new() { ["nodeId"] = id, ["activityVersionId"] = version, ["inputs"] = new JsonArray(inputs), ["outputs"] = new JsonArray() };

    private static JsonObject Input(string key, JsonNode value) => new() { ["referenceKey"] = key, ["value"] = value };
    private static JsonObject Literal(object value) => new() { ["expressionType"] = "Literal", ["value"] = JsonValue.Create(value) };
    private static JsonObject Variable(string key) => new() { ["expressionType"] = "Variable", ["value"] = new JsonObject { ["referenceKey"] = key } };

    private static JsonObject Set(string id, string variable, object value)
    {
        var node = Node(id, "elsa.intrinsic.set@1", Input("value", Literal(value)));
        node["intrinsic"] = new JsonObject
        {
            ["kind"] = "set", ["valueType"] = new JsonObject { ["alias"] = "Elsa.Any", ["collectionKind"] = "single" },
            ["variable"] = new JsonObject { ["referenceKey"] = variable }
        };
        return node;
    }

    private static JsonObject Output(string id, string name, JsonNode value)
    {
        var node = Node(id, "elsa.intrinsic.set-output@1", Input("name", Literal(name)), Input("value", value));
        node["intrinsic"] = new JsonObject { ["kind"] = "setOutput", ["valueType"] = new JsonObject { ["alias"] = "Elsa.Any", ["collectionKind"] = "single" } };
        return node;
    }

    private static JsonObject VariableDeclaration(string key, string alias, object value) => new()
    {
        ["referenceKey"] = key, ["name"] = key, ["storageDriverType"] = null,
        ["type"] = new JsonObject { ["alias"] = alias, ["collectionKind"] = "single" }, ["default"] = Literal(value)
    };

    private static object Stimulus(string eventName, string execution) => new
    {
        stimulusType = "Event", stimulusHash = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(eventName))),
        mode = "ResumeOnly", correlationId = execution, input = new { eventName }
    };

    private static string? Status(JsonNode view, string node) =>
        view["activities"]!.AsArray().FirstOrDefault(item => item!["executableNodeId"]!.GetValue<string>() == node)?["status"]?.GetValue<string>();

    private static int Count(JsonNode view, string node) =>
        view["activities"]!.AsArray().Count(item => item!["executableNodeId"]!.GetValue<string>() == node);

    private static IReadOnlyList<(string Id, string Status)> Occurrences(JsonNode view) =>
        view["activities"]!.AsArray().Select(item => (item!["activityExecutionId"]!.GetValue<string>(), item["status"]!.GetValue<string>())).ToArray();

    private static string Preview(JsonNode view, string output) => view["outputs"]![output]!["value"]!["preview"]!.GetValue<string>();

    private static async Task<JsonNode> Poll(Func<Task<JsonNode>> read, Func<JsonNode, bool> done)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(90);
        while (true)
        {
            var value = await read();
            if (done(value)) return value;
            Assert.True(DateTimeOffset.UtcNow < deadline, value.ToJsonString());
            await Task.Delay(200);
        }
    }
}
