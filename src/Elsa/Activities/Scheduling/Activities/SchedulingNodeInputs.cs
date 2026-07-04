using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Scheduling.Activities;

/// <summary>
/// Reads authored literal inputs off a published <see cref="ExecutableNode"/> for the scheduling triggers (W16).
/// A start trigger's stimulus and schedule are fixed at publish time, so the providers only accept literal
/// inputs; a missing or non-literal value yields <c>null</c> here and the calling provider throws, failing the
/// publish rather than persisting an unroutable trigger or unfireable schedule.
/// </summary>
internal static class SchedulingNodeInputs
{
    public static string? ReadLiteralString(ExecutableNode node, string inputName)
    {
        var binding = node.InputBindings
            .FirstOrDefault(item => StringComparer.OrdinalIgnoreCase.Equals(item.Key, inputName))
            .Value;

        if (binding?.Source != RuntimeInputBindingSource.Literal || binding.LiteralValue is not { } literal)
            return null;

        if (literal.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;

        var value = literal.ValueKind == JsonValueKind.String ? literal.GetString() : literal.ToString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
