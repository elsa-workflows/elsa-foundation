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
        Assert.Contains("kernel-unsupported", refused.Message, StringComparison.Ordinal);
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
        Assert.Contains(kind.ToString(), refused.Message, StringComparison.Ordinal);
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
    public void A_capture_into_a_variable_the_node_cannot_see_is_refused() =>
        AssertBinding(Refused(Sequence("root",
            Node("script", Types.RunJavaScript, [Literal("script", "1", "String")], captures: [Capture("result", "nowhere")]))), "script", "result");
}

internal static class ExecutableNodeExtensions
{
    /// <summary>The node with one more input binding.</summary>
    public static ExecutableNode With(this ExecutableNode node, RuntimeInputBinding binding) =>
        Node(node.ExecutableNodeId, node.ActivityType, [.. node.InputBindings.Values, binding], node.Structure, [.. node.ChildSlots]);
}
