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

/// <summary>
/// The <see cref="BpmnEventDefinition.Properties"/> keys this engine slice reads (spec 117). These establish the
/// property-key convention an event-defined start element carries; a later interchange unit populates them from
/// <c>messageRef</c>/<c>signalRef</c>/<c>timerEventDefinition</c>. No key existed before this slice.
/// </summary>
public static class BpmnEventDefinitionProperties
{
    /// <summary>The event name a message/signal start (or catch) resolves its stimulus from (drives <c>EventStimulus.Hash</c>).</summary>
    public const string Name = "name";

    /// <summary>An ISO-8601 duration for a timer start's recurring interval (mutually exclusive with <see cref="Cron"/>).</summary>
    public const string Interval = "interval";

    /// <summary>A cron expression for a timer start's recurring schedule (mutually exclusive with <see cref="Interval"/>).</summary>
    public const string Cron = "cron";
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
