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
    public void An_activity_result_without_a_scope_follows_the_placement_and_still_declares_none()
    {
        var node = NodeWith(Binding(
            RuntimeInputBindingSource.ActivityResult,
            activityResult: new RuntimeActivityResultReference("compute", "result", producerScopeId: null)));

        var result = PlacedReferenceRemapper.Remap(node, PlacedIds, GraphScope).InputBindings["value"].ActivityResult!;

        Assert.Equal("node-compute", result.ProducerExecutableNodeId);
        Assert.Null(result.ProducerScopeId);
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

    /// <summary>
    /// A request member read names a boundary input by its reference key, which placement does not rename, so it is
    /// returned as it is, even when a placed node or scope shares the key's text.
    /// </summary>
    [Theory]
    [InlineData("customer")]
    [InlineData("compute")]
    [InlineData("box")]
    public void A_request_member_read_is_returned_unchanged(string memberKey)
    {
        var node = NodeWith(new RuntimeInputBinding("value", new ValueTypeDescriptor("Object"), ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.WorkflowRequestMember, new RuntimeReadSelection(1, ["id"], RuntimeReadSelectionMode.DeferredFit),
            workflowRequest: new RuntimeWorkflowRequestReference(memberKey)));

        Assert.Same(node, PlacedReferenceRemapper.Remap(node, PlacedIds, GraphScope));
    }

    [Theory]
    [InlineData("box", "node-box")]
    [InlineData("workflow", GraphScope)]
    [InlineData("activity-graph", GraphScope)]
    public void A_variable_member_read_moves_with_its_scope_and_keeps_its_selection(string scope, string placedScope)
    {
        var selection = new RuntimeReadSelection(1, ["address", "a.b", ""], RuntimeReadSelectionMode.DeferredFit);
        var binding = new RuntimeInputBinding("value", new ValueTypeDescriptor("String"), ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.VariableMember, selection, variable: new RuntimeVariableReference("total", scope),
            metadata: new Dictionary<string, string> { ["input.meta"] = "kept" });

        var remapped = PlacedReferenceRemapper.Remap(NodeWith(binding), PlacedIds, GraphScope).InputBindings["value"];

        Assert.Equal(RuntimeInputBindingSource.VariableMember, remapped.Source);
        Assert.Equal(("total", placedScope), (remapped.Variable!.VariableKey, remapped.Variable.DeclaringScopeId));
        Assert.Same(selection, remapped.Selection);
        Assert.Equal("kept", remapped.Metadata["input.meta"]);
    }

    [Fact]
    public void A_variable_member_read_of_a_scope_outside_the_placement_is_returned_unchanged()
    {
        var node = NodeWith(new RuntimeInputBinding("value", new ValueTypeDescriptor("Object"), ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.VariableMember, new RuntimeReadSelection(1, ["id"], RuntimeReadSelectionMode.DeferredFit),
            variable: new RuntimeVariableReference("total", "elsewhere")));

        Assert.Same(node, PlacedReferenceRemapper.Remap(node, PlacedIds, GraphScope));
    }

    [Fact]
    public void A_result_member_read_moves_with_its_producer_and_keeps_its_selection()
    {
        var selection = new RuntimeReadSelection(1, ["address", "a.b", ""], RuntimeReadSelectionMode.Known, new ValueTypeDescriptor("String"),
            mayBeAbsent: true, mayBeNull: false, ValueRepresentation.TextValue, "elsa.activity-value-json/1");
        var plan = ValueConversionPlan.Identity(new ValueTypeDescriptor("String"), ValueRepresentation.TextValue);
        var binding = new RuntimeInputBinding("value", new ValueTypeDescriptor("String"), ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.ActivityResultMember, selection,
            activityResult: new RuntimeActivityResultReference("compute", "$result", producerScopeId: null, isOptional: true),
            metadata: new Dictionary<string, string> { ["input.meta"] = "kept" }, conversionPlan: plan);

        var remapped = PlacedReferenceRemapper.Remap(NodeWith(binding), PlacedIds, GraphScope).InputBindings["value"];

        Assert.Equal(RuntimeInputBindingSource.ActivityResultMember, remapped.Source);
        Assert.Equal("node-compute", remapped.ActivityResult!.ProducerExecutableNodeId);
        Assert.Null(remapped.ActivityResult.ProducerScopeId);
        Assert.True(remapped.ActivityResult.IsOptional);
        Assert.Same(selection, remapped.Selection);
        Assert.Same(plan, remapped.ConversionPlan);
        Assert.Equal("kept", remapped.Metadata["input.meta"]);
    }

    [Fact]
    public void A_result_member_read_of_a_producer_outside_the_placement_is_returned_unchanged()
    {
        var node = NodeWith(new RuntimeInputBinding("value", new ValueTypeDescriptor("Object"), ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.ActivityResultMember, new RuntimeReadSelection(1, ["id"], RuntimeReadSelectionMode.DeferredFit),
            activityResult: new RuntimeActivityResultReference("elsewhere", "$result", producerScopeId: null)));

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
