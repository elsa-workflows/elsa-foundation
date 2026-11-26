using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;
using static Elsa.Workflows.Runtime.KernelHost.Tests.Compiled;

namespace Elsa.Workflows.Runtime.KernelHost.Tests;

/// <summary>What the kernel does not run is refused with its code, naming the node and, for a binding, the input.</summary>
public sealed class MappingRefusalTests
{
    private static NativeMappingRefusedException Refused(ExecutableNode root, params RuntimeVariableDeclaration[] variables) =>
        Assert.Throws<NativeMappingRefusedException>(() => NativeExecutableMapper.Map(Executable(root, variables)));

    private static void AssertActivity(NativeMappingRefusedException refused, string node)
    {
        Assert.Equal((NativeMappingRefusedException.ActivityUnsupported, node, (string?)null), (refused.Code, refused.Node, refused.Input));
        Assert.Contains($"'{node}'", refused.Message, StringComparison.Ordinal);
    }

    private static void AssertBinding(NativeMappingRefusedException refused, string node, string input) =>
        Assert.Equal((NativeMappingRefusedException.BindingUnsupported, node, (string?)input), (refused.Code, refused.Node, refused.Input));

    private static ExecutableNode Reading(RuntimeInputBinding read) => Sequence("root", WriteLine("reader", read));

    [Fact]
    public void A_hidden_activity_is_refused_with_the_node_named()
    {
        var refused = Refused(Sequence("root", WriteLine("ok", Literal("text", "hi")),
            Node("compensate", Types.CompensationScope, structure: Structure("elsa.compensation-scope.structure", new { }))));

        AssertActivity(refused, "compensate");
        Assert.Contains("CompensationScope", refused.Message, StringComparison.Ordinal);
        Assert.Contains("release 1", refused.Message, StringComparison.Ordinal);
        Assert.Contains("Remove the node", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_activity_type_is_refused() =>
        AssertActivity(Refused(Sequence("root", Node("custom", "Acme.Activities.Unknown"))), "custom");

    [Theory]
    [InlineData(WorkflowIntrinsicKind.Merge)]
    [InlineData(WorkflowIntrinsicKind.Reduce)]
    [InlineData(WorkflowIntrinsicKind.Return)]
    [InlineData(WorkflowIntrinsicKind.Control)]
    public void An_intrinsic_outside_release_1_is_refused(WorkflowIntrinsicKind kind)
    {
        var node = kind switch
        {
            WorkflowIntrinsicKind.Merge or WorkflowIntrinsicKind.Reduce =>
                Intrinsic("intrinsic", kind, new RuntimeVariableReference("v-x", WorkflowScope), Literal(WorkflowIntrinsicInputKeys.Value, 1)),
            WorkflowIntrinsicKind.Return => Intrinsic("intrinsic", kind, null, Literal(WorkflowIntrinsicInputKeys.Value, 1)),
            _ => Intrinsic("intrinsic", kind, null, Literal(WorkflowIntrinsicInputKeys.Outcome, "Done", "String")),
        };

        var refused = Refused(Sequence("root", node), Variable("v-x", "x"));

        AssertActivity(refused, "intrinsic");
        Assert.Contains($"{kind} does not run on this host", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_secret_read_is_refused() => AssertBinding(Refused(Reading(Secret("text", "api-key"))), "reader", "text");

    [Fact]
    public void A_member_segment_with_a_dot_is_refused() =>
        AssertBinding(Refused(Reading(VariableMember("text", "v-order", "address", "zip.code")), Variable("v-order", "order")), "reader", "text");

    [Fact]
    public void A_request_member_key_with_a_dot_is_refused() =>
        AssertBinding(Refused(Reading(Request("text", "order.id"))), "reader", "text");

    [Fact]
    public void A_conversion_other_than_identity_or_numeric_widening_is_refused()
    {
        var refused = Refused(Reading(VariableRead("text", "v-n", plan: NumericToString())), Variable("v-n", "n", 1, "Int32"));

        AssertBinding(refused, "reader", "text");
        Assert.Contains("NumericToString", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_container_scoped_variable_is_refused() =>
        AssertBinding(Refused(Reading(VariableRead("text", "v-local", scope: "root")), Variable("v-order", "order")), "reader", "text");

    [Fact]
    public void A_container_that_declares_variables_is_refused()
    {
        var root = Node("root", Types.Sequence,
            structure: Structure("elsa.sequence.structure", new { activities = new[] { "w" }, variables = new[] { new { variableKey = "v-local", name = "local" } } }),
            slots: [Slot("Sequence.Activities", WriteLine("w", Literal("text", "hi")))]);

        AssertActivity(Refused(root), "root");
    }

    [Fact]
    public void A_transient_variable_read_is_refused() =>
        AssertBinding(Refused(Reading(VariableRead("text", "v-order", policy: ValueProtectionPolicy.Transient)), Variable("v-order", "order")), "reader", "text");

    [Fact]
    public void An_unknown_variable_is_refused() =>
        AssertBinding(Refused(Reading(VariableRead("text", "v-missing"))), "reader", "text");

    [Fact]
    public void An_expression_parameter_that_converts_or_reads_a_container_variable_is_refused()
    {
        var converting = Expression("text", "JavaScript", "n", "String",
            new Dictionary<string, ExpressionParameterBinding> { ["n"] = new VariableExpressionParameterBinding(WorkflowScope, "v-n") },
            new Dictionary<string, ValueConversionPlan> { ["n"] = NumericToString() });
        AssertBinding(Refused(Reading(converting), Variable("v-n", "n", 1, "Int32")), "reader", "text");

        var local = Expression("text", "JavaScript", "n", "String",
            new Dictionary<string, ExpressionParameterBinding> { ["n"] = new VariableExpressionParameterBinding("root", "v-n") });
        AssertBinding(Refused(Reading(local), Variable("v-n", "n", 1, "Int32")), "reader", "text");
    }

    [Fact]
    public void An_expression_language_other_than_javascript_or_liquid_is_refused() =>
        AssertBinding(Refused(Reading(Expression("text", "CSharp", "1", "String"))), "reader", "text");

    [Fact]
    public void An_input_the_kernel_does_not_model_is_refused_unless_native_behaves_as_it_says()
    {
        AssertBinding(Refused(Sequence("root", Node("wait", Types.Event, [Literal("EventName", "paid", "String"), Literal("CorrelationId", "order-1", "String")]))),
            "wait", "CorrelationId");
        AssertBinding(Refused(Sequence("root", WriteLine("w", Literal("text", "hi")).With(Literal("color", "red", "String")))), "w", "color");
        AssertBinding(Refused(Sequence("root", Node("delay", Types.Delay, [VariableRead("Duration", "v-d")])), Variable("v-d", "d")), "delay", "Duration");

        // A null correlation id is what Native does.
        NativeExecutableMapper.Map(Executable(Sequence("root", Node("wait", Types.Event, [Literal("EventName", "paid", "String"), Literal("CorrelationId", null, "String")]))));
    }

    [Fact]
    public void A_flowchart_policy_the_kernel_does_not_have_is_refused_naming_the_node()
    {
        var structure = Structure("elsa.flowchart.structure", new
        {
            connections = Array.Empty<object>(),
            nodeMetadata = new Dictionary<string, object> { ["a"] = new { policyKind = "implicitActivationJoin" } },
        });
        var root = Node("flow", Types.Flowchart, structure: structure, slots: [Slot("Flowchart.Activities", WriteLine("a", Literal("text", "a")))]);

        AssertActivity(Refused(root), "a");
    }

    [Fact]
    public void A_result_read_of_a_projection_the_kernel_does_not_produce_is_refused() =>
        AssertBinding(Refused(Sequence("root",
            Node("dispatch", Types.Dispatch, [Literal("WorkflowDefinitionId", "child", "String")]),
            WriteLine("reader", Result("text", "dispatch", "ChildWorkflowExecutionId")))), "reader", "text");

    [Fact]
    public void A_capture_into_a_variable_the_node_cannot_see_is_refused()
    {
        var refused = Refused(Sequence("root",
            Node("script", Types.RunJavaScript, [Literal("script", "1", "String")], captures: [Capture("result", "nowhere")])));

        AssertBinding(refused, "script", "result");
        Assert.Contains("variable 'nowhere'", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("variable:", refused.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("input")]
    [InlineData("output")]
    [InlineData("currentItem")]
    [InlineData("currentIndex")]
    [InlineData("index")]
    public void A_workflow_variable_with_a_name_this_host_uses_is_refused(string name)
    {
        var refused = Refused(Sequence("root", WriteLine("w", Literal("text", "hi"))), Variable("v-1", name));

        AssertBinding(refused, "root", name);
        Assert.EndsWith("Rename the variable.", refused.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Request", "headers")]
    [InlineData("Request", "query")]
    [InlineData("Request", "body")]
    [InlineData("Request", "path")]
    [InlineData("Request", null)]
    [InlineData("RouteData", null)]
    [InlineData("RouteData", "id")]
    public void An_http_endpoint_result_whose_shape_differs_is_refused(string projection, string? member)
    {
        var read = member is null ? Result("text", "http", projection) : ResultMember("text", "http", projection, member);
        var refused = Refused(Sequence("root", Node("http", Types.HttpEndpoint, [Literal("Path", "orders", "String")]), WriteLine("reader", read)));

        AssertBinding(refused, "reader", "text");
    }

    [Theory]
    [InlineData("ResponseBody", null)]
    [InlineData("ResponseHeaders", null)]
    [InlineData("ResponseHeaders", "content-type")]
    public void A_send_http_request_result_whose_shape_differs_is_refused(string projection, string? member)
    {
        var read = member is null ? Result("text", "send", projection) : ResultMember("text", "send", projection, member);
        AssertBinding(Refused(Sequence("root", Node("send", Types.SendHttpRequest, [Literal("Url", "https://partner.example", "Uri")]), WriteLine("reader", read))),
            "reader", "text");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("status")]
    [InlineData("outputs")]
    public void A_dispatch_result_read_is_refused(string? member)
    {
        var read = member is null ? Result("text", "dispatch", "Result") : ResultMember("text", "dispatch", "Result", member);
        AssertBinding(Refused(Sequence("root", Node("dispatch", Types.Dispatch, [Literal("WorkflowDefinitionId", "child", "String")]), WriteLine("reader", read))),
            "reader", "text");
    }

    [Theory]
    [InlineData("5")]
    [InlineData("PT5S")]
    [InlineData("soon")]
    public void A_delay_takes_a_literal_time_span_as_publishing_writes_it(string duration) =>
        AssertBinding(Refused(Sequence("root", Node("delay", Types.Delay, [Literal("Duration", duration, "TimeSpan")]))), "delay", "Duration");

    [Fact]
    public void A_bare_number_is_days_on_both_cores()
    {
        // Publishing converts a TimeSpan literal through System.Text.Json, which reads "5" as five days and writes it back in
        // its own form; the old core's Delay then waits five days, and so does this host's.
        var published = System.Text.Json.JsonSerializer.Serialize(System.Text.Json.JsonSerializer.Deserialize<TimeSpan>("\"5\""));
        Assert.Equal("\"5.00:00:00\"", published);

        var workflow = NativeExecutableMapper.Map(Executable(Sequence("root",
            Node("delay", Types.Delay, [Literal("Duration", System.Text.Json.JsonSerializer.Deserialize<string>(published), "TimeSpan")]))));
        Assert.Equal((long)TimeSpan.FromDays(5).TotalMilliseconds,
            ((Kernel.Native.LiteralBinding)workflow.Nodes["delay"].Inputs["Milliseconds"]).Value!.GetValue<long>());
    }

    [Fact]
    public void A_refusal_names_the_node_by_its_display_name_and_the_variable_by_its_name()
    {
        var root = Sequence("root", Node("w", Types.WriteLine, [VariableRead("text", "v-order", policy: ValueProtectionPolicy.Transient)], displayName: "Log the order"));

        var refused = Refused(root, Variable("v-order", "order"));

        Assert.Equal("Log the order", refused.DisplayName);
        Assert.StartsWith("Node 'Log the order' (w), input 'text': Variable 'order' is transient", refused.Message, StringComparison.Ordinal);
        Assert.EndsWith("Make the variable persistent.", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("kernel", refused.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_parallel_with_a_threshold_and_an_empty_branch_is_refused()
    {
        var root = Node("par", Types.Parallel, structure: Structure("elsa.parallel.structure", new
            {
                branches = new object[] { new { name = "left", activity = "l" }, new { name = "right", activity = (string?)null } },
                threshold = 1,
            }),
            slots: [Slot("Parallel.Branch[left]", WriteLine("l", Literal("text", "l")))]);

        var refused = Refused(root);

        AssertActivity(refused, "par");
        Assert.EndsWith("Remove the empty branch or the threshold.", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_native_definition_rule_is_refused_naming_the_node_that_breaks_it()
    {
        // A Complete belongs inside a reusable activity's body; outside one the Native definition refuses it.
        var refused = Refused(Sequence("root", WriteLine("w", Literal("text", "hi")), Node("done", Types.Complete, [Literal("outcome", "Done", "String")])));

        AssertActivity(refused, "done");
        Assert.DoesNotContain(ArtifactId, refused.Message, StringComparison.Ordinal);
    }

    private static ExecutableNode NestedForEach(RuntimeInputBinding read) =>
        Node("outer", Types.ForEach, [Literal("Collection", new[] { 1, 2 })], Structure("elsa.foreach.structure", new { body = "inner" }),
            [Slot("ForEach.Body", Node("inner", Types.ForEach, [Literal("Collection", new[] { 3 })], Structure("elsa.foreach.structure", new { body = "reader" }),
                [Slot("ForEach.Body", WriteLine("reader", read))]))]);

    [Fact]
    public void A_read_of_an_outer_loops_item_inside_an_inner_loop_is_refused()
    {
        var parameters = new Dictionary<string, ExpressionParameterBinding> { ["item"] = new VariableExpressionParameterBinding("outer", "currentItem") };
        foreach (var read in new[]
                 {
                     VariableRead("text", "currentItem", scope: "outer"),
                     new RuntimeInputBinding("text", new Elsa.Primitives.Models.ValueTypeDescriptor("Object"), Durable, RuntimeInputBindingSource.VariableMember,
                         new RuntimeReadSelection(RuntimeReadSelection.CurrentSchemaVersion, ["name"], RuntimeReadSelectionMode.DeferredFit),
                         variable: new RuntimeVariableReference("currentIndex", "outer")),
                     Expression("text", "JavaScript", "item", "String", parameters),
                 })
        {
            var refused = Refused(NestedForEach(read));

            AssertBinding(refused, "reader", "text");
            Assert.Contains("loop 'inner'", refused.Message, StringComparison.Ordinal);
            Assert.EndsWith("into a variable before the inner loop, and read that variable.", refused.Message, StringComparison.Ordinal);
        }

        // The inner loop's own item maps.
        var workflow = NativeExecutableMapper.Map(Executable(NestedForEach(VariableRead("text", "currentItem", scope: "inner"))));
        Assert.Equal(new Kernel.Native.VariableBinding("currentItem"), workflow.Nodes["reader"].Inputs["Text"]);
    }

    [Fact]
    public void A_read_of_a_loops_item_outside_its_body_is_refused() =>
        AssertBinding(Refused(Sequence("root",
            Node("each", Types.ForEach, [Literal("Collection", new[] { 1 })], Structure("elsa.foreach.structure", new { })),
            WriteLine("after", VariableRead("text", "currentItem", scope: "each")))), "after", "text");

    [Theory]
    [InlineData(Types.HttpEndpoint)]
    [InlineData(Types.Event)]
    public void Start_new_workflows_must_be_a_literal(string type)
    {
        var refused = Refused(Sequence("root", Node("trigger", type,
            [Literal(type == Types.Event ? "EventName" : "Path", "x", "String"), VariableRead("CanStartWorkflow", "v-start")])), Variable("v-start", "start", true, "Boolean"));

        AssertBinding(refused, "trigger", "CanStartWorkflow");
        Assert.EndsWith("Set Start new workflows on or off.", refused.Message, StringComparison.Ordinal);
    }
}

internal static class ExecutableNodeExtensions
{
    /// <summary>The node with one more input binding.</summary>
    public static ExecutableNode With(this ExecutableNode node, RuntimeInputBinding binding) =>
        Node(node.ExecutableNodeId, node.ActivityType, [.. node.InputBindings.Values, binding], node.Structure, [.. node.ChildSlots]);
}
