using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Values;

/// <summary>Read-only typed views of one admitted atomic fixture, never independent output slots.</summary>
internal static class RuntimeTestResultProjections
{
    internal static IReadOnlyDictionary<string, ValueEnvelope> Project(ValueEnvelope result, ActivityResultContract contract)
    {
        try { return ProjectCore(result, contract); }
        catch (InvalidOperationException exception)
        {
            throw new RuntimeTestContextUnavailableException("runtime.test-context.result-projection-unavailable",
                "The prior-result fixture differs from its captured projection contract.", innerException: exception);
        }
    }

    private static IReadOnlyDictionary<string, ValueEnvelope> ProjectCore(ValueEnvelope result, ActivityResultContract contract)
    {
        if (contract.EffectiveSourceRepresentation == ValueRepresentation.TransientResource ||
            contract.Projections.Values.Any(x => x.EffectiveSourceRepresentation == ValueRepresentation.TransientResource))
            throw new InvalidOperationException("Prior-result fixtures require JSON source representations.");
        var minimum = contract.Projections.Values.Aggregate(contract.Policy,
            (policy, projection) => ValuePolicyCombiner.Combine(policy, projection.Policy, "Atomic prior-result fixture"));
        var policy = ValuePolicyCombiner.RequireMinimum(result.Policy, minimum, "Prior-result fixture projections");
        var json = result.Presence == ValuePresence.ExplicitNull ? JsonSerializer.SerializeToElement<object?>(null) :
            result.InlineValue ?? throw new InvalidOperationException("Prior-result projections require an inline whole result.");
        var selected = RuntimeResultProjectionSelector.Select(contract, json);
        return contract.Projections.Values.ToDictionary(projection => projection.Key, projection =>
            selected[projection.Key] is not { } value ? ValueEnvelope.Absent(projection.Type, policy) :
            value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ? ValueEnvelope.Null(projection.Type, policy) :
            ValueEnvelope.Inline(projection.Type, value, policy), StringComparer.Ordinal);
    }

    internal static ValueEnvelope Read(ValueEnvelope result, ActivityResultContract contract, string key)
    {
        var values = Project(result, contract); // Validate all required declarations, even for a whole-result read.
        if (key == "$result") return result;
        if (!values.TryGetValue(key, out var value) || value.Presence == ValuePresence.Absent)
            throw new RuntimeTestContextUnavailableException("runtime.test-context.result-projection-unavailable",
                "The requested prior-result projection is unavailable; absent is not explicit null.");
        return value;
    }
}
