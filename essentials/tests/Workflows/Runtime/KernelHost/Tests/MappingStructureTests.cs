using System.Text.Json.Nodes;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;
using static Elsa.Workflows.Runtime.KernelHost.Tests.Compiled;
using Native = Kernel.Native;

namespace Elsa.Workflows.Runtime.KernelHost.Tests;

/// <summary>Each compiled structure onto its Native shape, and the workflow's identity.</summary>
public sealed class MappingStructureTests
{
    [Fact]
    public void The_workflow_is_the_artifact_under_its_definition_with_its_variables()
    {
        var workflow = NativeExecutableMapper.Map(Executable(Sequence("root", WriteLine("w", Literal("text", "hi"))),
            Variable("v-order", "order", new { id = 1 }), Variable("v-count", "count")));

        Assert.Equal(ArtifactId, workflow.Id);
        Assert.Equal(DefinitionId, workflow.Definition);
        Assert.Equal(1, workflow.Variables["order"]!["id"]!.GetValue<int>());
        Assert.True(workflow.Variables.ContainsKey("count"));
        Assert.Null(workflow.Variables["count"]);
    }

    [Fact]
    public void A_sequence_keeps_its_structure_order()
    {
        var a = WriteLine("a", Literal("text", "a"));
        var b = WriteLine("b", Literal("text", "b"));
        var c = WriteLine("c", Literal("text", "c"));
        // The slot carries them in another order; the structure decides.
        var root = Node("root", Types.Sequence, structure: Structure("elsa.sequence.structure", new { activities = new[] { "b", "c", "a" } }),
            slots: [Slot("Sequence.Activities", a, b, c)]);

        var workflow = NativeExecutableMapper.Map(Executable(root));

        Assert.Equal(typeof(Native.Sequence), workflow.Root.Type);
        Assert.Equal(["b", "c", "a"], workflow.Root.Children.Select(child => child.Id));
        Assert.Equal(typeof(Native.WriteLine), workflow.Nodes["a"].Type);
        Assert.Equal("a", Assert.IsType<Native.LiteralBinding>(workflow.Nodes["a"].Inputs["Text"]).Value!.GetValue<string>());
    }

    [Fact]
    public void An_if_fills_its_then_and_else_slots()
    {
        var root = Node("if", Types.If, [Literal("Condition", true, "Boolean")], Structure("elsa.if.structure", new { then = "t", @else = "e" }),
            [Slot("If.Then", WriteLine("t", Literal("text", "yes"))), Slot("If.Else", WriteLine("e", Literal("text", "no")))]);

        var workflow = NativeExecutableMapper.Map(Executable(root));

        Assert.Equal(typeof(Native.If), workflow.Root.Type);
        Assert.Equal("Then", workflow.Nodes["t"].Slot);
        Assert.Equal("Else", workflow.Nodes["e"].Slot);
        Assert.True(Assert.IsType<Native.LiteralBinding>(workflow.Root.Inputs["Condition"]).Value!.GetValue<bool>());
    }

    [Fact]
    public void A_switch_maps_its_cases_with_their_condition_bindings_and_its_default()
    {
        var structure = Structure("elsa.switch.structure", new
        {
            cases = new object[]
            {
                new { match = "big", activity = "big-branch", conditionInputKey = "case:big" },
                new { match = "small", activity = (string?)null },
            },
            @default = "other",
        }, "2.0.0");
        var root = Node("switch", Types.Switch,
            [Literal("Value", "small", "String"), VariableRead("case:big", "v-big")], structure,
            [Slot("Switch.Case[big]", WriteLine("big-branch", Literal("text", "big"))), Slot("Switch.Default", WriteLine("other", Literal("text", "other")))]);

        var workflow = NativeExecutableMapper.Map(Executable(root, Variable("v-big", "isBig", false, "Boolean")));

        Assert.Equal(["big", "small"], workflow.Root.Cases.Select(c => c.Label));
        Assert.Equal(new Native.VariableBinding("isBig"), workflow.Root.Cases[0].Condition);
        Assert.Null(workflow.Root.Cases[1].Condition);
        Assert.Equal(Native.Switch.CaseSlot("big"), workflow.Nodes["big-branch"].Slot);
        Assert.Equal(Native.Switch.DefaultSlot, workflow.Nodes["other"].Slot);
        // The case condition is the case's, not an input of the Switch.
        Assert.Equal(["Value"], workflow.Root.Inputs.Keys);
    }

    [Theory]
    [InlineData(Types.For, "For", "elsa.for.structure", typeof(Native.For))]
    [InlineData(Types.ForEach, "ForEach", "elsa.foreach.structure", typeof(Native.ForEach))]
    [InlineData(Types.While, "While", "elsa.while.structure", typeof(Native.While))]
    [InlineData(Types.Do, "Do", "elsa.do.structure", typeof(Native.Do))]
    public void A_loop_runs_its_body(string type, string name, string kind, Type native)
    {
        var root = Node("loop", type, structure: Structure(kind, new { body = "body" }), slots: [Slot($"{name}.Body", WriteLine("body", Literal("text", "pass")))]);

        var workflow = NativeExecutableMapper.Map(Executable(root));

        Assert.Equal(native, workflow.Root.Type);
        Assert.Equal("body", Assert.Single(workflow.Root.Children).Id);
    }

    [Fact]
    public void A_for_each_reads_its_collection_as_its_items()
    {
        var root = Node("loop", Types.ForEach, [Literal("Collection", new[] { 1, 2 }), Literal("ExposeIndex", true, "Boolean")],
            Structure("elsa.foreach.structure", new { body = "body" }), [Slot("ForEach.Body", WriteLine("body", Literal("text", "x")))]);

        var workflow = NativeExecutableMapper.Map(Executable(root));

        Assert.Equal(["Items"], workflow.Root.Inputs.Keys);
    }

    [Fact]
    public void A_parallel_forks_its_branches_with_its_threshold()
    {
        var root = Node("par", Types.Parallel, structure: Structure("elsa.parallel.structure", new
            {
                branches = new[] { new { name = "left", activity = "l" }, new { name = "right", activity = "r" } },
                threshold = 1,
            }),
            slots: [Slot("Parallel.Branch[left]", WriteLine("l", Literal("text", "l"))), Slot("Parallel.Branch[right]", WriteLine("r", Literal("text", "r")))]);

        var workflow = NativeExecutableMapper.Map(Executable(root));

        Assert.Equal(["l", "r"], workflow.Root.Children.Select(c => c.Id));
        Assert.Equal(1, Assert.IsType<Native.LiteralBinding>(workflow.Root.Inputs["Threshold"]).Value!.GetValue<int>());
    }

    [Fact]
    public void A_flowchart_maps_its_connections_its_start_node_and_its_gateways()
    {
        var structure = Structure("elsa.flowchart.structure", new
        {
            connections = new object[]
            {
                new { source = new { nodeId = "start", port = "Done" }, target = new { nodeId = "check" } },
                new { source = new { nodeId = "check", port = "True" }, target = new { nodeId = "join" } },
                new { source = new { nodeId = "check", port = "False" }, target = new { nodeId = "join" } },
            },
            startNodeId = "start",
            nodeMetadata = new Dictionary<string, object> { ["join"] = new { policyKind = "merge" }, ["check"] = new { policyKind = "decision" } },
            connectionMetadata = new Dictionary<string, object>(),
            variables = Array.Empty<object>(),
        });
        // The start node is last in the slot; Native starts at the first child nothing leads to.
        var root = Node("flow", Types.Flowchart, structure: structure, slots:
        [
            Slot("Flowchart.Activities",
                Node("check", Types.If, [Literal("Condition", true, "Boolean")], Structure("elsa.if.structure", new { })),
                WriteLine("join", Literal("text", "joined")),
                WriteLine("start", Literal("text", "go"))),
        ]);

        var workflow = NativeExecutableMapper.Map(Executable(root));

        Assert.Equal(["start", "check", "join"], workflow.Root.Children.Select(c => c.Id));
        Assert.Equal(
            [new Native.Connection("start", "check"), new Native.Connection("check", "join", "True"), new Native.Connection("check", "join", "False")],
            workflow.Root.Connections);
        Assert.Equal(Native.Gateway.Merge, workflow.Root.Gateways["join"]);
        Assert.Equal(Native.Gateway.Decision, workflow.Root.Gateways["check"]);
    }

    [Fact]
    public void A_graph_activity_descriptor_becomes_a_graph_boundary_whose_body_sees_only_it()
    {
        var workflow = NativeExecutableMapper.Map(Executable(Sequence("root", Graph("approve", out _)), Variable("v-out", "out")));
        var node = workflow.Nodes["approve"];
        var graph = node.Graph!;

        Assert.Equal(typeof(Native.GraphActivity), node.Type);
        var input = Assert.Single(graph.Inputs);
        Assert.Equal(("amount", true), (input.Name, input.Required));
        Assert.Equal(5, Assert.IsType<Native.LiteralBinding>(input.Default).Value!.GetValue<int>());
        var variable = Assert.Single(graph.Variables);
        Assert.Equal("total", variable.Name);
        Assert.Equal(new Native.VariableBinding("amount"), variable.Value);
        Assert.Equal(["result", "echo"], graph.Outputs.Select(o => o.Name));
        Assert.True(graph.Outputs[0].Required);
        Assert.Equal(new Native.VariableBinding("total"), graph.Outputs[1].Value);
        Assert.Equal("Approved", graph.OutcomeMappings["Done"]);
        Assert.Equal(7, Assert.IsType<Native.LiteralBinding>(node.Inputs["amount"]).Value!.GetValue<int>());
        // In the body a request read names the boundary's input, and a workflow-scope variable its own variable.
        Assert.Equal(new Native.VariableBinding("amount"), workflow.Nodes["body-set"].Inputs["Value"]);
        Assert.Equal("total", Assert.IsType<Native.LiteralBinding>(workflow.Nodes["body-set"].Inputs["Name"]).Value!.GetValue<string>());
        Assert.Equal(new Native.VariableBinding("total"), workflow.Nodes["body-output"].Inputs["Value"]);
        Assert.Equal("result", node.Captures["out"]);
    }

    /// <summary>A placed reusable activity: its boundary declares an input, a variable, two outputs and an outcome mapping.</summary>
    internal static ExecutableNode Graph(string id, out string body)
    {
        body = "body";
        var descriptor = new
        {
            definitionId = "approve-def", definitionVersionId = "approve-v1", version = "1", templateHash = "sha256:approve", entryNodeId = "body",
            inputs = new[] { new { referenceKey = "amount", type = new { alias = "Int32" }, storageDriverKey = "json", @default = new { syntax = "Literal", value = (object)5 } } },
            variables = new[] { new { referenceKey = "v-total", name = "total", type = new { alias = "Int32" }, storageDriverKey = "json", initialValue = new { syntax = "Input", value = (object)new { referenceKey = "amount" } } } },
            outputs = new[] { new { referenceKey = "result", type = new { alias = "Int32" }, storageDriverKey = "json" }, new { referenceKey = "echo", type = new { alias = "Int32" }, storageDriverKey = "json" } },
            outputMappings = new[] { new { outputReferenceKey = "echo", source = new { syntax = "Variable", value = (object)new { referenceKey = "v-total" } } } },
            requiredInputReferenceKeys = new[] { "amount" },
            requiredOutputReferenceKeys = new[] { "result" },
            outcomeMappings = new[] { new { sourceOutcomeName = "Done", boundaryOutcomeName = "Approved" } },
            completeOutcomes = Array.Empty<object>(),
        };
        var bodyNode = Sequence("body",
            Set("body-set", "v-total", Request("value", "amount")),
            SetOutput("body-output", "result", VariableRead("value", "v-total")));
        return Node(id, "Acme.Approve", [Literal("amount", 7, "Int32")], slots: [Slot("activity-graph", bodyNode)],
            captures: [Capture("result", "out")], descriptorType: "elsa.graph-activity", descriptor: descriptor);
    }

    [Fact]
    public void Intrinsics_map_by_their_kind()
    {
        var root = Sequence("root",
            Set("set", "v-x", Literal("value", 2, "Int32")),
            SetOutput("output", "total", VariableRead("value", "v-x")),
            Intrinsic("correlate", WorkflowIntrinsicKind.SetCorrelationId, null, Request(WorkflowIntrinsicInputKeys.Value, "orderId")),
            Intrinsic("name", WorkflowIntrinsicKind.SetInstanceName, null, Literal(WorkflowIntrinsicInputKeys.Value, "Order 42", "String")),
            Intrinsic("finish", WorkflowIntrinsicKind.Finish, null, Literal(WorkflowIntrinsicInputKeys.Outcome, "Approved", "String")));

        var workflow = NativeExecutableMapper.Map(Executable(root, Variable("v-x", "x")));

        Assert.Equal(typeof(Native.SetVariable), workflow.Nodes["set"].Type);
        Assert.Equal("x", ((Native.LiteralBinding)workflow.Nodes["set"].Inputs["Name"]).Value!.GetValue<string>());
        Assert.Equal(typeof(Native.SetOutput), workflow.Nodes["output"].Type);
        Assert.Equal(new Native.VariableBinding("x"), workflow.Nodes["output"].Inputs["Value"]);
        Assert.Equal(typeof(Native.SetCorrelationId), workflow.Nodes["correlate"].Type);
        Assert.Equal(new Native.VariableBinding("input", "orderId"), workflow.Nodes["correlate"].Inputs["Value"]);
        Assert.Equal(typeof(Native.SetInstanceName), workflow.Nodes["name"].Type);
        Assert.Equal(typeof(Native.Finish), workflow.Nodes["finish"].Type);
        Assert.Equal("Approved", ((Native.LiteralBinding)workflow.Nodes["finish"].Inputs["Outcome"]).Value!.GetValue<string>());
    }

    [Fact]
    public void Output_captures_become_node_captures_at_the_native_result_path()
    {
        var root = Sequence("root",
            Node("script", Types.RunJavaScript, [Literal("script", "return 1;", "String")], captures: [Capture("result", "answer")]),
            Node("wait", Types.Event, [Literal("EventName", "paid", "String")], captures: [Capture("Payload", "payment")]));

        var workflow = NativeExecutableMapper.Map(Executable(root, Variable("v-a", "answer"), Variable("v-p", "payment")));

        Assert.Equal("Value", workflow.Nodes["script"].Captures["answer"]);
        Assert.Equal("Payload", workflow.Nodes["wait"].Captures["payment"]);
    }

    [Fact]
    public void An_http_endpoint_takes_its_one_method_and_old_defaults()
    {
        var root = Sequence("root",
            Node("http", Types.HttpEndpoint, [Literal("Path", "orders", "String"), Literal("SupportedMethods", new[] { "post" }), Literal("CanStartWorkflow", true, "Boolean")]),
            Node("get", Types.HttpEndpoint, [Literal("Path", "status", "String")]),
            Node("delay", Types.Delay, [Literal("Duration", "00:00:05", "TimeSpan")]),
            Node("respond", Types.WriteHttpResponse, [Literal("StatusCode", 201, "Int32"), Result("Body", "http", "ParsedContent")]));

        var workflow = NativeExecutableMapper.Map(Executable(root));

        Assert.Equal("POST", ((Native.LiteralBinding)workflow.Nodes["http"].Inputs["Method"]).Value!.GetValue<string>());
        Assert.True(((Native.LiteralBinding)workflow.Nodes["http"].Inputs[Native.NativeStartTriggers.Input]).Value!.GetValue<bool>());
        Assert.Equal("GET", ((Native.LiteralBinding)workflow.Nodes["get"].Inputs["Method"]).Value!.GetValue<string>());
        Assert.Equal(5000L, ((Native.LiteralBinding)workflow.Nodes["delay"].Inputs["Milliseconds"]).Value!.GetValue<long>());
        Assert.Equal(new Native.ResultBinding("http", "Body"), workflow.Nodes["respond"].Inputs["Body"]);
    }

    [Fact]
    public void Can_start_workflow_passes_through_on_http_endpoints_and_events()
    {
        var root = Sequence("root",
            Node("http", Types.HttpEndpoint, [Literal("Path", "orders", "String"), Literal("CanStartWorkflow", false, "Boolean")]),
            Node("wait", Types.Event, [Literal("EventName", "paid", "String"), Literal("CanStartWorkflow", false, "Boolean")]));

        var workflow = NativeExecutableMapper.Map(Executable(root));

        Assert.False(Native.NativeStartTriggers.CanStart(workflow.Nodes["http"]));
        Assert.False(Native.NativeStartTriggers.CanStart(workflow.Nodes["wait"]));
        // At the root Sequence's start, the endpoint is a wait route, not a start route.
        var route = Assert.Single(new Native.HttpRoutes(new Native.WorkflowRegistry().Add(workflow)).Routes);
        Assert.False(route.Start);
    }

    [Fact]
    public void Unbound_start_new_workflows_follows_the_old_defaults()
    {
        var root = Sequence("root",
            Node("http", Types.HttpEndpoint, [Literal("Path", "orders", "String")]),
            Node("wait", Types.Event, [Literal("EventName", "paid", "String")]),
            Node("nulled", Types.HttpEndpoint, [Literal("Path", "nulled", "String"), Literal("CanStartWorkflow", null, "Boolean")]));

        var workflow = NativeExecutableMapper.Map(Executable(root));

        // The old HttpEndpoint declares no default, so unbound it only waits; the old Event's default is true, Native's too.
        Assert.False(Native.NativeStartTriggers.CanStart(workflow.Nodes["http"]));
        Assert.False(Native.NativeStartTriggers.CanStart(workflow.Nodes["nulled"]));
        Assert.True(Native.NativeStartTriggers.CanStart(workflow.Nodes["wait"]));
        Assert.False(workflow.Nodes["wait"].Inputs.ContainsKey(Native.NativeStartTriggers.Input));
        Assert.DoesNotContain(new Native.HttpRoutes(new Native.WorkflowRegistry().Add(workflow)).Routes, route => route.Start);
    }

    [Fact]
    public void A_loop_body_reads_an_outer_loops_variable_no_inner_loop_declares()
    {
        // The inner ForEach declares currentItem and currentIndex, not index, so the outer For's index stays visible.
        var root = Node("outer", Types.For, [Literal("Start", 0, "Int32"), Literal("End", 2, "Int32")], Structure("elsa.for.structure", new { body = "inner" }),
            [Slot("For.Body", Node("inner", Types.ForEach, [Literal("Collection", new[] { 1 })], Structure("elsa.foreach.structure", new { body = "read" }),
                [Slot("ForEach.Body", Sequence("read",
                    WriteLine("outer-index", VariableRead("text", "index", scope: "outer")),
                    WriteLine("inner-item", VariableRead("text", "currentItem", scope: "inner"))))]))]);

        var workflow = NativeExecutableMapper.Map(Executable(root));

        Assert.Equal(new Native.VariableBinding("index"), workflow.Nodes["outer-index"].Inputs["Text"]);
        Assert.Equal(new Native.VariableBinding("currentItem"), workflow.Nodes["inner-item"].Inputs["Text"]);
    }

    [Theory]
    [InlineData(Types.While, "While", "elsa.while.structure")]
    [InlineData(Types.Do, "Do", "elsa.do.structure")]
    public void A_loop_without_iteration_variables_between_a_body_and_its_for_each_is_looked_through(string type, string name, string kind)
    {
        var root = Node("outer", Types.ForEach, [Literal("Collection", new[] { 1, 2 })], Structure("elsa.foreach.structure", new { body = "middle" }),
            [Slot("ForEach.Body", Node("middle", type, structure: Structure(kind, new { body = "read" }),
                slots: [Slot($"{name}.Body", WriteLine("read", VariableRead("text", "currentItem", scope: "outer")))]))]);

        var workflow = NativeExecutableMapper.Map(Executable(root));

        Assert.Equal(new Native.VariableBinding("currentItem"), workflow.Nodes["read"].Inputs["Text"]);
    }

    [Fact]
    public void Send_http_request_content_goes_as_text_by_default()
    {
        var root = Sequence("root",
            Node("send", Types.SendHttpRequest, [Literal("Url", "https://partner.example/a", "Uri"), Literal("Content", "{\"raw\": 1}", "String")]),
            Node("typed", Types.SendHttpRequest, [Literal("Url", "https://partner.example/b", "Uri"), Literal("Content", "x", "String"), Literal("ContentType", "application/json", "String")]),
            Node("empty", Types.SendHttpRequest, [Literal("Url", "https://partner.example/c", "Uri")]));

        var workflow = NativeExecutableMapper.Map(Executable(root));

        Assert.Equal("text/plain", LiteralOf(workflow.Nodes["send"], "ContentType")!.GetValue<string>());
        Assert.Equal("GET", LiteralOf(workflow.Nodes["send"], "Method")!.GetValue<string>());
        Assert.Equal("application/json", LiteralOf(workflow.Nodes["typed"], "ContentType")!.GetValue<string>());
        Assert.False(workflow.Nodes["empty"].Inputs.ContainsKey("ContentType"));
    }

    [Fact]
    public void A_loop_body_reads_its_loops_item_and_index()
    {
        var root = Sequence("root",
            Node("each", Types.ForEach, [Literal("Collection", new[] { 1, 2 })], Structure("elsa.foreach.structure", new { body = "item" }),
                [Slot("ForEach.Body", Set("item", "v-last", VariableRead("value", "currentItem", scope: "each")))]),
            Node("count", Types.For, [Literal("Start", 0, "Int32"), Literal("End", 2, "Int32")], Structure("elsa.for.structure", new { body = "pass" }),
                [Slot("For.Body", WriteLine("pass", VariableRead("text", "index", scope: "count")))]));

        var workflow = NativeExecutableMapper.Map(Executable(root, Variable("v-last", "last")));

        Assert.Equal(new Native.VariableBinding("currentItem"), workflow.Nodes["item"].Inputs["Value"]);
        Assert.Equal(new Native.VariableBinding("index"), workflow.Nodes["pass"].Inputs["Text"]);
    }

    internal static JsonNode? LiteralOf(Native.Node node, string input) => ((Native.LiteralBinding)node.Inputs[input]).Value;
}
