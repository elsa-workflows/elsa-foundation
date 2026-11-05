using System.Text.Json.Serialization;

namespace Elsa.Activities.Bpmn.Models;

/// <summary>
/// An authored event definition translated to the released BPMN model. Execution requires
/// both a supported neutral construct and its Foundation host binding.
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
/// Foundation's retained <see cref="BpmnEventDefinition.Properties"/> conventions for
/// event names, timer schedules and control-event matching.
/// </summary>
public static class BpmnEventDefinitionProperties
{
    /// <summary>The event name a message/signal start (or catch) resolves its stimulus from (drives <c>EventStimulus.Hash</c>).</summary>
    public const string Name = "name";

    /// <summary>An ISO-8601 duration for a timer start's recurring interval (mutually exclusive with <see cref="Cron"/>).</summary>
    public const string Interval = "interval";

    /// <summary>A cron expression for a timer start's recurring schedule (mutually exclusive with <see cref="Interval"/>).</summary>
    public const string Cron = "cron";

    /// <summary>
    /// The element id a compensate throw/end event targets: compensate only that element's
    /// registrations. Absent → compensate everything registered in this process. Set on a
    /// <see cref="BpmnEventDefinitionTypes.Compensation"/> definition of a throw/end event.
    /// </summary>
    public const string ActivityRef = "activityRef";

    /// <summary>
    /// The escalation code an <see cref="BpmnEventDefinitionTypes.Escalation"/> definition carries:
    /// the matching key. <b>Required</b> and non-empty on an escalation throw/end event (a throw must say what
    /// it escalates); <b>optional</b> on an escalation boundary event — a code-less boundary is the code-less
    /// catch-all that matches any escalation whose specific code no other boundary on the host claims.
    /// </summary>
    public const string Code = "code";
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

    /// <summary>
    /// A cancel event definition: on an end event inside a transaction it triggers the transaction
    /// cancellation (compensate the scope's registered work, then complete with the <c>Cancelled</c> outcome);
    /// on a boundary event attached to a transaction host it routes the cancellation path in the parent scope.
    /// </summary>
    public const string Cancel = "cancel";
}
