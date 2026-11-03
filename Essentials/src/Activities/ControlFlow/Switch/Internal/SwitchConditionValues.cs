using System.Text.Json;
using Elsa.Activities.ControlFlow.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Switch.Internal;

internal static class SwitchConditionValues
{
    public static bool Boolean(ValueEnvelope value, string key) => value.InlineValue?.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => throw new ControlFlowExecutionException($"Switch condition '{key}' has no readable Boolean value.")
    };

    public static string? String(ValueEnvelope value, string key) => value.InlineValue?.ValueKind switch
    {
        JsonValueKind.String => value.InlineValue.Value.GetString(),
        JsonValueKind.Null => null,
        null when value.Presence is ValuePresence.ExplicitNull or ValuePresence.Absent => null,
        _ => throw new ControlFlowExecutionException($"Switch input '{key}' has no readable String value.")
    };
}
