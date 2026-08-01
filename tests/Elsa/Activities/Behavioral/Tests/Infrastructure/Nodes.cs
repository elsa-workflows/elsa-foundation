using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Behavioral.Infrastructure;

/// <summary>
/// Terse builders for the executable graphs the drives run. Every per-module test project re-derives these node
/// shapes by hand; the drives build a lot of small graphs, so they are factored once here instead.
/// </summary>
/// <remarks>
/// Only the pieces a hand-built test graph must supply are set. <c>WorkflowExecutionHarness</c> pins the CLR
/// activity contract, input bindings and outcome set from the real activity type at run time, so these builders
/// deliberately do not fabricate a contract — a drive therefore cannot accidentally test against a contract the
/// activity does not actually have.
/// </remarks>
public static class Nodes
{
    /// <summary>A leaf CLR activity node with literal input bindings.</summary>
    public static ExecutableNode Leaf(
        string nodeId,
        Type activityType,
        IReadOnlyDictionary<string, object?>? inputs = null) =>
        new(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: activityType.FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: "test",
            descriptorPayload: JsonSerializer.SerializeToElement(new { }),
            inputBindings: Literals(inputs),
            metadata: new Dictionary<string, string>());

    /// <summary>A structural CLR activity node with child slots and a structure payload.</summary>
    public static ExecutableNode Structural(
        string nodeId,
        Type activityType,
        string structureKind,
        string structureSchemaVersion,
        object structurePayload,
        IReadOnlyCollection<ExecutableChildSlot> childSlots,
        IReadOnlyDictionary<string, object?>? inputs = null) =>
        new(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: activityType.FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: "test",
            descriptorPayload: JsonSerializer.SerializeToElement(new { }),
            inputBindings: Literals(inputs),
            metadata: new Dictionary<string, string>(),
            childSlots: childSlots,
            structure: new ExecutableActivityStructure(
                structureKind,
                structureSchemaVersion,
                JsonSerializer.SerializeToElement(structurePayload)));

    /// <summary>Binds every entry as an inline literal, inferring the value-type alias from the CLR value.</summary>
    private static IReadOnlyDictionary<string, RuntimeInputBinding> Literals(IReadOnlyDictionary<string, object?>? inputs)
    {
        var bindings = new Dictionary<string, RuntimeInputBinding>(StringComparer.Ordinal);
        if (inputs is null)
            return bindings;

        foreach (var (key, value) in inputs)
            bindings[key] = Literal(key, value);

        return bindings;
    }

    private static RuntimeInputBinding Literal(string key, object? value)
    {
        var type = new ValueTypeDescriptor(Alias(value));
        var envelope = value is null
            ? ValueEnvelope.Null(type, ValueProtectionPolicy.InstanceInline)
            : ValueEnvelope.Inline(type, JsonSerializer.SerializeToElement(value, value.GetType()), ValueProtectionPolicy.InstanceInline);
        return new RuntimeInputBinding(key, type, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Literal, literal: envelope);
    }

    // The harness re-derives each binding's declared type from the activity property before execution, so this
    // alias only has to be a truthful description of the literal — it is not the contract.
    private static string Alias(object? value) => value switch
    {
        null => "Object",
        bool => "Boolean",
        int => "Int32",
        long => "Int64",
        double => "Double",
        string => "String",
        TimeSpan => "TimeSpan",
        JsonElement => "Object",
        _ => "Object"
    };
}
