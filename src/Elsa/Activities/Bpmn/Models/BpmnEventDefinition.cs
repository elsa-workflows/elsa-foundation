using System.Text.Json.Serialization;

namespace Elsa.Activities.Bpmn.Models;

/// <summary>
/// An event definition attached to a BPMN event element. The engine interprets
/// <see cref="BpmnEventDefinitionTypes.Terminate"/> on end events and
/// <see cref="BpmnEventDefinitionTypes.Timer"/>/<see cref="BpmnEventDefinitionTypes.Message"/>/
/// <see cref="BpmnEventDefinitionTypes.Signal"/> on intermediate catch events (spec 116); other
/// types are carried for later phases.
/// </summary>
public sealed class BpmnEventDefinition
{
    [JsonConstructor]
    public BpmnEventDefinition(string type, IReadOnlyDictionary<string, string>? properties = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);

        Type = type;
        Properties = properties ?? new Dictionary<string, string>();
    }

    [JsonPropertyName("type")]
    public string Type { get; }

    [JsonPropertyName("properties")]
    public IReadOnlyDictionary<string, string> Properties { get; }
}

public static class BpmnEventDefinitionTypes
{
    public const string Terminate = "terminate";
    public const string Timer = "timer";
    public const string Message = "message";
    public const string Signal = "signal";
    public const string Error = "error";
    public const string Escalation = "escalation";
    public const string Compensation = "compensation";
}
