using System.Text.Json.Serialization;

namespace Elsa.Activities.Bpmn.Models;

/// <summary>
/// The immutable decision returned by an <c>IBpmnElementBehavior</c>. The engine's behavior applier
/// validates and applies the commands; behaviors never mutate state or schedule children themselves.
/// </summary>
public sealed record BpmnBehaviorDecision
{
    [JsonConstructor]
    public BpmnBehaviorDecision(IReadOnlyCollection<BpmnBehaviorCommand>? commands = null)
    {
        Commands = commands ?? [];
    }

    public IReadOnlyCollection<BpmnBehaviorCommand> Commands { get; init; }

    public static BpmnBehaviorDecision Of(params BpmnBehaviorCommand[] commands) => new(commands);
}

public sealed record BpmnBehaviorCommand
{
    [JsonConstructor]
    public BpmnBehaviorCommand(
        BpmnBehaviorCommandKind kind,
        IReadOnlyCollection<string>? flowIds = null,
        string? faultCode = null,
        string? message = null)
    {
        Kind = kind;
        FlowIds = flowIds ?? [];
        FaultCode = faultCode;
        Message = message;
    }

    public BpmnBehaviorCommandKind Kind { get; init; }

    /// <summary>The sequence flows to take (for <see cref="BpmnBehaviorCommandKind.EmitTokens"/>).</summary>
    public IReadOnlyCollection<string> FlowIds { get; init; }

    public string? FaultCode { get; init; }
    public string? Message { get; init; }

    public static BpmnBehaviorCommand EmitTokens(IReadOnlyCollection<string> flowIds) =>
        new(BpmnBehaviorCommandKind.EmitTokens, flowIds);

    public static BpmnBehaviorCommand ScheduleChild() => new(BpmnBehaviorCommandKind.ScheduleChild);

    public static BpmnBehaviorCommand ConsumeToken() => new(BpmnBehaviorCommandKind.ConsumeToken);

    public static BpmnBehaviorCommand TerminateProcess(string? message = null) =>
        new(BpmnBehaviorCommandKind.TerminateProcess, message: message);

    public static BpmnBehaviorCommand Fault(string faultCode, string message) =>
        new(BpmnBehaviorCommandKind.Fault, faultCode: faultCode, message: message);
}

public enum BpmnBehaviorCommandKind
{
    /// <summary>Consume the current token and emit one token per listed outbound sequence flow.</summary>
    EmitTokens,

    /// <summary>Park the current token and schedule the element's bound Elsa child activity.</summary>
    ScheduleChild,

    /// <summary>Consume the current token without emitting successors (none end event).</summary>
    ConsumeToken,

    /// <summary>Consume every live token and complete the process (terminate end event).</summary>
    TerminateProcess,

    /// <summary>Fault the composite deterministically.</summary>
    Fault
}
