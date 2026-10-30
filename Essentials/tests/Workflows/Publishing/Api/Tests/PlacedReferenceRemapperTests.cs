using System.Text.Json;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

/// <summary>A closed-scope body's template-local references follow its placement.</summary>
public sealed class PlacedReferenceRemapperTests
{
    private const string GraphScope = "node-graph";

    private static readonly IReadOnlyDictionary<string, string> PlacedIds = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["compute"] = "node-compute",
        ["box"] = "node-box"
    };

    [Theory]
    [InlineData("workflow", GraphScope)]
    [InlineData("activity-graph", GraphScope)]
    [InlineData("box", "node-box")]
    [InlineData("elsewhere", "elsewhere")]
    public void Variable_read_scope_follows_the_placement(string authoredScope, string placedScope)
    {
        var node = NodeWith(Binding(RuntimeInputBindingSource.VariableRead, variable: new RuntimeVariableReference("total", authoredScope)));

        var remapped = PlacedReferenceRemapper.Remap(node, PlacedIds, GraphScope);

        var variable = remapped.InputBindings["value"].Variable!;
        Assert.Equal("total", variable.VariableKey);
        Assert.Equal(placedScope, variable.DeclaringScopeId);
    }

    [Fact]
    public void Activity_result_producer_and_scope_follow_the_placement_and_keep_their_conversion()
    {
        var conversion = new RuntimeValueConversionRequest(ValueConversionMode.Json);
        var node = NodeWith(Binding(
            RuntimeInputBindingSource.ActivityResult,
            activityResult: new RuntimeActivityResultReference("compute", "result", "box", isOptional: true),
            conversionRequest: conversion));

        var remapped = PlacedReferenceRemapper.Remap(node, PlacedIds, GraphScope);

        var binding = remapped.InputBindings["value"];
        Assert.Equal("node-compute", binding.ActivityResult!.ProducerExecutableNodeId);
        Assert.Equal("node-box", binding.ActivityResult.ProducerScopeId);
        Assert.Equal("result", binding.ActivityResult.ProjectionKey);
        Assert.True(binding.ActivityResult.IsOptional);
        Assert.Same(conversion, binding.ConversionRequest);
    }

    [Fact]
    public void Expression_parameters_follow_the_placement_and_keep_the_rest_of_the_expression()
    {
        var parameters = new Dictionary<string, ExpressionParameterBinding>(StringComparer.Ordinal)
        {
            ["a"] = new WorkflowRequestExpressionParameterBinding("a"),
            ["total"] = new VariableExpressionParameterBinding("workflow", "total"),
            ["inner"] = new VariableExpressionParameterBinding("box", "inner"),
            ["computed"] = new ActivityResultExpressionParameterBinding("compute", "result", isOptional: true)
        };
        var expression = new RuntimeExpressionBinding(
            "JavaScript", "args.a + args.total", capabilityProfile: "profile", parameters: parameters,
            options: JsonSerializer.SerializeToElement(new { strict = true }));
        var node = NodeWith(Binding(RuntimeInputBindingSource.Expression, expression: expression));

        var remapped = PlacedReferenceRemapper.Remap(node, PlacedIds, GraphScope).InputBindings["value"].Expression!;

        Assert.Equal("args.a + args.total", remapped.Expression);
        Assert.Equal("profile", remapped.CapabilityProfile);
        Assert.True(remapped.Options.GetProperty("strict").GetBoolean());
        Assert.Equal("a", Assert.IsType<WorkflowRequestExpressionParameterBinding>(remapped.Parameters["a"]).MemberKey);
        Assert.Equal(GraphScope, Assert.IsType<VariableExpressionParameterBinding>(remapped.Parameters["total"]).DeclaringScopeNodeId);
        Assert.Equal("node-box", Assert.IsType<VariableExpressionParameterBinding>(remapped.Parameters["inner"]).DeclaringScopeNodeId);
        var computed = Assert.IsType<ActivityResultExpressionParameterBinding>(remapped.Parameters["computed"]);
        Assert.Equal("node-compute", computed.ProducerNodeId);
        Assert.True(computed.IsOptional);
    }

    [Fact]
    public void Intrinsic_variable_follows_the_placement()
    {
        var value = Binding(RuntimeInputBindingSource.Literal, literal: ValueEnvelope.Inline(
            new ValueTypeDescriptor("System.String"), JsonSerializer.SerializeToElement("x"), ValueProtectionPolicy.InstanceInline));
        var node = new ExecutableNode(
            "set", "set", "elsa.intrinsic.set", "1.0.0", new("intrinsic", "1", JsonSerializer.SerializeToElement(new { })),
            new Dictionary<string, RuntimeInputBinding> { [value.InputName] = value },
            new Dictionary<string, RuntimeOutputCapture>(), new Dictionary<string, string>(),
            intrinsicKind: WorkflowIntrinsicKind.Set,
            intrinsicVariable: new RuntimeVariableReference("total", "workflow"));

        var remapped = PlacedReferenceRemapper.Remap(node, PlacedIds, GraphScope);

        Assert.Equal(GraphScope, remapped.IntrinsicVariable!.DeclaringScopeId);
        Assert.Equal(WorkflowIntrinsicKind.Set, remapped.IntrinsicKind);
    }

    [Fact]
    public void A_node_without_references_to_remap_is_returned_unchanged()
    {
        var node = NodeWith(Binding(RuntimeInputBindingSource.WorkflowRequest, workflowRequest: new RuntimeWorkflowRequestReference("a")));

        Assert.Same(node, PlacedReferenceRemapper.Remap(node, PlacedIds, GraphScope));
    }

    private static ExecutableNode NodeWith(RuntimeInputBinding binding) => new(
        "node", "node", "local", "1", new("local", "1", JsonSerializer.SerializeToElement(new { })),
        new Dictionary<string, RuntimeInputBinding> { [binding.InputName] = binding },
        new Dictionary<string, RuntimeOutputCapture>(), new Dictionary<string, string> { ["kept"] = "yes" });

    private static RuntimeInputBinding Binding(
        RuntimeInputBindingSource source,
        ValueEnvelope? literal = null,
        RuntimeWorkflowRequestReference? workflowRequest = null,
        RuntimeVariableReference? variable = null,
        RuntimeActivityResultReference? activityResult = null,
        RuntimeExpressionBinding? expression = null,
        RuntimeValueConversionRequest? conversionRequest = null) => new(
        "value",
        new ValueTypeDescriptor("Object"),
        ValueProtectionPolicy.InstanceInline,
        source,
        literal,
        workflowRequest,
        variable,
        activityResult,
        expression,
        new Dictionary<string, string> { ["input.meta"] = "kept" },
        conversionRequest: conversionRequest);
}
