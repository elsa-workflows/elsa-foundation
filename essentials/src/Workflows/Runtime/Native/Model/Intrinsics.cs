using System.Text.Json.Nodes;

namespace Kernel.Native;

// The engine intrinsics the palette always offers besides Set Variable (owner decision of 25 November: all four run).
// Like SetVariable they execute nothing themselves: the model applies them (Burst.Intrinsics).

/// <summary>The labels Set Correlation Id and Set Instance Name set on the instance (filterable through ActorQuery.Labels).</summary>
public static class NativeLabels
{
    public const string CorrelationId = "correlationId";
    public const string InstanceName = "instanceName";
}

/// <summary>
/// The old core's Set Output: a named output of the workflow, which ends with its outputs as one object; inside a
/// GraphActivity's body, an output of that boundary which it declares and maps no binding to. The name is a literal.
/// </summary>
public sealed class SetOutput : ActivityBase
{
    [Input] public string Name { get; set; } = "";
    [Input] public JsonNode? Value { get; set; }
}

/// <summary>The old core's Set Correlation Id: the instance's correlation id, a string, or null to clear it.</summary>
public sealed class SetCorrelationId : ActivityBase
{
    [Input] public JsonNode? Value { get; set; }
}

/// <summary>The old core's Set Instance Name: the instance's display name, a string, or null to clear it.</summary>
public sealed class SetInstanceName : ActivityBase
{
    [Input] public JsonNode? Value { get; set; }
}

/// <summary>The old core's Finish: the workflow completes now, with the named outcome, ending whatever else still runs. The outcome is a literal.</summary>
public sealed class Finish : ActivityBase
{
    [Input] public string? Outcome { get; set; }
}
