using System.Text.Json;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Resolvers;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

public sealed class VariableExpressionEvaluatorTests
{
    private static readonly ValueTypeDescriptor Int32Type = new("Int32");

    [Fact]
    public void Resolve_reads_workflow_variable_from_its_stable_frame_address()
    {
        var resolved = Resolve(
            Binding("var-counter", VariableReference.WorkflowScopeId),
            Values((VariableReference.WorkflowScopeId, "var-counter", 42)));

        Assert.Equal(RuntimeInputBindingSource.VariableRead, resolved.Source);
        Assert.Equal(42, resolved.Envelope!.InlineValue!.Value.GetInt32());
    }

    [Fact]
    public void Resolve_uses_reference_key_instead_of_authored_variable_name()
    {
        var values = Values((VariableReference.WorkflowScopeId, "var-counter", 42));

        var resolved = Resolve(Binding("var-counter", VariableReference.WorkflowScopeId), values);
        var exception = Assert.Throws<InvalidOperationException>(() =>
            Resolve(Binding("Counter", VariableReference.WorkflowScopeId), values));

        Assert.Equal(42, resolved.Envelope!.InlineValue!.Value.GetInt32());
        Assert.Contains("Variable 'Counter'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_rejects_variable_from_an_unavailable_scope()
    {
        var values = Values((VariableReference.WorkflowScopeId, "var-counter", 42));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            Resolve(Binding("var-counter", "sibling-scope"), values));

        Assert.Contains("sibling-scope", exception.Message, StringComparison.Ordinal);
        Assert.Contains("unavailable", exception.Message, StringComparison.Ordinal);
    }

    private static RuntimeResolvedInput Resolve(
        RuntimeInputBinding binding,
        IReadOnlyDictionary<RuntimeVariableValueAddress, ValueEnvelope> values) =>
        new RuntimeInputBindingResolver().Resolve(
            binding,
            new RuntimeInputBindingResolutionContext(
                "workflow-1",
                "activity-1",
                variableEnvelopes: values));

    private static RuntimeInputBinding Binding(string variableKey, string scopeId) =>
        new(
            "counter",
            Int32Type,
            ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.VariableRead,
            variable: new RuntimeVariableReference(variableKey, scopeId));

    private static IReadOnlyDictionary<RuntimeVariableValueAddress, ValueEnvelope> Values(
        params (string ScopeId, string Key, int Value)[] values) =>
        values.ToDictionary(
            item => new RuntimeVariableValueAddress(item.ScopeId, item.Key),
            item => ValueEnvelope.Inline(
                Int32Type,
                JsonSerializer.SerializeToElement(item.Value),
                ValueProtectionPolicy.InstanceInline));
}
