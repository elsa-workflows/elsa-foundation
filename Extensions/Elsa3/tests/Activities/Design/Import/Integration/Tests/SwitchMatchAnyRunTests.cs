using System.Net;
using System.Text.Json.Nodes;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa3.Activities.Design.Import.Integration.Tests.Support;
using Elsa3.Models;
using Xunit;

namespace Elsa3.Activities.Design.Import.Integration.Tests;

/// <summary>Real import, publication and execution of structured Elsa 3 MatchAny bodies (S-12).</summary>
public sealed class SwitchMatchAnyRunTests
{
    [Theory]
    [InlineData("MatchAny", true, true, "a,c,empty", "a,c")]
    [InlineData("MatchFirst", true, true, "a", "a")]
    [InlineData("MatchAny", false, true, "Default", "fallback")]
    [InlineData("MatchAny", false, false, "Default", "")]
    public async Task Converted_bodies_and_Default_keep_selection_and_one_Sequence_successor(
        string mode, bool matches, bool withDefault, string labels, string effects)
    {
        await using var host = await Elsa3ImportHost.StartAsync();
        var source = Source(mode, matches, withDefault);
        var workflow = await ImportAndPublish(host, source);
        var run = await host.RunAsync(workflow);
        Assert.Equal(WorkflowExecutionStatus.Completed, run.Workflow?.Status);
        Assert.Empty(run.Incidents);
        Assert.Equal(labels.Split(','), Assert.Single(run.Activities, item => item.Execution.ExecutableNodeId == "route").Completion!.OutcomeNames);
        var expected = effects.Split(',', StringSplitOptions.RemoveEmptyEntries).Append("after").Order(StringComparer.Ordinal);
        Assert.Equal(expected, run.ExecutedNodeIds.Where(id => id is "a" or "b" or "c" or "fallback" or "after").Order(StringComparer.Ordinal));
        Assert.Single(run.Activities, item => item.Execution.ExecutableNodeId == "after");
    }

    [Fact]
    public async Task A_waiting_converted_body_holds_the_Switch_and_Done_successor_until_resume()
    {
        await using var host = await Elsa3ImportHost.StartAsync(withScheduling: true);
        var source = Source("MatchAny", true, true);
        var cases = source["root"]!["activities"]![0]!["cases"]!.AsArray();
        cases[2]!["activity"] = Activity("c", "Elsa.Delay", ("timeSpan", Literal("00:00:05", "TimeSpan")));
        var workflow = await ImportAndPublish(host, source);
        var executionId = await host.ExecuteAsync(workflow);
        var waiting = await host.WaitForAsync(executionId, state => state.Activities.Any(item =>
            item.Execution.ExecutableNodeId == "c" && item.Status == ActivityExecutionStatus.Suspended));
        Assert.Contains(waiting.Activities, item => item.Execution.ExecutableNodeId == "a" && item.Status == ActivityExecutionStatus.Completed);
        Assert.DoesNotContain("after", waiting.ExecutedNodeIds);
        Assert.NotEqual(ActivityExecutionStatus.Completed, Assert.Single(waiting.Activities, item => item.Execution.ExecutableNodeId == "route").Status);
        var completed = await host.WaitForSettledAsync(executionId);
        Assert.Equal(WorkflowExecutionStatus.Completed, completed.Workflow?.Status);
        Assert.Empty(completed.Incidents);
        Assert.Single(completed.Activities, item => item.Execution.ExecutableNodeId == "a");
        Assert.Single(completed.Activities, item => item.Execution.ExecutableNodeId == "c");
        Assert.Single(completed.Activities, item => item.Execution.ExecutableNodeId == "after");
        Assert.Equal(new[] { "a", "c", "empty" }, Assert.Single(completed.Activities, item => item.Execution.ExecutableNodeId == "route").Completion!.OutcomeNames);
    }

    [Fact]
    public async Task Done_mapping_after_multiple_case_bodies_keeps_one_default_join_and_a_dead_path()
    {
        await using var host = await Elsa3ImportHost.StartAsync();
        var source = Source("MatchAny", true, true);
        var route = source["root"]!["activities"]![0]!.DeepClone();
        JsonObject Connection(string port, string target) => new()
        {
            ["source"] = new JsonObject { ["activity"] = "route", ["port"] = port },
            ["target"] = new JsonObject { ["activity"] = target, ["port"] = "In" }, ["vertices"] = new JsonArray()
        };
        source["root"] = Activity("root", "Elsa.Flowchart", ("activities", new JsonArray(route,
            Activity("joined", "Elsa.WriteLine", ("text", Literal("joined once"))),
            Activity("dead", "Elsa.WriteLine", ("text", Literal("never"))))),
            ("connections", new JsonArray(Connection("Done", "joined"), Connection("b", "dead"))));
        var run = await host.RunAsync(await ImportAndPublish(host, source));
        Assert.Equal(WorkflowExecutionStatus.Completed, run.Workflow?.Status);
        Assert.Empty(run.Incidents);
        Assert.Single(run.Activities, item => item.Execution.ExecutableNodeId == "joined");
        Assert.DoesNotContain("dead", run.ExecutedNodeIds);
        Assert.Single(run.Activities, item => item.Execution.ExecutableNodeId == "a");
        Assert.Single(run.Activities, item => item.Execution.ExecutableNodeId == "c");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Proven_safe_Break_ports_or_case_local_loops_leave_the_other_selected_wait_running(bool localLoop)
    {
        await using var host = await Elsa3ImportHost.StartAsync(withScheduling: true);
        var source = Source("MatchAny", true, true);
        var cases = source["root"]!["activities"]![0]!["cases"]!.AsArray();
        cases[0]!["activity"] = localLoop
            ? Activity("a", "Elsa.While", ("condition", Literal("true", "Boolean")), ("body", Activity("inner-break", "Elsa.Break")))
            : Activity("a", "Elsa.RunJavaScript", ("script", Literal("setOutcome('Ordinary');")),
                ("possibleOutcomes", Literal("[\"Break\",\"Ordinary\"]", "List<String>")));
        cases[2]!["activity"] = Activity("c", "Elsa.Delay", ("timeSpan", Literal("00:00:05", "TimeSpan")));
        var workflow = await ImportAndPublish(host, source);
        var executionId = await host.ExecuteAsync(workflow);
        var waiting = await host.WaitForAsync(executionId, state => state.Activities.Any(item => item.Execution.ExecutableNodeId == "c" && item.Status == ActivityExecutionStatus.Suspended)
            && state.Activities.Any(item => item.Execution.ExecutableNodeId == "a" && item.Status == ActivityExecutionStatus.Completed));
        Assert.DoesNotContain("after", waiting.ExecutedNodeIds);
        Assert.NotEqual(ActivityExecutionStatus.Completed, Assert.Single(waiting.Activities, item => item.Execution.ExecutableNodeId == "route").Status);
        var completed = await host.WaitForSettledAsync(executionId);
        Assert.Equal(WorkflowExecutionStatus.Completed, completed.Workflow?.Status);
        Assert.Empty(completed.Incidents);
        Assert.Single(completed.Activities, item => item.Execution.ExecutableNodeId == "after");
    }

    [Fact]
    public async Task Converted_Finish_in_a_selected_body_keeps_workflow_terminal_without_running_Sequence_successor()
    {
        await using var host = await Elsa3ImportHost.StartAsync();
        var source = Source("MatchAny", true, true);
        source["root"]!["activities"]![0]!["cases"]![0]!["activity"] = Activity("a", "Elsa.Finish");
        var run = await host.RunAsync(await ImportAndPublish(host, source));
        Assert.Equal(WorkflowExecutionStatus.Completed, run.Workflow?.Status);
        Assert.DoesNotContain("after", run.ExecutedNodeIds);
        Assert.NotEqual(ActivityExecutionStatus.Completed, Assert.Single(run.Activities, item => item.Execution.ExecutableNodeId == "route").Status);
    }

    [Theory]
    [InlineData("activity")]
    [InlineData("script")]
    [InlineData("computed")]
    [InlineData("done-set")]
    [InlineData("native-alias")]
    [InlineData("global-native-alias")]
    [InlineData("nested-done")]
    [InlineData("nested-label")]
    [InlineData("Break")]
    [InlineData("Done")]
    public async Task Incompatible_control_outcomes_are_refused_at_the_import_boundary(string form)
    {
        await using var host = await Elsa3ImportHost.StartAsync(withScheduling: true);
        var source = Source("MatchAny", true, true);
        source["root"]!["activities"]![0]!["cases"]![2]!["activity"] = Activity("c", "Elsa.Delay", ("timeSpan", Literal("00:00:05", "TimeSpan")));
        var first = source["root"]!["activities"]![0]!["cases"]![0]!;
        if (form is "Break" or "Done") first["label"] = form;
        else first["activity"] = form switch
        {
            "activity" => Activity("unsafe", "Elsa.Break"),
            "script" => Activity("unsafe", "Elsa.RunJavaScript", ("script", Literal("setOutcome('Break');"))),
            "computed" => Activity("unsafe", "Elsa.RunJavaScript", ("script", Literal("const name = 'Break'; setOutcome(name);")),
                ("possibleOutcomes", Literal("[\"Break\"]", "List<String>"))),
            "done-set" => Activity("unsafe", "Elsa.RunJavaScript", ("script", Literal("setOutcomes(['Done','Other']);"))),
            "native-alias" => Activity("unsafe", "Elsa.RunJavaScript", ("script", Literal("const emit = typeof complete === 'function' ? complete : (value => value); return emit(null, ['Break']);")),
                ("possibleOutcomes", Literal("[\"Break\"]", "List<String>"))),
            "global-native-alias" => Activity("unsafe", "Elsa.RunJavaScript", ("script", Literal("const key = 'complete'; const emit = typeof globalThis[key] === 'function' ? globalThis[key] : (value => value); return emit(null, ['Break']);")),
                ("possibleOutcomes", Literal("[\"Break\"]", "List<String>"))),
            "nested-done" => Activity("unsafe", "Elsa.Switch", ("mode", Literal("MatchAny")), ("cases", new JsonArray(
                new JsonObject { ["label"] = "Done", ["condition"] = Literal("true", "Boolean") },
                new JsonObject { ["label"] = "Other", ["condition"] = Literal("true", "Boolean") }))),
            _ => Activity("unsafe", "Elsa.Switch", ("cases", new JsonArray(new JsonObject
            {
                ["label"] = "Break", ["condition"] = Literal("true", "Boolean"),
                ["activity"] = Activity("nested", "Elsa.WriteLine", ("text", Literal("nested")))
            })))
        };
        var (items, apply) = await Import(host, source);
        Assert.Single(items);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, apply.StatusCode);
        Assert.Contains(Elsa3ActivityDiagnosticCodes.UnsupportedMode, apply.Body.ToJsonString(), StringComparison.Ordinal);
        Assert.Contains("cases[0]", apply.Body.ToJsonString(), StringComparison.Ordinal);
    }

    private static async Task<PublishedWorkflow> ImportAndPublish(Elsa3ImportHost host, JsonObject source)
    {
        var (items, apply) = await Import(host, source);
        Assert.Equal(HttpStatusCode.Created, apply.StatusCode);
        return await host.PublishWorkflowAsync(Elsa3ImportHost.Required(Assert.Single(items), "workflowVersionId"));
    }

    private static async Task<(IReadOnlyList<JsonNode> Items, ApplyResponse Apply)> Import(Elsa3ImportHost host, JsonObject source)
    {
        var handle = await host.UploadCollectionJsonAsync(new JsonArray(source).ToJsonString());
        var (plan, items) = await host.AnalyzeAsync(handle);
        return (items, await host.ApplyAsync(handle, plan, "import-switch-bodies", "switch-bodies-v1"));
    }

    private static JsonObject Source(string mode, bool matches, bool withDefault)
    {
        var source = JsonNode.Parse(Elsa3ImportHost.Collection("flow-switch-match-any"))!.AsArray()[0]!.DeepClone().AsObject();
        source["id"] = "switch-bodies-v1"; source["definitionId"] = "switch-bodies"; source["name"] = "switch-bodies";
        JsonObject Case(string label, bool condition, JsonObject? body = null) => new()
        {
            ["label"] = label, ["condition"] = Literal(condition ? "true" : "false", "Boolean"), ["activity"] = body
        };
        var route = Activity("route", "Elsa.Switch", ("mode", Literal(mode)), ("cases", new JsonArray(
            Case("a", matches, Activity("a", "Elsa.WriteLine", ("text", Literal("a")))),
            Case("b", false, Activity("b", "Elsa.WriteLine", ("text", Literal("never")))),
            Case("c", matches, Activity("c", "Elsa.WriteLine", ("text", Literal("c")))), Case("empty", matches))));
        if (withDefault) route["default"] = Activity("fallback", "Elsa.WriteLine", ("text", Literal("fallback")));
        source["root"] = Activity("root", "Elsa.Sequence", ("activities", new JsonArray(route,
            Activity("after", "Elsa.WriteLine", ("text", Literal("after"))))));
        return source;
    }

    private static JsonObject Literal(string value, string type = "String") => new()
    {
        ["typeName"] = type, ["expression"] = new JsonObject { ["type"] = "Literal", ["value"] = value }
    };

    private static JsonObject Activity(string id, string type, params (string Key, JsonNode Value)[] properties)
    {
        var node = new JsonObject { ["id"] = id, ["nodeId"] = id, ["name"] = id, ["type"] = type, ["version"] = 1,
            ["customProperties"] = new JsonObject(), ["metadata"] = new JsonObject() };
        foreach (var (key, value) in properties) node[key] = value;
        return node;
    }
}
