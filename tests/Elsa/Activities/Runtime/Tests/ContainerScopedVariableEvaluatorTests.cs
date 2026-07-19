using System.Text.Json;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Resolvers;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// Canonical variable reads address immutable values by declaration key and concrete lexical frame;
/// they expose no ambient name lookup or mutation surface.
/// </summary>
public sealed class ContainerScopedVariableEvaluatorTests
{
    private const string WorkflowScopeId = VariableReference.WorkflowScopeId;
    private const string OuterScopeId = "outer";
    private const string InnerScopeId = "inner";
    private static readonly ValueTypeDescriptor Int32Type = new("Int32");
    private static readonly ValueTypeDescriptor StringType = new("String");

    [Fact]
    public void Descendant_resolves_container_variable_by_declaration_and_scope_identity()
    {
        var frames = Frames();

        var result = Resolve(frames.Visible, "var-inner", InnerScopeId, Int32Type);

        Assert.Equal(2, result.Envelope!.InlineValue!.Value.GetInt32());
        Assert.Equal(InnerScopeId, frames.Inner.ScopeId);
        Assert.Equal(frames.Outer.FrameId, frames.Inner.ParentFrameId);
    }

    [Fact]
    public void Workflow_variable_remains_visible_from_nested_container_frames()
    {
        var frames = Frames();

        var result = Resolve(frames.Visible, "var-greeting", WorkflowScopeId, StringType);

        Assert.Equal("hello", result.Envelope!.InlineValue!.Value.GetString());
    }

    [Fact]
    public void Nested_frames_resolve_each_declaration_explicitly_without_name_shadowing()
    {
        var frames = Frames();

        var inner = Resolve(frames.Visible, "var-inner", InnerScopeId, Int32Type);
        var outer = Resolve(frames.Visible, "var-outer", OuterScopeId, Int32Type);

        Assert.Equal(2, inner.Envelope!.InlineValue!.Value.GetInt32());
        Assert.Equal(1, outer.Envelope!.InlineValue!.Value.GetInt32());
    }

    [Fact]
    public void Reference_to_sibling_scope_is_rejected_as_unavailable()
    {
        var frames = Frames();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            Resolve(frames.Visible, "var-inner", "sibling", Int32Type));

        Assert.Contains("sibling", exception.Message, StringComparison.Ordinal);
        Assert.Contains("unavailable", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Closed_frame_is_not_part_of_a_later_visible_frame_snapshot()
    {
        var frames = Frames();
        var closedInner = frames.Inner.Close(frames.Inner.Revision);
        var afterCompletion = new RuntimeVisibleVariableFrames([frames.Root, frames.Outer]);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            Resolve(afterCompletion, "var-inner", InnerScopeId, Int32Type));

        Assert.Equal(VariableFrameStatus.Closed, closedInner.Status);
        Assert.Contains("unavailable", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Explicit_frame_update_is_visible_only_through_the_new_immutable_snapshot()
    {
        var frames = Frames();
        var updatedInner = frames.Inner.Set("var-inner", Envelope(Int32Type, 42), frames.Inner.Revision);
        var updatedVisible = new RuntimeVisibleVariableFrames([frames.Root, frames.Outer, updatedInner]);

        var before = Resolve(frames.Visible, "var-inner", InnerScopeId, Int32Type);
        var after = Resolve(updatedVisible, "var-inner", InnerScopeId, Int32Type);

        Assert.Equal(2, before.Envelope!.InlineValue!.Value.GetInt32());
        Assert.Equal(42, after.Envelope!.InlineValue!.Value.GetInt32());
        Assert.Equal(0, frames.Inner.Revision);
        Assert.Equal(1, updatedInner.Revision);
    }

    private static RuntimeResolvedInput Resolve(
        RuntimeVisibleVariableFrames frames,
        string variableKey,
        string scopeId,
        ValueTypeDescriptor type) =>
        new RuntimeInputBindingResolver().Resolve(
            new RuntimeInputBinding(
                "value",
                type,
                ValueProtectionPolicy.InstanceInline,
                RuntimeInputBindingSource.VariableRead,
                variable: new RuntimeVariableReference(variableKey, scopeId)),
            new RuntimeInputBindingResolutionContext(
                "workflow-1",
                "activity-1",
                variableEnvelopes: frames.Values));

    private static FrameSet Frames()
    {
        var factory = new VariableFrameFactory();
        var root = factory.CreateRoot(
            "workflow-1",
            WorkflowScopeId,
            Values(("var-greeting", StringType, "hello")));
        var outer = factory.CreateContainer(
            OuterScopeId,
            "outer-activation",
            root,
            Values(("var-outer", Int32Type, 1)));
        var inner = factory.CreateContainer(
            InnerScopeId,
            "inner-activation",
            outer,
            Values(("var-inner", Int32Type, 2)));
        return new FrameSet(root, outer, inner, new RuntimeVisibleVariableFrames([root, outer, inner]));
    }

    private static IReadOnlyDictionary<string, ValueEnvelope> Values(
        params (string Key, ValueTypeDescriptor Type, object Value)[] values) =>
        values.ToDictionary(
            item => item.Key,
            item => Envelope(item.Type, item.Value),
            StringComparer.Ordinal);

    private static ValueEnvelope Envelope(ValueTypeDescriptor type, object value) =>
        ValueEnvelope.Inline(
            type,
            JsonSerializer.SerializeToElement(value, value.GetType()),
            ValueProtectionPolicy.InstanceInline);

    private sealed record FrameSet(
        VariableFrameState Root,
        VariableFrameState Outer,
        VariableFrameState Inner,
        RuntimeVisibleVariableFrames Visible);
}
